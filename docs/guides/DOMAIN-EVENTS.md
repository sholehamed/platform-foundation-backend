# F05 — راهنمای Domain Events و Transactional Outbox

## هدف و مرزبندی
این قابلیت در Backend و در همان Modular Monolith اجرا می‌شود؛ هیچ Dashboard یا UI سمت Backend اضافه نشده است. مسیر CQRS و Notification همچنان مستقل است.

| مسیر | چه زمانی | تضمین |
| --- | --- | --- |
| `ICommandHandler/IQueryHandler` | در Pipeline جاری | پاسخ هم‌زمان |
| `IBeforeCommitDomainEventHandler<T>` | `SavingChangesAsync`، **قبل از EF SQL write** | Exception باعث Abort شدن همان Save می‌شود |
| `IDomainEventMessageMapper<T>` | `SavingChangesAsync`، بعد از BeforeCommit Handler | `IMessage` در Outbox **همان DbContext** Stage می‌شود |
| `INotificationPublisher` | **فراخوانی صریح Application** پس از Commit در صورت نیاز | فوری، بدون تحویل تضمین‌شده |
| `OutboxWorker + IMessageHandler<T>` | پس از قابل مشاهده شدن رکورد Committed | Retry/Dead Letter، At-Least-Once |

**EF SavedChangesAsync برابر Commit نیست.** اگر تراکنش بیرونی هنوز باز باشد، رکورد Outbox در همان تراکنش قرار دارد و Worker آن را تا Commit نمی‌بیند. بنابراین هیچ انتشار خارجی/Notification خودکاری در SavedChanges انجام نمی‌شود.

## ثبت سرویس‌ها

ابتدا سرویس‌های اصلی Context (از جمله Audit Interceptors) را رجیستر کنید، سپس Messaging و Domain Event routing را:

```csharp
// Module composition root:
services.AddBaseInfrastructureServices<OrderDbContext>(configuration, "Orders");
services.AddPlatformMessaging<OrderDbContext>(
    options => options.MaxAttempts = 5,
    typeof(CreateOrderHandler).Assembly);
services.AddPlatformDomainEvents<OrderDbContext>(
    typeof(CreateOrderHandler).Assembly);

// In OrderDbContext.OnModelCreating:
modelBuilder.AddMessagingOutbox();

// Host composition root after a real SQL connection exists:
services.AddPlatformMessagingHangfire<OrderDbContext>(connectionString);
```

**ترتیب مهم است:** `AddPlatformDomainEvents` به‌شکل Opt-in، Interceptor قدیمی `DispatchDomainEventsInterceptor` را از DI حذف می‌کند، اما Audit Interceptor را نگه می‌دارد. اگر DbContext را با `AddDbContext` دستی ثبت می‌کنید، Interceptorها را صریح به EF اضافه کنید:

```csharp
services.AddDbContext<OrderDbContext>((sp, options) =>
{
    options.UseSqlServer(connectionString);
    options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
});
```

همهٔ DbContextها باید مدل Outbox و Migration **ویژه همان ماژول** را داشته باشند. Migration نسخهٔ مستقل `PlatformMessagingDbContext` فقط برای Context مستقل است و خودبه‌خود به `OrderDbContext` منتقل نمی‌شود.

## تعریف Event، Mapper و Handler

```csharp
public sealed record OrderCreated(Guid OrderId, DateTime OccurredOn) : IDomainEvent;

[MessageContract("orders.created.v1")]
public sealed record OrderCreatedMessage(Guid OrderId) : IMessage;

// Handler مجاز به mutate کردن Entityهای Track شده است.
// خودش SaveChanges/HTTP/SMS/Publish بیرونی نمی‌زند.
public sealed class CreateOrderRules : IBeforeCommitDomainEventHandler<OrderCreated>
{
    public Task HandleAsync(OrderCreated evt, CancellationToken ct)
    {
        // Validate in-memory invariants; throw to reject the Save.
        return Task.CompletedTask;
    }
}

public sealed class OrderCreatedMapper : IDomainEventMessageMapper<OrderCreated>
{
    public IReadOnlyCollection<IMessage> Map(OrderCreated evt)
        => [new OrderCreatedMessage(evt.OrderId)];
}

public sealed class OrderCreatedHandler : IMessageHandler<OrderCreatedMessage>
{
    public Task HandleAsync(OrderCreatedMessage message, CancellationToken ct)
    {
        // Subscriber delivery occurs only after the outbox row commits.
        // External effects require idempotency (DeliveryId/provider idempotency key).
        return Task.CompletedTask;
    }
}

// The aggregate calls AddDomainEvent(new OrderCreated(...))
// on its domain operation, NOT from EF materialization constructors.
await db.SaveChangesAsync(ct);
```

