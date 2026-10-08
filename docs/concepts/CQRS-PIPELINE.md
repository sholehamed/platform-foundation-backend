# CQRS، Pipeline، Validation و Notifications

**نوع:** Concept / Under Review  
**آخرین بررسی سورس:** 2026-10-08

## چرا این مفاهیم را بررسی می‌کنیم؟

هدف این است که Command و Query به شیوه‌ای قابل پیش‌بینی، قابل تست و قابل رصد اجرا شوند و کد تکراری Validation، Logging و سایر Cross-cutting Concernها داخل Handlerها پخش نشود.

## مفاهیم

- **Command:** درخواستی برای تغییر وضعیت سیستم.
- **Query:** درخواستی برای خواندن اطلاعات، بدون تغییر عمدی وضعیت.
- **Handler:** پیاده‌کننده رفتار یک درخواست مشخص.
- **Dispatcher:** انتخاب و اجرای Handler مناسب.
- **Pipeline Behavior:** اجرای رفتار مشترک قبل و بعد از Handler.
- **Validation:** رد ورودی نامعتبر پیش از ورود به منطق اصلی.
- **Notification:** انتشار پیام درون‌پردازه‌ای برای صفر یا چند Handler.
- **Domain Event:** بیان رخدادی در مدل دامنه؛ نحوه و زمان انتشار نیازمند سیاست جداست.
- **Integration Event:** رویداد مخصوص عبور از مرز پردازش/سیستم که ممکن است تحویل قابل اعتماد بخواهد.

## آنچه در کد فعلی مشاهده شده

- `ICommand`، `ICommand<TResult>`، `IQuery<TResult>`، Handlerها و `IDispatcher` وجود دارند.
- `Dispatcher` با DI، Reflection و `dynamic` Handler را اجرا می‌کند.
- ساختار `IPipelineBehavior` و زنجیره اجرای Behaviorها در Dispatcher فعلی وجود ندارد.
- نوع `ValidationException` وجود دارد، اما در Dispatcher فعلی فراخوانی و اجرای خودکار Validator مشاهده نشد.
- `Publish<TNotification>`، Handlerها را به‌صورت ترتیبی اجرا می‌کند.
- `DispatchDomainEventsInterceptor` در `SavedChangesAsync` رویدادهای دامنه را منتشر می‌کند؛ Failure Policy و تضمین تحویل نیازمند بررسی است.

**تذکر:** وجود FluentValidation در وابستگی‌ها به معنی فعال‌بودن Validation Pipeline نیست.

## مسیر پیشنهادی برای مطالعه

```text
Command/Query
   → Tracing / Metrics
   → Validation
   → Additional Behaviors (when applicable)
   → Handler
   → Result / Error
```

ترتیب بالا **تصمیم قطعی نیست** و باید سیاست Transaction، Exception و Caching نیز بررسی شود.

## پرسش‌های باز

1. Dispatcher اختصاصی را تقویت کنیم یا کتابخانه موجود انتخاب کنیم؟
2. `void` Command چگونه در قرارداد مشترک نمایش داده شود؟
3. Validatorهای متعدد چگونه و با چه ترتیبی اجرا شوند؟
4. Failure حاصل Validation از چه قالب HTTP/Error عبور کند؟
5. Transaction Behavior مخصوص چه Commandهایی است؟
6. Notification Handlerها ترتیبی یا موازی باشند؟ خطای یک Handler چه اثری داشته باشد؟
7. Domain Event قبل یا بعد از Commit منتشر شود؟ Outbox در چه مواردی لازم است؟
8. چه Behaviorهایی باید به صورت پیش‌فرض فعال شوند و چگونه آزمایش شوند؟

## معیار پذیرش آینده

- تست ترتیب Pipeline و توقف اجرا در Validation شکست‌خورده.
- تست CancellationToken و انتشار چند Handler.
- Trace هر درخواست بدون ثبت اطلاعات حساس.
- تست Failure بعد از Commit و سیاست Retry/Outbox.
- Benchmark و تست ثبت Handlerها.

مستند حاضر **تصمیم اجرایی جدید صادر نمی‌کند**.
