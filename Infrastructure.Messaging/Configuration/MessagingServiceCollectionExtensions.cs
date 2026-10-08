using System.Reflection;
using Application.SharedKernel.Abstractions.Messaging;
using Hangfire;
using Hangfire.Common;
using Hangfire.SqlServer;
using FluentValidation;
using Infrastructure.Messaging.Behaviors;
using Infrastructure.Messaging.Processing;
using Infrastructure.Messaging.Publishers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Infrastructure.Messaging.Configuration;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the durable outbox against the module's own business DbContext.
    /// The host must also call modelBuilder.AddMessagingOutbox() and apply a migration.
    /// </summary>
    public static IServiceCollection AddPlatformMessaging<TDbContext>(
        this IServiceCollection services,
        Action<MessagingOptions>? configure = null,
        params Assembly[] handlerAssemblies) where TDbContext : DbContext
    {
        var options = new MessagingOptions();
        configure?.Invoke(options);
        options.Validate();

        var registry = new MessageRegistry(handlerAssemblies);
        services.AddSingleton(registry);
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddValidatorsFromAssemblies(handlerAssemblies);

        // Outermost first: diagnostics -> validation -> custom behaviors -> handler.
        services.TryAddEnumerable(ServiceDescriptor.Transient(
            typeof(INotificationPipelineBehavior<>), typeof(NotificationDiagnosticsBehavior<>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(
            typeof(INotificationPipelineBehavior<>), typeof(NotificationValidationBehavior<>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(
            typeof(IMessagePipelineBehavior<>), typeof(MessageDiagnosticsBehavior<>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(
            typeof(IMessagePipelineBehavior<>), typeof(MessageValidationBehavior<>)));

        services.TryAddScoped<IMessagePublisher, EfMessagePublisher<TDbContext>>();
        services.TryAddScoped<INotificationPublisher, InProcessNotificationPublisher>();
        services.TryAddScoped<IMessageDeliveryAdmin, MessageDeliveryAdmin<TDbContext>>();
        services.AddScoped<OutboxWorker<TDbContext>>();

        foreach (var type in registry.HandlerTypes)
            services.TryAdd(ServiceDescriptor.Scoped(type, type));

        foreach (var type in handlerAssemblies.Distinct().SelectMany(x => x.GetTypes())
            .Where(x => x.IsClass && !x.IsAbstract && !x.ContainsGenericParameters))
        {
            foreach (var iface in type.GetInterfaces().Where(x => x.IsGenericType &&
                         x.GetGenericTypeDefinition() == typeof(INotificationHandler<>)))
                services.TryAddEnumerable(ServiceDescriptor.Scoped(iface, type));
        }

        return services;
    }

    /// <summary>Enables the Hangfire polling worker in the SAME ASP.NET host.</summary>
    public static IServiceCollection AddPlatformMessagingHangfire<TDbContext>(
        this IServiceCollection services, string sqlServerConnectionString)
        where TDbContext : DbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlServerConnectionString);

        services.AddHangfire(configuration => configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(sqlServerConnectionString,
                new SqlServerStorageOptions { PrepareSchemaIfNecessary = true }));
        services.AddHangfireServer();
        services.AddHostedService<MessagingScheduleInitializer<TDbContext>>();
        return services;
    }
}

/// <summary>Registers only the polling schedule, never exposes Hangfire Dashboard.</summary>
internal sealed class MessagingScheduleInitializer<TDbContext>(
    IRecurringJobManager recurring) : IHostedService where TDbContext : DbContext
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurring.AddOrUpdate(
            $"platform.messaging.{typeof(TDbContext).Name}.dispatch",
            Job.FromExpression<OutboxWorker<TDbContext>>(
                worker => worker.RunAsync(CancellationToken.None)),
            Cron.Minutely(),
            new RecurringJobOptions());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
