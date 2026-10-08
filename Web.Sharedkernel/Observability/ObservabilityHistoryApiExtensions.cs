using Application.SharedKernel.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Web.SharedKernel.Observability;

/// <summary>
/// Angular JSON history endpoints. Host MUST register authentication + explicit
/// permission and opt-in persistence before mapping this group.
/// </summary>
public static class ObservabilityHistoryApiExtensions
{
    public static RouteGroupBuilder MapPlatformObservabilityHistoryApi(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/platform/observability")
            .RequireAuthorization(ObservabilityReadApiExtensions.ReadPolicy)
            .WithTags("Platform Monitoring History (authorized)");

        group.MapGet("/records", async (
            ITelemetryHistoryReader reader,
            DateTimeOffset? from, DateTimeOffset? to,
            TelemetryKind? kind, string? source, string? name, string? traceId,
            bool? failed, bool? slow, int? page, int? pageSize,
            CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var end = to ?? now;
            var begin = from ?? end.AddHours(-1);
            var p = page ?? 1;
            var size = pageSize ?? 50;
            if (!ValidRange(begin, end, now) || p is < 1 or > 10000 ||
                size is < 1 or > 100 || (long)(p - 1) * size > 1_000_000 ||
                (traceId is not null && !ValidTraceId(traceId)) ||
                source is { Length: > 128 } || name is { Length: > 128 } ||
                (kind is { } selected && !Enum.IsDefined(selected)))
                return Results.BadRequest(new { code = "observability.invalid_query" });

            var data = await reader.SearchAsync(
                new TelemetryQuery(begin, end, kind, source, name, traceId,
                    failed, slow, p, size), ct);
            return Results.Ok(data);
        })
        .WithName("PlatformObservabilityRecords")
        .Produces<TelemetryPage<TelemetryItem>>()
        .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/traces/{traceId}", async (
            string traceId, ITelemetryHistoryReader reader,
            DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var end = to ?? now;
            var begin = from ?? end.AddHours(-24);
            if (!ValidRange(begin, end, now) || !ValidTraceId(traceId))
                return Results.BadRequest(new { code = "observability.invalid_query" });
            return Results.Ok(await reader.GetTraceAsync(traceId, begin, end, ct));
        })
        .WithName("PlatformObservabilityTrace")
        .Produces<TelemetryTraceResult>()
        .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/summary", async (
            ITelemetryHistoryReader reader, DateTimeOffset? from,
            DateTimeOffset? to, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var end = to ?? now;
            var begin = from ?? end.AddHours(-1);
            if (!ValidRange(begin, end, now))
                return Results.BadRequest(new { code = "observability.invalid_query" });
            return Results.Ok(await reader.GetSummaryAsync(begin, end, ct));
        })
        .WithName("PlatformObservabilityHistorySummary")
        .Produces<TelemetrySummary>()
        .Produces(StatusCodes.Status400BadRequest);

        return group;
    }

    private static bool ValidRange(DateTimeOffset from, DateTimeOffset to,
        DateTimeOffset now) => from <= to && to - from <= TimeSpan.FromDays(30) &&
                              from >= now.AddYears(-2) && to <= now.AddMinutes(5);

    private static bool ValidTraceId(string id) =>
        id.Length == 32 && id.All(Uri.IsHexDigit) &&
        !id.Equals(new string('0', 32), StringComparison.Ordinal);
}
