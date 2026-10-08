using System.Diagnostics;
using Application.SharedKernel.Abstractions.Messaging;
using Application.SharedKernel.Observability;
using Microsoft.Extensions.Logging;

namespace Application.SharedKernel.Behaviors;

/// <summary>Reports ALL command/query timings; warns only when a configurable threshold is exceeded.</summary>
public sealed class RequestPerformanceBehavior<TRequest, TResponse>(
    ILogger<RequestPerformanceBehavior<TRequest, TResponse>> logger,
    PerformanceOptions options,
    IOperationTelemetrySink? sink = null) : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var failed = false;
        try
        {
            return await next();
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            var ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var slow = ms >= options.SlowRequestThresholdMs;
            var name = typeof(TRequest).Name;
            const string category = "cqrs";

            FoundationMetrics.Duration.Record(ms,
                new KeyValuePair<string, object?>("operation.category", category),
                new KeyValuePair<string, object?>("operation.name", name));
            FoundationMetrics.Executions.Add(1,
                new KeyValuePair<string, object?>("operation.category", category),
                new KeyValuePair<string, object?>("operation.status", failed ? "error" : "ok"));

            if (slow)
                logger.LogWarning(
                    "Slow CQRS operation {OperationName} took {ElapsedMs} ms (threshold {ThresholdMs} ms)",
                    name, ms, options.SlowRequestThresholdMs);

            // Monitoring is best effort and must never change the application's result.
            try
            {
                sink?.Record(new OperationObservation(DateTimeOffset.UtcNow, category,
                    name, ms, failed, slow, Activity.Current?.TraceId.ToString()));
            }
            catch (Exception exception)
            {
                logger.LogDebug("Telemetry sink unavailable: {ErrorType}", exception.GetType().Name);
            }
        }
    }
}
