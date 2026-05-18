using Microsoft.Extensions.DependencyInjection;

namespace MiniBus;

public class MiniBus(IServiceProvider services)
{
    public async Task<Result<TResponse>> Handle<TRequest, TResponse>(TRequest request)
    {
        var handler = services.GetRequiredService<IDispatcher<TRequest, TResponse>>();
        return await handler.Handle(request);
    }
}
