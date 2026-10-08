# راهنمای CQRS Pipeline و Validation

**مرحله:** F01.1–F01.2 (PR پیشنهادی، تا قبل از Merge قطعی نیست)  
**دامنه:** `Application.Sharedkernel`  
**وضعیت بررسی:** تغییرات سورس در PR؛ Build و Test باید در CI یا ماشین توسعه اجرا شوند.

## مسئله

`Dispatcher` قدیمی Handler را مستقیم از DI پیدا و اجرا می‌کرد. `IValidator<T>`های FluentValidation (مانند `MenuCreateCommandValidator`) به شکل خودکار اجرا نمی‌شدند. `Publish` وجود داشت ولی رفتار شکست و ترتیب آن صریح نبود.

## قرارداد نهایی پیاده‌سازی در این PR

- امضاهای `IDispatcher`، `ICommand`، `IQuery<T>` و Handlerهای فعلی حفظ شده‌اند.
- `IPipelineBehavior<TRequest,TResponse>` و `RequestHandlerDelegate<TResponse>` افزوده شده است.
- Commandهای بدون نتیجه داخل Pipeline از `Unit` استفاده می‌کنند؛ امضای عمومی `Task Send(ICommand)` تغییر نمی‌کند.
- Behaviorها به ترتیب ثبت در DI اجرا می‌شوند؛ اولین Behavior بیرونی‌ترین است.
- `RequestDiagnosticsBehavior`، Activity و Timing بدون Log کردن Request Payload ثبت می‌کند.
- `ValidationBehavior` همه `IValidator<TRequest>`ها را **ترتیبی** با `ValidateAsync` اجرا می‌کند و خطاها را در `Application.SharedKernel.Exceptions.ValidationException` جمع می‌کند.
- اگر Validation شکست بخورد، Handler اجرا نمی‌شود.
- `Publish<TNotification>` ترتیبی و Fail-Fast باقی مانده است. این مسیر در این مرحله Pipeline عمومی Command/Query را طی نمی‌کند.
- `IDomainEventHandler<T>` از `INotificationHandler<T>` ارث می‌برد و از طریق DI برای Publish قابل کشف است.

## اضافه کردن Validator

```csharp
public sealed record CreateItemCommand(string Name) : ICommand<Guid>;

public sealed class CreateItemValidator : AbstractValidator<CreateItemCommand>
{
    public CreateItemValidator() =>
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
}
```

در Composition Root:

```csharp
services.AddCustomCqrs(typeof(CreateItemCommand).Assembly);
```

این متد Handlerها و Validatorهای همان Assembly را ثبت می‌کند. اجرای Validator به عهده Pipeline است.

## رفتار مشترک سفارشی

```csharp
public sealed class MyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct) => next();
}
```

ثبت بعد از `AddCustomCqrs`:

```csharp
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(MyBehavior<,>));
```

در این حالت ترتیب پیش‌فرض: `Diagnostics → Validation → MyBehavior → Handler`.

## Tracing

نام `ActivitySource` برابر `PlatformFoundation.Cqrs` است. در مرحله Observability Host می‌تواند آن را با OpenTelemetry ثبت کند:

```csharp
// نمونه برای فاز Observability، نه کد فعلی Program.cs
tracing.AddSource(CqrsDiagnostics.ActivitySourceName);
```

## Failure Semantics

- Validation: `ValidationException` با `Errors` گروه‌بندی شده؛ HTTP Mapping در Milestone مربوط به Error Handling بررسی خواهد شد.
- Handler exception: دوباره پرتاب می‌شود؛ Pipeline آن را Swallow نمی‌کند.
- Notification: اگر Handler خطا بدهد، انتشار متوقف و خطا به فراخوان منتقل می‌شود.
- Domain Event: انتشار فعلی در `SavedChangesAsync` است و تضمین تحویل/Outbox ندارد. **در این PR تغییر نکرده است.**

## تست و محدودیت

تست‌های جدید در `tests/Platform.Foundation.Application.Tests/` موارد اعتبارسنجی Command/Query/Void، ترتیب Behaviorها، Cancellation و Publish را پوشش می‌دهند.

```bash
dotnet test tests/Platform.Foundation.Application.Tests/Platform.Foundation.Application.Tests.csproj
```

**توجه:** PR هنوز به معنی اجرای موفق تست‌ها نیست؛ نتیجه CI باید جداگانه ثبت شود. Performance/Benchmark و Transaction/Outbox جزو گام‌های بعدی هستند.
