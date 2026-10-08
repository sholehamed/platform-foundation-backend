using Domain.SharedKernel.Common.Events;

namespace Application.SharedKernel.Abstractions.Messaging;

/// <summary>
/// Invoked before EF Core writes to the database. Handlers may mutate tracked
/// aggregates or reject invalid state; they must not call SaveChanges or I/O.
/// </summary>
public interface IBeforeCommitDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Synchronous deterministic mapping from a business fact to zero or more
/// versioned durable messages, staged in the SAME business DbContext.
/// </summary>
public interface IDomainEventMessageMapper<in TEvent> where TEvent : IDomainEvent
{
    IReadOnlyCollection<IMessage> Map(TEvent domainEvent);
}

/// <summary>
/// Explicitly stages a durable message without saving changes or sending to a broker.
/// The application must commit its DbContext transaction.
/// </summary>
public interface IOutboxMessageStager
{
    Guid Stage(IMessage message);
}

/// <summary>DI marker, per DbContext, so opt-in modules do not alter other modules.</summary>
public interface IDomainEventRoutingMode
{
    Type DbContextType { get; }
}
