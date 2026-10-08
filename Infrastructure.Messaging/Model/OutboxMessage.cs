namespace Infrastructure.Messaging.Model;

/// <summary>Serialized envelope; persists with the module's business entities.</summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Contract { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public enum DeliveryStatus { Pending = 0, Processing = 1, Completed = 2, DeadLetter = 3 }

/// <summary>A separate durable receipt for every subscriber.</summary>
public sealed class OutboxDelivery
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public OutboxMessage Message { get; set; } = null!;
    public string HandlerKey { get; set; } = string.Empty;
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? LastErrorType { get; set; }
}
