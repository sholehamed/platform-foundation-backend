using System.Diagnostics;
using Application.SharedKernel.Exceptions;
using FluentValidation.Results;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using AppValidationException = Application.SharedKernel.Exceptions.ValidationException;
using FluentValidationException = FluentValidation.ValidationException;

namespace Web.SharedKernel.Exceptions;

/// <summary>
/// Single HTTP boundary for exceptions, with safe RFC 9457 ProblemDetails responses.
/// Does not turn exceptions back into Application Results.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // The HTTP client is gone; no meaningful response can be delivered.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
            return false;

        var error = Classify(exception);
        var traceId = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;

        if (error.Status < StatusCodes.Status500InternalServerError)
            logger.LogInformation(
                "Application request failed: {ErrorCode}, HTTP {StatusCode}, trace {TraceId}",
                error.Code, error.Status, traceId);
        // Unexpected exceptions are logged/emitted by ExceptionHandlerMiddleware.
        // .NET 10 suppresses handled-exception diagnostics by default; see registration.

        var problem = new ProblemDetails
        {
            Status = error.Status,
            Title = ReasonPhrases.GetReasonPhrase(error.Status),
            Detail = error.Detail,
            Instance = httpContext.Request.Path
        };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] = traceId;
        if (error.Errors is not null)
            problem.Extensions["errors"] = error.Errors;

        httpContext.Response.StatusCode = error.Status;
        if (!await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = problem
            }))
        {
            // Force a consistent JSON contract even for clients with unsupported Accept.
            await httpContext.Response.WriteAsJsonAsync(
                problem,
                contentType: "application/problem+json",
                cancellationToken: cancellationToken);
        }

        return true;
    }

    private static Failure Classify(Exception exception) => exception switch
    {
        AppValidationException validation => new(
            StatusCodes.Status400BadRequest,
            "validation.failed",
            "One or more validation errors occurred.",
            validation.Errors),

        FluentValidationException validation => new(
            StatusCodes.Status400BadRequest,
            "validation.failed",
            "One or more validation errors occurred.",
            validation.Errors
                .GroupBy(failure => failure.PropertyName, failure => failure.ErrorMessage)
                .ToDictionary(group => group.Key, group => group.ToArray())),

        AppException app => new(
            MapStatus(app.ErrorType), app.Code, app.Message, null),

        ForbiddenAccessException => new(
            StatusCodes.Status403Forbidden,
            "authorization.forbidden",
            "You do not have permission to perform this action.",
            null),

        // Compatibility for existing legacy modules; migrate callers to AppException.
        ServiceExeption legacy => new(
            StatusCodes.Status400BadRequest,
            $"legacy.service_error.{legacy.ErrorCode}",
            "The request could not be processed.",
            null),

        _ => new(
            StatusCodes.Status500InternalServerError,
            "common.unexpected_error",
            "An unexpected error occurred.",
            null)
    };

    private static int MapStatus(AppErrorType kind) => kind switch
    {
        AppErrorType.BadRequest => StatusCodes.Status400BadRequest,
        AppErrorType.NotFound => StatusCodes.Status404NotFound,
        AppErrorType.Conflict => StatusCodes.Status409Conflict,
        AppErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        AppErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        AppErrorType.Forbidden => StatusCodes.Status403Forbidden,
        AppErrorType.ServiceUnavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError
    };

    private sealed record Failure(
        int Status, string Code, string Detail, object? Errors);
}
