using Application.SharedKernel.Abstractions.Messaging;
using Domain.SharedKernel.Common.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Messaging.Publishers;

public sealed class InProcessNotificationPublisher(IServiceProvider services) : INotificationPublisher
{
    public async Task PublishAsync<TNotification>(
        TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);
        var behaviors = services.GetServices<INotificationPipelineBehavior<TNotification>>().ToArray();
        NotificationHandlerDelegate next = async () =>
        {
            // Resolve handlers only after passing through the pipeline, so
            // short-circuiting does not instantiate or execute subscribers.
            foreach (var handler in services.GetServices<INotificationHandler<TNotification>>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await handler.Handle(notification, cancellationToken);
            }
        };

        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var continuation = next;
            next = () => behavior.HandleAsync(notification, continuation, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await next();
    }
}
