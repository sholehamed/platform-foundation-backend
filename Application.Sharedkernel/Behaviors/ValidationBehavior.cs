using Application.SharedKernel.Abstractions.Messaging;
using AppValidationException = Application.SharedKernel.Exceptions.ValidationException;
using FluentValidation;
using FluentValidation.Results;

namespace Application.SharedKernel.Behaviors;

/// <summary>Runs all FluentValidation validators before the request handler.</summary>
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();

        // Validators may share a scoped EF Core DbContext: never run them in parallel.
        foreach (var validator in validators)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await validator.ValidateAsync(
                new ValidationContext<TRequest>(request), cancellationToken);
            failures.AddRange(result.Errors.Where(error => error is not null));
        }

        if (failures.Count > 0)
            throw new AppValidationException(failures);

        return await next();
    }
}
