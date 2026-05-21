using Microsoft.Extensions.DependencyInjection;
using System;
using System.Reflection;

namespace MiniBus;

public static class ServiceCollectionExtensions
{
    private static readonly Func<IServiceCollection, IServiceCollection>? AddGeneratedHandlers = ResolveAddGeneratedHandlers();

    public static IServiceCollection AddMiniBus(this IServiceCollection services)
    {
        services.AddTransient<MiniBus>();
        return AddGeneratedHandlers is null
            ? services
            : AddGeneratedHandlers(services);
    }

    private static Func<IServiceCollection, IServiceCollection>? ResolveAddGeneratedHandlers()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var generatedType = assembly.GetType("MiniBus.GeneratedHandlerRegistrations");
            var method = generatedType?.GetMethod(
                "AddGeneratedHandlers",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(IServiceCollection) },
                modifiers: null);

            if (method is null)
            {
                continue;
            }

            return services => (IServiceCollection)method.Invoke(null, new object[] { services })!;
        }

        return null;
    }
}
