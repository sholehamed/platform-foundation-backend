using System.Diagnostics;
using Application.SharedKernel.Abstractions.Messaging;
using Application.SharedKernel.Observability;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Messaging.Behaviors;

/// <summary>Records each individual subscriber delivery, including failures and retries.</summary>
public sealed class MessagePerformanceBehavior<TMessage>(
    ILogger<MessagePerformanceBehavior<TMessage>> logger,
    PerformanceOptions options,
    IOperationTelemetrySink? sink = null)
    : IMessagePipelineBehavior<TMessage> where TMessage : IMessage
{
    public async Task HandleAsync(TMessage message, MessageContext context,
        MessageHandlerDelegate next, CancellationToken ct)
    {
        var start = Stopwatch.GetTimestamp();
        var failed = false;
        try { await next(); }
        catch { failed = true; throw; }
        finally
        {
            var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var slow = ms >= options.SlowMessageThresholdMs;
            const string category = "message";
            var name = typeof(TMessage).Name;
            FoundationMetrics.Duration.Record(ms,
                new KeyValuePair<string, object?>("operation.category", category),
                new KeyValuePair<string, object?>("operation.name", name));
            FoundationMetrics.Executions.Add(1,
                new KeyValuePair<string, object?>("operation.category", category),
                new KeyValuePair<string, object?>("operation.status", failed ? "error" : "ok"));
            if (slow)
                logger.LogWarning(
                    "Slow message delivery {MessageType} {DeliveryId}: {ElapsedMs} ms (threshold {ThresholdMs} ms)",
                    name, context.DeliveryId, ms, options.SlowMessageThresholdMs);
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
