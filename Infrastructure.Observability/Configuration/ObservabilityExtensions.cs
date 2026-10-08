using Application.SharedKernel.Behaviors;
using Application.SharedKernel.Observability;
using Infrastructure.Observability.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Infrastructure.Observability.Configuration;

public static class ObservabilityExtensions
{
    /// <summary>
    /// Backend instrumentation and read-model only. Never maps an HTML dashboard.
    /// OTLP export is opt-in; the bounded local reader works without a collector.
    /// </summary>
    public static IServiceCollection AddPlatformObservability(
        this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Observability").Get<ObservabilityOptions>()
            ?? new ObservabilityOptions();
        settings.Validate();

        services.AddFoundationPerformance(options =>
        {
            configuration.GetSection("Observability:Performance").Bind(options);
        });

        // EF's built-in command logs can include raw SQL and values. Disable
        // them for every registered provider (including an optional OTLP exporter).
        // Our DbTimingInterceptor emits sanitized duration/status metadata.
        services.AddLogging(logging => logging.AddFilter(
            "Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(settings);
        services.TryAddSingleton<BoundedObservabilityStore>(sp =>
            new BoundedObservabilityStore(settings.RecentSampleCapacity,
                sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<IOperationTelemetrySink>(sp =>
            sp.GetRequiredService<BoundedObservabilityStore>());
        services.TryAddSingleton<IObservabilityReader>(sp =>
            sp.GetRequiredService<BoundedObservabilityStore>());

        var otel = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(settings.ServiceName))
            .WithTracing(tracing => tracing
                .AddSource(CqrsDiagnostics.ActivitySourceName)
                .AddSource("PlatformFoundation.Messaging")
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = false; // exception boundary logs safely
                })
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddMeter(FoundationMetrics.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation());

        if (settings.Otlp.Enabled)
        {
            // The environment controls OTLP endpoint and credentials.
            // Traces, metrics and structured logs go to the configured collector.
            // The cross-cutting exporter registers logs, traces and metrics.
            // Endpoint/credentials are controlled via OTEL_EXPORTER_OTLP_* variables.
            otel.UseOtlpExporter();
        }

        return services;
    }
}
