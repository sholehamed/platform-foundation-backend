namespace Application.SharedKernel.Abstractions.Messaging;

/// <summary>
/// A cross-cutting step surrounding a command or query handler.
/// Behaviors execute in their DI registration order (outermost first).
/// </summary>
public interface IPipelineBehavior<TRequest, TResponse>
{
    Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}

public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();
