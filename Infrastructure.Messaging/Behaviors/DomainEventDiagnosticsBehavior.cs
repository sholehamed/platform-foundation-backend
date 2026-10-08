using System.Diagnostics;
using Application.SharedKernel.Abstractions.Messaging;
using Application.SharedKernel.Observability;
using Domain.SharedKernel.Common.Events;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Messaging.Behaviors;

/// <summary>Before-save domain event tracing and performance; never logs event payload.</summary>
public sealed class DomainEventDiagnosticsBehavior<TEvent>(
    ILogger<DomainEventDiagnosticsBehavior<TEvent>> logger,
    PerformanceOptions options,
    IOperationTelemetrySink? sink = null) : IDomainEventPipelineBehavior<TEvent>
    where TEvent : IDomainEvent
{
    private static readonly ActivitySource ActivitySource = new("PlatformFoundation.Messaging");

    public async Task<IReadOnlyList<IMessage>> HandleAsync(
        TEvent domainEvent, DomainEventHandlerDelegate next,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("domain.event");
        var name = typeof(TEvent).Name;
        activity?.SetTag("domain.event.type", name);
        var start = Stopwatch.GetTimestamp();
        var failed = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await next();
        }
        catch (Exception ex)
        {
            failed = true;
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            throw;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var slow = elapsed >= options.SlowDomainEventThresholdMs;
            FoundationMetrics.Duration.Record(elapsed,
                new KeyValuePair<string, object?>("operation.category", "domain-event"),
                new KeyValuePair<string, object?>("operation.name", name));
            FoundationMetrics.Executions.Add(1,
                new KeyValuePair<string, object?>("operation.category", "domain-event"),
                new KeyValuePair<string, object?>("operation.status", failed ? "error" : "ok"));
            if (slow)
                logger.LogWarning(
                    "Slow domain event {EventType} took {ElapsedMs} ms (threshold {ThresholdMs} ms)",
                    name, elapsed, options.SlowDomainEventThresholdMs);
            try
            {
                sink?.Record(new OperationObservation(DateTimeOffset.UtcNow,
                    "domain-event", name, elapsed, failed, slow,
                    Activity.Current?.TraceId.ToString()));
            }
            catch (Exception ex)
            {
                logger.LogDebug("Domain event telemetry sink failed: {ErrorType}",
                    ex.GetType().Name);
            }
        }
    }
}
