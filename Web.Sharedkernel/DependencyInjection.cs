using System.Diagnostics;
using Application.SharedKernel.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Web.SharedKernel.Exceptions;
using AppValidationException = Application.SharedKernel.Exceptions.ValidationException;
using FluentValidationException = FluentValidation.ValidationException;

namespace Web.SharedKernel;

public static class DependencyInjection
{
    public static IServiceCollection AddBaseApiServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                var problem = context.ProblemDetails;
                problem.Instance ??= context.HttpContext.Request.Path.Value;
                problem.Extensions.TryAdd("traceId",
                    Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier);

                // Also standardize empty-body auth failures and other 4xx status responses.
                var status = problem.Status ?? context.HttpContext.Response.StatusCode;
                var code = status switch
                {
                    StatusCodes.Status400BadRequest => "request.bad_request",
                    StatusCodes.Status401Unauthorized => "authentication.required",
                    StatusCodes.Status403Forbidden => "authorization.forbidden",
                    StatusCodes.Status404NotFound => "resource.not_found",
                    StatusCodes.Status405MethodNotAllowed => "request.method_not_allowed",
                    StatusCodes.Status409Conflict => "resource.conflict",
                    StatusCodes.Status429TooManyRequests => "request.rate_limited",
                    StatusCodes.Status503ServiceUnavailable => "service.unavailable",
                    _ => status >= 500 ? "common.unexpected_error" : "request.failed"
                };
                problem.Extensions.TryAdd("code", code);
            };
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();
        return services;
    }

    public static IApplicationBuilder UseBaseApiExceptionHandling(this IApplicationBuilder app)
    {
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            AllowStatusCode404Response = true,
            // Known expected errors are normal application outcomes. For unexpected
            // errors preserve the built-in .NET 10 exception logs and error metrics.
            SuppressDiagnosticsCallback = context => context.Exception is
                AppException or AppValidationException or FluentValidationException
                    or ForbiddenAccessException or ServiceExeption
        });
        app.UseStatusCodePages();
        return app;
    }
}
