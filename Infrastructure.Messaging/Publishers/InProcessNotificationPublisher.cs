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
        foreach (var handler in services.GetServices<INotificationHandler<TNotification>>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await handler.Handle(notification, cancellationToken);
        }
    }
}
