using Infrastructure.Messaging.Model;
using Infrastructure.Messaging.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Infrastructure.Messaging.Persistence.Migrations;

/// <summary>
/// Frozen baseline model for future EF Core migration diffs.
/// Do not call AddMessagingOutbox() here; that extension evolves with runtime model.
/// </summary>
[DbContext(typeof(PlatformMessagingDbContext))]
public sealed class PlatformMessagingDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.10");

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("MessagingOutboxMessages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Contract).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Payload).IsRequired();
            entity.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<OutboxDelivery>(entity =>
        {
            entity.ToTable("MessagingOutboxDeliveries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.HandlerKey).HasMaxLength(512).IsRequired();
            entity.Property(x => x.LastErrorType).HasMaxLength(256);
            entity.Property(x => x.Status).HasConversion<int>();
            entity.HasOne(x => x.Message).WithMany().HasForeignKey(x => x.MessageId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.Status, x.NextAttemptAt });
            entity.HasIndex(x => new { x.MessageId, x.HandlerKey }).IsUnique();
        });
    }
}
