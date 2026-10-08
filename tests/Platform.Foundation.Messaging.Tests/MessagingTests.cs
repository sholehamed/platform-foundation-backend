using Application.SharedKernel;
using Application.SharedKernel.Abstractions.Messaging;
using Domain.SharedKernel.Common.Events;
using Infrastructure.Messaging.Configuration;
using Infrastructure.Messaging.Model;
using Infrastructure.Messaging.Persistence;
using Infrastructure.Messaging.Processing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Platform.Foundation.Messaging.Tests;

public sealed class MessagingTests
{
    [Fact]
    public async Task Publish_stages_records_but_does_not_commit_implicitly()
    {
        using var test = new Fixture();
        await using (var scope = test.Services.CreateAsyncScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IMessagePublisher>();
            await publisher.PublishAsync(new CustomerCreated("alice"));
        }

        await using var scope2 = test.Services.CreateAsyncScope();
        var context = scope2.ServiceProvider.GetRequiredService<MessagingTestDbContext>();
        Assert.Empty(await context.Set<OutboxMessage>().ToListAsync());
    }

    [Fact]
    public async Task Durable_message_is_fanned_out_once_per_subscriber()
    {
        using var test = new Fixture();
        await using (var scope = test.Services.CreateAsyncScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IMessagePublisher>();
            var db = scope.ServiceProvider.GetRequiredService<MessagingTestDbContext>();
            var id = await publisher.PublishAsync(new CustomerCreated("alice"));
            await db.SaveChangesAsync();
            Assert.Equal(2, await db.Set<OutboxDelivery>().CountAsync(x => x.MessageId == id));

            await scope.ServiceProvider.GetRequiredService<OutboxWorker<MessagingTestDbContext>>()
                .RunAsync(CancellationToken.None);
        }

        Assert.Equal(new[] { "first:alice", "second:alice" }, test.Probe.Events);
        await using var verify = test.Services.CreateAsyncScope();
        var database = verify.ServiceProvider.GetRequiredService<MessagingTestDbContext>();
        Assert.Equal(2, await database.Set<OutboxDelivery>()
            .CountAsync(x => x.Status == DeliveryStatus.Completed));
    }

