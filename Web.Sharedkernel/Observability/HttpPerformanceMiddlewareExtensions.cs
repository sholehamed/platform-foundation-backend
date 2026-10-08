using System.Diagnostics;
using Application.SharedKernel.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Web.SharedKernel.Observability;

/// <summary>
/// Records HTTP duration with low-cardinality *route templates*, not raw paths.
/// Runs inside correlation middleware, outside Minimal API endpoint execution.
/// </summary>
public static class HttpPerformanceMiddlewareExtensions
{
    public static IApplicationBuilder UsePlatformHttpPerformance(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            var started = Stopwatch.GetTimestamp();
            var failed = false;
            try { await next(context); }
            catch { failed = true; throw; }
            finally
            {
                var options = context.RequestServices.GetService<PerformanceOptions>()
                    ?? new PerformanceOptions();
                var milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var isSlow = milliseconds >= options.SlowRequestThresholdMs;
                var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
                    ?? "unmatched";
                // Route templates are static server-defined strings; never expose raw URLs.
                var name = $"{context.Request.Method} {route}";
                if (name.Length > 128) name = name[..128];
                var isFailure = failed || context.Response.StatusCode >= 500;

                FoundationMetrics.Duration.Record(milliseconds,
                    new KeyValuePair<string, object?>("operation.category", "http"),
                    new KeyValuePair<string, object?>("operation.name", name));
                FoundationMetrics.Executions.Add(1,
                    new KeyValuePair<string, object?>("operation.category", "http"),
                    new KeyValuePair<string, object?>("operation.status", isFailure ? "error" : "ok"));

                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("PlatformFoundation.HttpPerformance");
                if (isSlow)
                    logger.LogWarning(
                        "Slow HTTP operation {OperationName} took {ElapsedMs} ms (threshold {ThresholdMs} ms)",
                        name, milliseconds, options.SlowRequestThresholdMs);

                try
                {
                    context.RequestServices.GetService<IOperationTelemetrySink>()?.Record(
                        new OperationObservation(DateTimeOffset.UtcNow, "http", name,
                            milliseconds, isFailure, isSlow,
                            Activity.Current?.TraceId.ToString()));
                }
                catch (Exception exception)
                {
                    logger.LogDebug("Telemetry sink unavailable: {ErrorType}",
                        exception.GetType().Name);
                }
            }
        });
}
