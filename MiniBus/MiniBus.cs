using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace MiniBus;

public class MiniBus(IServiceProvider services)
{
    private static readonly ActivitySource ActivitySource = new ActivitySource("MiniBus");

    public async Task<TResult> Handle<TRequest, TResult>(TRequest request)
    {
        var handler = services.GetRequiredService<IDispatcher<TRequest, TResult>>();
        using var activity = ActivitySource.StartActivity($"minibus.dispatch {handler.HandlerName}");
        activity?.SetTag("minibus.request.type", typeof(TRequest).FullName);
        try
        {
            var result = await handler.Handle(request);

            var resultType = result is IDispatchResult dispatchResult
                ? dispatchResult.Value?.GetType().FullName
                : typeof(TResult).FullName;
            activity?.SetTag("minibus.result.type", resultType);

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
