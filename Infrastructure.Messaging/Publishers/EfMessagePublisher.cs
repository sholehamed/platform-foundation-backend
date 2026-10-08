using System.Text.Json;
using Application.SharedKernel.Abstractions.Messaging;
using Infrastructure.Messaging.Configuration;
using Infrastructure.Messaging.Model;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Messaging.Publishers;

/// <summary>Stage a message and one receipt per subscriber in the caller's DbContext.</summary>
public sealed class EfMessagePublisher<TDbContext>(
    TDbContext context, MessageRegistry registry, TimeProvider clock) : IMessagePublisher
    where TDbContext : DbContext
{
    public Task<Guid> PublishAsync<TMessage>(
        TMessage message, CancellationToken cancellationToken = default)
        where TMessage : IMessage
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        var descriptor = registry.ForType(message.GetType());
        var id = Guid.NewGuid();
        var now = clock.GetUtcNow().UtcDateTime;
        context.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = id,
            Contract = descriptor.Contract,
            Payload = JsonSerializer.Serialize(message, descriptor.MessageType, JsonSerializerOptions.Web),
            CreatedAt = now
        });
        foreach (var handler in descriptor.Handlers)
            context.Set<OutboxDelivery>().Add(new OutboxDelivery
            {
                Id = Guid.NewGuid(),
                MessageId = id,
                HandlerKey = handler,
                NextAttemptAt = now
            });

        // Intentionally no SaveChanges: the command's SaveChanges/Transaction
        // commits both domain changes and the outbox entries.
        return Task.FromResult(id);
    }
}
