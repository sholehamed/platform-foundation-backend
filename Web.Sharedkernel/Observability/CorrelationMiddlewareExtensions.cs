using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Web.SharedKernel.Observability;

/// <summary>Correlates HTTP errors, structured logs and frontend requests without logging headers/bodies.</summary>
public static class CorrelationMiddlewareExtensions
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "PlatformFoundation.CorrelationId";

    public static IApplicationBuilder UsePlatformCorrelation(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var inbound = context.Request.Headers[HeaderName].ToString();
            var id = IsSafe(inbound) ? inbound : Guid.NewGuid().ToString("N");
            context.Items[ItemKey] = id;
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[HeaderName] = id;
                return Task.CompletedTask;
            });

            using var scope = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("PlatformFoundation.Correlation")
                .BeginScope(new Dictionary<string, object>
                {
                    ["CorrelationId"] = id,
                    ["TraceId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier
                });
            await next(context);
        });

    public static bool IsSafe(string value)
        => value.Length is >= 1 and <= 64 &&
            value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
