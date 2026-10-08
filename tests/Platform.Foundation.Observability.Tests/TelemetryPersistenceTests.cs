using System.Diagnostics;
using Application.SharedKernel.Observability;
using Infrastructure.Observability.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Platform.Foundation.Observability.Tests;

public sealed class TelemetryPersistenceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private const string Trace = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Sqlite_history_filters_pages_and_reconstructs_trace()
    {
        using var fixture = new DatabaseFixture();
        await fixture.Insert(
            Event(TelemetryKind.Operation, "cqrs", "CreateAccount", 15, trace: Trace),
            Event(TelemetryKind.Operation, "cqrs", "CreateAccount", 27, true, trace: Trace),
            Event(TelemetryKind.Log, "Modules.Identity", "log.event", trace: Trace),
            Event(TelemetryKind.Exception, "Modules.Identity", "InvalidOperationException",
                failed: true, trace: Trace),
            Event(TelemetryKind.Operation, "http", "GET /api/stats", 70));

        var reader = fixture.Reader;
        var page = await reader.SearchAsync(new TelemetryQuery(
            Now.AddHours(-1), Now.AddMinutes(1), TelemetryKind.Operation,
            Source: "cqrs", Page: 1, PageSize: 1));
        Assert.Equal(2, page.Total);
        Assert.Single(page.Items);
        Assert.Equal("CreateAccount", page.Items[0].Name);
        Assert.Equal(1, page.Page);
        Assert.Equal(1, page.PageSize);

        var trace = await reader.GetTraceAsync(Trace, Now.AddHours(-1), Now.AddMinutes(1));
        Assert.Equal(4, trace.Total);
        Assert.False(trace.Truncated);
        Assert.All(trace.Items, x => Assert.Equal(Trace, x.TraceId));

        var exceptions = await reader.SearchAsync(new TelemetryQuery(
            Now.AddHours(-1), Now.AddMinutes(1), TelemetryKind.Exception));
        Assert.Equal(1, exceptions.Total);
        Assert.Equal("InvalidOperationException", exceptions.Items[0].Name);
    }

    [Fact]
    public async Task Summary_returns_aggregates_and_percentiles()
    {
        using var fixture = new DatabaseFixture();
        await fixture.Insert(
            Event(TelemetryKind.Operation, "cqrs", "A", 10),
            Event(TelemetryKind.Operation, "cqrs", "A", 20),
            Event(TelemetryKind.Operation, "cqrs", "A", 30, true, failed: true),
            Event(TelemetryKind.Operation, "cqrs", "A", 100, true),
            Event(TelemetryKind.Log, "cqrs", "log.event", 999, true));

        var summary = await fixture.Reader.GetSummaryAsync(
            Now.AddHours(-1), Now.AddMinutes(1));
        Assert.Equal(4, summary.Total);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(2, summary.Slow);
        Assert.Equal(40, summary.AverageDurationMs);
        Assert.Equal(20, summary.P50DurationMs);
        Assert.Equal(100, summary.P95DurationMs);
        Assert.Equal(100, summary.P99DurationMs);
        Assert.False(summary.PercentilesSampled);
        Assert.Equal(4, Assert.Single(summary.Trend).Count);
    }

    [Fact]
    public async Task Invalid_search_range_pagination_and_trace_are_rejected()
    {
        using var fixture = new DatabaseFixture();
        var reader = fixture.Reader;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            reader.SearchAsync(new TelemetryQuery(Now.AddDays(-31), Now)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            reader.SearchAsync(new TelemetryQuery(Now.AddHours(-1), Now, PageSize: 101)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            reader.GetTraceAsync("INVALID", Now.AddHours(-1), Now));
    }

    [Fact]
    public async Task Retention_removes_only_expired_rows()
    {
        using var fixture = new DatabaseFixture();
        await fixture.Insert(
            Event(TelemetryKind.Operation, "cqrs", "Old", 1) with
            { Timestamp = Now.AddDays(-8) },
            Event(TelemetryKind.Operation, "cqrs", "Recent", 1));

        var worker = new TelemetryRetentionWorker(
            fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            new TelemetryCaptureOptions { RetentionDays = 7 },
            fixture.Clock,
            fixture.Services.GetRequiredService<ILogger<TelemetryRetentionWorker>>());
        Assert.Equal(1, await worker.SweepAsync());
        var records = await fixture.Reader.SearchAsync(new TelemetryQuery(
            Now.AddDays(-30), Now.AddMinutes(1)));
        Assert.Equal(1, records.Total);
        Assert.Equal("Recent", records.Items[0].Name);
    }

    [Fact]
    public void Channel_rejects_sensitive_strings_and_limits_capacity()
    {
        var options = new TelemetryCaptureOptions { QueueCapacity = 100 };
        var channel = new ChannelTelemetrySink(options);
        Assert.False(channel.TryRecord(Event(TelemetryKind.Log, "audit",
            "password=supersecret")));
        Assert.False(channel.TryRecord(Event(TelemetryKind.Trace, "cqrs", "Ok",
            trace: "not-a-trace-id")));
        for (var i = 0; i < 100; i++)
            Assert.True(channel.TryRecord(Event(TelemetryKind.Operation, "cqrs", "Safe")));
        Assert.False(channel.TryRecord(Event(TelemetryKind.Operation, "cqrs", "Overflow")));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TelemetryCaptureOptions { QueueCapacity = 1 }.Validate());
    }

    [Fact]
    public async Task Safe_logger_captures_metadata_without_message_or_exception_text()
    {
        using var fixture = new DatabaseFixture();
        var capture = new ChannelTelemetrySink(new TelemetryCaptureOptions());
        using var provider = new SafeTelemetryLoggerProvider(capture);
        var logger = provider.CreateLogger("Modules.Identity.Auth");
        logger.Log(LogLevel.Error, new EventId(42), "CREDENTIAL_SECRET",
            new InvalidOperationException("SENSITIVE_EXCEPTION_MESSAGE"),
            (s, e) => s);

        Assert.True(capture.TryRecord(Event(TelemetryKind.Operation, "cqrs", "Last")));
        // We cannot inspect raw buffered items from another assembly; flush through
        // the same public worker path against the SQLite history store.
        using var worker = new TelemetryPersistenceWorker(
            capture, fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            new TelemetryCaptureOptions(),
            fixture.Services.GetRequiredService<ILogger<TelemetryPersistenceWorker>>());
        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(async () => await fixture.Reader.SearchAsync(
            new TelemetryQuery(Now.AddHours(-1), DateTimeOffset.UtcNow.AddMinutes(1),
                PageSize: 20)) is { Total: >= 3 }, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        var items = await fixture.Reader.SearchAsync(new TelemetryQuery(
            Now.AddHours(-1), DateTimeOffset.UtcNow.AddMinutes(1)));
        Assert.Contains(items.Items, x => x.Kind == TelemetryKind.Log);
        Assert.Contains(items.Items, x => x.Kind == TelemetryKind.Exception);
        Assert.DoesNotContain(items.Items, x => x.Name.Contains("CREDENTIAL_SECRET"));
        Assert.DoesNotContain(items.Items, x => x.Name.Contains("SENSITIVE_EXCEPTION_MESSAGE"));
    }

    [Fact]
    public async Task Worker_flushes_enqueued_operations_to_sqlite()
    {
        using var fixture = new DatabaseFixture();
        var channel = new ChannelTelemetrySink(new TelemetryCaptureOptions());
        var live = new Infrastructure.Observability.Storage.BoundedObservabilityStore(
            10, fixture.Clock);
        var bridge = new PersistentOperationTelemetrySink(live, channel);
        bridge.Record(new OperationObservation(Now, "cqrs", "CreateAccount",
            31, false, true, Trace));

        using var worker = new TelemetryPersistenceWorker(
            channel, fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            new TelemetryCaptureOptions(),
            fixture.Services.GetRequiredService<ILogger<TelemetryPersistenceWorker>>());
        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(async () => await fixture.Reader.SearchAsync(
            new TelemetryQuery(Now.AddMinutes(-1), Now.AddMinutes(1))) is { Total: 1 },
            TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, live.GetSnapshot(TimeSpan.FromDays(1)).Total);
        var result = await fixture.Reader.SearchAsync(
            new TelemetryQuery(Now.AddMinutes(-1), Now.AddMinutes(1)));
        Assert.Single(result.Items);
        Assert.Equal(31, result.Items[0].DurationMs);
    }

    [Fact]
    public void Sql_server_initial_migration_generates_expected_indexes()
    {
        var options = new DbContextOptionsBuilder<TelemetryDbContext>()
            .UseSqlServer("Server=localhost;Database=OfflineTelemetrySchema;User Id=sa;Password=PlaceholderOnly123!;TrustServerCertificate=True")
            .Options;
        using var db = new TelemetryDbContext(options);
        var sql = db.GetService<IMigrator>().GenerateScript();
        Assert.Contains("CREATE TABLE [PlatformTelemetryEntries]", sql);
        Assert.Contains("IX_PlatformTelemetryEntries_TraceId_TimestampUtc", sql);
        Assert.Contains("IX_PlatformTelemetryEntries_Kind_TimestampUtc", sql);
    }

    private static TelemetryEvent Event(
        TelemetryKind kind, string source, string name,
        double? duration = null, bool slow = false,
        bool failed = false, string? trace = null)
        => new(Now, kind, source, name, DurationMs: duration,
            Failed: failed, Slow: slow, TraceId: trace);

    private static async Task WaitUntilAsync(Func<Task<bool>> check, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            if (await check()) return;
            try { await Task.Delay(50, cts.Token); }
            catch (OperationCanceledException) { break; }
        }
        Assert.Fail("Background telemetry writer did not flush within the test deadline.");
    }

    private sealed class FakeClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class DatabaseFixture : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public ServiceProvider Services { get; }
        public FakeClock Clock { get; } = new();
        public SqlTelemetryHistoryReader Reader
        {
            get
            {
                var db = Services.GetRequiredService<TelemetryDbContext>();
                return new SqlTelemetryHistoryReader(db, Clock);
            }
        }

        public DatabaseFixture()
        {
            connection.Open();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddDbContext<TelemetryDbContext>(o => o.UseSqlite(connection));
            Services = services.BuildServiceProvider();
            Services.GetRequiredService<TelemetryDbContext>().Database.EnsureCreated();
        }

        public async Task Insert(params TelemetryEvent[] values)
        {
            var db = Services.GetRequiredService<TelemetryDbContext>();
            db.Entries.AddRange(values.Select(TelemetryEntry.From));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            Services.Dispose();
            connection.Dispose();
        }
    }
}
