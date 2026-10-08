using Application.SharedKernel.Observability;
using Infrastructure.Observability.Storage;

namespace Infrastructure.Observability.Persistence;

/// <summary>
/// Retains the existing live snapshot while asynchronously persisting safe samples.
/// Both paths are best effort and must never throw into application operations.
/// </summary>
public sealed class PersistentOperationTelemetrySink(
    BoundedObservabilityStore live, ChannelTelemetrySink durable)
    : IOperationTelemetrySink
{
    public void Record(OperationObservation observation)
    {
        try { live.Record(observation); } catch { /* monitoring cannot break business flow */ }
        try
        {
            durable.TryRecord(new TelemetryEvent(
                observation.Timestamp, TelemetryKind.Operation,
                observation.Category, observation.Name, DurationMs: observation.DurationMs,
                Failed: observation.Failed, Slow: observation.Slow,
                TraceId: observation.TraceId));
        }
        catch { /* dropped telemetry is always best-effort */ }
    }
}
