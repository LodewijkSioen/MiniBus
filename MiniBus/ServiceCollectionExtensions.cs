using Microsoft.Extensions.DependencyInjection;

namespace MiniBus;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMiniBus(this IServiceCollection services)
    {
        services.AddTransient<MiniBus>();
        return services;
    }
}
