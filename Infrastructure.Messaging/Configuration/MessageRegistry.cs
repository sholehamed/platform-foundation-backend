using System.Reflection;
using System.Linq.Expressions;
using System.Text.Json;
using Application.SharedKernel.Abstractions.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Messaging.Configuration;

public sealed class MessageRegistry
{
    private readonly Dictionary<Type, MessageDescriptor> byType = [];
    private readonly Dictionary<string, MessageDescriptor> byContract = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HandlerDescriptor> byHandler = new(StringComparer.Ordinal);

    public MessageRegistry(IEnumerable<Assembly> assemblies)
    {
        foreach (var implementation in assemblies.Distinct().SelectMany(x => x.GetTypes())
            .Where(x => x.IsClass && !x.IsAbstract && !x.ContainsGenericParameters))
        {
            foreach (var iface in implementation.GetInterfaces().Where(x =>
                         x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IMessageHandler<>)))
            {
                var type = iface.GetGenericArguments()[0];
                var attribute = type.GetCustomAttribute<MessageContractAttribute>()
                    ?? throw new InvalidOperationException(
                        $"Message {type.FullName} must declare [MessageContract].");

                if (!byType.TryGetValue(type, out var message))
                {
                    message = new MessageDescriptor(type, attribute.Name);
                    byType.Add(type, message);
                    if (!byContract.TryAdd(message.Contract, message))
                        throw new InvalidOperationException(
                            $"Duplicate message contract: {message.Contract}");
                }

                var handlerKey = implementation.FullName
                    ?? throw new InvalidOperationException("Message handler must have a stable name.");
                var key = $"{attribute.Name}|{handlerKey}";
                if (!byHandler.TryAdd(key,
                        new HandlerDescriptor(message.Contract, handlerKey, implementation,
                            CreateInvoker(type, implementation))))
                    throw new InvalidOperationException($"Duplicate subscription: {key}");
                message.Handlers.Add(handlerKey);
            }
        }
    }

    public IReadOnlyCollection<Type> HandlerTypes =>
        byHandler.Values.Select(x => x.HandlerType).Distinct().ToArray();

    public MessageDescriptor ForType(Type type) =>
        byType.TryGetValue(type, out var message)
            ? message : throw new InvalidOperationException(
                $"No subscribed message contract registered for {type.FullName}.");

    public HandlerDescriptor GetHandler(string contract, string handlerKey) =>
        byHandler.TryGetValue($"{contract}|{handlerKey}", out var handler)
            ? handler : throw new InvalidOperationException(
                $"Missing message subscription {contract}|{handlerKey}.");

    private static Func<IServiceProvider, string, CancellationToken, Task> CreateInvoker(Type type, Type handlerType)
    {
        var method = typeof(MessageRegistry)
            .GetMethod(nameof(InvokeAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type);

        // Compile once instead of MethodInfo.Invoke on every delivery. This also
        // preserves the original Handler exception type for retry diagnostics.
        var provider = Expression.Parameter(typeof(IServiceProvider), "services");
        var payload = Expression.Parameter(typeof(string), "payload");
        var token = Expression.Parameter(typeof(CancellationToken), "token");
        return Expression.Lambda<Func<IServiceProvider, string, CancellationToken, Task>>(
            Expression.Call(method, provider, Expression.Constant(handlerType), payload, token),
            provider, payload, token).Compile();
    }

    private static Task InvokeAsync<TMessage>(
        IServiceProvider services, Type handlerType, string payload, CancellationToken token)
        where TMessage : IMessage
    {
        var message = JsonSerializer.Deserialize<TMessage>(payload, JsonSerializerOptions.Web)
            ?? throw new JsonException("Message payload cannot be null.");
        var handler = (IMessageHandler<TMessage>)services.GetRequiredService(handlerType);
        return handler.HandleAsync(message, token);
    }
}

public sealed record HandlerDescriptor(
    string Contract, string HandlerKey, Type HandlerType,
    Func<IServiceProvider, string, CancellationToken, Task> Execute);

public sealed class MessageDescriptor(Type messageType, string contract)
{
    public Type MessageType { get; } = messageType;
    public string Contract { get; } = contract;
    public List<string> Handlers { get; } = [];
}