Mapping هر Event به صفر یا چند Durable Message مجاز است. Mapper باید Deterministic باشد و I/O نداشته باشد. Eventهایی که هیچ Handler/Mapper ندارند بعد از Save موفق Clear می‌شوند؛ در صورت نیاز به تحویل، Mapper/Handler صریح اضافه کنید.

## Failure و Retry

- `SaveChangesAsync` اگر BeforeCommit Handler یا Mapper خطا بدهد، Save متوقف می‌شود؛ Domain Event روی Aggregate باقی می‌ماند.
- در خطای ذخیره دیتابیس، ردیف‌های Outbox که این Interceptor Stage کرده از ChangeTracker Detach می‌شوند تا Retry رکورد اضافی نسازد.
- بعد از `SaveChangesAsync` موفق Eventها پاک می‌شوند، **حتی اگر تراکنش بیرونی بعداً Rollback شود**. Rollback داده بیزینسی و Outbox را با هم بازمی‌گرداند، ولی Domain Event در حافظه دوباره احیا نمی‌شود؛ برای تلاش دوباره روی همان Aggregate پس از Rollback باید State را Reload/بازسازی کنید.
- `SaveChanges()` هم‌زمان با Domain Event پردازش‌نشده با خطای واضح رد می‌شود؛ از `SaveChangesAsync` استفاده کنید.
- SaveChanges تودرتو در Handlerها پشتیبانی نمی‌شود.
- بعد از Commit، Worker هر Subscriber را مستقل، با Lease/Retry/Dead Letter اجرا می‌کند. Exactly-Once ادعا نمی‌شود.
- اگر به Notification فوری پس از Commit نیاز دارید، **پس از `CommitAsync`** خود Application از `INotificationPublisher.PublishAsync` استفاده کند، با پذیرش احتمال Loss در Crash. برای الزام Reliability از Outbox استفاده کنید.

## Trace و امنیت

فیلد nullable و حداکثر ۵۵ کاراکتری `TraceParent` (W3C) در `MessagingOutboxMessages` ثبت می‌شود. Worker هنگام Consume یک Activity جدید با Parent همان Trace ایجاد می‌کند؛ Pipelineهای Diagnostics فرزند آن می‌شوند. هیچ UserId/TenantId، Header خام یا Request Body به Envelope اضافه نشده است؛ انتقال Tenant Context باید با سیاست اعتماد/اعتبارسنجی مجزا طراحی شود.

Migration افزایشی Context مستقل: `20261009150000_AddOutboxTraceParent`. برای Context ماژول، Migration همان DbContext را تولید کنید. این کار امکان Traceهای مرتبط در فرانت Angular را فراهم می‌کند، ولی **رابطه‌دهی Tenant و Scope مجوزها هنوز باید تکمیل شود**.

## تست‌ها و محدودیت‌ها

`tests/Platform.Foundation.Messaging.Tests/DomainEventIntegrationTests.cs`: تراکنش و Rollback، اجرای BeforeCommit، Fail/Retry، Sync guard، Notification دستی، TraceParent و Migration. تست SQLite جایگزین آزمون SQL Server واقعی نمی‌شود.

پیشنهادهای بعدی: تست SQL Server در CI، Tenant Context امن، idempotent external adapters، کنترل چند instance و trace linking برای retryهای پیچیده.

## Pipeline مستقل Domain Events

`AddPlatformDomainEvents<TDbContext>` قبل از `IBeforeCommitDomainEventHandler<T>` و `IDomainEventMessageMapper<T>`، رفتارهای `DomainEventDiagnosticsBehavior<T>` و `DomainEventValidationBehavior<T>` را رجیستر می‌کند. Validators از Assemblyهای اعلام‌شده پیدا می‌شوند.

```csharp
public sealed class OrderCreatedValidator : AbstractValidator<OrderCreated>
{
    public OrderCreatedValidator() =>
        RuleFor(x => x.OrderId).NotEmpty();
}

public sealed class TenantGuardDomainBehavior<T> : IDomainEventPipelineBehavior<T>
    where T : IDomainEvent
{
    public Task<IReadOnlyList<IMessage>> HandleAsync(
        T domainEvent, DomainEventHandlerDelegate next, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return next();
    }
}

// پس از AddPlatformDomainEvents:
services.AddTransient(
    typeof(IDomainEventPipelineBehavior<>),
    typeof(TenantGuardDomainBehavior<>));
```

Domain Event Pipeline برخلاف Notification Pipeline اجازه Silent Short-Circuit ندارد: اگر `next()` صدا زده نشود یا خطا بلعیده شود، Save موفق نمی‌شود و Event حفظ می‌شود. اجرای چندباره Terminal نیز رد می‌شود.

آستانهٔ عملکرد Domain Event با `Observability:Performance:SlowDomainEventThresholdMs` (پیش‌فرض ۵۰۰ میلی‌ثانیه) تنظیم می‌شود؛ فقط نام Type و مدت زمان در Structured Logs / Metrics ثبت می‌شود، نه Payload.
