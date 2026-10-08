using System.Diagnostics;
using Application.SharedKernel.Abstractions.Messaging;
using Application.SharedKernel.Observability;
using Domain.SharedKernel.Common.Events;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Messaging.Behaviors;

public sealed class NotificationPerformanceBehavior<TNotification>(
    ILogger<NotificationPerformanceBehavior<TNotification>> logger,
    PerformanceOptions options,
    IOperationTelemetrySink? sink = null)
    : INotificationPipelineBehavior<TNotification> where TNotification : INotification
{
    public async Task HandleAsync(TNotification notification,
        NotificationHandlerDelegate next, CancellationToken ct)
    {
        var start = Stopwatch.GetTimestamp();
        var failed = false;
        try { await next(); }
        catch { failed = true; throw; }
        finally
        {
            var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var slow = ms >= options.SlowNotificationThresholdMs;
            const string category = "notification";
            var name = typeof(TNotification).Name;
            FoundationMetrics.Duration.Record(ms,
                new KeyValuePair<string, object?>("operation.category", category),
                new KeyValuePair<string, object?>("operation.name", name));
            FoundationMetrics.Executions.Add(1,
                new KeyValuePair<string, object?>("operation.category", category),
                new KeyValuePair<string, object?>("operation.status", failed ? "error" : "ok"));
            if (slow)
                logger.LogWarning(
                    "Slow notification {NotificationType}: {ElapsedMs} ms (threshold {ThresholdMs} ms)",
                    name, ms, options.SlowNotificationThresholdMs);
            try
            {
                sink?.Record(new OperationObservation(DateTimeOffset.UtcNow, category,
                    name, ms, failed, slow, Activity.Current?.TraceId.ToString()));
            }
            catch (Exception ex)
            {
                logger.LogDebug("Telemetry sink unavailable: {ErrorType}", ex.GetType().Name);
            }
        }
    }
}
