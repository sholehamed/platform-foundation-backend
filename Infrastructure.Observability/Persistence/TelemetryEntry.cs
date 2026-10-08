using Application.SharedKernel.Observability;

namespace Infrastructure.Observability.Persistence;

/// <summary>Provider-agnostic EF representation; DateTime is always UTC.</summary>
public sealed class TelemetryEntry
{
    public Guid Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public TelemetryKind Kind { get; set; }
    public string Source { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Level { get; set; }
    public double? DurationMs { get; set; }
    public bool Failed { get; set; }
    public bool Slow { get; set; }
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string? ParentSpanId { get; set; }
    public string? ErrorType { get; set; }

    public static TelemetryEntry From(TelemetryEvent item) => new()
    {
        Id = Guid.NewGuid(),
        TimestampUtc = item.Timestamp.UtcDateTime,
        Kind = item.Kind,
        Source = item.Source,
        Name = item.Name,
        Level = item.Level,
        DurationMs = item.DurationMs,
        Failed = item.Failed,
        Slow = item.Slow,
        TraceId = item.TraceId,
        SpanId = item.SpanId,
        ParentSpanId = item.ParentSpanId,
        ErrorType = item.ErrorType
    };

    public TelemetryItem ToContract() => new(
        Id, new DateTimeOffset(DateTime.SpecifyKind(TimestampUtc, DateTimeKind.Utc)),
        Kind, Source, Name, Level, DurationMs, Failed, Slow,
        TraceId, SpanId, ParentSpanId, ErrorType);
}
