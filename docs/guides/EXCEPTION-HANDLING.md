# راهنمای استفاده از Exception Handling

**وضعیت:** Implemented in PR / Awaiting Review  
**پیش‌نیاز:** CQRS Validation Pipeline در PR #2

## نحوه استفاده

در Application برای شکست قابل انتظار:

```csharp
throw AppException.NotFound("user.not_found", "User not found.");
throw AppException.Conflict("user.duplicate", "Username already exists.");
throw AppException.BusinessRule("subscription.limit", "Limit exceeded.");
```

برای ورودی‌های Request از FluentValidation استفاده شود؛ Validatorها در Pipeline اجرا می‌شوند و خطای آن‌ها بدون کد تکراری Endpoint به HTTP 400 تبدیل می‌شود.

## در Host

```csharp
builder.Services.AddBaseApiServices();
var app = builder.Build();
app.UseBaseApiExceptionHandling();
```

این Middleware باید قبل از Endpointها و Middlewareهایی باشد که ممکن است Exception تولید کنند.

## قرارداد پاسخ

- `application/problem+json`
- `status` و `title` استاندارد
- `code`: شناسه ثابت و قابل تصمیم‌گیری در Angular؛ در قالب `domain.reason`
- `traceId`: قابل استفاده برای پشتیبانی و اتصال به Log/Trace
- `errors`: تنها برای Validation، نگاشت نام فیلد به آرایه پیام‌ها

Angular باید به `code` و `status` تکیه کند، نه متن `detail`؛ ترجمه نمایش خطا بعداً در Localization انجام می‌شود.

## قواعد

- هرگز برای خطای غیرمنتظره `exception.Message` را مستقیم به Client ارسال نکنید.
- خطاهای دامنه باید Client-safe و با Code پایدار باشند.
- `401` و `403` را ترجیحاً از Authentication/Authorization Middleware تولید کنید؛ Exceptionها مسیر جایگزین هستند.
- `KeyNotFoundException` و `UnauthorizedAccessException` عمومی .NET به‌صورت خودکار به 404/401 تعبیر نمی‌شوند تا باگ‌های برنامه به شکست عادی تبدیل نشوند.
- Exceptionهای قدیمی `ServiceExeption` و `ForbiddenAccessException` برای سازگاری نگاشت می‌شوند، اما کد جدید باید `AppException` به کار ببرد.

## تست

```bash
dotnet test tests/Platform.Foundation.Web.Tests/Platform.Foundation.Web.Tests.csproj -c Release
```

مسیرهای مهم: Validation 400، NotFound 404، Conflict 409، Business 422، Unauthorized 401، Forbidden 403، 500 امن و پاسخ خطاهای StatusCode-only.

Domain Eventها در این Milestone تغییر نکرده‌اند.
