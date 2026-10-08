using System.Data.Common;
using System.Diagnostics;
using Application.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Infrastructure.SharedKernel.Observability;

/// <summary>
/// EF Core command timings. Explicitly NEVER logs command text, parameters,
/// connection strings, table names or database names.
/// </summary>
public sealed class DbTimingInterceptor(
    ILogger<DbTimingInterceptor> logger, PerformanceOptions options,
    IOperationTelemetrySink? sink = null) : DbCommandInterceptor
{
    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Observe(eventData, command.CommandType.ToString(), failed: false);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default)
    {
        Observe(eventData, command.CommandType.ToString(), failed: false);
        return new ValueTask<DbDataReader>(result);
    }

    public override int NonQueryExecuted(
        DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Observe(eventData, command.CommandType.ToString(), failed: false);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        Observe(eventData, command.CommandType.ToString(), failed: false);
        return new ValueTask<int>(result);
    }

    public override object? ScalarExecuted(
        DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Observe(eventData, command.CommandType.ToString(), failed: false);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result,
        CancellationToken cancellationToken = default)
    {
        Observe(eventData, command.CommandType.ToString(), failed: false);
        return new ValueTask<object?>(result);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
        => Observe(eventData, command.CommandType.ToString(), failed: true);

    public override Task CommandFailedAsync(DbCommand command,
        CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Observe(eventData, command.CommandType.ToString(), failed: true);
        return Task.CompletedTask;
    }

    private void Observe(CommandEndEventData data, string kind, bool failed)
    {
        var ms = data.Duration.TotalMilliseconds;
        var slow = ms >= options.SlowDatabaseThresholdMs;
        const string category = "database";

        FoundationMetrics.Duration.Record(ms,
            new KeyValuePair<string, object?>("operation.category", category),
            new KeyValuePair<string, object?>("operation.name", kind));
        FoundationMetrics.Executions.Add(1,
            new KeyValuePair<string, object?>("operation.category", category),
            new KeyValuePair<string, object?>("operation.status", failed ? "error" : "ok"));
        if (slow)
            logger.LogWarning("Slow EF command {CommandKind} took {ElapsedMs} ms (threshold {ThresholdMs} ms)",
                kind, ms, options.SlowDatabaseThresholdMs);

        Activity.Current?.AddEvent(new ActivityEvent("database.command", tags:
            new ActivityTagsCollection
            {
                { "db.command.kind", kind },
                { "db.command.duration_ms", ms },
                { "db.command.failed", failed }
            }));
        try
        {
            sink?.Record(new OperationObservation(DateTimeOffset.UtcNow,
                category, kind, ms, failed, slow, Activity.Current?.TraceId.ToString()));
        }
        catch (Exception ex)
        {
            logger.LogDebug("Telemetry sink unavailable: {ErrorType}", ex.GetType().Name);
        }
    }
}
