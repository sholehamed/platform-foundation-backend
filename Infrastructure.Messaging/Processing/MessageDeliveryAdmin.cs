using Infrastructure.Messaging.Model;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Messaging.Processing;

/// <summary>Application service only. Never expose admin retries without authorization.</summary>
public interface IMessageDeliveryAdmin
{
    Task<bool> RetryDeadLetterAsync(Guid deliveryId, CancellationToken cancellationToken = default);
}

public sealed class MessageDeliveryAdmin<TDbContext>(TDbContext db, TimeProvider clock)
    : IMessageDeliveryAdmin where TDbContext : DbContext
{
    public async Task<bool> RetryDeadLetterAsync(
        Guid deliveryId, CancellationToken cancellationToken = default)
    {
        var updated = await db.Set<OutboxDelivery>()
            .Where(d => d.Id == deliveryId && d.Status == DeliveryStatus.DeadLetter)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, DeliveryStatus.Pending)
                .SetProperty(d => d.Attempts, 0)
                .SetProperty(d => d.NextAttemptAt, clock.GetUtcNow().UtcDateTime)
                .SetProperty(d => d.LastErrorType, (string?)null)
                .SetProperty(d => d.LeaseToken, (Guid?)null)
                .SetProperty(d => d.LeaseExpiresAt, (DateTime?)null),
                cancellationToken);

        return updated == 1;
    }
}
