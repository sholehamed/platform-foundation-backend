# Logging، Monitoring، Tracing و Metrics

**نوع:** Concept / Under Review  
**وضعیت اجرا:** در سورس فعلی ابزار یکپارچه Observability مشاهده نشده است؛ Build/Runtime هنوز بررسی نشده‌اند.

## تفاوت مفاهیم

- **Log:** رویداد متنیِ ساختاریافته با سطح اهمیت و Context.
- **Trace:** مسیر یک عملیات بین HTTP، Dispatcher، EF Core و سرویس‌های جانبی؛ شامل Spanهای مرتبط.
- **Metric:** مقادیر تجمیعی مثل نرخ خطا، مدت اجرا، Memory و Cache Hit Rate.
- **Monitoring:** مشاهده وضعیت و هشدار بر پایه این داده‌ها.
- **Audit Log:** سابقه اعمال امنیتی/تجاری؛ با Log تشخیصی یکی نیست و الزامات نگهداری متفاوتی دارد.

## ابزارهای پیشنهادی برای بررسی

- `ILogger<T>` برای Structured Logging.
- `System.Diagnostics.ActivitySource` برای Traceهای اختصاصی.
- `System.Diagnostics.Metrics.Meter` برای Metricهای اختصاصی.
- OpenTelemetry SDK/Exporter برای Instrumentation و ارسال به Backend مشاهده‌پذیری.
- مقایسه Dashboard آماده با Dashboard داخلی؛ انتخاب نهایی هنوز انجام نشده است.

## نمونه مسیر مورد انتظار

```text
HTTP Request (TraceId)
  └─ Dispatcher
      ├─ Validation
      ├─ Command Handler
      │   └─ EF Core / SQL
      └─ Notification / Event
```

## اصول طراحی برای تصمیم‌گیری

1. سیستم بدون Dashboard هم باید قابل اجرا بماند.
2. اطلاعات حساس، Token، Password و Bodyهای محرمانه Log نمی‌شوند.
3. سطح Logging، Sampling و Retention قابل تنظیم باشد.
4. TraceId بین لایه‌ها قابل دنبال کردن باشد.
5. در Metrics از Labelهای بسیار پرتنوع مثل UserId/TenantId پرهیز شود.
6. خرابی Backend لاگ نباید مسیر اصلی درخواست را بی‌دلیل مختل کند.
7. Monitoring Endpointها باید کنترل دسترسی و سیاست منابع داشته باشند.

## پرسش‌های باز

- خروجی اولیه Console، OTLP، فایل، دیتابیس یا ترکیب آن‌ها باشد؟
- داشبورد Embedded بسازیم یا از ابزار آماده استفاده کنیم؟
- آیا Audit Trail نیاز به پایگاه‌داده و Retention مستقل دارد؟
- چه Metricها و Alertهایی برای Foundation پیش‌فرض هستند؟
- سربار روی مسیرهای پرترافیک چطور اندازه‌گیری شود؟

## سناریوهای تست آتی

خطای HTTP، Query کند، Validation Failure، Exception در Event Handler، قطعی مقصد Telemetry، Sampling، Redaction و محدودیت دسترسی Monitoring.
