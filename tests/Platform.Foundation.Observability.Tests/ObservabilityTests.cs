using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Application.SharedKernel;
using Application.SharedKernel.Abstractions.Messaging;
using Application.SharedKernel.Observability;
using Domain.SharedKernel.Common.Events;
using Infrastructure.Messaging.Configuration;
using Infrastructure.Observability.Storage;
using Infrastructure.SharedKernel.Observability;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Web.SharedKernel.Observability;
using Xunit;

namespace Platform.Foundation.Observability.Tests;

public sealed class ObservabilityTests
{
    [Fact]
    public void Ring_buffer_is_bounded_and_snapshot_calculates_percentiles()
    {
        var clock = new FakeClock();
        var buffer = new BoundedObservabilityStore(3, clock);
        buffer.Record(new(clock.GetUtcNow(), "cqrs", "A", 10, false, false, "trace-a"));
        buffer.Record(new(clock.GetUtcNow(), "cqrs", "A", 20, true, false, "trace-b"));
        buffer.Record(new(clock.GetUtcNow(), "cqrs", "A", 30, false, true, "trace-c"));
        buffer.Record(new(clock.GetUtcNow(), "message", "B", 50, false, true, "trace-d"));

        var data = buffer.GetSnapshot(TimeSpan.FromMinutes(15), recentLimit: 2);
        Assert.Equal(3, data.Total);
        Assert.Equal(1, data.Failed);
        Assert.Equal(2, data.Slow);
        Assert.Equal(50, data.P95DurationMs);
        Assert.Equal(2, data.Recent.Count);
        Assert.Equal(2, data.Operations.Count);
        Assert.DoesNotContain(data.Recent, x => x.TraceId == "trace-a");
    }

    [Fact]
    public void Invalid_reader_window_or_options_are_rejected()
    {
        var clock = new FakeClock();
        var buffer = new BoundedObservabilityStore(2, clock);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            buffer.GetSnapshot(TimeSpan.FromDays(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            buffer.GetSnapshot(TimeSpan.FromMinutes(1), 201));
        var options = new PerformanceOptions { SlowRequestThresholdMs = -1 };
        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Fact]
    public async Task Slow_command_is_counted_and_warned_without_logging_payload()
    {
        var clock = new FakeClock();
        var buffer = new BoundedObservabilityStore(30, clock);
        using var logs = new MemoryLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(logs));
        services.AddCustomCqrs(typeof(ObservabilityTests).Assembly);
        services.AddFoundationPerformance(options => options.SlowRequestThresholdMs = 0);
        services.AddSingleton<IOperationTelemetrySink>(buffer);
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
            .Send(new TraceCommand("SECRET_DO_NOT_LOG"));
        Assert.Equal("ok", result);

        var snap = buffer.GetSnapshot(TimeSpan.FromHours(1));
        Assert.Equal(1, snap.Total);
        Assert.True(snap.Slow > 0);
        Assert.Equal("TraceCommand", Assert.Single(snap.Operations).Name);
        Assert.Contains(logs.Lines, x => x.Level == LogLevel.Warning &&
            x.Message.Contains("Slow CQRS operation"));
        Assert.DoesNotContain(logs.Lines, x => x.Message.Contains("SECRET_DO_NOT_LOG"));
    }

    [Fact]
    public async Task Notification_performance_is_collected_by_messaging_pipeline()
    {
        var buffer = new BoundedObservabilityStore(20, new FakeClock());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<Db>(o => o.UseSqlite("Data Source=:memory:"));
        services.AddSingleton<IOperationTelemetrySink>(buffer);
        services.AddPlatformMessaging<Db>(null, typeof(ObservabilityTests).Assembly);
        services.AddFoundationPerformance(o => o.SlowNotificationThresholdMs = 0);
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<INotificationPublisher>()
            .PublishAsync(new TraceNotification());

        Assert.Contains(buffer.GetSnapshot(TimeSpan.FromHours(1)).Operations,
            x => x.Category == "notification" && x.Name == nameof(TraceNotification));
    }

