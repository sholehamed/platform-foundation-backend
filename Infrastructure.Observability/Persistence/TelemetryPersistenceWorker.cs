using Application.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Observability.Persistence;

/// <summary>
/// Flushes bounded telemetry batches to a separate database. Failed batches remain
/// in memory and are retried; the original request is never blocked.
/// </summary>
public sealed class TelemetryPersistenceWorker(
    ChannelTelemetrySink channel,
    IServiceScopeFactory scopes,
    TelemetryCaptureOptions options,
    ILogger<TelemetryPersistenceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = channel.Reader;
        var batch = new List<TelemetryEvent>(options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (batch.Count == 0)
            {
                try
                {
                    var item = await reader.ReadAsync(stoppingToken);
                    batch.Add(item);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            while (batch.Count < options.BatchSize && reader.TryRead(out var next))
                batch.Add(next);

            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<TelemetryDbContext>();
                db.Entries.AddRange(batch.Select(TelemetryEntry.From));
                await db.SaveChangesAsync(stoppingToken);
                batch.Clear();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // No message bodies or SQL in our logs; error class only.
                logger.LogWarning("Telemetry batch persistence failed: {ErrorType}",
                    ex.GetType().Name);
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        // Graceful shutdown is best effort. No persistence guarantee during
        // abrupt host termination, unlike a transactional business outbox.
    }
}

/// <summary>Hourly retention job, independent of the request pipeline.</summary>
public sealed class TelemetryRetentionWorker(
    IServiceScopeFactory scopes,
    TelemetryCaptureOptions options,
    TimeProvider clock,
    ILogger<TelemetryRetentionWorker> logger) : BackgroundService
{
    public async Task<int> SweepAsync(CancellationToken ct = default)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-options.RetentionDays);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TelemetryDbContext>();
        return await db.Entries.Where(x => x.TimestampUtc < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await SweepAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                { break; }
                catch (Exception ex)
                {
                    logger.LogWarning("Telemetry retention failed: {ErrorType}",
                        ex.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
