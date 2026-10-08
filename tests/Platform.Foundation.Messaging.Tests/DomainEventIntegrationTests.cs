using System.Diagnostics;
using Application.SharedKernel.Abstractions.Messaging;
using Domain.SharedKernel.Common.Events;
using Infrastructure.Messaging.Configuration;
using Infrastructure.Messaging.Model;
using Infrastructure.Messaging.Persistence;
using Infrastructure.Messaging.Processing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Platform.Foundation.Messaging.Tests;

public sealed class DomainEventIntegrationTests
{
    [Fact]
    public async Task Domain_event_runs_before_save_and_stages_durable_message_atomically()
    {
        using var test = new Fixture();
        var id = Guid.NewGuid();
        await using (var scope = test.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EventDb>();
            db.Orders.Add(EventOrder.Create(id, "sample"));
            await db.SaveChangesAsync();
            Assert.Equal(new[] { "before:sample" }, test.Probe.Steps);

            var outbox = await db.Set<OutboxMessage>().SingleAsync();
            Assert.Equal("test.domain.order.created.v1", outbox.Contract);
            Assert.Single(await db.Set<OutboxDelivery>().ToListAsync());
            Assert.Empty(db.Orders.Local.Single().DomainEvents);
        }

        await using (var scope = test.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<OutboxWorker<EventDb>>()
                .RunAsync(CancellationToken.None);
        }
        Assert.Equal(new[] { "before:sample", "durable:sample" }, test.Probe.Steps);
    }

    [Fact]
    public async Task Outer_transaction_rollback_discards_both_business_state_and_durable_event()
    {
        using var test = new Fixture();
        await using (var scope = test.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EventDb>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.Orders.Add(EventOrder.Create(Guid.NewGuid(), "rolled-back"));
            await db.SaveChangesAsync();
            Assert.Single(await db.Set<OutboxMessage>().ToListAsync());
            await transaction.RollbackAsync();
        }

        await using var verify = test.Services.CreateAsyncScope();
        var context = verify.ServiceProvider.GetRequiredService<EventDb>();
        Assert.Empty(await context.Orders.AsNoTracking().ToListAsync());
        Assert.Empty(await context.Set<OutboxMessage>().AsNoTracking().ToListAsync());
        Assert.Empty(await context.Set<OutboxDelivery>().AsNoTracking().ToListAsync());
        Assert.DoesNotContain(test.Probe.Steps, x => x.StartsWith("durable:"));
    }

    [Fact]
    public async Task Before_commit_failure_preserves_domain_events_without_staging_outbox()
    {
        using var test = new Fixture();
        test.Probe.ThrowBeforeCommit = true;
        await using var scope = test.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EventDb>();
        var order = EventOrder.Create(Guid.NewGuid(), "reject");
        db.Orders.Add(order);

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Single(order.DomainEvents);
        Assert.Empty(db.Set<OutboxMessage>().Local);
        Assert.Empty(db.Set<OutboxDelivery>().Local);
        Assert.Empty(await db.Set<OutboxMessage>().AsNoTracking().ToListAsync());

        test.Probe.ThrowBeforeCommit = false;
        await db.SaveChangesAsync();
        Assert.Single(await db.Set<OutboxMessage>().AsNoTracking().ToListAsync());
        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public async Task Failed_database_save_detaches_staged_outbox_and_can_retry_without_duplicates()
    {
        using var test = new Fixture();
        await using var scope = test.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EventDb>();
        db.Orders.Add(EventOrder.Create(Guid.NewGuid(), "duplicate"));
        await db.SaveChangesAsync();

        var other = EventOrder.Create(Guid.NewGuid(), "duplicate");
        db.Orders.Add(other);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.Single(other.DomainEvents);
        Assert.Empty(db.Set<OutboxMessage>().Local.Where(x =>
            db.Entry(x).State == EntityState.Added));

        other.Name = "recovered";
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.Set<OutboxMessage>().AsNoTracking().CountAsync());
        Assert.Equal(2, await db.Set<OutboxDelivery>().AsNoTracking().CountAsync());
        Assert.Empty(other.DomainEvents);
    }

    [Fact]
    public async Task Synchronous_SaveChanges_with_domain_events_is_rejected()
    {
        using var test = new Fixture();
        using var scope = test.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EventDb>();
        var order = EventOrder.Create(Guid.NewGuid(), "sync");
        db.Orders.Add(order);

        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Single(order.DomainEvents);
        Assert.Empty(db.Set<OutboxMessage>().Local);
    }

    [Fact]
    public async Task Explicit_immediate_notification_is_not_emitted_automatically()
    {
        using var test = new Fixture();
        await using var scope = test.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EventDb>();
        db.Orders.Add(EventOrder.Create(Guid.NewGuid(), "explicit"));
        await db.SaveChangesAsync();
        Assert.DoesNotContain(test.Probe.Steps, x => x == "immediate");

        await scope.ServiceProvider.GetRequiredService<INotificationPublisher>()
            .PublishAsync(new ImmediateOrderNotification());
        Assert.Contains("immediate", test.Probe.Steps);
    }

