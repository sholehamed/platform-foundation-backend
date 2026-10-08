# Authentication و Authorization

**نوع:** Concept / Under Review

## مرزبندی

- **Authentication:** کاربر یا Client چه هویتی دارد؟
- **Authorization:** آن هویت مجاز به انجام کدام عمل بر روی کدام Resource است؟
- **Session / Token Lifecycle:** ورود، تمدید، ابطال، خروج و مدیریت نشست‌ها.
- **Tenant Scope:** مرز داده‌ها و عملیات برای هر Tenant؛ صرف داشتن Token مجوز عبور از مرز Tenant نیست.

## وضعیت مشاهده‌شده در مخزن

- `Modules.Identity` با OpenIddict و موجودیت‌های Role، Group، Permission، Resource و User وجود دارد.
- Password Grant و Authorization Code + PKCE در تنظیمات OpenIddict ماژول فعال‌سازی شده‌اند؛ انتخاب جریان اصلی کلاینت هنوز در این پروژه تأیید نشده است.
- `PermissionMiddleware` در `UseIdentityModule` کامنت شده؛ خودِ `UseIdentityModule` نیز در `Web/Program.cs` مشاهده نشد.
- در `TenantSaveChangesInterceptor` کنترل صریح Writable Tenant و جلوگیری از تغییر TenantId کامنت شده‌اند.
- `GetUserMenusQuery` از RoleId ورودی استفاده می‌کند؛ باید عضویت/اختیار آن Role و سازگاری با Group و Tenant بررسی شود.

این‌ها **ریسک‌ها و پرسش‌های بررسی** هستند، نه اثبات آسیب‌پذیری قابل بهره‌برداری در یک استقرار واقعی.

## موضوعات مطالعه و تصمیم

1. OAuth 2.0، OpenID Connect و PKCE برای SPA.
2. Refresh Token Rotation، Revocation، Single Active Session و پاسخ‌های 401/403.
3. RBAC در برابر Permission / Policy / Resource-based Authorization.
4. نقش‌های مستقیم، عضویت Group، Deny Override و Scope.
5. Permission Cache و Invalidation هنگام تغییر عضویت.
6. Tenant Isolation در خواندن، نوشتن و Background Job.
7. تست مسیرهای Unauthorized، Forbidden و Cross-Tenant.

## معیار پذیرش آینده

تا پیش از تأیید معماری و اجرای تست‌های امنیتی، هیچ مسیر حساس نباید Done اعلام شود.
