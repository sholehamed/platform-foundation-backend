using Application.SharedKernel;
using Application.SharedKernel.Abstractions.Messaging;
using AppValidationException = Application.SharedKernel.Exceptions.ValidationException;
using Application.SharedKernel.Models;
using Domain.SharedKernel.Common.Events;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Platform.Foundation.Application.Tests;

public sealed class DispatcherPipelineTests
{
    private static ServiceProvider CreateProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddCustomCqrs(typeof(DispatcherPipelineTests).Assembly);
        services.AddSingleton<ExecutionLog>();
        configure?.Invoke(services);
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public async Task Valid_command_runs_validator_and_handler()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var response = await dispatcher.Send(new CreateSampleCommand("valid"));

        Assert.Equal("valid", response);
        Assert.Equal(new[] { "command" }, scope.ServiceProvider.GetRequiredService<ExecutionLog>().Events);
    }

    [Fact]
    public async Task Invalid_command_stops_before_handler()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var exception = await Assert.ThrowsAsync<AppValidationException>(
            () => dispatcher.Send(new CreateSampleCommand("")));

        Assert.Contains(nameof(CreateSampleCommand.Name), exception.Errors.Keys);
        Assert.Empty(scope.ServiceProvider.GetRequiredService<ExecutionLog>().Events);
    }

    [Fact]
    public async Task Query_passes_through_validation()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await Assert.ThrowsAsync<AppValidationException>(() => dispatcher.Query(new EchoQuery("")));
        Assert.Equal("echo", await dispatcher.Query(new EchoQuery("echo")));
    }

    [Fact]
    public async Task Void_command_passes_through_pipeline()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await Assert.ThrowsAsync<AppValidationException>(() => dispatcher.Send(new TrackCommand("")));
        await dispatcher.Send(new TrackCommand("okay"));

        Assert.Equal(new[] { "void" }, scope.ServiceProvider.GetRequiredService<ExecutionLog>().Events);
    }

    [Fact]
    public async Task Custom_behaviors_run_in_registration_order()
    {
        using var provider = CreateProvider(services =>
        {
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(OuterBehavior<,>));
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(InnerBehavior<,>));
        });
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await dispatcher.Send(new CreateSampleCommand("valid"));

        Assert.Equal(new[] { "outer-before", "inner-before", "command", "inner-after", "outer-after" },
            scope.ServiceProvider.GetRequiredService<ExecutionLog>().Events);
    }

    [Fact]
    public async Task Cancellation_token_reaches_handler()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => dispatcher.Send(new TrackCommand("valid"), cts.Token));
        Assert.Empty(scope.ServiceProvider.GetRequiredService<ExecutionLog>().Events);
    }

    [Fact]
    public async Task Publish_runs_all_handlers_sequentially()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await dispatcher.Publish(new SampleNotification());

        Assert.Equal(new[] { "first-notification", "second-notification" },
            scope.ServiceProvider.GetRequiredService<ExecutionLog>().Events);
    }

    [Fact]
    public async Task Publish_resolves_domain_event_handlers_as_notifications()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await dispatcher.Publish(new SampleDomainEvent());

        Assert.Equal(new[] { "domain-event" },
            scope.ServiceProvider.GetRequiredService<ExecutionLog>().Events);
    }

    [Fact]
    public async Task Publish_propagates_handler_errors()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.Publish(new FailingNotification()));

        Assert.Equal(new[] { "failed-notification" },
            scope.ServiceProvider.GetRequiredService<ExecutionLog>().Events);
    }

    public sealed class ExecutionLog
    {
        public List<string> Events { get; } = [];
    }

    public sealed record CreateSampleCommand(string Name) : ICommand<string>;

    public sealed class CreateSampleValidator : AbstractValidator<CreateSampleCommand>
    {
        public CreateSampleValidator() => RuleFor(x => x.Name).NotEmpty().MinimumLength(3);
    }

    public sealed class CreateSampleHandler(ExecutionLog log)
        : ICommandHandler<CreateSampleCommand, string>
    {
        public Task<string> Handle(CreateSampleCommand command, CancellationToken ct)
        {
            log.Events.Add("command");
            return Task.FromResult(command.Name);
        }
    }

    public sealed record EchoQuery(string Value) : IQuery<string>;

    public sealed class EchoValidator : AbstractValidator<EchoQuery>
    {
        public EchoValidator() => RuleFor(x => x.Value).NotEmpty();
    }

    public sealed class EchoHandler : IQueryHandler<EchoQuery, string>
    {
        public Task<string> Handle(EchoQuery query, CancellationToken ct) =>
            Task.FromResult(query.Value);
    }

    public sealed record TrackCommand(string Name) : ICommand;

    public sealed class TrackValidator : AbstractValidator<TrackCommand>
    {
        public TrackValidator() => RuleFor(x => x.Name).NotEmpty();
    }

    public sealed class TrackHandler(ExecutionLog log) : ICommandHandler<TrackCommand>
    {
        public Task Handle(TrackCommand command, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            log.Events.Add("void");
            return Task.CompletedTask;
        }
    }

    public sealed class OuterBehavior<TRequest, TResponse>(ExecutionLog log)
        : IPipelineBehavior<TRequest, TResponse>
    {
        public async Task<TResponse> Handle(
            TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
        {
            log.Events.Add("outer-before");
            var result = await next();
            log.Events.Add("outer-after");
            return result;
        }
    }

    public sealed class InnerBehavior<TRequest, TResponse>(ExecutionLog log)
        : IPipelineBehavior<TRequest, TResponse>
    {
        public async Task<TResponse> Handle(
            TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
        {
            log.Events.Add("inner-before");
            var result = await next();
            log.Events.Add("inner-after");
            return result;
        }
    }

    public sealed record SampleNotification : INotification;

    public sealed class FirstNotificationHandler(ExecutionLog log)
        : INotificationHandler<SampleNotification>
    {
        public Task Handle(SampleNotification notification, CancellationToken ct)
        {
            log.Events.Add("first-notification");
            return Task.CompletedTask;
        }
    }

    public sealed class SecondNotificationHandler(ExecutionLog log)
        : INotificationHandler<SampleNotification>
    {
        public Task Handle(SampleNotification notification, CancellationToken ct)
        {
            log.Events.Add("second-notification");
            return Task.CompletedTask;
        }
    }

    public sealed record SampleDomainEvent : IDomainEvent
    {
        public DateTime OccurredOn => DateTime.UtcNow;
    }

    public sealed class SampleDomainEventHandler(ExecutionLog log)
        : IDomainEventHandler<SampleDomainEvent>
    {
        public Task Handle(SampleDomainEvent notification, CancellationToken ct)
        {
            log.Events.Add("domain-event");
            return Task.CompletedTask;
        }
    }

    public sealed record FailingNotification : INotification;

    public sealed class FailingNotificationHandler(ExecutionLog log)
        : INotificationHandler<FailingNotification>
    {
        public Task Handle(FailingNotification notification, CancellationToken ct)
        {
            log.Events.Add("failed-notification");
            throw new InvalidOperationException("failure");
        }
    }
}