    [Fact]
    public async Task Rolled_back_business_transaction_also_rolls_back_message()
    {
        using var test = new Fixture();
        await using (var scope = test.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MessagingTestDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.Set<Customer>().Add(new Customer { Id = Guid.NewGuid(), Name = "bob" });
            await scope.ServiceProvider.GetRequiredService<IMessagePublisher>()
                .PublishAsync(new CustomerCreated("bob"));
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var verify = test.Services.CreateAsyncScope();
        var context = verify.ServiceProvider.GetRequiredService<MessagingTestDbContext>();
        Assert.Empty(await context.Set<Customer>().ToListAsync());
        Assert.Empty(await context.Set<OutboxMessage>().ToListAsync());
        Assert.Empty(await context.Set<OutboxDelivery>().ToListAsync());
    }

    [Fact]
    public async Task Retry_and_deadletter_are_independent_per_subscriber()
    {
        using var test = new Fixture(options => { options.MaxAttempts = 2; options.BaseRetryDelay = TimeSpan.FromSeconds(5); });
        test.Probe.Fail = true;
        Guid id;
        await using (var scope = test.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MessagingTestDbContext>();
            id = await scope.ServiceProvider.GetRequiredService<IMessagePublisher>()
                .PublishAsync(new FlakyMessage("one"));
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<OutboxWorker<MessagingTestDbContext>>()
                .RunAsync(CancellationToken.None);
        }

        await using (var inspect = test.Services.CreateAsyncScope())
        {
            var db = inspect.ServiceProvider.GetRequiredService<MessagingTestDbContext>();
            var receipt = await db.Set<OutboxDelivery>().SingleAsync(x => x.MessageId == id);
            Assert.Equal(1, receipt.Attempts);
            Assert.Equal(DeliveryStatus.Pending, receipt.Status);
        }

        test.Clock.Now = test.Clock.Now.AddMinutes(2);
        await using (var scope = test.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<OutboxWorker<MessagingTestDbContext>>()
                .RunAsync(CancellationToken.None);
        }

        Guid deliveryId;
        await using (var inspect = test.Services.CreateAsyncScope())
        {
            var db = inspect.ServiceProvider.GetRequiredService<MessagingTestDbContext>();
            var receipt = await db.Set<OutboxDelivery>().SingleAsync(x => x.MessageId == id);
            Assert.Equal(2, receipt.Attempts);
            Assert.Equal(DeliveryStatus.DeadLetter, receipt.Status);
            Assert.Equal(nameof(InvalidOperationException), receipt.LastErrorType);
            deliveryId = receipt.Id;
            Assert.True(await inspect.ServiceProvider.GetRequiredService<IMessageDeliveryAdmin>()
                .RetryDeadLetterAsync(deliveryId));
        }

        test.Probe.Fail = false;
        await using (var scope = test.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<OutboxWorker<MessagingTestDbContext>>()
                .RunAsync(CancellationToken.None);
        }

        await using var verify = test.Services.CreateAsyncScope();
        var last = await verify.ServiceProvider.GetRequiredService<MessagingTestDbContext>()
            .Set<OutboxDelivery>().SingleAsync(x => x.Id == deliveryId);
        Assert.Equal(DeliveryStatus.Completed, last.Status);
        Assert.Equal(1, last.Attempts);
    }

    [Fact]
    public async Task Expired_lease_is_recovered()
    {
        using var test = new Fixture();
        await using (var scope = test.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MessagingTestDbContext>();
            await scope.ServiceProvider.GetRequiredService<IMessagePublisher>()
                .PublishAsync(new CustomerCreated("lease"));
            await db.SaveChangesAsync();
            foreach (var delivery in await db.Set<OutboxDelivery>().ToListAsync())
            {
                delivery.Status = DeliveryStatus.Processing;
                delivery.LeaseExpiresAt = test.Clock.Now.UtcDateTime.AddSeconds(-1);
                delivery.LeaseToken = Guid.NewGuid();
            }
            await db.SaveChangesAsync();
        }

        await using (var scope = test.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<OutboxWorker<MessagingTestDbContext>>()
                .RunAsync(CancellationToken.None);
        }
        Assert.Equal(2, test.Probe.Events.Count);
    }

    [Fact]
    public async Task Immediate_notification_stays_in_process()
    {
        using var test = new Fixture();
        await using var scope = test.Services.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<INotificationPublisher>();

        await publisher.PublishAsync(new PingNotification());

        Assert.Equal(new[] { "ping" }, test.Probe.Events);
        var db = scope.ServiceProvider.GetRequiredService<MessagingTestDbContext>();
        Assert.Empty(await db.Set<OutboxMessage>().ToListAsync());
    }

    [Fact]
    public async Task Unknown_message_is_rejected_before_persisting()
    {
        using var test = new Fixture();
        await using var scope = test.Services.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IMessagePublisher>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.PublishAsync(new UnknownMessage()));
    }

    [Fact]
    public async Task Legacy_dispatcher_publish_uses_registered_notification_publisher()
    {
        using var test = new Fixture();
        await using var scope = test.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IDispatcher>()
            .Publish(new PingNotification());

        Assert.Equal(new[] { "ping" }, test.Probe.Events);
    }

    [Fact]
    public void Standalone_sql_server_migration_is_discoverable()
    {
        using var test = new Fixture();
        using var scope = test.Services.CreateScope();

        var migrations = scope.ServiceProvider
            .GetRequiredService<MessagingTestDbContext>()
            .Database.GetMigrations();

        // Business DbContext migrations are module-owned; the standalone
        // PlatformMessagingDbContext has the shipped baseline migration.
        Assert.Empty(migrations);

        var standaloneOptions = new DbContextOptionsBuilder<PlatformMessagingDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var standalone = new PlatformMessagingDbContext(standaloneOptions);
        Assert.Contains("20261008220000_InitialMessagingOutbox", standalone.Database.GetMigrations());
    }

