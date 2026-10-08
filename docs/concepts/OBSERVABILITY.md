# Observability در Platform Foundation

**وضعیت:** زیرساخت اولیه Backend در F03 پیاده‌سازی شده؛ Dashboard Angular برنامه‌ریزی شده است.

هدف ما نظارت یکپارچه روی Application، CQRS، EF Core، Messaging و HTTP است؛ **بدون ایجاد UI در Backend**.

- **Log:** `ILogger` ساختاریافته با Scope شامل CorrelationId و TraceId. فقط نام عملیات و اندازه‌گیری ثبت می‌شود؛ Payloadها و SQL حساس حذف شده‌اند.
- **Trace:** `ActivitySource` برای CQRS و Messaging، instrumentation برای ASP.NET Core و HttpClient، Event مدت زمان EF Core روی Span جاری.
- **Metric:** Meter واحد `PlatformFoundation.Operations`؛ Histogram مدت اجرای عملیات و Counter نتیجه.
- **Performance:** `RequestPerformanceBehavior`، `NotificationPerformanceBehavior`، `MessagePerformanceBehavior` و `DbTimingInterceptor` با Threshold قابل تنظیم.
- **Monitoring API:** `IObservabilityReader` و Endpoint امن JSON (خاموش به‌صورت پیش‌فرض) برای داشبورد Angular آینده.

تصمیم کامل در [ADR-003](../architecture/decisions/ADR-003-FRONTEND-FIRST-OBSERVABILITY.md) و راهنمای ثبت سرویس و API در [OBSERVABILITY-INTEGRATION](../guides/OBSERVABILITY-INTEGRATION.md) قرار دارد.

## محدودیت‌ها

هنوز ذخیره طولانی‌مدت، چند Replica، TraceParent ماندگار بین Outbox/Worker، Alerting و Dashboard Frontend پیاده‌سازی نشده‌اند. Performance Sink فعلی نمونه‌های محدود را در RAM نگه می‌دارد؛ OTLP اختیاری است.

## اولویت‌های بعدی

1. اضافه‌کردن Storage پایدار و Retention و خروجی Query برای frontend با Authorization کامل.
2. برقراری Correlation بین HTTP، Command، Outbox و Hangfire (trace context durable).
3. تکمیل داشبورد Angular با Overview، Tracing، Slow Operations، Exceptions و وضعیت Jobها.
4. تست یکپارچه SQL Server، بار و امنیت. هیچ Endpoint مدیریتی Hangfire نباید بدون مجوز در دسترس قرار گیرد.
