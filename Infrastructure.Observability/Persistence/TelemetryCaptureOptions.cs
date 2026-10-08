namespace Infrastructure.Observability.Persistence;

public sealed class TelemetryCaptureOptions
{
    public bool Enabled { get; set; }
    public int QueueCapacity { get; set; } = 2000;
    public int BatchSize { get; set; } = 100;
    public int RetentionDays { get; set; } = 7;
    public bool CaptureSanitizedLogs { get; set; } = true;
    public bool CaptureFoundationTraces { get; set; } = true;

    public void Validate()
    {
        if (QueueCapacity is < 100 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(QueueCapacity));
        if (BatchSize is < 1 or > 1000 || BatchSize > QueueCapacity)
            throw new ArgumentOutOfRangeException(nameof(BatchSize));
        if (RetentionDays is < 1 or > 730)
            throw new ArgumentOutOfRangeException(nameof(RetentionDays));
    }
}