    [Fact]
    public async Task Persisted_delivery_survives_a_complete_service_provider_restart()
    {
        var databasePath = Path.Combine(Path.GetTempPath(),
            "platform-messaging-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var firstProbe = new Probe();
            using (var first = CreateFileProvider(databasePath, firstProbe))
            {
                await using var scope = first.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<MessagingTestDbContext>();
                await db.Database.EnsureCreatedAsync();
                await scope.ServiceProvider.GetRequiredService<IMessagePublisher>()
                    .PublishAsync(new CustomerCreated("restart"));
                await db.SaveChangesAsync();
            }

            // A completely new ServiceProvider opens the same durable SQLite store.
            var secondProbe = new Probe();
            using (var second = CreateFileProvider(databasePath, secondProbe))
            {
                await using var scope = second.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<OutboxWorker<MessagingTestDbContext>>()
                    .RunAsync(CancellationToken.None);
                var db = scope.ServiceProvider.GetRequiredService<MessagingTestDbContext>();
                Assert.Equal(2, await db.Set<OutboxDelivery>()
                    .CountAsync(x => x.Status == DeliveryStatus.Completed));
            }

            Assert.Equal(new[] { "first:restart", "second:restart" }, secondProbe.Events);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    private static ServiceProvider CreateFileProvider(string path, Probe probe)
    {
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddSingleton<TimeProvider>(new TestClock());
        services.AddDbContext<MessagingTestDbContext>(o =>
            o.UseSqlite($"Data Source={path}"));
        services.AddPlatformMessaging<MessagingTestDbContext>(
            null, typeof(CustomerCreatedHandler1).Assembly);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public ServiceProvider Services { get; }
        public Probe Probe { get; } = new();
        public TestClock Clock { get; } = new();

        public Fixture(Action<MessagingOptions>? configure = null)
        {
            connection.Open();
            var services = new ServiceCollection();
            services.AddSingleton(Probe);
            services.AddCustomCqrs(typeof(PingHandler).Assembly);
            services.AddSingleton<TimeProvider>(Clock);
            services.AddDbContext<MessagingTestDbContext>(o => o.UseSqlite(connection));
            services.AddPlatformMessaging<MessagingTestDbContext>(
                configure,
                typeof(CustomerCreatedHandler1).Assembly);

            Services = services.BuildServiceProvider(validateScopes: true);
            using var scope = Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<MessagingTestDbContext>().Database.EnsureCreated();
        }

        public void Dispose()
        {
            Services.Dispose();
            connection.Dispose();
        }
    }

    public sealed class MessagingTestDbContext(DbContextOptions<MessagingTestDbContext> options)
        : DbContext(options)
    {
        public DbSet<Customer> Customers => Set<Customer>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>().HasKey(x => x.Id);
            modelBuilder.AddMessagingOutbox();
        }
    }

    public sealed class Customer
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
    }

    public sealed class Probe
    {
        public bool Fail { get; set; }
        public List<string> Events { get; } = [];
    }

    public sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } =
            new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [MessageContract("test.customer.created.v1")]
    public sealed record CustomerCreated(string Name) : IMessage;

    public sealed class CustomerCreatedHandler1(Probe probe) : IMessageHandler<CustomerCreated>
    {
        public Task HandleAsync(CustomerCreated message, CancellationToken ct)
        {
            probe.Events.Add("first:" + message.Name);
            return Task.CompletedTask;
        }
    }

    public sealed class CustomerCreatedHandler2(Probe probe) : IMessageHandler<CustomerCreated>
    {
        public Task HandleAsync(CustomerCreated message, CancellationToken ct)
        {
            probe.Events.Add("second:" + message.Name);
            return Task.CompletedTask;
        }
    }

    [MessageContract("test.flaky.v1")]
    public sealed record FlakyMessage(string Name) : IMessage;

    public sealed class FlakyHandler(Probe probe) : IMessageHandler<FlakyMessage>
    {
        public Task HandleAsync(FlakyMessage message, CancellationToken ct)
        {
            if (probe.Fail) throw new InvalidOperationException("transient");
            probe.Events.Add("flaky:" + message.Name);
            return Task.CompletedTask;
        }
    }

    public sealed record UnknownMessage : IMessage;
    public sealed record MissingContractMessage : IMessage;

    // Abstract types are deliberately excluded from assembly scanning.
    private abstract class MissingContractHandler : IMessageHandler<MissingContractMessage>
    {
        public abstract Task HandleAsync(MissingContractMessage message, CancellationToken token);
    }

    public sealed record PingNotification : INotification;

    public sealed class PingHandler(Probe probe) : INotificationHandler<PingNotification>
    {
        public Task Handle(PingNotification notification, CancellationToken ct)
        {
            probe.Events.Add("ping");
            return Task.CompletedTask;
        }
    }
}
