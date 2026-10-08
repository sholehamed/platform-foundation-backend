using Domain.SharedKernel.Common.Events;

namespace Application.SharedKernel.Abstractions.Messaging;

/// <summary>Immediate, in-process notification delivery; no durability guarantee.</summary>
public interface INotificationPublisher
{
    Task PublishAsync<TNotification>(
        TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
