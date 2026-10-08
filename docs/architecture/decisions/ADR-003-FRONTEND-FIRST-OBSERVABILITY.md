# ADR-003 — Observability با Dashboard در Angular، نه Backend

**وضعیت:** Implemented in F03 (initial foundation)  
**تاریخ:** 2026-10-09

## تصمیم

Platform Foundation یک محصول Full-stack است. **بک‌اند هیچ UI، Dashboard، Embedded HTML یا Endpoint عمومی مشاهده Log ندارد.** داشبورد کامل Monitoring/Tracing به‌عنوان بخشی از اپلیکیشن Angular، با زبان طراحی و دسترسی‌های همان اپ، توسعه می‌یابد.

بک‌اند صرفاً مسئول جمع‌آوری داده، اندازه‌گیری، Correlation، ذخیره موقت/قابل تعویض و API امن است.

## معماری

```text
HTTP → Correlation middleware → Dispatcher
                          │
                          ├─ RequestDiagnostics → RequestPerformance → Validation → Handler
                          ├─ EF Core DbTimingInterceptor
                          └─ IMessagePublisher → Outbox → Hangfire
                                                       └─ MessageDiagnostics
                                                           → MessagePerformance
                                                           → Validation → Handler

Telemetry: ActivitySource + System.Diagnostics.Metrics
                    │
                    ├─ BoundedObservabilityStore (volatile, one process)
                    │    └─ IObservabilityReader → authorized JSON API
                    │                                    └─ Angular dashboard (separate work)
                    └─ Optional OTLP exporter (traces/metrics/logs)
```

## محدودیت‌ها و هشدارهای معماری

1. `BoundedObservabilityStore` **In-Memory و غیرماندگار** است؛ داده‌ها پس از Restart از دست می‌روند و هر Replica فقط داده‌های خودش را دارد. برای محیط Production و داشبورد چندسروری، Adapter ذخیره پایدار و Aggregation لازم است.
2. Endpoint `/api/platform/observability/snapshot` **به‌صورت پیش‌فرض Map نمی‌شود**. فعال‌سازی آن نیازمند احراز هویت واقعی، سیاست `PlatformFoundation.Observability.Read` و Claim دقیق `permission=platform.observability.read` است.
3. هیچ Job، Query، Command، UserId، TenantId، Parameter SQL، Token، ConnectionString یا Request Body داخل Sample ثبت نمی‌شود؛ فقط نام Type و Category، زمان، Outcome و TraceId.
4. مدت زمان EF صرفاً `CommandExecutedEventData.Duration` را بازنمایی می‌کند، نه کل latency شبکه، materialization یا صف SQL. از CommandText/پارامترها استفاده نمی‌کنیم.
5. Trace HTTP و CQRS در طول `Activity.Current` متصل است؛ **انتقال durable TraceParent از Outbox به Worker هنوز پیاده‌سازی نشده** و نیازمند تغییر Envelope و Migration است.
6. پردازش Telemetry Best Effort است؛ خرابی Sink درخواست اصلی را متوقف نمی‌کند. Exporter OTLP فقط با opt-in فعال می‌شود.
7. `Stopwatch` مدت اجرای Pipeline را با Validation/Handler اندازه می‌گیرد، نه تفکیک داخلی آن. پیکربندی Thresholdها میلی‌ثانیه و قابل تنظیم است.
8. هر Instrument با نام Type ثابت و Outcome کم‌تنوع برچسب‌گذاری شده؛ User/Tenant/MessageId به Metric Labels افزوده نشده‌اند.
9. Audit Trail الزام جداگانه‌ای دارد؛ Metrics/Logs جایگزین Audit امنیتی یا تراکنشی نیستند.
10. Frontend Dashboard باید روی APIهای مجوزدار تکیه کند، نه Hangfire Dashboard؛ Hangfire UI هم در Host به‌صورت پیش‌فرض وجود ندارد.

## مسیر توسعه

- اکنون: Performance Behaviors، Structured Warnings، HTTP Correlation، EF timings، OTel opt-in، Snapshot Reader و API امن.
- آینده Backend: ذخیره پایدار با Retention و Pagination، چندسروری، TraceParent Outbox، هشدارها و فیلترهای دقیق، بررسی Tenant Scope.
- آینده Angular: Overview، Latency (P50/P95/P99)، Error Rate، Slow Operations، Traces، Exceptions، Job Health و فیلترها؛ با Authorization واقعی و RTL فارسی.
- پیش از Production: آزمون یکپارچه SQL Server/OTLP، سیاست داده حساس، Benchmark زیر بار و عدم دسترسی غیرمجاز.
