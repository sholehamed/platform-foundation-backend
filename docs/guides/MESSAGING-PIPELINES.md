# راهنمای Pipeline برای Notification و Durable Message

**Platform Foundation — 2026-10-08**  
**محدوده:** `Application.Sharedkernel` و `Infrastructure.Messaging`

## ساختار سه زنجیره مستقل

1. `IDispatcher.Send/Query` از `IPipelineBehavior<TRequest,TResponse>` استفاده می‌کند. این قرارداد برای CQRS باقی می‌ماند.
2. `INotificationPublisher.PublishAsync` (و `IDispatcher.Publish` سازگار با نسخه قبل) از `INotificationPipelineBehavior<TNotification>` استفاده می‌کند: انتشار فوری، Fail-Fast، بدون Outbox.
3. `OutboxWorker` پس از Claim، برای **هر Subscriber مستقل** از `IMessagePipelineBehavior<TMessage>` استفاده می‌کند: تحویل ماندگار، خطا → Retry/Dead Letter.

ثبت پیش‌فرض هر دو Pipeline در `AddPlatformMessaging<TDbContext>()`:
`Diagnostics → FluentValidation → Custom Behaviors → Handler`.

ترتیب Custom Behaviorها بر اساس ترتیب ثبت در DI است؛ اولین Behavior بیرونی‌ترین است. در حالت معمول قبل/بعد Handler اجرا می‌شود.

## مثال: Notification Behavior

```csharp
public sealed class AuditNotificationBehavior<TNotification> :
    INotificationPipelineBehavior<TNotification>
    where TNotification : INotification
{
    public async Task HandleAsync(
        TNotification notification,
        NotificationHandlerDelegate next,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Before
        await next();
        // After: only runs if next() completes successfully
    }
}

// در Web / Composition Root، بعد از AddPlatformMessaging
builder.Services.AddTransient(
    typeof(INotificationPipelineBehavior<>),
    typeof(AuditNotificationBehavior<>));
```

Behavior می‌تواند `next()` را صدا نزند و کل Notification را Short-Circuit کند؛ در این حالت هیچ Subscriber اجرا نمی‌شود. اگر Handlerی Exception پرتاب کند، Exception به Publisher و فراخوان برمی‌گردد. Notification همچنان ماندگار نیست.

## مثال: Durable Message Behavior

```csharp
public sealed class AuditDeliveryBehavior<TMessage> :
    IMessagePipelineBehavior<TMessage>
    where TMessage : IMessage
{
    public async Task HandleAsync(
        TMessage message,
        MessageContext context,
        MessageHandlerDelegate next,
        CancellationToken ct)
    {
        // Context شامل MessageId، DeliveryId، Contract، HandlerKey، Attempt است.
        // به‌عنوان مثال می‌توان قبل از اجرای Handler، Tenant/Correlation را اعمال کرد،
        // اما انتقال و اعتبارسنجی آن اطلاعات هنوز پیاده‌سازی نشده است.
        await next();
    }
}

builder.Services.AddTransient(
    typeof(IMessagePipelineBehavior<>),
    typeof(AuditDeliveryBehavior<>));
```

- این زنجیره داخل Scope همان Job Hangfire اجرا می‌شود.
- هر Subscriber، `DeliveryId` و `Attempt` مختص خود را دارد.
- `IValidator<TMessage>` و `IValidator<TNotification>` از Assemblyهای اعلام‌شده در `AddPlatformMessaging` اسکن می‌شوند و `ValidateAsync` را با `CancellationToken` اجرا می‌کنند.
- `MessageValidationBehavior` در صورت خطا، `Application.SharedKernel.Exceptions.ValidationException` پرتاب می‌کند؛ Worker آن را طبق سیاست Retry/Dead Letter ثبت می‌کند. برای خطاهای دائمی می‌توان سیاست طبقه‌بندی Non-retryable را در فاز بعد افزود.
- Worker تنها پس از اجرای **موفق Handler** وضعیت `Completed` را ثبت می‌کند. اگر یک Behavior `next()` را نخواند یا خطای Handler را ببلعد، Delivery موفق محسوب نمی‌شود؛ استثنا به Worker می‌رسد و Retry می‌شود.
- `OutboxWorker` خودش مسئول Claim، Lease، وضعیت Delivery، Retry و Dead Letter است؛ این قسمت‌ها داخل Pipeline کاربر قرار نمی‌گیرند.
- Pipeline **جایگزین Idempotency واقعی** نیست. Handler باید برای اثر جانبی از Inbox/Idempotency Key یا قرارداد Provider استفاده کند.
- Messaging هنوز Tenant Context، CorrelationId پایدار، Ordering و Lease Heartbeat را به‌صورت خودکار منتقل نمی‌کند؛ افزودن آنها نیازمند تغییر Outbox Envelope و Migration است.

## تجربه عملی مسیر کامل

```csharp
[MessageContract("users.welcome.v1")]
public sealed record SendWelcomeMessage(Guid UserId) : IMessage;

public sealed class WelcomeHandler : IMessageHandler<SendWelcomeMessage>
{
    public Task HandleAsync(SendWelcomeMessage message, CancellationToken ct)
    {
        // با MessageId/DeliveryId در Behavior یا Inbox، اثر جانبی را Idempotent کنید.
        return Task.CompletedTask;
    }
}

public async Task RegisterUserAsync(CancellationToken ct)
{
    db.Users.Add(new User { /* ... */ });
    await publisher.PublishAsync(new SendWelcomeMessage(userId), ct);
    await db.SaveChangesAsync(ct); // Business + Outbox در همان DbContext
}
// سپس Hangfire → OutboxWorker → Message Pipeline → WelcomeHandler
```

برای Worker عملی باید Handler Assembly در `AddPlatformMessaging<YourDbContext>(..., typeof(WelcomeHandler).Assembly)` ثبت شده، Schema Outbox Migration شده و `AddPlatformMessagingHangfire<YourDbContext>(connectionString)` در Web فعال باشد.

## تست‌ها

`tests/Platform.Foundation.Messaging.Tests/MessagingPipelineTests.cs` سناریوهای ترتیب Behavior، Context تحویل، Validation، Short-Circuit، Cancellation، انتقال خطا به Worker و Dead Letter را تست می‌کند. تست‌های قبلی Outbox، Restart، Rollback و Migration نیز باقی‌اند.

```bash
dotnet test tests/Platform.Foundation.Messaging.Tests/Platform.Foundation.Messaging.Tests.csproj -c Release
dotnet build shinera-back.slnx -c Release
```