    [Fact]
    public async Task Durable_message_restores_parent_trace_after_transaction_commit()
    {
        using var test = new Fixture();
        var spans = new List<(string Name, string Trace, string Parent)>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "PlatformFoundation.Messaging",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => spans.Add((a.OperationName,
                a.TraceId.ToString(), a.ParentSpanId.ToString()))
        };
        ActivitySource.AddActivityListener(listener);
        using var parent = new Activity("HTTP").SetIdFormat(ActivityIdFormat.W3C);
        parent.Start();
        var expected = parent.TraceId.ToString();
        var parentSpan = parent.SpanId.ToString();
        await using (var scope = test.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EventDb>();
            db.Orders.Add(EventOrder.Create(Guid.NewGuid(), "traced"));
            await db.SaveChangesAsync();
            var persisted = await db.Set<OutboxMessage>().SingleAsync();
            Assert.Equal(parent.Id, persisted.TraceParent);
        }
        parent.Stop();

        await using (var scope = test.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<OutboxWorker<EventDb>>()
                .RunAsync(CancellationToken.None);
        }
        var consumed = Assert.Single(spans.Where(x => x.Name == "message.consume" && x.Trace == expected));
        Assert.Equal(expected, consumed.Trace);
        Assert.Equal(parentSpan, consumed.Parent);
    }

    [Fact]
    public async Task Event_and_message_migrations_generate_trace_column()
    {
        var options = new DbContextOptionsBuilder<PlatformMessagingDbContext>()
            .UseSqlServer("Server=localhost;Database=OfflineF05;User Id=sa;Password=NotARealSecret123!;TrustServerCertificate=True")
            .Options;
        using var db = new PlatformMessagingDbContext(options);
        var sql = db.Database.GenerateCreateScript();
        Assert.Contains("TraceParent", sql);
        Assert.Contains("nvarchar(55)", sql);
        Assert.Contains("20261009150000_AddOutboxTraceParent", db.Database.GetMigrations());
        await Task.CompletedTask;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public Probe Probe { get; } = new();
        public ServiceProvider Services { get; }

        public Fixture()
        {
            connection.Open();
            var services = new ServiceCollection();
            services.AddSingleton(Probe);
            services.AddDbContext<EventDb>((sp, options) =>
                options.UseSqlite(connection)
                    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
            services.AddPlatformMessaging<EventDb>(null, typeof(Handler).Assembly);
            services.AddPlatformDomainEvents<EventDb>(typeof(Handler).Assembly);
            Services = services.BuildServiceProvider(validateScopes: true);
            using var scope = Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<EventDb>().Database.EnsureCreated();
        }

        public void Dispose()
        {
            Services.Dispose();
            connection.Dispose();
        }
    }

    public sealed class EventDb(DbContextOptions<EventDb> options) : DbContext(options)
    {
        public DbSet<EventOrder> Orders => Set<EventOrder>();
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<EventOrder>().HasKey(x => x.Id);
            model.Entity<EventOrder>().Ignore(x => x.DomainEvents);
            model.Entity<EventOrder>().HasIndex(x => x.Name).IsUnique();
            model.AddMessagingOutbox();
        }
    }

    public sealed class EventOrder : IHasDomainEvents
    {
        private readonly List<IDomainEvent> events = [];
        private EventOrder() { }
        public static EventOrder Create(Guid id, string name)
        {
            var order = new EventOrder { Id = id, Name = name };
            order.events.Add(new OrderCreated(id, name));
            return order;
        }

        public Guid Id { get; private set; }
        public string Name { get; set; } = "";
        public IReadOnlyCollection<IDomainEvent> DomainEvents => events.AsReadOnly();
        public void ClearDomainEvents() => events.Clear();
    }

    public sealed record OrderCreated(Guid OrderId, string Name) : IDomainEvent
    {
        public DateTime OccurredOn { get; } = DateTime.UtcNow;
    }

    [MessageContract("test.domain.order.created.v1")]
    public sealed record OrderCreatedMessage(Guid OrderId, string Name) : IMessage;

    public sealed class Handler(Probe probe) : IMessageHandler<OrderCreatedMessage>
    {
        public Task HandleAsync(OrderCreatedMessage message, CancellationToken ct)
        {
            probe.Steps.Add("durable:" + message.Name);
            return Task.CompletedTask;
        }
    }

    public sealed class BeforeCommit(Probe probe) : IBeforeCommitDomainEventHandler<OrderCreated>
    {
        public Task HandleAsync(OrderCreated evt, CancellationToken ct)
        {
            probe.Steps.Add("before:" + evt.Name);
            if (probe.ThrowBeforeCommit)
                throw new InvalidOperationException("rejected");
            return Task.CompletedTask;
        }
    }

    public sealed class Mapper : IDomainEventMessageMapper<OrderCreated>
    {
        public IReadOnlyCollection<IMessage> Map(OrderCreated evt) =>
            [new OrderCreatedMessage(evt.OrderId, evt.Name)];
    }

    public sealed record ImmediateOrderNotification : INotification;
    public sealed class ImmediateHandler(Probe probe) : INotificationHandler<ImmediateOrderNotification>
    {
        public Task Handle(ImmediateOrderNotification evt, CancellationToken ct)
        {
            probe.Steps.Add("immediate");
            return Task.CompletedTask;
        }
    }

    public sealed class Probe
    {
        public bool ThrowBeforeCommit { get; set; }
        public List<string> Steps { get; } = [];
    }
}
