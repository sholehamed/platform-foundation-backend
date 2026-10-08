using System.Diagnostics;
using Application.SharedKernel.Abstractions.Messaging;
using Microsoft.Extensions.Logging;

namespace Application.SharedKernel.Behaviors;

/// <summary>
/// Adds an Activity span and structured timing logs without serializing request data.
/// This works without an OpenTelemetry exporter and is instrumented later by the host.
/// </summary>
public sealed class RequestDiagnosticsBehavior<TRequest, TResponse>(
    ILogger<RequestDiagnosticsBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        using var activity = CqrsDiagnostics.ActivitySource.StartActivity(
            $"CQRS {requestName}", ActivityKind.Internal);
        activity?.SetTag("cqrs.request.name", requestName);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next();
            stopwatch.Stop();
            logger.LogDebug(
                "CQRS request {RequestName} completed in {ElapsedMs} ms",
                requestName, stopwatch.Elapsed.TotalMilliseconds);
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            logger.LogDebug(
                "CQRS request {RequestName} cancelled after {ElapsedMs} ms",
                requestName, stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            // Log the exception at the HTTP/background boundary; avoid duplicate stacks here.
            logger.LogWarning(
                "CQRS request {RequestName} failed with {ErrorType} after {ElapsedMs} ms",
                requestName, exception.GetType().Name, stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
    }
}

public static class CqrsDiagnostics
{
    public const string ActivitySourceName = "PlatformFoundation.Cqrs";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
