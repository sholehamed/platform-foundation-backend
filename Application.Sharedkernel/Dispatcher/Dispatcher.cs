using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Application.SharedKernel.Abstractions.Messaging;
using Application.SharedKernel.Models;
using Domain.SharedKernel.Common.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Application.SharedKernel.Dispatcher;

/// <summary>
/// Dispatches the concrete command/query type through registered pipeline behaviors.
/// Compiled invokers are cached once per concrete request type; no dynamic binder is used.
/// </summary>
public sealed class Dispatcher(IServiceProvider serviceProvider) : IDispatcher
{
    public Task<TResult> Send<TResult>(
        ICommand<TResult> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return CommandInvoker<TResult>.Invoke(this, command, cancellationToken);
    }

    public Task Send(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return VoidCommandInvoker.Invoke(this, command, cancellationToken);
    }

    public Task<TResult> Query<TResult>(
        IQuery<TResult> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return QueryInvoker<TResult>.Invoke(this, query, cancellationToken);
    }

    public async Task Publish<TNotification>(
        TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);

        // Deliberately sequential and fail-fast. Domain events are not durable here;
        // retry/outbox semantics require an explicit follow-up architecture decision.
        foreach (var handler in serviceProvider.GetServices<INotificationHandler<TNotification>>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await handler.Handle(notification, cancellationToken);
        }
    }

    private Task<TResult> DispatchCommandCore<TCommand, TResult>(
        TCommand command, CancellationToken cancellationToken)
        where TCommand : ICommand<TResult>
    {
        var handler = serviceProvider.GetRequiredService<ICommandHandler<TCommand, TResult>>();
        return ExecutePipeline<TCommand, TResult>(
            command, () => handler.Handle(command, cancellationToken), cancellationToken);
    }

    private async Task DispatchVoidCommandCore<TCommand>(
        TCommand command, CancellationToken cancellationToken)
        where TCommand : ICommand
    {
        var handler = serviceProvider.GetRequiredService<ICommandHandler<TCommand>>();
        await ExecutePipeline<TCommand, Unit>(
            command,
            async () =>
            {
                await handler.Handle(command, cancellationToken);
                return Unit.Value;
            },
            cancellationToken);
    }

    private Task<TResult> DispatchQueryCore<TQuery, TResult>(
        TQuery query, CancellationToken cancellationToken)
        where TQuery : IQuery<TResult>
    {
        var handler = serviceProvider.GetRequiredService<IQueryHandler<TQuery, TResult>>();
        return ExecutePipeline<TQuery, TResult>(
            query, () => handler.Handle(query, cancellationToken), cancellationToken);
    }

    private Task<TResult> ExecutePipeline<TRequest, TResult>(
        TRequest request, RequestHandlerDelegate<TResult> terminal,
        CancellationToken cancellationToken)
    {
        var behaviors = serviceProvider
            .GetServices<IPipelineBehavior<TRequest, TResult>>()
            .ToArray();

        var next = terminal;
        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var captured = next;
            next = () => behavior.Handle(request, captured, cancellationToken);
        }

        return next();
    }

    private static MethodInfo CoreMethod(string name) =>
        typeof(Dispatcher).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(Dispatcher).Name, name);

    private static class CommandInvoker<TResult>
    {
        private static readonly ConcurrentDictionary<Type,
            Func<Dispatcher, ICommand<TResult>, CancellationToken, Task<TResult>>> Cache = new();

        public static Task<TResult> Invoke(
            Dispatcher dispatcher, ICommand<TResult> command, CancellationToken token) =>
            Cache.GetOrAdd(command.GetType(), Create)(dispatcher, command, token);

        private static Func<Dispatcher, ICommand<TResult>, CancellationToken, Task<TResult>>
            Create(Type commandType)
        {
            var method = CoreMethod(nameof(DispatchCommandCore))
                .MakeGenericMethod(commandType, typeof(TResult));
            var target = Expression.Parameter(typeof(Dispatcher), "dispatcher");
            var request = Expression.Parameter(typeof(ICommand<TResult>), "command");
            var token = Expression.Parameter(typeof(CancellationToken), "token");

            return Expression.Lambda<Func<Dispatcher, ICommand<TResult>, CancellationToken, Task<TResult>>>(
                Expression.Call(target, method, Expression.Convert(request, commandType), token),
                target, request, token).Compile();
        }
    }

    private static class QueryInvoker<TResult>
    {
        private static readonly ConcurrentDictionary<Type,
            Func<Dispatcher, IQuery<TResult>, CancellationToken, Task<TResult>>> Cache = new();

        public static Task<TResult> Invoke(
            Dispatcher dispatcher, IQuery<TResult> query, CancellationToken token) =>
            Cache.GetOrAdd(query.GetType(), Create)(dispatcher, query, token);

        private static Func<Dispatcher, IQuery<TResult>, CancellationToken, Task<TResult>>
            Create(Type queryType)
        {
            var method = CoreMethod(nameof(DispatchQueryCore))
                .MakeGenericMethod(queryType, typeof(TResult));
            var target = Expression.Parameter(typeof(Dispatcher), "dispatcher");
            var request = Expression.Parameter(typeof(IQuery<TResult>), "query");
            var token = Expression.Parameter(typeof(CancellationToken), "token");

            return Expression.Lambda<Func<Dispatcher, IQuery<TResult>, CancellationToken, Task<TResult>>>(
                Expression.Call(target, method, Expression.Convert(request, queryType), token),
                target, request, token).Compile();
        }
    }

    private static class VoidCommandInvoker
    {
        private static readonly ConcurrentDictionary<Type,
            Func<Dispatcher, ICommand, CancellationToken, Task>> Cache = new();

        public static Task Invoke(
            Dispatcher dispatcher, ICommand command, CancellationToken token) =>
            Cache.GetOrAdd(command.GetType(), Create)(dispatcher, command, token);

        private static Func<Dispatcher, ICommand, CancellationToken, Task> Create(Type commandType)
        {
            var method = CoreMethod(nameof(DispatchVoidCommandCore))
                .MakeGenericMethod(commandType);
            var target = Expression.Parameter(typeof(Dispatcher), "dispatcher");
            var request = Expression.Parameter(typeof(ICommand), "command");
            var token = Expression.Parameter(typeof(CancellationToken), "token");

            return Expression.Lambda<Func<Dispatcher, ICommand, CancellationToken, Task>>(
                Expression.Call(target, method, Expression.Convert(request, commandType), token),
                target, request, token).Compile();
        }
    }
}
