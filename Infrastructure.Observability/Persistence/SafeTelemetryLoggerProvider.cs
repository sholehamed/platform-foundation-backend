using System.Diagnostics;
using Application.SharedKernel.Observability;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Observability.Persistence;

/// <summary>
/// Captures structured metadata for warning+ log events, *not* formatted messages,
/// argument values, stack traces or exception messages. Excludes monitoring internals.
/// </summary>
public sealed class SafeTelemetryLoggerProvider(ITelemetryEventSink sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new SafeLogger(categoryName, sink);
    public void Dispose() { }

    private sealed class SafeLogger(string category, ITelemetryEventSink sink) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
            => Noop.Instance;
        public bool IsEnabled(LogLevel logLevel) =>
            logLevel >= LogLevel.Warning &&
            !category.StartsWith("Infrastructure.Observability", StringComparison.Ordinal) &&
            !category.StartsWith("Microsoft.EntityFrameworkCore.Database.Command", StringComparison.Ordinal);

        public void Log<TState>(LogLevel logLevel, EventId eventId,
            TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            try
            {
                // EventId.Name is developer-controlled metadata. Never format 'state'.
                // Conservative fallback avoids free-text log messages entirely.
                var name = eventId.Name is { Length: > 0 } n ? n : "log.event";
                sink.TryRecord(new TelemetryEvent(
                    DateTimeOffset.UtcNow, TelemetryKind.Log,
                    category, name, logLevel.ToString(),
                    Failed: logLevel >= LogLevel.Error,
                    TraceId: Activity.Current?.TraceId.ToString(),
                    SpanId: Activity.Current?.SpanId.ToString(),
                    ParentSpanId: Activity.Current?.ParentSpanId.ToString(),
                    ErrorType: exception?.GetType().Name));
                if (exception is not null)
                    sink.TryRecord(new TelemetryEvent(
                        DateTimeOffset.UtcNow, TelemetryKind.Exception,
                        category, exception.GetType().Name, logLevel.ToString(), Failed: true,
                        TraceId: Activity.Current?.TraceId.ToString(),
                        SpanId: Activity.Current?.SpanId.ToString(),
                        ParentSpanId: Activity.Current?.ParentSpanId.ToString(),
                        ErrorType: exception.GetType().Name));
            }
            catch { /* never throw while logging */ }
        }

        private sealed class Noop : IDisposable
        {
            internal static readonly Noop Instance = new();
            public void Dispose() { }
        }
    }
}
