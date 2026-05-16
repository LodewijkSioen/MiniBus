using Microsoft.Extensions.DependencyInjection;

namespace MiniBus.Convention;

public class ConventionBus(IServiceProvider services)
{
    public async Task<Result<TResponse>> Handle<TRequest, TResponse>(TRequest request)
    {
        var handler = services.GetRequiredService<IConventionHandler<TRequest, TResponse>>();
        return await handler.Handle(request);
    }
}