    [Fact]
    public async Task Database_interceptor_records_timing_without_sql_or_parameters()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var buffer = new BoundedObservabilityStore(100, new FakeClock());
        using var logs = new MemoryLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(logs));
        var options = new PerformanceOptions { SlowDatabaseThresholdMs = 0 };
        var interceptor = new DbTimingInterceptor(loggerFactory.CreateLogger<DbTimingInterceptor>(),
            options, buffer);
        var dbOptions = new DbContextOptionsBuilder<Db>()
            .UseSqlite(connection).AddInterceptors(interceptor).Options;
        await using var db = new Db(dbOptions);
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE PrivateSample(Id INTEGER, Secret TEXT)");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO PrivateSample (Id,Secret) VALUES ({42},{"PASSWORD_DO_NOT_LOG"})");

        var snapshot = buffer.GetSnapshot(TimeSpan.FromHours(1));
        Assert.Contains(snapshot.Operations, x => x.Category == "database");
        Assert.Contains(logs.Lines, x => x.Level == LogLevel.Warning &&
            x.Message.Contains("Slow EF command"));
        Assert.DoesNotContain(logs.Lines, x => x.Message.Contains("PASSWORD_DO_NOT_LOG"));
        Assert.DoesNotContain(logs.Lines, x => x.Message.Contains("PrivateSample"));
    }

    [Theory]
    [InlineData("allowed", HttpStatusCode.OK)]
    [InlineData("no-permission", HttpStatusCode.Forbidden)]
    [InlineData("", HttpStatusCode.Unauthorized)]
    public async Task Angular_read_endpoint_requires_explicit_permission(
        string identity, HttpStatusCode expected)
    {
        using var server = CreateServer();
        using var client = server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "/api/platform/observability/snapshot?minutes=15&limit=5");
        if (identity.Length > 0)
            request.Headers.Add("X-Test-Identity", identity);

        using var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("averageDurationMs", body);
            Assert.DoesNotContain("PASSWORD_DO_NOT_LOG", body);
        }
    }

    [Fact]
    public async Task Correlation_header_is_preserved_only_when_safe()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/echo");
        req.Headers.Add(CorrelationMiddlewareExtensions.HeaderName, "frontend-123");
        using var good = await client.SendAsync(req);
        Assert.Equal("frontend-123",
            good.Headers.GetValues(CorrelationMiddlewareExtensions.HeaderName).Single());

        using var badRequest = new HttpRequestMessage(HttpMethod.Get, "/echo");
        badRequest.Headers.Add(CorrelationMiddlewareExtensions.HeaderName, new string('x', 90));
        using var bad = await client.SendAsync(badRequest);
        var id = bad.Headers.GetValues(CorrelationMiddlewareExtensions.HeaderName).Single();
        Assert.NotEqual(new string('x', 90), id);
        Assert.Equal(32, id.Length);
    }

    private static TestServer CreateServer()
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton<IObservabilityReader>(
                    new BoundedObservabilityStore(10, TimeProvider.System));
                services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    "Test", _ => { });
                services.AddPlatformObservabilityReadAuthorization();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UsePlatformCorrelation();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(routes =>
                {
                    routes.MapPlatformObservabilityReadApi();
                    routes.MapGet("/echo", () => Results.Ok());
                });
            });
        return new TestServer(builder);
    }

    public sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(
            options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = Request.Headers["X-Test-Identity"].ToString();
            if (string.IsNullOrEmpty(identity))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "test") };
            if (identity == "allowed")
                claims.Add(new Claim("permission", "platform.observability.read"));
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    public sealed class FakeClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;
    }

    public sealed record TraceCommand(string Secret) : ICommand<string>;
    public sealed class TraceCommandHandler : ICommandHandler<TraceCommand, string>
    {
        public Task<string> Handle(TraceCommand request, CancellationToken token)
            => Task.FromResult("ok");
    }

    public sealed record TraceNotification : INotification;
    public sealed class TraceNotificationHandler : INotificationHandler<TraceNotification>
    {
        public Task Handle(TraceNotification notification, CancellationToken token)
            => Task.CompletedTask;
    }

    public sealed class Db(DbContextOptions<Db> options) : DbContext(options);

    public sealed class MemoryLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = [];
        public ILogger CreateLogger(string categoryName) => new MemoryLogger(Lines);
        public void Dispose() { }
        private sealed class MemoryLogger(List<(LogLevel Level, string Message)> lines) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull
                => Scope.Instance;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
                => lines.Add((level, formatter(state, exception)));
            private sealed class Scope : IDisposable
            {
                public static Scope Instance { get; } = new();
                public void Dispose() { }
            }
        }
    }
}
