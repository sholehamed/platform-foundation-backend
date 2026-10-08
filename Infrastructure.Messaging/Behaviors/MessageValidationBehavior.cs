using Application.SharedKernel.Abstractions.Messaging;
using ApplicationValidationException = Application.SharedKernel.Exceptions.ValidationException;
using FluentValidation;
using FluentValidation.Results;

namespace Infrastructure.Messaging.Behaviors;

public sealed class MessageValidationBehavior<TMessage>(
    IEnumerable<IValidator<TMessage>> validators)
    : IMessagePipelineBehavior<TMessage> where TMessage : IMessage
{
    public async Task HandleAsync(
        TMessage message, MessageContext context,
        MessageHandlerDelegate next, CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();
        foreach (var validator in validators)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await validator.ValidateAsync(
                new ValidationContext<TMessage>(message), cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count > 0)
            throw new ApplicationValidationException(failures);

        await next();
    }
}
