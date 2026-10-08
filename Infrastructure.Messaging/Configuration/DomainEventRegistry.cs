using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Application.SharedKernel.Abstractions.Messaging;
using Domain.SharedKernel.Common.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Messaging.Configuration;

/// <summary>Cached typed dispatch for before-save handlers and durable message mappings.</summary>
public sealed class DomainEventRegistry
{
    private static readonly MethodInfo InvokeMethod = typeof(DomainEventRegistry)
        .GetMethod(nameof(InvokeTypedAsync), BindingFlags.Static | BindingFlags.NonPublic)!;

    private readonly ConcurrentDictionary<Type,
        Func<IServiceProvider, IDomainEvent, CancellationToken, Task<IReadOnlyList<IMessage>>>> invokers = new();

    public Task<IReadOnlyList<IMessage>> DispatchAsync(
        IServiceProvider services, IDomainEvent domainEvent, CancellationToken ct) =>
        invokers.GetOrAdd(domainEvent.GetType(), Create)(services, domainEvent, ct);

    private static Func<IServiceProvider, IDomainEvent, CancellationToken, Task<IReadOnlyList<IMessage>>>
        Create(Type type)
    {
        var services = Expression.Parameter(typeof(IServiceProvider), "services");
        var domainEvent = Expression.Parameter(typeof(IDomainEvent), "domainEvent");
        var token = Expression.Parameter(typeof(CancellationToken), "token");
        return Expression.Lambda<
            Func<IServiceProvider, IDomainEvent, CancellationToken, Task<IReadOnlyList<IMessage>>>>(
            Expression.Call(InvokeMethod.MakeGenericMethod(type),
                services, Expression.Convert(domainEvent, type), token),
            services, domainEvent, token).Compile();
    }

    private static async Task<IReadOnlyList<IMessage>> InvokeTypedAsync<TEvent>(
        IServiceProvider services, TEvent domainEvent, CancellationToken ct)
        where TEvent : IDomainEvent
    {
        foreach (var handler in services.GetServices<IBeforeCommitDomainEventHandler<TEvent>>())
        {
            ct.ThrowIfCancellationRequested();
            await handler.HandleAsync(domainEvent, ct);
        }

        var results = new List<IMessage>();
        foreach (var mapper in services.GetServices<IDomainEventMessageMapper<TEvent>>())
        {
            ct.ThrowIfCancellationRequested();
            var messages = mapper.Map(domainEvent) ??
                throw new InvalidOperationException("A domain event mapper returned null.");
            foreach (var message in messages)
                results.Add(message ?? throw new InvalidOperationException(
                    "A domain event mapper returned a null durable message."));
        }

        return results;
    }
}
