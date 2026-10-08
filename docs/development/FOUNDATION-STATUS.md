# وضعیت بررسی Foundation

**وضعیت:** Initial Source Audit — Partial  
**تاریخ:** 2026-10-08  
**مرجع:** `sholehamed/platform-foundation-backend`، شاخه `main` پیش از PR مستندسازی

## یافته‌های تأییدشده از سورس

| حوزه | مشاهده |
| --- | --- |
| Runtime | `Directory.Build.props`، TargetFramework برابر `net10.0` |
| Solution | `shinera-back.slnx` با پوشه‌های مجازی `src` و `tests` |
| Application | Dispatcher اختصاصی، Command / Query و Notification Handler |
| Validation | `ValidationException` موجود؛ Pipeline / فراخوانی خودکار Validator در Dispatcher مشاهده نشد |
| Events | انتشار Domain Event در `SavedChangesAsync` |
| Identity | OpenIddict و مدل User/Role/Permission/Group |
| Persistence | EF Core، SQL Server و Interceptorهای Auditing/Tenant |
| Localization | فیلد Title تک‌زبانه در Menu و MenuCategory |
| Host | `Web/Program.cs` فعلاً OpenAPI/Scalar را تنظیم می‌کند |
| Tests | پروژه تست یا فایل تست در درخت Git این نسخه پیدا نشد |
| Observability | پروژه/فایل اختصاصی Monitoring، Trace یا Logging Configuration در درخت مخزن پیدا نشد |
| Caching | زیرساخت مرکزی Cache در درخت مخزن پیدا نشد |

## ریسک‌ها و بدهی‌های نیازمند بررسی

- کنترل Writable Tenant در Interceptor کامنت شده است.
- `GetUserMenusQuery` به RoleId ورودی متکی است و تمام مسیرهای مجوزدهی باید با هم تطبیق داده شوند.
- `PermissionMiddleware` در bootstrap ماژول کامنت شده و ماژول از `Web/Program.cs` فراخوانی نشده است.
- نام Solution و بعضی Enumها هنوز وابسته به Domain سابق‌اند.
- هشدار `NU1903` در `Directory.Build.props` از گزارش حذف شده؛ ضرورت و خطر آن باید بررسی شود.
- تست‌های Validation، Event Delivery و Security هنوز راستی‌آزمایی اجرایی نشده‌اند.

## آنچه هنوز نمی‌دانیم

- وضعیت قطعی `dotnet build` / `dotnet test`.
- صحت Migrationها در پایگاه‌داده واقعی.
- عملکرد Runtime، سیاست DI و امنیت Endpointها در Host فعال.
- نتایج Load Test، Profile و Benchmark.

**قاعده:** «در سورس پیدا نشد» به معنی اثبات نبود قابلیت در همه محیط‌ها نیست. «وجود کد» نیز به معنی اجراشدن یا صحیح‌بودن آن نیست.

## مرحله بعد

Audit تکمیلی Solution/DI → آزمون Build → فهرست تصمیم‌های معماری → تصویب اولین ADR فنی → توسعه با تست.
