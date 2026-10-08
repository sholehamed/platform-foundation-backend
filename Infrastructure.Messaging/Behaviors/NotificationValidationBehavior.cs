using Application.SharedKernel.Abstractions.Messaging;
using ApplicationValidationException = Application.SharedKernel.Exceptions.ValidationException;
using Domain.SharedKernel.Common.Events;
using FluentValidation;
using FluentValidation.Results;

namespace Infrastructure.Messaging.Behaviors;

public sealed class NotificationValidationBehavior<TNotification>(
    IEnumerable<IValidator<TNotification>> validators)
    : INotificationPipelineBehavior<TNotification> where TNotification : INotification
{
    public async Task HandleAsync(
        TNotification notification, NotificationHandlerDelegate next,
        CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();
        foreach (var validator in validators)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await validator.ValidateAsync(
                new ValidationContext<TNotification>(notification), cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count > 0)
            throw new ApplicationValidationException(failures);

        await next();
    }
}
