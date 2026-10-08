namespace Infrastructure.Messaging.Configuration;

public sealed class MessagingOptions
{
    public int BatchSize { get; set; } = 50;
    public int MaxAttempts { get; set; } = 5;
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(1);

    internal void Validate()
    {
        if (BatchSize is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(BatchSize));
        if (MaxAttempts is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(MaxAttempts));
        if (LeaseDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(LeaseDuration));
        if (BaseRetryDelay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(BaseRetryDelay));
        if (MaxRetryDelay < BaseRetryDelay) throw new ArgumentOutOfRangeException(nameof(MaxRetryDelay));
    }
}
