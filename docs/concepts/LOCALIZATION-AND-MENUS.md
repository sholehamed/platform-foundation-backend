# Localization و Dynamic Menu

**نوع:** Concept / Proposed

## هدف

امکان مدیریت منوهای پویا با ترجمه‌های چندزبانه، دسترسی صحیح و عملکرد مناسب در Backend و Angular؛ بدون تکرار منطق امنیتی در UI.

## وضعیت فعلی

- موجودیت‌های `Menu` و `MenuCategory` دارای `Title` تک‌زبانه هستند.
- `GetUserMenusQuery` درخت منو را در Application تشکیل می‌دهد.
- ساختار ترجمه جدا برای منو/دسته‌بندی در فایل‌های بررسی‌شده مشاهده نشد.

## مدل پیشنهادی برای گفتگو

```text
Menu
  Id, Key, ParentId, Route, Icon, Order, PermissionId
MenuTranslation
  MenuId, Culture, Title
MenuCategory
  Id, Key, Order
MenuCategoryTranslation
  MenuCategoryId, Culture, Title
```

این مدل هنوز تصویب نشده؛ ارتباط و Unique Index و Fallback باید روشن شود.

## مسئولیت Backend و Frontend

- **Backend:** اعمال مجوز واقعی، Culture Fallback، خروجی منو، Cache مناسب، Invalidation.
- **Angular:** مدیریت زبان جاری، تغییر زنده متن‌ها، RTL/LTR، همگام‌سازی مسیر و رندر منو.
- مخزن ترجمه متن‌های ثابت UI الزاماً با ترجمه داده‌های Database یکسان نیست.

## تصمیم‌های باز

1. Culture کاربر از Profile، Header، Route یا Preference انتخاب شود؟
2. Fallback `fa-IR → fa → default` چگونه باشد؟
3. Cache ترجمه‌ها جدا از Cache درخت منو باشد؟
4. در زمان تغییر زبان بدون Reload چه بخش‌هایی Refresh شوند؟
5. چه سیاستی برای عنوان ترجمه‌نشده در UI داریم؟

## تست‌های آینده

تغییر زبان، ترجمه گمشده، مجوزهای مختلف، تغییر Permission، Invalidation Cache، درخت چندسطحی و RTL.
