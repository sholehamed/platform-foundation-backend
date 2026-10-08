using Hangfire;
using Infrastructure.Messaging.Configuration;
using Infrastructure.Messaging.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Messaging.Processing;

/// <summary>
/// Hangfire wakes this job every minute. Database state, not Hangfire jobs,
/// is authoritative for subscription delivery and retry.
/// </summary>
public sealed class OutboxWorker<TDbContext>(
    TDbContext db,
    MessageRegistry registry,
    IServiceProvider services,
    MessagingOptions options,
    TimeProvider clock,
    ILogger<OutboxWorker<TDbContext>> logger)
    where TDbContext : DbContext
{
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var ids = await db.Set<OutboxDelivery>()
            .AsNoTracking()
            .Where(d =>
                (d.Status == DeliveryStatus.Pending && d.NextAttemptAt <= now) ||
                (d.Status == DeliveryStatus.Processing &&
                    d.LeaseExpiresAt != null && d.LeaseExpiresAt <= now))
            .OrderBy(d => d.NextAttemptAt)
            .Select(d => d.Id)
            .Take(options.BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessOneAsync(id, cancellationToken);
        }
    }

    private async Task ProcessOneAsync(Guid id, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var token = Guid.NewGuid();
        var expiresAt = now.Add(options.LeaseDuration);
        var claimed = await db.Set<OutboxDelivery>()
            .Where(d => d.Id == id && (
                (d.Status == DeliveryStatus.Pending && d.NextAttemptAt <= now) ||
                (d.Status == DeliveryStatus.Processing &&
                    d.LeaseExpiresAt != null && d.LeaseExpiresAt <= now)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, DeliveryStatus.Processing)
                .SetProperty(d => d.LeaseToken, token)
                .SetProperty(d => d.LeaseExpiresAt, expiresAt)
                .SetProperty(d => d.Attempts, d => d.Attempts + 1),
                cancellationToken);

        if (claimed == 0) return; // Another instance already owns the delivery.

        var delivery = await db.Set<OutboxDelivery>()
            .AsNoTracking()
            .Include(d => d.Message)
            .SingleAsync(d => d.Id == id, cancellationToken);

        try
        {
            var subscriber = registry.GetHandler(delivery.Message.Contract, delivery.HandlerKey);
            // Hangfire creates a DI scope for the job. The publisher's request
            // scope is already gone; handlers receive dependencies from this scope.
            await subscriber.Execute(services, delivery.Message.Payload, cancellationToken);

            var count = await db.Set<OutboxDelivery>()
                .Where(d => d.Id == id && d.Status == DeliveryStatus.Processing && d.LeaseToken == token)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Status, DeliveryStatus.Completed)
                    .SetProperty(d => d.CompletedAt, clock.GetUtcNow().UtcDateTime)
                    .SetProperty(d => d.LeaseToken, (Guid?)null)
                    .SetProperty(d => d.LeaseExpiresAt, (DateTime?)null),
                    cancellationToken);

            if (count == 0)
                logger.LogWarning("Message delivery lease was lost for {DeliveryId}", id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Leave in processing; expired leases will be recovered by the next scan.
            throw;
        }
        catch (Exception ex)
        {
            var attempt = delivery.Attempts;
            var dead = attempt >= options.MaxAttempts;
            var seconds = Math.Min(options.MaxRetryDelay.TotalSeconds,
                options.BaseRetryDelay.TotalSeconds * Math.Pow(2, Math.Min(attempt - 1, 30)));
            var nextAttempt = clock.GetUtcNow().UtcDateTime.AddSeconds(seconds);

            await db.Set<OutboxDelivery>()
                .Where(d => d.Id == id && d.Status == DeliveryStatus.Processing && d.LeaseToken == token)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Status, dead ? DeliveryStatus.DeadLetter : DeliveryStatus.Pending)
                    .SetProperty(d => d.NextAttemptAt, nextAttempt)
                    .SetProperty(d => d.LastErrorType, ex.GetType().Name)
                    .SetProperty(d => d.LeaseToken, (Guid?)null)
                    .SetProperty(d => d.LeaseExpiresAt, (DateTime?)null),
                    cancellationToken);

            // The exception detail stays in protected logs, never in queue payload metadata.
            logger.LogError(ex,
                "Message delivery {DeliveryId} failed on attempt {Attempt}; dead letter: {DeadLetter}",
                id, attempt, dead);
        }
    }
}
