# نقشه راه اکتشافی Platform Foundation

**وضعیت:** Proposed — نیازمند بازبینی مشترک  
**هدف:** یادگیری، ارزیابی، مستندسازی و پیاده‌سازی تدریجی Foundation قابل استفاده مجدد

| شناسه | Milestone | خروجی پیشنهادی |
| --- | --- | --- |
| F00 | Discovery & Baseline | Inventory، Build/Test Baseline، ثبت بدهی‌ها |
| F01.1 | CQRS Contracts | Command / Query / Handler / Dispatcher |
| F01.2 | Pipeline & Validation | Behaviorها، Validator Registration، تست ترتیب اجرا |
| F01.3 | Notifications & Domain Events | Failure Policy، Transaction Boundary، Outbox Decision |
| F02 | Observability | Structured Logs، Traces، Metrics، Monitoring Decision |
| F03 | Security Foundation | Authentication، Authorization، Tenant Isolation، Security Tests |
| F04 | Caching Foundation | Cache Abstraction، Key Policy، Invalidation، Benchmark |
| F05 | Localization & Menus | Culture، Translation، Permission-aware Menus، Cache |
| F06 | Angular Foundation | API Client، Auth، Dynamic Navigation، Localization، UI Tests |
| F07 | Hardening | Load/Performance Tests، Reliability، CI/CD، Release Readiness |

## قواعد شروع و پایان Milestone

1. کد و اسناد مرتبط پیش از اجرا دوباره بررسی شوند.
2. `Concept → Alternatives → Decision → Tests → Implementation → Documentation` دنبال شود.
3. هیچ Milestone صرفاً با نوشتن کد یا یک Build موفق Done نیست.
4. اگر قابلیت Full-Stack است، Backend و Frontend با قرارداد و تست یکپارچه بسته شوند.
5. PR باید شناسه Milestone، اسناد خوانده‌شده، Acceptance Criteria و نتیجه تست داشته باشد.
6. تغییر بنیادی معماری باید ADR تأییدشده داشته باشد.
7. کمبود تست/ابهام را با وضعیت `Blocked` یا `In Progress` ثبت کنیم، نه Done.

در فاز فعلی فقط مخزن Backend برقرار است. مسیر Frontend و انتقال مستندات به مخازن مستقل در زمان مناسب دنبال خواهد شد.
