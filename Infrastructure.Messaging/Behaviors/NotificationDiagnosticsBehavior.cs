using System.Diagnostics;
using Application.SharedKernel.Abstractions.Messaging;
using Domain.SharedKernel.Common.Events;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Messaging.Behaviors;

public sealed class NotificationDiagnosticsBehavior<TNotification>(
    ILogger<NotificationDiagnosticsBehavior<TNotification>> logger)
    : INotificationPipelineBehavior<TNotification> where TNotification : INotification
{
    private static readonly ActivitySource Source = new("PlatformFoundation.Messaging");

    public async Task HandleAsync(
        TNotification notification, NotificationHandlerDelegate next,
        CancellationToken cancellationToken)
    {
        using var activity = Source.StartActivity("notification.publish");
        activity?.SetTag("messaging.operation", "notification");
        activity?.SetTag("messaging.message_type", typeof(TNotification).Name);
        var start = Stopwatch.GetTimestamp();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await next();
            logger.LogDebug("Notification {NotificationType} finished in {ElapsedMs} ms",
                typeof(TNotification).Name, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            logger.LogWarning(
                "Notification {NotificationType} failed with {ExceptionType}",
                typeof(TNotification).Name, exception.GetType().Name);
            throw;
        }
    }
}
