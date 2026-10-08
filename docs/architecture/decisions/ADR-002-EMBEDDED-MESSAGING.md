# ADR-002 — Embedded Messaging با Hangfire و Transactional Outbox

**وضعیت:** Proposed / Implemented in PR برای بازبینی  
**تاریخ:** 2026-10-08

## مسئله

Platform Foundation به یک زیرساخت Publish/Subscribe ماندگار و Background Delivery نیاز دارد، بدون اجبار به Kafka/RabbitMQ یا اجرای یک Broker جداگانه. قراردادها باید در SharedKernel باقی بمانند و اجرای فنی در Infrastructure باشد.

## تصمیم پیشنهادی

1. `IMessage`، `IMessagePublisher`، `IMessageHandler<T>` و `INotificationPublisher` در `Application.Sharedkernel` باشند.
2. `INotificationPublisher` پیام را فوری و به‌صورت In-Process به Handlerهای ثبت‌شده ارسال کند و Fail-Fast باشد.
3. `Infrastructure.Messaging`، Outbox در همان `DbContext` عملیات اصلی قرار دهد؛ `PublishAsync` **فقط Stage می‌کند** و Commit را به Handler/Unit of Work می‌سپارد.
4. برای هر Subscriber یک `OutboxDelivery` مستقل ثبت شود تا موفقیت و خطای آن‌ها جدا باشد.
5. Hangfire مسئول برنامه‌ریزی و اجرای Worker در همان Host است. وضعیت Delivery در دیتابیس Messaging نگهداری می‌شود، نه صرفاً Hangfire Storage.
6. Worker با Claim اتمیک، Lease، Retry نمایی محدود و Dead Letter کار کند.
7. قرارداد پیام با `[MessageContract("name.version")]` ثابت باشد و Payload به Typeهای Whitelistشده در Registry Deserialize شود.
8. Delivery Mode برای این مسیر **At-Least-Once** است؛ Exactly-Once Side Effect تضمین نمی‌شود. Handlerها باید Idempotent طراحی شوند.
9. Dashboard عمومی Hangfire به‌صورت پیش‌فرض غیرفعال است.
10. Domain Event Dispatch فعلی **در این مرحله تغییر نمی‌کند**؛ تعامل Transactional/Post-Commit را در ADR بعدی تعریف می‌کنیم.

## تضمین‌ها و محدودیت‌ها

- Outbox زمانی همراه داده بیزینسی اتمیک Commit می‌شود که **دقیقاً در همان DbContext و همان تراکنش** ذخیره شود. استفاده از `PlatformMessagingDbContext` مستقل این تضمین را در کنار DbContext دیگر ایجاد نمی‌کند.
- `ExecuteUpdateAsync` برای Claim وابسته به پشتیبانی Provider رابطه‌ای است؛ این نسخه برای SQL Server طراحی و با SQLite تست می‌شود.
- پیام هنگام شکست Worker یا Hangfire در دیتابیس باقی می‌ماند و Poller در اجرای بعدی آن را پیدا می‌کند.
- Hangfire Recurring Job در این نسخه هر دقیقه اجرا می‌شود؛ تأخیر بیشتر از صف In-Memory است.
- Worker می‌تواند پس از انجام Side Effect و پیش از ثبت Completed متوقف شود؛ پیام دوباره تحویل می‌شود و نیاز به Idempotency دارد.
- Lease پس از انقضا قابل تصاحب مجدد است؛ در Handlerهای خیلی طولانی ممکن است تحویل هم‌زمان دوباره اتفاق بیفتد. Extension/Heartbeat Lease در فاز Hardening بررسی شود.
- Renaming یک Subscriber یا Contract بدون Migration، مانع پردازش backlog آن می‌شود.
- حذف/Retention و مدیریت Schema Evolution جزو فازهای بعدی هستند.
- در حالت چند ماژول با DbContext مستقل، هر Context نیازمند ثبت جداگانه Outbox Worker/Store و تنظیم مناسب است؛ ثبت فعلی یک Context را پوشش می‌دهد.

## گزینه‌های جایگزین

- `Channel<T>` صرفاً In-Memory: بدون تضمین پایداری بعد Restart.
- Hangfire Enqueue مستقیم: شکاف Commit/Enqueue برای تراکنش بیزینسی.
- RabbitMQ/Kafka: زیرساخت جدا و سربار عملیاتی بالاتر، در عوض امکانات Broker مستقل و Scale بیشتر.
- ساخت موتور Job Scheduler اختصاصی: هزینه بالای توسعه و نگهداری در مقایسه با Hangfire.

## تست‌های مورد انتظار

Rollback تراکنش، Restart، Multi-Subscriber Delivery، Retry/Dead Letter، Claim/Lease، ناشناخته‌بودن Contract، Logging و Build کل Solution.

## شرط پذیرش

اجرای CI، بازبینی امنیتی، تعریف Idempotency در Handlerها، مستندسازی Migration و تأیید این محدودیت‌ها.
