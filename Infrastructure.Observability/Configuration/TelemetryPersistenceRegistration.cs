using Application.SharedKernel.Observability;
using Infrastructure.Observability.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Observability.Configuration;

/// <summary>Opt-in historical telemetry, independent from Hangfire or business transactions.</summary>
public static class TelemetryPersistenceRegistration
{
    public static IServiceCollection AddPlatformTelemetryPersistence(
        this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Observability:Persistence")
            .Get<TelemetryCaptureOptions>() ?? new TelemetryCaptureOptions();
        if (!options.Enabled) return services;
        options.Validate();
        var connectionString = configuration.GetConnectionString("Telemetry");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "ConnectionStrings:Telemetry is required when telemetry persistence is enabled.");

        services.AddSingleton(options);
        services.AddDbContext<TelemetryDbContext>(db => db.UseSqlServer(connectionString));
        services.AddScoped<ITelemetryHistoryReader, SqlTelemetryHistoryReader>();
        services.AddSingleton<ChannelTelemetrySink>();
        services.AddSingleton<ITelemetryEventSink>(sp =>
            sp.GetRequiredService<ChannelTelemetrySink>());

        // Decorate original live buffer; do not invoke EF from request path.
        services.RemoveAll<IOperationTelemetrySink>();
        services.AddSingleton<IOperationTelemetrySink, PersistentOperationTelemetrySink>();

        services.AddHostedService<TelemetryPersistenceWorker>();
        services.AddHostedService<TelemetryRetentionWorker>();
        if (options.CaptureFoundationTraces)
            services.AddHostedService<FoundationActivityCapture>();
        if (options.CaptureSanitizedLogs)
            services.AddSingleton<ILoggerProvider, SafeTelemetryLoggerProvider>();

        return services;
    }
}
