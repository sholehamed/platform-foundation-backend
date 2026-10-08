# مستندات Platform Foundation

> وضعیت: فعال (نسخه آغازین) | زبان اصلی: فارسی | آخرین بررسی: 2026-10-08

این پوشه مرجع موقت مستندات پروژه **Platform Foundation** است. در این مرحله مستندات کنار کد Backend نگهداری می‌شوند. پس از شکل‌گیری Frontend و ایجاد مخازن مجزای مستندات، انتقال برنامه‌ریزی‌شده و بدون ایجاد نسخه‌های متناقض انجام می‌شود.

## مسیرهای مستندات

| مسیر | هدف |
| --- | --- |
| [concepts/](concepts/) | آموزش مفاهیم، کاربردها، مزایا و محدودیت‌ها؛ **نه تصمیم قطعی** |
| [architecture/ARCHITECTURE-OVERVIEW.md](architecture/ARCHITECTURE-OVERVIEW.md) | نقشه معماری موجود و مرزبندی مسئولیت‌ها |
| [architecture/decisions/](architecture/decisions/) | Architecture Decision Record (ADR) برای تصمیم‌های مصوب |
| [development/FOUNDATION-STATUS.md](development/FOUNDATION-STATUS.md) | شواهد بررسی مخزن، بدهی‌های فنی و وضعیت راستی‌آزمایی |
| [development/ROADMAP.md](development/ROADMAP.md) | نقشه راه اکتشافی و Milestoneها |
| [guides/DOCUMENTATION-STANDARDS.md](guides/DOCUMENTATION-STANDARDS.md) | قواعد نوشتن و به‌روزرسانی مستندات |
| [guides/ENGINEERING-WORKFLOW.md](guides/ENGINEERING-WORKFLOW.md) | چرخه بررسی، تصمیم، پیاده‌سازی و تحویل |

## وضعیت تصمیم‌ها

- **Accepted:** تصمیم صریحاً تأیید شده و باید رعایت شود.
- **Proposed:** پیشنهادی است؛ برای توسعه الزام‌آور نیست.
- **Under Review:** گزینه‌های فنی در حال بررسی‌اند.
- **Superseded:** تصمیم با ADR جدید جایگزین شده است.
- **Observed:** واقعیتی که در نسخه مشخصی از سورس مشاهده شده؛ به معنی تأیید کیفیت آن نیست.

## موضوعات شروع

- [CQRS، Pipeline، Validation و Notifications](concepts/CQRS-PIPELINE.md)
- [Logging، Monitoring و Tracing](concepts/OBSERVABILITY.md)
- [Caching](concepts/CACHING.md)
- [Authentication و Authorization](concepts/IDENTITY-AND-AUTHORIZATION.md)
- [Localization و منوهای چندزبانه](concepts/LOCALIZATION-AND-MENUS.md)

**قانون:** هیچ سند آموزشی یا پیشنهادی به‌تنهایی مجوز تغییر معماری محسوب نمی‌شود. ابتدا کد واقعی بررسی، سپس تصمیم ثبت و بعد اجرا می‌شود.
