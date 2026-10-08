using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Application.SharedKernel.Observability;

namespace Infrastructure.Observability.Persistence;

/// <summary>
/// Non-blocking bounded async spool. Never uses SQL on a request thread.
/// Capacity exhaustion is observable and drops newest samples intentionally.
/// </summary>
public sealed class ChannelTelemetrySink : ITelemetryEventSink
{
    private readonly Channel<TelemetryEvent> channel;
    private static readonly Meter Meter = new("PlatformFoundation.TelemetryStorage");
    private static readonly Counter<long> Dropped = Meter.CreateCounter<long>("foundation.telemetry.dropped");

    public ChannelTelemetrySink(TelemetryCaptureOptions options)
    {
        options.Validate();
        channel = Channel.CreateBounded<TelemetryEvent>(new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    public bool TryRecord(TelemetryEvent item)
    {
        if (!TelemetrySafety.IsSafe(item)) return false;
        if (channel.Writer.TryWrite(item)) return true;
        Dropped.Add(1);
        return false;
    }

    internal ChannelReader<TelemetryEvent> Reader => channel.Reader;
    internal void Complete() => channel.Writer.TryComplete();
}

internal static class TelemetrySafety
{
    internal static bool IsSafe(TelemetryEvent item) =>
        item is not null &&
        Enum.IsDefined(item.Kind) &&
        IsCode(item.Source, 128) && IsCode(item.Name, 128) &&
        (item.Level is null || item.Level is "Trace" or "Debug" or "Information"
            or "Warning" or "Error" or "Critical") &&
        (item.ErrorType is null || IsCode(item.ErrorType, 128)) &&
        (item.DurationMs is null ||
            double.IsFinite(item.DurationMs.Value) && item.DurationMs.Value >= 0) &&
        (item.TraceId is null || IsHex(item.TraceId, 32)) &&
        (item.SpanId is null || IsHex(item.SpanId, 16)) &&
        (item.ParentSpanId is null || IsHex(item.ParentSpanId, 16)) &&
        item.Timestamp > DateTimeOffset.UnixEpoch &&
        item.Timestamp < DateTimeOffset.UtcNow.AddDays(1);

    private static bool IsCode(string? text, int max) =>
        text is { Length: > 0 } && text.Length <= max &&
        text.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_' or ' ' or ':');

    private static bool IsHex(string text, int len) =>
        text.Length == len && text.All(Uri.IsHexDigit);
}
