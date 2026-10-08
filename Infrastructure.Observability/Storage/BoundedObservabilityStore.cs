using System.Collections.Concurrent;
using Application.SharedKernel.Observability;

namespace Infrastructure.Observability.Storage;

/// <summary>
/// Thread-safe bounded in-process telemetry sample store for the Angular read API.
/// Deliberately volatile: multi-instance aggregation and retention need a persistent adapter.
/// </summary>
public sealed class BoundedObservabilityStore(int capacity, TimeProvider clock)
    : IOperationTelemetrySink, IObservabilityReader
{
    private readonly ConcurrentQueue<OperationObservation> queue = new();
    private int count;

    public int Capacity { get; } = capacity is >= 1 and <= 50000
        ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));

    public void Record(OperationObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        // Safety guard, even for custom instrumentation callers.
        if (string.IsNullOrWhiteSpace(observation.Category) ||
            string.IsNullOrWhiteSpace(observation.Name) ||
            observation.Category.Length > 32 || observation.Name.Length > 128 ||
            !double.IsFinite(observation.DurationMs) || observation.DurationMs < 0)
            return;

        queue.Enqueue(observation);
        Interlocked.Increment(ref count);
        while (Volatile.Read(ref count) > Capacity && queue.TryDequeue(out _))
            Interlocked.Decrement(ref count);
    }

    public ObservabilitySnapshot GetSnapshot(TimeSpan window, int recentLimit = 50)
    {
        if (window < TimeSpan.FromMinutes(1) || window > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(window));
        if (recentLimit is < 0 or > 200)
            throw new ArgumentOutOfRangeException(nameof(recentLimit));

        var now = clock.GetUtcNow();
        var cutoff = now - window;
        var events = queue.ToArray()
            .Where(x => x.Timestamp >= cutoff && x.Timestamp <= now)
            .OrderByDescending(x => x.Timestamp)
            .ToArray();

        var aggregate = events
            .GroupBy(x => (x.Category, x.Name))
            .Select(group => new OperationAggregate(
                group.Key.Category, group.Key.Name,
                group.LongCount(), group.LongCount(x => x.Failed),
                group.LongCount(x => x.Slow),
                Math.Round(group.Average(x => x.DurationMs), 2),
                Percentile(group.Select(x => x.DurationMs))))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Category, StringComparer.Ordinal)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ToArray();

        return new ObservabilitySnapshot(
            now, cutoff, events.LongLength,
            events.LongCount(x => x.Failed),
            events.LongCount(x => x.Slow),
            events.Length == 0 ? 0 : Math.Round(events.Average(x => x.DurationMs), 2),
            Percentile(events.Select(x => x.DurationMs)),
            aggregate,
            events.Take(recentLimit).ToArray());
    }

    private static double Percentile(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(x => x).ToArray();
        if (sorted.Length == 0) return 0;
        var index = (int)Math.Ceiling(sorted.Length * .95) - 1;
        return Math.Round(sorted[Math.Max(index, 0)], 2);
    }
}
