using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace MiniBus;

public class MiniBus(IServiceProvider services)
{
    private static readonly ActivitySource _activitySource = new ActivitySource("MiniBus");

    public async Task<Result<TResponse>> Handle<TRequest, TResponse>(TRequest request)
    {
        var handler = services.GetRequiredService<IDispatcher<TRequest, TResponse>>();
        using var activity = _activitySource.StartActivity($"minibus.dispatch {handler.HandlerName}");
        activity?.SetTag("minibus.request.type", typeof(TRequest).FullName);
        activity?.SetTag("minibus.response.type", typeof(TResponse).FullName);
        try
        {
            var result = await handler.Handle(request);
            activity?.SetTag("minibus.result.status", result.Status.ToString());
            return result;
        }
        catch (OperationCanceledException)
        {
            activity?.SetTag("minibus.result.status", "Canceled");
            throw;
        }
        catch (Exception ex)
        {
            activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                { "exception.type", ex.GetType().FullName },
                { "exception.message", ex.Message },
                { "exception.stacktrace", ex.StackTrace }
            }));
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
