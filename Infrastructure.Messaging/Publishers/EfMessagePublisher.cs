using System.Diagnostics;
using System.Text.Json;
using Application.SharedKernel.Abstractions.Messaging;
using Infrastructure.Messaging.Configuration;
using Infrastructure.Messaging.Model;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Messaging.Publishers;

/// <summary>Stage a message and a delivery receipt per subscriber in the caller's DbContext.</summary>
public sealed class EfMessagePublisher<TDbContext>(
    TDbContext context, MessageRegistry registry, TimeProvider clock)
    : IMessagePublisher, IOutboxMessageStager where TDbContext : DbContext
{
    public Task<Guid> PublishAsync<TMessage>(
        TMessage message, CancellationToken cancellationToken = default) where TMessage : IMessage
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Stage(message));
    }

    public Guid Stage(IMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var descriptor = registry.ForType(message.GetType());
        var id = Guid.NewGuid();
        var now = clock.GetUtcNow().UtcDateTime;
        var activity = Activity.Current;
        var traceParent = activity is { IdFormat: ActivityIdFormat.W3C } ? activity.Id : null;

        context.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = id,
            Contract = descriptor.Contract,
            Payload = JsonSerializer.Serialize(message, descriptor.MessageType, JsonSerializerOptions.Web),
            CreatedAt = now,
            TraceParent = traceParent
        });
        foreach (var handler in descriptor.Handlers)
            context.Set<OutboxDelivery>().Add(new OutboxDelivery
            {
                Id = Guid.NewGuid(),
                MessageId = id,
                HandlerKey = handler,
                NextAttemptAt = now
            });

        // This is ONLY a tracked entity insertion, never a SaveChanges/commit.
        return id;
    }
}
