namespace Application.SharedKernel.Observability;

/// <summary>Safe, finite vocabulary of telemetry items. No arbitrary payload fields.</summary>
public enum TelemetryKind { Operation = 0, Log = 1, Trace = 2, Exception = 3 }

/// <summary>
/// Write-only sanitized envelope. Never add message bodies, raw log text,
/// stack traces, SQL, headers, identities or exception messages to this contract.
/// </summary>
public sealed record TelemetryEvent(
    DateTimeOffset Timestamp,
    TelemetryKind Kind,
    string Source,
    string Name,
    string? Level = null,
    double? DurationMs = null,
    bool Failed = false,
    bool Slow = false,
    string? TraceId = null,
    string? SpanId = null,
    string? ParentSpanId = null,
    string? ErrorType = null);

/// <summary>Best-effort, nonblocking enqueue. False means capacity was exhausted.</summary>
public interface ITelemetryEventSink
{
    bool TryRecord(TelemetryEvent item);
}

public sealed record TelemetryQuery(
    DateTimeOffset From, DateTimeOffset To,
    TelemetryKind? Kind = null,
    string? Source = null,
    string? Name = null,
    string? TraceId = null,
    bool? Failed = null,
    bool? Slow = null,
    int Page = 1,
    int PageSize = 50);

/// <summary>Stable Angular API contract. Count includes all matching rows.</summary>
public sealed record TelemetryPage<T>(
    IReadOnlyList<T> Items, long Total, int Page, int PageSize);

/// <summary>Trace timeline result with explicit truncation for long traces.</summary>
public sealed record TelemetryTraceResult(
    string TraceId, long Total, bool Truncated, IReadOnlyList<TelemetryItem> Items);

public sealed record TelemetryItem(
    Guid Id,
    DateTimeOffset Timestamp,
    TelemetryKind Kind,
    string Source,
    string Name,
    string? Level,
    double? DurationMs,
    bool Failed,
    bool Slow,
    string? TraceId,
    string? SpanId,
    string? ParentSpanId,
    string? ErrorType);

public sealed record TelemetryTrendBucket(
    DateTimeOffset Start, long Count, long Failed, long Slow);

public sealed record TelemetrySummary(
    DateTimeOffset From, DateTimeOffset To,
    long Total, long Failed, long Slow,
    double AverageDurationMs, double P50DurationMs,
    double P95DurationMs, double P99DurationMs,
    bool PercentilesSampled,
    IReadOnlyList<TelemetryTrendBucket> Trend);

public interface ITelemetryHistoryReader
{
    Task<TelemetryPage<TelemetryItem>> SearchAsync(
        TelemetryQuery query, CancellationToken cancellationToken = default);

    Task<TelemetryTraceResult> GetTraceAsync(
        string traceId, DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<TelemetrySummary> GetSummaryAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
