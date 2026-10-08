using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Messaging.Persistence;

/// <summary>
/// Optional standalone store for hosts with no domain DbContext yet.
/// For atomic business+outbox commits, use AddMessagingOutbox on the module DbContext
/// instead of this separate store.
/// </summary>
public sealed class PlatformMessagingDbContext(DbContextOptions<PlatformMessagingDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddMessagingOutbox();
        base.OnModelCreating(modelBuilder);
    }
}
