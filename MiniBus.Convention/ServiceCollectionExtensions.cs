using Microsoft.Extensions.DependencyInjection;

namespace MiniBus.Convention;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddConventionBus(this IServiceCollection services)
    {
        services.AddTransient<ConventionBus>();
        return services;
    }
}
