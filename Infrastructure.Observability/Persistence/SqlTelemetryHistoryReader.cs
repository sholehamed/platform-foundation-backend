using Application.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Observability.Persistence;

/// <summary>Read-only, bounded, filterable SQL history for the authorized Angular API.</summary>
public sealed class SqlTelemetryHistoryReader(TelemetryDbContext db, TimeProvider clock)
    : ITelemetryHistoryReader
{
    public async Task<TelemetryPage<TelemetryItem>> SearchAsync(
        TelemetryQuery query, CancellationToken cancellationToken = default)
    {
        ValidateRange(query.From, query.To);
        if (query.Page is < 1 or > 10000 || query.PageSize is < 1 or > 100 ||
            (long)(query.Page - 1) * query.PageSize > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(query), "Invalid pagination.");
        if (!string.IsNullOrEmpty(query.Source) && query.Source.Length > 128 ||
            !string.IsNullOrEmpty(query.Name) && query.Name.Length > 128)
            throw new ArgumentOutOfRangeException(nameof(query));
        if (query.TraceId is not null && !ValidTraceId(query.TraceId))
            throw new ArgumentException("Invalid trace ID.", nameof(query));

        var source = Range(query.From, query.To);
        if (query.Kind is { } kind) source = source.Where(x => x.Kind == kind);
        if (!string.IsNullOrWhiteSpace(query.Source))
            source = source.Where(x => x.Source == query.Source);
        if (!string.IsNullOrWhiteSpace(query.Name))
            source = source.Where(x => x.Name == query.Name);
        if (query.TraceId is { } traceId)
            source = source.Where(x => x.TraceId == traceId);
        if (query.Failed is { } failed)
            source = source.Where(x => x.Failed == failed);
        if (query.Slow is { } slow)
            source = source.Where(x => x.Slow == slow);

        var total = await source.LongCountAsync(cancellationToken);
        var records = await source.OrderByDescending(x => x.TimestampUtc)
            .ThenByDescending(x => x.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize).ToListAsync(cancellationToken);
        return new TelemetryPage<TelemetryItem>(
            records.Select(x => x.ToContract()).ToArray(), total, query.Page, query.PageSize);
    }

    public async Task<IReadOnlyList<TelemetryItem>> GetTraceAsync(
        string traceId, DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        ValidateRange(from, to);
        if (!ValidTraceId(traceId))
            throw new ArgumentException("Invalid trace ID.", nameof(traceId));

        return (await Range(from, to).Where(x => x.TraceId == traceId)
            .OrderBy(x => x.TimestampUtc).ThenBy(x => x.Id).Take(500)
            .ToListAsync(cancellationToken))
            .Select(x => x.ToContract()).ToArray();
    }

    public async Task<TelemetrySummary> GetSummaryAsync(
        DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        ValidateRange(from, to);
        var query = Range(from, to).Where(x => x.Kind == TelemetryKind.Operation);
        var total = await query.LongCountAsync(cancellationToken);
        var failed = await query.LongCountAsync(x => x.Failed, cancellationToken);
        var slow = await query.LongCountAsync(x => x.Slow, cancellationToken);
        var average = total == 0 ? 0d :
            await query.AverageAsync(x => x.DurationMs ?? 0, cancellationToken);
        // Bounded sample: do not materialize millions of SQL rows into memory.
        const int sampleLimit = 10000;
        var samples = await query.OrderByDescending(x => x.TimestampUtc)
            .Take(sampleLimit).Select(x => new { x.TimestampUtc, x.DurationMs, x.Failed, x.Slow })
            .ToArrayAsync(cancellationToken);

        var durations = samples.Where(x => x.DurationMs.HasValue)
            .Select(x => x.DurationMs!.Value).OrderBy(x => x).ToArray();
        static double Percentile(double[] values, double percentile) =>
            values.Length == 0 ? 0 :
            Math.Round(values[Math.Max(0, (int)Math.Ceiling(values.Length * percentile) - 1)], 2);

        var trend = samples.GroupBy(x => new DateTime(
                x.TimestampUtc.Year, x.TimestampUtc.Month, x.TimestampUtc.Day,
                x.TimestampUtc.Hour, x.TimestampUtc.Minute, 0, DateTimeKind.Utc))
            .OrderBy(x => x.Key)
            .Select(x => new TelemetryTrendBucket(
                new DateTimeOffset(x.Key), x.LongCount(), x.LongCount(y => y.Failed),
                x.LongCount(y => y.Slow)))
            .ToArray();

        return new TelemetrySummary(from, to, total, failed, slow,
            Math.Round(average, 2),
            Percentile(durations, .50), Percentile(durations, .95),
            Percentile(durations, .99), total > sampleLimit, trend);
    }

    private IQueryable<TelemetryEntry> Range(DateTimeOffset from, DateTimeOffset to)
    {
        var start = from.UtcDateTime;
        var end = to.UtcDateTime;
        return db.Entries.AsNoTracking().Where(x => x.TimestampUtc >= start && x.TimestampUtc <= end);
    }

    private void ValidateRange(DateTimeOffset from, DateTimeOffset to)
    {
        if (from > to || to - from > TimeSpan.FromDays(30) ||
            to > clock.GetUtcNow().AddMinutes(5) || from < clock.GetUtcNow().AddYears(-2))
            throw new ArgumentOutOfRangeException(nameof(from), "Range exceeds policy.");
    }

    public static bool ValidTraceId(string? id) =>
        id is { Length: 32 } && id.All(c => Uri.IsHexDigit(c)) &&
        !string.Equals(id, new string('0', 32), StringComparison.Ordinal);
}
