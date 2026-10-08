namespace Application.SharedKernel.Observability;

/// <summary>Operational thresholds in milliseconds. No request payloads are logged.</summary>
public sealed class PerformanceOptions
{
    public double SlowRequestThresholdMs { get; set; } = 500;
    public double SlowNotificationThresholdMs { get; set; } = 500;
    public double SlowMessageThresholdMs { get; set; } = 1000;
    public double SlowDatabaseThresholdMs { get; set; } = 250;

    public void Validate()
    {
        if (!double.IsFinite(SlowRequestThresholdMs) || SlowRequestThresholdMs < 0 ||
            !double.IsFinite(SlowNotificationThresholdMs) || SlowNotificationThresholdMs < 0 ||
            !double.IsFinite(SlowMessageThresholdMs) || SlowMessageThresholdMs < 0 ||
            !double.IsFinite(SlowDatabaseThresholdMs) || SlowDatabaseThresholdMs < 0)
            throw new ArgumentOutOfRangeException(nameof(PerformanceOptions),
                "Thresholds must be finite, nonnegative milliseconds.");
    }
}
