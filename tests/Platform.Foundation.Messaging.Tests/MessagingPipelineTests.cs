using Application.SharedKernel.Abstractions.Messaging;
using ApplicationValidationException = Application.SharedKernel.Exceptions.ValidationException;
using Domain.SharedKernel.Common.Events;
using FluentValidation;
using Infrastructure.Messaging.Configuration;
using Infrastructure.Messaging.Model;
using Infrastructure.Messaging.Persistence;
using Infrastructure.Messaging.Processing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Platform.Foundation.Messaging.Tests;

public sealed class MessagingPipelineTests
{
    [Fact]
    public async Task Notification_behaviors_wrap_handler_in_DI_registration_order()
    {
        using var fixture = new Fixture(s =>
        {
            s.AddTransient<INotificationPipelineBehavior<PipelineNotification>, NotificationOuter>();
            s.AddTransient<INotificationPipelineBehavior<PipelineNotification>, NotificationInner>();
        });
        await using var scope = fixture.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<INotificationPublisher>()
            .PublishAsync(new PipelineNotification("valid"));

        Assert.Equal(new[] { "n.outer.before", "n.inner.before", "notification.handler",
            "n.inner.after", "n.outer.after" }, fixture.Probe.Events);
    }

    [Fact]
    public async Task Notification_can_explicitly_short_circuit_without_invoking_handler()
    {
        using var fixture = new Fixture(s =>
            s.AddTransient<INotificationPipelineBehavior<PipelineNotification>, SuppressNotification>());
        await using var scope = fixture.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<INotificationPublisher>()
            .PublishAsync(new PipelineNotification("valid"));

        Assert.Equal(new[] { "notification.suppressed" }, fixture.Probe.Events);
    }

