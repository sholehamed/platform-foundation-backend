namespace Application.SharedKernel.Abstractions.Messaging;

/// <summary>Immutable delivery metadata passed to each durable subscriber pipeline.</summary>
public sealed record MessageContext(
    Guid MessageId,
    Guid DeliveryId,
    string Contract,
    string HandlerKey,
    int Attempt);

/// <summary>
/// Runs around each individual durable message subscriber, not around an entire batch.
/// Calling next() delegates to the next behavior or the concrete handler.
/// </summary>
public interface IMessagePipelineBehavior<TMessage> where TMessage : IMessage
{
    Task HandleAsync(
        TMessage message,
        MessageContext context,
        MessageHandlerDelegate next,
        CancellationToken cancellationToken);
}

public delegate Task MessageHandlerDelegate();
