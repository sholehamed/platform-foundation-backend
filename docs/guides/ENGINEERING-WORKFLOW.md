# چرخه مهندسی Platform Foundation

**وضعیت:** Proposed برای تأیید پیش از اولین PR اجرایی

## چرخه هر قابلیت

1. **Explore:** بررسی کد فعلی، نسخه بسته‌ها و الگوهای موجود.
2. **Learn:** مرور مفهوم، مثال، محدودیت و دلایل وجود قابلیت.
3. **Compare:** بررسی گزینه‌های آماده و اختصاصی همراه Trade-off.
4. **Decide:** تأیید تصمیم، ثبت ADR در صورت اثر معماری.
5. **Implement:** تغییر کوچک، متمرکز و قابل بازگشت.
6. **Verify:** Unit/Integration/Security/Performance Test مناسب.
7. **Document:** راهنمای استفاده، وضعیت و محدودیت‌ها.
8. **Review & Merge:** PR دارای شواهد و شناسه Milestone.

## الگوی PR

```text
Milestone:
Problem:
Sources reviewed:
Decision / ADR:
Implementation:
Tests executed (command + result):
Security considerations:
Known risks / deferred work:
```

## هنگام رسیدن به Angular

قابلیت‌های Full-Stack با یک شناسه Milestone و PRهای مرتبط برای Backend و Frontend بررسی می‌شوند. تست Contract و E2E جزء معیار تکمیل خواهند بود.

## قوانین

- یکپارچگی و امنیت بر سرعت افزودن ابزار جدید مقدم است.
- رفتار موجود بدون شواهد به‌عنوان استاندارد قطعی پذیرفته نمی‌شود.
- مستند Proposed اجازه تعویض کتابخانه یا Refactor گسترده نمی‌دهد.
- Test، Build و Benchmark فقط در صورت اجراشدن «Pass» اعلام شوند.
