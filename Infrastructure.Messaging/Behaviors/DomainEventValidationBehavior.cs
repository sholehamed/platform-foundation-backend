using Application.SharedKernel.Abstractions.Messaging;
using ApplicationValidationException = Application.SharedKernel.Exceptions.ValidationException;
using Domain.SharedKernel.Common.Events;
using FluentValidation;
using FluentValidation.Results;

namespace Infrastructure.Messaging.Behaviors;

/// <summary>Optional FluentValidation for domain event contracts before handlers and mapping.</summary>
public sealed class DomainEventValidationBehavior<TEvent>(
    IEnumerable<IValidator<TEvent>> validators) : IDomainEventPipelineBehavior<TEvent>
    where TEvent : IDomainEvent
{
    public async Task<IReadOnlyList<IMessage>> HandleAsync(
        TEvent domainEvent, DomainEventHandlerDelegate next,
        CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();
        foreach (var validator in validators)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await validator.ValidateAsync(
                new ValidationContext<TEvent>(domainEvent), cancellationToken);
            failures.AddRange(result.Errors);
        }
        if (failures.Count > 0)
            throw new ApplicationValidationException(failures);
        return await next();
    }
}
