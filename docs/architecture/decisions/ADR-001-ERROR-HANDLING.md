# ADR-001 — قرارداد Exception Handling و HTTP Error

**وضعیت:** Proposed (Implementation for Review)  
**تاریخ:** 2026-10-08  
**حوزه:** Application، HTTP API، Validation، Security، Observability

## مسئله

در Foundation فعلی، `ValidationException`، `ServiceExeption` و `ForbiddenAccessException` داریم اما مدیریت یکپارچه HTTP برای آن‌ها وجود نداشت. Command Handlerها نباید مسئول تعیین کد HTTP یا ساخت پاسخ کلاینت باشند. برای Angular هم یک شکل قابل پیش‌بینی خطا لازم است.

## گزینه‌ها

1. Result-first برای تمامی شکست‌ها: قرارداد صریح اما تغییر گسترده Handlerها.
2. Exception-first: خطاهای شناخته‌شده با Exception تایپ‌شده و یک Global Handler.
3. ترکیب دو مسیر: فعلاً پیچیدگی و دوگانگی بیشتر بدون نیاز روشن.

## تصمیم پیشنهادی

از Exception-first استفاده کنیم. `AppException` با `Code` و `AppErrorType` در Application قرار می‌گیرد. HTTP status صرفاً در Web نگاشت می‌شود. `ValidationBehavior`، `ValidationException` اختصاصی را تولید می‌کند و Global Handler آن را به HTTP 400 تبدیل می‌کند.

خروجی HTTP با `ProblemDetails` استاندارد است؛ با Extensionهای `code` و `traceId` و برای Validation، `errors`.

| Failure | HTTP |
| --- | --- |
| Validation | 400 |
| BadRequest | 400 |
| NotFound | 404 |
| Conflict | 409 |
| BusinessRule | 422 |
| Unauthorized | 401 |
| Forbidden | 403 |
| ServiceUnavailable | 503 |
| Unknown Exception | 500 |

## امنیت و سازگاری

- برای `Unknown Exception`، پیام داخلی یا Stack Trace هرگز در HTTP منتشر نمی‌شود.
- `ServiceExeption` قدیمی فعلاً به `legacy.service_error.{number}` + HTTP 400 تبدیل می‌شود؛ پیام احتمالی حساس آن به Client داده نمی‌شود.
- `ForbiddenAccessException` قدیمی فعلاً HTTP 403 است؛ در آینده به `AppException.Forbidden` مهاجرت می‌کند.
- وضعیت‌های خالی 401/403 (مثل Middleware احراز هویت) نیز باید پاسخ استاندارد دریافت کنند.
- `OperationCanceledException` که ناشی از `RequestAborted` است، نباید به یک 500 مصنوعی قابل ارسال تبدیل شود.
- برای خطاهای شناخته‌شده از Log سبک و بدون Payload استفاده می‌شود؛ خطاهای ناشناخته باید در Diagnostics/Monitoring دیده شوند.
- در .NET 10 با `SuppressDiagnosticsCallback` فقط Diagnostics خطاهای انتظاررفته Suppress می‌شود تا 500ها قابل ردیابی باشند.

## مثال

```csharp
throw AppException.NotFound("users.not_found", "User not found.");
throw AppException.Conflict("users.duplicate", "Username is already in use.");
```

نمونه `ProblemDetails`:

```json
{
  "title": "Not Found",
  "status": 404,
  "detail": "User not found.",
  "instance": "/api/users/123",
  "code": "users.not_found",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736"
}
```

## محدوده این PR

- تغییر `Dispatcher`، `Domain Event` و `Outbox` انجام نمی‌شود.
- اگر Handlerی موفق به تولید `Result<T>` شد، تبدیل آن موضوع جداگانه است؛ فعلاً Result عمومی جدید اضافه نمی‌شود.
- Integration Test روی TestServer و Build کل Solution مورد انتظار است.
- این ADR تا Review و Merge نهایی **Proposed** می‌ماند.