    [Fact]
    public async Task Notification_validation_blocks_handlers()
    {
        using var fixture = new Fixture();
        await using var scope = fixture.Services.CreateAsyncScope();

        var ex = await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            scope.ServiceProvider.GetRequiredService<INotificationPublisher>()
                .PublishAsync(new PipelineNotification("")));
        Assert.True(ex.Errors.ContainsKey("Name"));
        Assert.Empty(fixture.Probe.Events);
    }

    [Fact]
    public async Task Durable_behaviors_wrap_individual_handler_and_see_delivery_context()
    {
        using var fixture = new Fixture(s =>
        {
            s.AddTransient<IMessagePipelineBehavior<PipelineMessage>, MessageOuter>();
            s.AddTransient<IMessagePipelineBehavior<PipelineMessage>, MessageInner>();
        });
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PipelineDbContext>();
        var messageId = await scope.ServiceProvider.GetRequiredService<IMessagePublisher>()
            .PublishAsync(new PipelineMessage("valid"));
        await db.SaveChangesAsync();
        var delivery = await db.Set<OutboxDelivery>().SingleAsync(x => x.MessageId == messageId);

        await scope.ServiceProvider.GetRequiredService<OutboxWorker<PipelineDbContext>>()
            .RunAsync(CancellationToken.None);

        Assert.Equal(new[] { "m.outer.before", "m.inner.before", "message.handler",
            "m.inner.after", "m.outer.after" }, fixture.Probe.Events);
        var context = Assert.Single(fixture.Probe.Contexts);
        Assert.Equal(messageId, context.MessageId);
        Assert.Equal(delivery.Id, context.DeliveryId);
        Assert.Equal("test.pipeline.message.v1", context.Contract);
        Assert.EndsWith("PipelineMessageHandler", context.HandlerKey);
        Assert.Equal(1, context.Attempt);
        Assert.Equal(DeliveryStatus.Completed, (await db.Set<OutboxDelivery>()
            .AsNoTracking().SingleAsync(x => x.Id == delivery.Id)).Status);
    }

    [Fact]
    public async Task Durable_short_circuit_is_not_acknowledged_and_is_retried()
    {
        using var fixture = new Fixture(s =>
            s.AddTransient<IMessagePipelineBehavior<PipelineMessage>, SuppressMessage>());
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PipelineDbContext>();
        await scope.ServiceProvider.GetRequiredService<IMessagePublisher>()
            .PublishAsync(new PipelineMessage("valid"));
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<OutboxWorker<PipelineDbContext>>()
            .RunAsync(CancellationToken.None);

        var delivery = await db.Set<OutboxDelivery>().AsNoTracking().SingleAsync();
        Assert.Equal(DeliveryStatus.Pending, delivery.Status);
        Assert.Equal(1, delivery.Attempts);
        Assert.Equal(nameof(InvalidOperationException), delivery.LastErrorType);
        Assert.Equal(new[] { "message.suppressed" }, fixture.Probe.Events);
    }

    [Fact]
    public async Task Message_pipeline_exception_flows_to_worker_dead_letter()
    {
        using var fixture = new Fixture(s =>
            s.AddTransient<IMessagePipelineBehavior<PipelineMessage>, ThrowingMessage>(),
            options => options.MaxAttempts = 1);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PipelineDbContext>();
        await scope.ServiceProvider.GetRequiredService<IMessagePublisher>()
            .PublishAsync(new PipelineMessage("valid"));
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<OutboxWorker<PipelineDbContext>>()
            .RunAsync(CancellationToken.None);

        var delivery = await db.Set<OutboxDelivery>().AsNoTracking().SingleAsync();
        Assert.Equal(DeliveryStatus.DeadLetter, delivery.Status);
        Assert.Equal(nameof(InvalidOperationException), delivery.LastErrorType);
        Assert.Equal(new[] { "message.pipeline.failed" }, fixture.Probe.Events);
    }

    [Fact]
    public async Task Durable_validation_occurs_before_handler_and_is_dead_lettered()
    {
        using var fixture = new Fixture(configure: options => options.MaxAttempts = 1);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PipelineDbContext>();
        await scope.ServiceProvider.GetRequiredService<IMessagePublisher>()
            .PublishAsync(new PipelineMessage(""));
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<OutboxWorker<PipelineDbContext>>()
            .RunAsync(CancellationToken.None);

        var delivery = await db.Set<OutboxDelivery>().AsNoTracking().SingleAsync();
        Assert.Equal(DeliveryStatus.DeadLetter, delivery.Status);
        Assert.Equal(nameof(ApplicationValidationException), delivery.LastErrorType);
        Assert.Empty(fixture.Probe.Events);
    }

    [Fact]
    public async Task Cancellation_is_propagated_to_notification_pipeline()
    {
        using var fixture = new Fixture();
        await using var scope = fixture.Services.CreateAsyncScope();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            scope.ServiceProvider.GetRequiredService<INotificationPublisher>()
                .PublishAsync(new PipelineNotification("valid"), cts.Token));
        Assert.Empty(fixture.Probe.Events);
    }

    public sealed class PipelineDbContext(DbContextOptions<PipelineDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.AddMessagingOutbox();
    }

    public sealed class Probe
    {
        public List<string> Events { get; } = [];
        public List<MessageContext> Contexts { get; } = [];
    }

    public sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public Probe Probe { get; } = new();
        public ServiceProvider Services { get; }

        public Fixture(Action<IServiceCollection>? customize = null,
            Action<MessagingOptions>? configure = null)
        {
            connection.Open();
            var services = new ServiceCollection();
            services.AddSingleton(Probe);
            services.AddDbContext<PipelineDbContext>(o => o.UseSqlite(connection));
            services.AddPlatformMessaging<PipelineDbContext>(
                configure, typeof(PipelineMessageHandler).Assembly);
            customize?.Invoke(services);
            Services = services.BuildServiceProvider(validateScopes: true);
            using var scope = Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<PipelineDbContext>()
                .Database.EnsureCreated();
        }

        public void Dispose()
        {
            Services.Dispose();
            connection.Dispose();
        }
    }

    [MessageContract("test.pipeline.message.v1")]
    public sealed record PipelineMessage(string Name) : IMessage;

    public sealed class PipelineMessageHandler(Probe probe) : IMessageHandler<PipelineMessage>
    {
        public Task HandleAsync(PipelineMessage message, CancellationToken ct)
        {
            probe.Events.Add("message.handler");
            return Task.CompletedTask;
        }
    }

    public sealed record PipelineNotification(string Name) : INotification;

    public sealed class PipelineNotificationHandler(Probe probe)
        : INotificationHandler<PipelineNotification>
    {
        public Task Handle(PipelineNotification notification, CancellationToken ct)
        {
            probe.Events.Add("notification.handler");
            return Task.CompletedTask;
        }
    }

    public sealed class PipelineMessageValidator : AbstractValidator<PipelineMessage>
    {
        public PipelineMessageValidator() => RuleFor(x => x.Name).NotEmpty();
    }

    public sealed class PipelineNotificationValidator : AbstractValidator<PipelineNotification>
    {
        public PipelineNotificationValidator() => RuleFor(x => x.Name).NotEmpty();
    }

    public sealed class NotificationOuter(Probe probe)
        : INotificationPipelineBehavior<PipelineNotification>
    {
        public async Task HandleAsync(PipelineNotification notification,
            NotificationHandlerDelegate next, CancellationToken ct)
        {
            probe.Events.Add("n.outer.before");
            await next();
            probe.Events.Add("n.outer.after");
        }
    }

    public sealed class NotificationInner(Probe probe)
        : INotificationPipelineBehavior<PipelineNotification>
    {
        public async Task HandleAsync(PipelineNotification notification,
            NotificationHandlerDelegate next, CancellationToken ct)
        {
            probe.Events.Add("n.inner.before");
            await next();
            probe.Events.Add("n.inner.after");
        }
    }

    public sealed class SuppressNotification(Probe probe)
        : INotificationPipelineBehavior<PipelineNotification>
    {
        public Task HandleAsync(PipelineNotification notification,
            NotificationHandlerDelegate next, CancellationToken ct)
        {
            probe.Events.Add("notification.suppressed");
            return Task.CompletedTask;
        }
    }

    public sealed class MessageOuter(Probe probe) : IMessagePipelineBehavior<PipelineMessage>
    {
        public async Task HandleAsync(PipelineMessage message, MessageContext context,
            MessageHandlerDelegate next, CancellationToken ct)
        {
            probe.Contexts.Add(context);
            probe.Events.Add("m.outer.before");
            await next();
            probe.Events.Add("m.outer.after");
        }
    }

    public sealed class MessageInner(Probe probe) : IMessagePipelineBehavior<PipelineMessage>
    {
        public async Task HandleAsync(PipelineMessage message, MessageContext context,
            MessageHandlerDelegate next, CancellationToken ct)
        {
            probe.Events.Add("m.inner.before");
            await next();
            probe.Events.Add("m.inner.after");
        }
    }

    public sealed class SuppressMessage(Probe probe) : IMessagePipelineBehavior<PipelineMessage>
    {
        public Task HandleAsync(PipelineMessage message, MessageContext context,
            MessageHandlerDelegate next, CancellationToken ct)
        {
            probe.Events.Add("message.suppressed");
            return Task.CompletedTask;
        }
    }

    public sealed class ThrowingMessage(Probe probe) : IMessagePipelineBehavior<PipelineMessage>
    {
        public Task HandleAsync(PipelineMessage message, MessageContext context,
            MessageHandlerDelegate next, CancellationToken ct)
        {
            probe.Events.Add("message.pipeline.failed");
            throw new InvalidOperationException("pipeline failure");
        }
    }
}
