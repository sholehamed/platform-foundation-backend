# نمای کلی معماری Platform Foundation

**وضعیت:** Observed + Under Review  
**مبنای بررسی:** شاخه `main` در 2026-10-08  
**دامنه:** Backend فعلی؛ طراحی نهایی هنوز تصویب نشده است.

## هدف

ساخت زیرساخت آموزشی و قابل استفاده مجدد برای توسعه برنامه‌های .NET و در آینده Angular، بدون وابستگی به Domain محصول خاص.

## ساختار مشاهده‌شده

- `Domain` و `Domain.Sharedkernel`
- `Application` و `Application.Sharedkernel`
- `Infrastructure` و `Infrastructure.Sharedkernel`
- `Web` و `Web.Sharedkernel`
- `Modules.Identity`، `Modules.MediaStorage`، `Scalar.ClientGeneration` و `Migrator`
- Solution فعلی: `shinera-back.slnx`

فولدرهای `/src/` و `/tests/` داخل Solution **مجازی** هستند؛ پروژه‌ها فعلاً مستقیماً در ریشه Git قرار دارند. این مرحله هیچ جابه‌جایی کدی انجام نمی‌دهد.

## مرزبندی پیشنهادی برای بررسی

```text
Domain                  → موجودیت‌ها و قوانین مستقل از زیرساخت
Application             → Command / Query / Handler / قراردادها
Infrastructure          → EF Core، Cache Provider و Adapterها
Web                     → HTTP، Authentication، OpenAPI، Composition Root
Observability adapters  → ثبت Log / Trace / Metric، بدون آلودن Domain
```

این بخش پیشنهاد آموزشی است، نه دستور Refactor فوری.

## نقاط نیازمند بررسی

1. مرزهای ماژول‌ها و وابستگی متقابل Shared Kernelها.
2. ثبت و اجرای `IDispatcher` و Pipeline مشترک.
3. جداسازی خطاهای قابل انتظار از Exceptionهای غیرمنتظره.
4. سازگاری Authentication / Authorization با Tenant Isolation.
5. سیاست Caching، Localization و Invalidation.
6. قابلیت تست و اجرای واقعی ماژول‌ها از `Web/Program.cs`.
7. جدا کردن نام‌ها و Enumهای وابسته به حوزه سالن زیبایی از Foundation عمومی.

## قواعد تغییر

- ابتدا با تست و شواهد از کد نتیجه بگیریم.
- تصمیم‌های معماری جدید باید ADR مستقل داشته باشند.
- از معرفی هم‌زمان چند الگوی رقیب برای یک مسئولیت پرهیز شود.
- وضعیت اجراشده با وضعیت پیشنهادی قاطی نشود.
