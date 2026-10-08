using System.Diagnostics.Metrics;

namespace Application.SharedKernel.Observability;

/// <summary>
/// A safe, intentionally small observation for the future Angular monitoring UI.
/// Never put route parameters, request bodies, SQL, credentials, user IDs or tenant IDs here.
/// </summary>
public sealed record OperationObservation(
    DateTimeOffset Timestamp,
    string Category,
    string Name,
    double DurationMs,
    bool Failed,
    bool Slow,
    string? TraceId);

/// <summary>Nonblocking observer; must not fail requests when monitoring is unavailable.</summary>
public interface IOperationTelemetrySink
{
    void Record(OperationObservation observation);
}

/// <summary>Read-only contract for the future *authorized* Angular monitoring dashboard.</summary>
public interface IObservabilityReader
{
    ObservabilitySnapshot GetSnapshot(TimeSpan window, int recentLimit = 50);
}

public sealed record OperationAggregate(
    string Category, string Name, long Count, long Failed, long Slow,
    double AverageDurationMs, double P95DurationMs);

public sealed record ObservabilitySnapshot(
    DateTimeOffset GeneratedAt,
    DateTimeOffset WindowStart,
    long Total, long Failed, long Slow,
    double AverageDurationMs, double P95DurationMs,
    IReadOnlyList<OperationAggregate> Operations,
    IReadOnlyList<OperationObservation> Recent);

/// <summary>Low-cardinality instruments consumed by the OpenTelemetry host.</summary>
public static class FoundationMetrics
{
    public const string MeterName = "PlatformFoundation.Operations";
    private static readonly Meter Meter = new(MeterName);
    public static readonly Histogram<double> Duration =
        Meter.CreateHistogram<double>("foundation.operation.duration", "ms");
    public static readonly Counter<long> Executions =
        Meter.CreateCounter<long>("foundation.operation.count");
}
