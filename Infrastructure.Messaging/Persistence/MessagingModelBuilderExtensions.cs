using Infrastructure.Messaging.Model;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Messaging.Persistence;

public static class MessagingModelBuilderExtensions
{
    /// <summary>Call from the same DbContext that commits the business operation.</summary>
    public static ModelBuilder AddMessagingOutbox(this ModelBuilder builder)
    {
        builder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("MessagingOutboxMessages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Contract).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Payload).IsRequired();
            entity.HasIndex(x => x.CreatedAt);
        });

        builder.Entity<OutboxDelivery>(entity =>
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

        return builder;
    }
}
