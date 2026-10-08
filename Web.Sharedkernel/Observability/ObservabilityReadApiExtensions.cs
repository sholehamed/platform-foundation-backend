using Application.SharedKernel.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Web.SharedKernel.Observability;

/// <summary>
/// JSON-only read contract for the Angular dashboard. No backend HTML/dashboard.
/// Disabled by default; MUST be used after a real authentication scheme is registered.
/// </summary>
public static class ObservabilityReadApiExtensions
{
    public const string ReadPolicy = "PlatformFoundation.Observability.Read";

    public static IServiceCollection AddPlatformObservabilityReadAuthorization(
        this IServiceCollection services)
    {
        services.AddAuthorizationBuilder().AddPolicy(ReadPolicy,
            policy => policy.RequireAuthenticatedUser()
                .RequireClaim("permission", "platform.observability.read"));
        return services;
    }

    public static RouteGroupBuilder MapPlatformObservabilityReadApi(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/platform/observability")
            .RequireAuthorization(ReadPolicy)
            .WithTags("Platform Monitoring (authorized)");

        group.MapGet("/snapshot", (IObservabilityReader reader, int? minutes, int? limit) =>
        {
            var window = minutes ?? 15;
            var take = limit ?? 50;
            if (window is < 1 or > 1440 || take is < 0 or > 200)
                return Results.BadRequest(new { code = "observability.invalid_query" });

            return Results.Ok(reader.GetSnapshot(TimeSpan.FromMinutes(window), take));
        })
        .WithName("PlatformObservabilitySnapshot")
        .Produces<ObservabilitySnapshot>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        return group;
    }
}
