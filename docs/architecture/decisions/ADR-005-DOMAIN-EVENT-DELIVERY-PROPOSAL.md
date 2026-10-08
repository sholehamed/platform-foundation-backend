# ADR-005 — Domain Events و Transactional Outbox

**وضعیت:** Accepted — F05 (2026-10-09)

## تصمیم
Eventهای Domain در Aggregate باقی می‌مانند و با `IDomainEvent` مشخص می‌شوند. هیچ Domain Eventای در `SavedChangesAsync` خودکار Notification خارجی نمی‌فرستد، زیرا این callback الزاماً بعد از Commit تراکنش بیرونی نیست.

در `SavingChangesAsync`، `IBeforeCommitDomainEventHandler<T>` روی Aggregateهای Track شده اجرا می‌شود؛ سپس `IDomainEventMessageMapper<T>` Message نسخه‌دار می‌سازد و `IOutboxMessageStager` آن را در **همان DbContext/Transaction** Stage می‌کند. پس از ذخیره موفق، Eventها Clear می‌شوند. در خطای Save، Outbox Stage شده Detach و Domain Eventها قابل Retry باقی می‌مانند.

انتشار فوری فقط از `INotificationPublisher` و صریحاً در Application صورت می‌گیرد؛ `IDispatcher.Send/Query` و Message Pipelineها بدون تغییر هستند.

W3C `TraceParent` در Outbox Persist می‌شود و Consumer Activity در Hangfire با Parent مناسب ساخته می‌شود. Message Handler باید Idempotent باشد، زیرا At-Least-Once برقرار است، نه Exactly-Once.

## سازگاری
این سیاست **Opt-in** است: `AddPlatformDomainEvents<TDbContext>(assemblies)` مسیر جدید را رجیستر و Interceptor قبلی `DispatchDomainEventsInterceptor` را در Context مربوط حذف می‌کند. برای Moduleهای دیگری که Opt-in نکرده‌اند رفتار Legacy قبلی باقی می‌ماند، همراه با ریسک SavedChanges/Commit timing. ترتیب DI را رعایت کنید؛ `AddPlatformDomainEvents` باید پس از `AddBaseInfrastructureServices` اجرا شود.

## مواردی که عمداً حل نشده‌اند
- `BeforeCommit` طبق این نام یعنی قبل از EF Save write، نه اجرای I/O داخل تراکنش. Handlerها نباید Nested SaveChanges بزنند.
- Rollback تراکنش بیرونی بعد از Save موفق، Events حافظه‌ای را احیا نمی‌کند؛ Domain aggregate باید Reload شود.
- Tenant/Branch Context در Durable Message به دلیل ریسک Isolation بدون قرارداد امنیتی منتقل نشده.
- Migration برای هر ماژول جداگانه ایجاد می‌شود؛ Migration افزایشی آمادهٔ مستقل به دیگر DbContextها خودکار اعمال نمی‌شود.
- AfterCommit Notification با Crash gap همراه است؛ برای تحویل قابل‌اتکا به Outbox Map کنید.
- SQL Server integration، benchmark، ordering subscription و idempotency external-provider همچنان گام‌های سخت‌سازی Production هستند.

## تست‌ها
`DomainEventIntegrationTests` و تست‌های قبلی Messaging / CQRS / HTTP / Observability در GitHub Actions اجرا می‌شوند.

[راهنمای پیاده‌سازی](../../guides/DOMAIN-EVENTS.md)
