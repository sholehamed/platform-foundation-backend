using System.Diagnostics;
using Application.SharedKernel.Observability;
using Microsoft.Extensions.Hosting;

namespace Infrastructure.Observability.Persistence;

/// <summary>Records selected Foundation spans for the Angular trace explorer.</summary>
public sealed class FoundationActivityCapture(ITelemetryEventSink sink) : IHostedService
{
    private ActivityListener? listener;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "PlatformFoundation.Cqrs"
                or "PlatformFoundation.Messaging",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                try
                {
                    sink.TryRecord(new TelemetryEvent(
                        DateTimeOffset.UtcNow, TelemetryKind.Trace,
                        activity.Source.Name, activity.OperationName,
                        DurationMs: activity.Duration.TotalMilliseconds,
                        Failed: activity.Status == ActivityStatusCode.Error,
                        TraceId: activity.TraceId.ToString(),
                        SpanId: activity.SpanId.ToString(),
                        ParentSpanId: activity.ParentSpanId.ToString(),
                        ErrorType: activity.Status == ActivityStatusCode.Error
                            ? "ActivityError" : null));
                }
                catch { /* best effort */ }
            }
        };
        ActivitySource.AddActivityListener(listener);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        listener?.Dispose();
        return Task.CompletedTask;
    }
}
