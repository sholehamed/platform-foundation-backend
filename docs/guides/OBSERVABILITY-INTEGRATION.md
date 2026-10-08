# راهنمای اتصال Observability بین .NET 10 و Angular

## اصل طراحی

**هیچ Dashboard در Backend ساخته نمی‌شود.** تمام UI مانیتورینگ باید در Angular اجرا شود؛ بک‌اند فقط API امن، ابزار اندازه‌گیری و خروجی استاندارد فراهم می‌کند.

## سرویس‌ها و مسیرهای اصلی

- `Application.Sharedkernel/Observability`: `PerformanceOptions`، `IOperationTelemetrySink`، `IObservabilityReader` و مدل‌های تایپ‌دار.
- `Application.Sharedkernel/Behaviors/RequestPerformanceBehavior`: هشدار کندی CQRS.
- `Infrastructure.Messaging/Behaviors/*PerformanceBehavior`: Notification و Delivery.
- `Infrastructure.Sharedkernel/Observability/DbTimingInterceptor`: اندازه‌گیری EF Core بدون SQL و پارامتر.
- `Infrastructure.Observability`: Store حافظه‌ای محدود و پیکربندی OTel.
- `Web.Sharedkernel/Observability`: Correlation middleware و Endpointهای JSON مجوزدار.
- `tests/Platform.Foundation.Observability.Tests`: تست‌ها.

## پیکربندی

```json
{
  "Observability": {
    "ServiceName": "PlatformFoundation.Api",
    "RecentSampleCapacity": 5000,
    "EnableFrontendReadApi": false,
    "Performance": {
      "SlowRequestThresholdMs": 500,
      "SlowNotificationThresholdMs": 500,
      "SlowMessageThresholdMs": 1000,
      "SlowDatabaseThresholdMs": 250
    },
    "Otlp": { "Enabled": false }
  }
}
```

`Web` با `AddPlatformObservability(configuration)` کار می‌کند و `UsePlatformCorrelation()` را به Pipeline اضافه می‌کند. خروجی OTLP اختیاری است؛ در صورت فعال‌سازی، از متغیرهای امن محیطی مانند `OTEL_EXPORTER_OTLP_ENDPOINT` و تنظیمات احراز هویت Collector استفاده کنید. Credentials در appsettings عمومی قرار نگیرند.

Pipelineها خودکار در ثبت `AddCustomCqrs` و `AddPlatformMessaging` فعال‌اند و آستانه‌هایشان از `PerformanceOptions` مشترک می‌آید.

برای EF Core باید DbContext از `AddBaseInfrastructureServices<TDbContext>` استفاده کند؛ Contextهایی که مستقلاً رجیستر شده‌اند، باید `DbTimingInterceptor` را صریح اضافه کنند.

## قرارداد امن برای Angular

Host باید Authentication معتبر نصب کرده باشد و پس از آن امکان نمایش API را با `Observability:EnableFrontendReadApi=true` فعال کند.

```http
GET /api/platform/observability/snapshot?minutes=15&limit=50
Authorization: Bearer <token-with-platform.observability.read>
X-Correlation-ID: ui-monitoring-8cf3
```

**Policy موردنیاز:** `PlatformFoundation.Observability.Read` با Claim دقیق `permission=platform.observability.read`. صرف ورود به سیستم برای دیدن Telemetry کافی نیست.

نمونه پاسخ:

```json
{
  "generatedAt": "2026-10-09T00:00:00Z",
  "windowStart": "2026-10-08T23:45:00Z",
  "total": 12,
  "failed": 1,
  "slow": 3,
  "averageDurationMs": 81.2,
  "p95DurationMs": 225.5,
  "operations": [
    {
      "category": "cqrs",
      "name": "CreateSampleCommand",
      "count": 12,
      "failed": 1,
      "slow": 3,
      "averageDurationMs": 81.2,
      "p95DurationMs": 225.5
    }
  ],
  "recent": []
}
```

عددها فقط **نمونه قرارداد** هستند و خروجی واقعی از عملیات پردازش‌شده توسط Host محاسبه می‌شود. `minutes` بین ۱ تا ۱۴۴۰ و `limit` بین ۰ تا ۲۰۰ قابل تنظیم است. `X-Correlation-ID` ورودی فقط در صورت داشتن حداکثر ۶۴ نویسه مجاز پذیرفته می‌شود، در غیر این صورت مقدار جدید ایجاد می‌شود.

در Angular از یک `ObservabilityService` تایپ‌دار با Interceptor احراز هویت، Polling کنترل‌شده و Angular Material Chart استفاده می‌کنیم. API فعلی برای Overview و Slow Operations اولیه مناسب است؛ Logs/Traces کامل، Pagination و Alerting هنوز به قراردادهای جداگانه نیاز دارند.

## بررسی محلی و تست

```bash
dotnet test tests/Platform.Foundation.Observability.Tests -c Release
dotnet build shinera-back.slnx -c Release
```

نکات مهم:
- OTel exporter بدون فعال‌سازی، Collector جدا نمی‌خواهد.
- Store کنونی volatile/per-process است و برای داشبورد Production کافی نیست.
- SQL command text، query arguments، body و token عمداً وارد نمونه‌های عملکرد نمی‌شوند.
- بازه Dashboard باید با Scope دسترسی داده‌ها و Retention هماهنگ باشد.
- Context اجرایی Hangfire هنوز نیاز به انتشار TraceParent در Outbox دارد.
