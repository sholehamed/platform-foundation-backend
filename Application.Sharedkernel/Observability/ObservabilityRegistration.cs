using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Application.SharedKernel.Observability;

public static class ObservabilityRegistration
{
    /// <summary>Configure once at the composition root, without monitoring infrastructure dependencies.</summary>
    public static IServiceCollection AddFoundationPerformance(
        this IServiceCollection services, Action<PerformanceOptions>? configure = null)
    {
        var options = new PerformanceOptions();
        configure?.Invoke(options);
        options.Validate();
        services.RemoveAll<PerformanceOptions>();
        services.AddSingleton(options);
        return services;
    }
}
