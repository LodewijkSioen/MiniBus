namespace MiniBus;

public interface IDispatcher<in TRequest, TResponse>
{
    string HandlerName { get; }
    Task<Result<TResponse>> Handle(TRequest request);
}
