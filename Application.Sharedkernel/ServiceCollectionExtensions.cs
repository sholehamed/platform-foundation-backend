using System.Reflection;
using Application.SharedKernel.Abstractions;
using Application.SharedKernel.Abstractions.Mapping;
using Application.SharedKernel.Abstractions.Messaging;
using Application.SharedKernel.Behaviors;
using Application.SharedKernel.Services;
using Application.SharedKernel.Observability;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DispatcherClass = Application.SharedKernel.Dispatcher.Dispatcher;

namespace Application.SharedKernel;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCustomCqrs<TProfile>(
        this IServiceCollection services, params Assembly[] assemblies)
        where TProfile : MappingProfile, new()
    {
        services.AddAutoMapper(options => options.AddProfile<TProfile>());
        return RegisterCore(services, assemblies);
    }

    public static IServiceCollection AddCustomCqrs(
        this IServiceCollection services, params Assembly[] assemblies) =>
        RegisterCore(services, assemblies);

    private static IServiceCollection RegisterCore(
        IServiceCollection services, Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        services.TryAddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddLogging();
        services.TryAddSingleton(new PerformanceOptions());
        services.TryAddScoped<IDispatcher, DispatcherClass>();

        // Registration order defines wrapping order: diagnostics -> performance -> validation -> handler.
        services.TryAddEnumerable(ServiceDescriptor.Transient(
            typeof(IPipelineBehavior<,>), typeof(RequestDiagnosticsBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(
            typeof(IPipelineBehavior<,>), typeof(RequestPerformanceBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(
            typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>)));

        services.AddValidatorsFromAssemblies(assemblies);
        RegisterHandlers(services, assemblies);
        return services;
    }

    private static void RegisterHandlers(IServiceCollection services, Assembly[] assemblies)
    {
        foreach (var assembly in assemblies.Distinct())
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters)
                    continue;

                foreach (var handlerInterface in type.GetInterfaces())
                {
                    if (!handlerInterface.IsGenericType)
                        continue;

                    var definition = handlerInterface.GetGenericTypeDefinition();
                    if (definition == typeof(ICommandHandler<,>) ||
                        definition == typeof(ICommandHandler<>) ||
                        definition == typeof(IQueryHandler<,>) ||
                        definition == typeof(INotificationHandler<>) ||
                        definition == typeof(IDomainEventHandler<>))
                    {
                        services.TryAddEnumerable(
                            ServiceDescriptor.Scoped(handlerInterface, type));
                    }
                }
            }
        }
    }
}
