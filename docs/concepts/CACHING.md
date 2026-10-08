# زیرساخت Caching

**نوع:** Concept / Proposed  
**وضعیت:** Cache Layer یکپارچه هنوز در سورس فعلی تأیید نشده است.

## هدف

ایجاد سیاست کش قابل استفاده مجدد برای Command/Query، سرویس‌ها، منوها، Permissionها و داده‌های مرجع با کنترل Consistency و Security.

## انتخاب‌هایی که باید مقایسه شوند

- `IMemoryCache`: حافظه محلی همان Instance؛ بدون اشتراک‌گذاری بین Nodeها.
- `IDistributedCache`: قرارداد Cache توزیع‌شده و مستقل از Process.
- `HybridCache`: ترکیب Cache محلی و لایه توزیع‌شده اختیاری؛ بررسی تناسب با .NET پروژه.
- Redis: گزینه‌ای برای Backend توزیع‌شده؛ لازم نیست از روز اول اجباری باشد.
- Cache-Aside، TTL، Eviction، Stampede Protection و Versioned Keys.

## ریسک‌های اصلی

- Stale Data پس از تغییر Role، Permission یا ترجمه.
- نشت داده بین Tenant/Userها با کلید ناقص.
- Cache Stampede زیر بار.
- Cache کردن پاسخ خطا یا اطلاعات حساس.
- Invalidation نامعتبر در چند Instance.

## سناریوی پیشنهادی برای بررسی

```text
GetUserMenus
 → اعتبارسنجی هویت و Tenant
 → Permission Context / Version
 → Culture
 → Cache lookup
 → تولید درخت منوی مجاز
 → Cache/store با سیاست مشخص
```

نباید Cache Key تنها با زبان یا Role ساخته شود، مگر اینکه ثابت کنیم برای تمام کاربران آن محدوده خروجی یکسان است.

## معیارهای پیشنهادی آزمون

Hit/Miss Rate، تأخیر P50/P95، صحت Invalidation، هم‌زمانی، رفتار قطعی Redis و Cross-Tenant Isolation.

**هیچ Provider یا TTL نهایی در این سند انتخاب نشده است.**
