# راهنمای Embedded Messaging

**وضعیت:** Implemented in PR — نیازمند Review و Merge

## محل کد

- `Application.Sharedkernel/Abstractions/Messaging/`: قراردادها
- `Infrastructure.Messaging/`: EF Outbox، Publisher، Registry، Worker، Hangfire
- `tests/Platform.Foundation.Messaging.Tests/`: تست‌ها

## آماده‌سازی DbContext ماژول

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.AddMessagingOutbox();
    base.OnModelCreating(modelBuilder);
}
```

**حتماً Migration ایجاد و اجرا کنید**؛ صرف اضافه کردن مدل، جدول ایجاد نمی‌کند:

```bash
dotnet ef migrations add AddMessagingOutbox --project Modules.YourModule --startup-project Web
dotnet ef database update --project Modules.YourModule --startup-project Web
```

پروژه مثال باید در عمل به DbContext ماژول مربوطه و Design-time Factory درست اشاره کند.

## قرارداد پیام

```csharp
[MessageContract("orders.created.v1")]
public sealed record OrderCreated(Guid OrderId) : IMessage;

public sealed class SendOrderNotificationHandler : IMessageHandler<OrderCreated>
{
    public Task HandleAsync(OrderCreated message, CancellationToken ct)
    {
        // Side effect must be idempotent; a message can be delivered again.
        return Task.CompletedTask;
    }
}
```

## ثبت در Web / Composition Root

```csharp
builder.Services.AddDbContext<YourDbContext>(o => o.UseSqlServer(connectionString));

builder.Services.AddPlatformMessaging<YourDbContext>(
    options =>
    {
        options.BatchSize = 50;
        options.MaxAttempts = 5;
    },
    typeof(SendOrderNotificationHandler).Assembly);

builder.Services.AddPlatformMessagingHangfire<YourDbContext>(connectionString);
```

اگر هنوز DbContext دامنه‌ای ندارید، `PlatformMessagingDbContext` آماده است و در `Web/Program.cs` فقط وقتی `ConnectionStrings:PlatformMessaging` تنظیم شده، فعال می‌شود. این حالت برای شروع مستقل است و **اتمیک بودن بین DbContext مستقل بیزینسی و Messaging را تضمین نمی‌کند**.

## انتشار

```csharp
public async Task Handle(CreateOrderCommand command, CancellationToken ct)
{
    db.Orders.Add(new Order { /* ... */ });
    await messages.PublishAsync(new OrderCreated(command.OrderId), ct);

    // Atomic: Order + OutboxMessage + OutboxDelivery in the SAME DbContext.
    await db.SaveChangesAsync(ct);
}
```

`PublishAsync` بدون `SaveChangesAsync` پیام را Durable نمی‌کند.

## Immediate Notifications

```csharp
await notificationPublisher.PublishAsync(new UserChangedNotification(userId), ct);
```

این عملیات از Channel/DB/Hangfire عبور نمی‌کند؛ Handlerهای `INotificationHandler<T>` در همان درخواست اجرا می‌شوند.

## پردازش و مدیریت

Hangfire Job با شناسه `platform.messaging.<DbContext>.dispatch` هر دقیقه pending deliveryها را بررسی می‌کند. عملیات هر Subscriber رسید مستقل دارد:

`Pending → Processing → Completed`

در شکست:
`Processing → Pending (Retry) → DeadLetter`

`IMessageDeliveryAdmin.RetryDeadLetterAsync(id)` برای Retry دستی تعریف شده؛ در API عمومی expose نشده و اگر بعداً Endpoint ساخته شد، باید با مجوز مدیریتی محدود شود.

به‌صورت پیش‌فرض Hangfire Dashboard به اینترنت ارائه نمی‌شود. برای دسترسی بعدی باید Authorization و محدودیت دسترسی اضافه شود.

## مسئولیت Idempotency

دریافت At-Least-Once است؛ حتی Completed شدن کار در بیرون DB ممکن است پیش از ثبت وضعیت آن به Crash برخورد کند. برای Email/SMS/Payment از Idempotency Key (ترجیحاً شناسه Delivery یا Message و نوع Handler) و طراحی Inbox یا Provider Key استفاده کنید.

## Test

```bash
dotnet test tests/Platform.Foundation.Messaging.Tests/Platform.Foundation.Messaging.Tests.csproj -c Release
```

**محدودیت نسخه اول:** SQL Server تحت Integration Test واقعی (با SQL Server عملیاتی) تست نشده و نیازمند پوشش CI مستقل است. تست‌های EF با SQLite و Build کل Solution انجام می‌شوند.

## نکات بعدی

Domain Event Routing، Ordering/Partitioning، Lease Heartbeat، Retention، Monitoring Dashboard، Message Schema Versioning و تست‌های Load.

## Migration دیتابیس مستقل Messaging

در حالت Standalone، یک Migration اولیه SQL Server همراه `Infrastructure.Messaging` ارائه می‌شود. قبل از راه‌اندازی با Connection String، آن را اجرا کنید:

```bash
# Environment variable: set it in the shell without committing secrets.
dotnet ef database update \
  --project Infrastructure.Messaging \
  --context PlatformMessagingDbContext
```

مقدار `PLATFORM_MESSAGING_CONNECTION_STRING` باید به همان دیتابیسی اشاره کند که در Host به‌عنوان `ConnectionStrings:PlatformMessaging` تنظیم شده است. `PlatformMessagingDesignTimeFactory` اجازه می‌دهد Migration بدون بالا آوردن Web/Hangfire اجرا شود.

**توجه:** این Migration فقط برای DbContext مستقل `PlatformMessagingDbContext` است؛ اگر `AddMessagingOutbox` را به DbContext یک ماژول اضافه کنید، Migration مخصوص همان ماژول باید جداگانه ساخته شود.
