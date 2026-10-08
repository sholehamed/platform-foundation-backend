using Domain.SharedKernel.Common.Events;

namespace Application.SharedKernel.Abstractions.Messaging;

/// <summary>In-process notification middleware. Behaviors run in DI registration order.</summary>
public interface INotificationPipelineBehavior<TNotification>
    where TNotification : INotification
{
    Task HandleAsync(
        TNotification notification,
        NotificationHandlerDelegate next,
        CancellationToken cancellationToken);
}

public delegate Task NotificationHandlerDelegate();
