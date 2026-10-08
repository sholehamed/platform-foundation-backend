using System.Diagnostics;
using Application.SharedKernel.Abstractions.Messaging;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Messaging.Behaviors;

public sealed class MessageDiagnosticsBehavior<TMessage>(
    ILogger<MessageDiagnosticsBehavior<TMessage>> logger)
    : IMessagePipelineBehavior<TMessage> where TMessage : IMessage
{
    private static readonly ActivitySource Source = new("PlatformFoundation.Messaging");

    public async Task HandleAsync(
        TMessage message, MessageContext context,
        MessageHandlerDelegate next, CancellationToken cancellationToken)
    {
        using var activity = Source.StartActivity("message.deliver");
        activity?.SetTag("messaging.message_id", context.MessageId.ToString());
        activity?.SetTag("messaging.delivery_id", context.DeliveryId.ToString());
        activity?.SetTag("messaging.contract", context.Contract);
        activity?.SetTag("messaging.handler", context.HandlerKey);
        activity?.SetTag("messaging.attempt", context.Attempt);
        var start = Stopwatch.GetTimestamp();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await next();
            logger.LogDebug(
                "Delivery {DeliveryId} ({Contract}) finished after {ElapsedMs} ms",
                context.DeliveryId, context.Contract, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            logger.LogWarning(
                "Delivery {DeliveryId} ({Contract}) failed with {ExceptionType}",
                context.DeliveryId, context.Contract, exception.GetType().Name);
            throw; // Retry and dead-letter policies belong to OutboxWorker.
        }
    }
}
