namespace Application.SharedKernel.Abstractions.Messaging;

/// <summary>
/// Stages durable messages in the caller's EF Core unit of work.
/// The caller MUST commit its DbContext with SaveChangesAsync.
/// This method does not enqueue a Hangfire job or commit the transaction.
/// </summary>
public interface IMessagePublisher
{
    Task<Guid> PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : IMessage;
}

public interface IMessageHandler<in TMessage> where TMessage : IMessage
{
    Task HandleAsync(TMessage message, CancellationToken cancellationToken);
}
