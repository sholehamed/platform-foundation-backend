# ADR-005 — Domain Events و Messaging (طرح تصمیم، هنوز پیاده‌سازی نشده)

**وضعیت:** Proposed، نیازمند تصمیم معماری و تست Transaction  
**تاریخ:** 2026-10-09

## مسأله

در Foundation فعلی سه مسیر داریم:
- `IDispatcher.Send/Query` برای Command/Query و Pipeline.
- `INotificationPublisher` برای Notificationهای فوری داخل Process.
- `IMessagePublisher` برای Durable Message و Outbox/Hangfire.

`DispatchDomainEventsInterceptor` فعلی Domain Eventها را از EF Core جمع‌آوری می‌کند. زمان دقیق Delivery نسبت به Commit تراکنش خارجی، ذخیره تغییرات Handler و Error Recovery هنوز یک سیاست قراردادی صریح ندارد.

## تصمیم‌هایی که باید قبل از Coding قطعی شوند

1. **Before Commit Domain Events**: تغییرات داخل همان UnitOfWork/Transaction؛ Handler ممکن است state را تغییر دهد. خطر reentrancy، recursion و Handlerهای طولانی باید کنترل شود.
2. **After Commit Notifications**: Side Effectهای غیرحیاتی فوری، با این هشدار که در Crash ممکن است رویداد از دست برود؛ نباید برای ضمانت تحویل استفاده شود.
3. **Durable Integration Events**: Domain Event به Message پایدار با نام نسخه‌دار Map شود؛ رکورد Outbox **قبل از Commit و در همان DbContext/Transaction** ثبت شود، سپس Worker/Hangfire آن را منتشر کند.
4. **Subscriber Isolation و Idempotency**: هر Subscriber Delivery مستقل دارد؛ اجرای دقیقاً یک‌باره تضمین نمی‌شود. فرآیند باید idempotent باشد.
5. **Trace Propagation**: TraceParent/Correlation Context در Outbox ذخیره و زمان مصرف از همان Context استفاده شود، با کنترل Tenant isolation و عدم اعتماد به Context دستکاری‌شده.
6. **Ordering، Retries و Fault Semantics**: چه Handlerهایی می‌توانند خطا را به تراکنش اصلی برگردانند و کدام‌ها به Dead Letter می‌روند.

## پیشنهاد اولیه

```text
Command → Domain Aggregate raises IDomainEvent
             ↓
      UnitOfWork / SaveChanges
             ├─ transaction-safe domain handlers (optional, before commit)
             └─ DomainEventMapper → IMessagePublisher stage in SAME DbContext
                            ↓
                         Commit
                            ↓
                 Hangfire → Message Pipeline → Subscribers
```

**تا پیش از تصمیم نهایی، Interceptor فعلی و Domain Event Contract را تغییر نمی‌دهیم.** نباید هر `IDomainEvent` به‌صورت کورکورانه durable شود یا از `SaveChanges`، ارسال شبکه‌ای انجام گیرد.

## تست‌های پذیرش آینده

Rollback و عدم ارسال، crash بعد از Commit، failure/recursion در Domain Handler، چند Subscriber و Idempotency، Tenant Context، TraceParent و Transaction خارجی.
