# راهنمای Backend → Angular Monitoring API (v1)

## وضعیت

این API فقط **JSON** است و Dashboard آن در فرانت Angular پیاده خواهد شد. تمام Endpointها طبق Policy `PlatformFoundation.Observability.Read` مجوزدار هستند و در Backend هیچ صفحه داشبوردی وجود ندارد.

## فعال‌سازی

در Host:
```json
{
  "ConnectionStrings": {
    "Telemetry": "<set-through-environment-or-user-secrets>"
  },
  "Observability": {
    "EnableFrontendReadApi": true,
    "Persistence": {
      "Enabled": true,
      "QueueCapacity": 2000,
      "BatchSize": 100,
      "RetentionDays": 7,
      "CaptureSanitizedLogs": true,
      "CaptureFoundationTraces": true
    }
  }
}
```

Connection String را **در ریپو Commit نکنید**. در Environment یا Secret Manager:
```text
ConnectionStrings__Telemetry
PLATFORM_TELEMETRY_CONNECTION_STRING
```

قبل از فعال‌سازی Worker، Migration را اجرا کنید:
```bash
dotnet ef database update --project Infrastructure.Observability --context TelemetryDbContext
```

`PLATFORM_TELEMETRY_CONNECTION_STRING` در محیط CLI همان Connection String مورد استفاده Web باشد. SQL Server Schema Migration این پروژه مستقل است و به Migration ماژول‌های بیزینسی کاری ندارد.

همچنین قبل از `EnableFrontendReadApi=true` باید Authentication در Web فعال شود و Permission `platform.observability.read` برای نقش‌های مجاز وجود داشته باشد. از UI Angular با Token معتبر استفاده کنید.

## Endpointهای Angular

| HTTP | Route | کاربرد |
|---|---|---|
| GET | `/api/platform/observability/snapshot?minutes=15&limit=50` | نمای سریع حافظه‌ای، موجود در F03 |
| GET | `/api/platform/observability/records?from=...&to=...&kind=Operation&page=1&pageSize=50` | تاریخچه Log/Exception/Operation/Trace، جست‌وجو و صفحه‌بندی |
| GET | `/api/platform/observability/traces/{traceId}?from=...&to=...` | ترتیب رویدادهای یک Trace |
| GET | `/api/platform/observability/summary?from=...&to=...` | Overview و Latency Metrics |

`kind`: `Operation`, `Log`, `Trace`, `Exception`. تاریخ‌ها ISO 8601 و در Storage UTC هستند. پیش‌فرض بازه ۱ ساعت است (Trace Detail یک روز). PageSize ۱ تا ۱۰۰، صفحه ۱ تا ۱۰۰۰۰، بازه حداکثر ۳۰ روز. Query نامعتبر: HTTP 400. API برای کاربر بدون Permission کدهای استاندارد 401/403 دارد.

### نمونه پاسخ تاریخچه

```json
{
  "items": [
    {
      "id": "67bb9d67-cb4c-4f5a-817a-1ad3da94a01e",
      "timestamp": "2026-10-09T00:00:00Z",
      "kind": 0,
      "source": "cqrs",
      "name": "CreateSampleCommand",
      "level": null,
      "durationMs": 35.5,
      "failed": false,
      "slow": false,
      "traceId": "0123456789abcdef0123456789abcdef",
      "spanId": null,
      "parentSpanId": null,
      "errorType": null
    }
  ],
  "total": 1,
  "page": 1,
  "pageSize": 50
}
```

`kind` در JSON به‌صورت **عدد Enum** ارسال می‌شود؛ مدل TypeScript باید Enum عددی متناظر داشته باشد. برای خواندن نام بهتر در UI، Mapping سمت Angular پیاده می‌کنیم.

### نکات Frontend

- Overview می‌تواند از `summary` استفاده کند؛ `snapshot` برای Live/Polling سریع در همان Instance است.
- Logs/Exceptions: `records?kind=Log` و `records?kind=Exception`.
- Traces: `records?kind=Trace` و سپس `traces/{traceId}`. ارتباط با Outbox تا زمان افزودن TraceParent کامل نیست.
- `TelemetrySummary.PercentilesSampled` یعنی Latency Percentiles و Trend از حداکثر ۱۰هزار نمونه اخیر گرفته شده‌اند و دقیقِ کل بازه نیستند.
- داده Logها در نسخه اول فقط Metadata است؛ متن Log یا StackTrace برای جلوگیری از نشت داده حساس ذخیره نمی‌شود.
- `TelemetryDbContext` می‌تواند با SQL مشترک تاریخچه چند Instance را Query کند، اما Queue ورودی هر Instance محلی و best effort است.
- حتماً Error States، Empty States و Permission Guard را در Angular لحاظ کنید؛ از Polling مکرر بدون Backoff خودداری کنید.

## توسعه API

قراردادها در `Application.Sharedkernel/Observability/TelemetryContracts.cs` و APIها در `Web.Sharedkernel/Observability/ObservabilityHistoryApiExtensions.cs` تعریف شده‌اند.

اگر فیلدی به UI اضافه می‌کنیم، ابتدا Contract و تست Integration API را به‌روزرسانی و بعد Client TypeScript را Generate می‌کنیم. DTO/Queryهای Backend نباید مستقیم به EF Entity وابسته شوند.

## Eventها و Outbox

Domain Eventها هنوز به Messaging متصل نشده‌اند. قبل از باز شدن Workflow آن‌ها باید روشن کنیم Handler داخل UnitOfWork اجرا شود یا خروجی Integration Event بعد از Commit باشد؛ طراحی مکمل در ADR-005.
