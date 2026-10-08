# ADR-004 — قراردادهای پایدار Monitoring و ذخیره‌سازی مستقل از Backend UI

**Status:** Implemented in F04 draft, pending review  
**Date:** 2026-10-09

## تصمیم

- Dashboard فقط در Angular پیاده می‌شود. Backend شامل Data Capture، Read Models، نسخه API و امنیت است؛ **هیچ UI، HTML Dashboard یا Hangfire Dashboard** به Backend اضافه نمی‌شود.
- قراردادهای Type-Safe در `Application.Sharedkernel/Observability/TelemetryContracts.cs` تعریف می‌شوند.
- Persistent Storage مستقل از DbContextهای بیزینسی در `Infrastructure.Observability` قرار می‌گیرد.
- ثبت Telemetry از مسیر synchronous درخواست به یک **bounded Channel غیرهم‌زمان** منتقل می‌شود؛ Worker آن را به جدول SQL منتقل می‌کند. در حالت نبود ظرفیت داده Drop می‌شود و Meter `foundation.telemetry.dropped` افزایش می‌یابد.
- داده‌های تاریخچه با Retention زمان‌دار حذف می‌شوند. ادعای At-Least-Once/Exactly-Once برای Telemetry نداریم؛ Crash، قطعی طولانی دیتابیس یا پر شدن Buffer می‌تواند Telemetry را از بین ببرد. این قابل قبول است زیرا تله‌متری نباید تراکنش بیزینسی را مختل کند.
- `IOperationTelemetrySink` قدیمی برای داشبورد Live حفظ می‌شود. در صورت فعال‌سازی SQL، `PersistentOperationTelemetrySink` داده را هم به Buffer Live و هم به صف پایدارکننده می‌فرستد.
- Backend سه Endpoint جدید read-only و مجوزدار اضافه می‌کند: `records`، `traces/{traceId}` و `summary` که پیش‌فرض غیرفعال هستند.

## Contract v1

`TelemetryKind = Operation | Log | Trace | Exception`.

`TelemetryItem` فیلدهای `Id`, `Timestamp`, `Kind`, `Source`, `Name`, `Level`, `DurationMs`, `Failed`, `Slow`, `TraceId`, `SpanId`, `ParentSpanId`, `ErrorType` دارد.

`TelemetryQuery`: `From`, `To`, `Kind`, `Source`, `Name`, `TraceId`, `Failed`, `Slow`, `Page`, `PageSize`.

`TelemetrySummary`: Total/Failed/Slow, Average/P50/P95/P99 Latency و Minute Buckets. Percentileها بر مبنای **حداکثر ۱۰۰۰۰ نمونه اخیر** محاسبه می‌شوند و `PercentilesSampled=true` بودن نشانگر Approximation است؛ شمارش و میانگین کل بازه از SQL محاسبه می‌شود. Trend نیز در نسخه اول نمونه‌ای است و برای نمودار دقیق یک بازه بسیار حجیم باید در نسخه بعد به SQL aggregation تبدیل شود.

## امنیت و دسترسی

- API تنها با `Observability:EnableFrontendReadApi = true` و در صورت فعال بودن SQL Persistence Map می‌شود؛ کل گروه `RequireAuthorization(PlatformFoundation.Observability.Read)` دارد.
- Claim لازم: `permission=platform.observability.read`. Permission باید در Identity Module/Role Assignment واقعی تنظیم شود؛ صرف وجود Endpoint کافی نیست.
- Log Provider از `ILogger` **متن فرمت‌شده، پارامترها، داده‌های State، Exception.Message و StackTrace را ذخیره نمی‌کند**؛ فقط Category، EventId.Name، Level و Exception Type را ثبت می‌کند. بنابراین Logهای فعلی صرفاً **Metadata** دارند؛ برای نمایش متن با کیفیت بالا در Angular بعداً به طراحی Redaction/Allowlist و قرارداد مجزا نیاز است.
- Traceها از `PlatformFoundation.Cqrs` و `PlatformFoundation.Messaging` جمع‌آوری می‌شوند؛ در کنار HTTP Correlation و `Operation`های ثبت‌شده، ارتباط اولیه TraceId را فراهم می‌کنند.
- برای جلوگیری از PII، `TelemetrySafety` متن‌ها را با Character Allowlist و Length Bound محدود می‌کند. هیچ SQL Raw، Request Body، Header، Password، Token، TenantId یا UserId به‌صورت خودکار Persist نمی‌شود.
- این روش تضمین جلوگیری از داده حساس در **Telemetry سفارشی توسعه‌دهنده** نیست؛ هر Source سفارشی باید بازبینی امنیتی شود.
- Trace Context ماندگار Outbox هنوز به این PR افزوده نشده؛ رابطه یک درخواست با Message پس از Worker ممکن است قطع باشد. Domain Events هم هنوز مهاجرت داده نشده‌اند.
- API محدوده زمان حداکثر ۳۰ روز و PageSize حداکثر ۱۰۰ دارد. Trace Detail حداکثر ۵۰۰ ردیف را بازمی‌گرداند. با این حال برای Production باید Rate Limit و Permissionهای Scopeدار هم اعمال شود.

## Migration و عملیات

- `TelemetryDbContext` یک Context مستقل است و Migration اولیه SQL Server در `Infrastructure.Observability/Persistence/Migrations/` موجود است.
- برای حالت SQL نیاز به `ConnectionStrings:Telemetry` و `PLATFORM_TELEMETRY_CONNECTION_STRING` در زمان اجرای Migration وجود دارد.
- داده‌ها در `PlatformTelemetryEntries` ذخیره می‌شوند، با Indexهای timestamp/kind/traceId/failed.
- `TelemetryRetentionWorker` هر ساعت رکوردهای قدیمی‌تر از `RetentionDays` را حذف می‌کند. Retention از Startup بلافاصله اجرا نمی‌شود.
- این Adapter برای SQL Server نوشته شده و SQL Query/Retention/Worker با SQLite به‌صورت integration test بررسی می‌شوند. تست با SQL Server واقعی لازم است.

## محدودیت‌های آگاهانه

- Nonblocking best-effort یعنی از دست دادن Telemetry قابل قبول است، اما برای Audit امنیتی **قابل قبول نیست**.
- برای دریافت Traceهای کامل HTTP/DB با Span و Attribute، نیاز به OpenTelemetry Collector یا Processor قابل تنظیم و سیاست Redaction جداگانه داریم.
- Aggregation چند-Replica از طریق SQL مشترک امکان Query دارد، اما Queue هر Host مجزاست؛ شناسه Host و Partitioning هنوز به Contract افزوده نشده.
- API جست‌وجو Offsets را استفاده می‌کند؛ برای حجم بالا باید Cursor pagination جایگزین شود.
- `IMessagePublisher`، Domain Events و Pipeline رفتار بیزینسی تغییر داده نشده‌اند.

## پیوند به مرحله بعد

Frontend به‌عنوان Repository مستقل `platform-foundation-frontend` با Angular، Material، Router، Localization/RTL و typed API client شروع می‌شود. قبل از آن باید سیاست Event Routing را مشخص کنیم: Domain Event قبل/بعد Commit، Notification فوری و Durable Message با Transactional Outbox. این تصمیم در ADR-005 جداگانه مستند می‌شود.
