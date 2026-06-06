namespace MiniBus;

public interface IDispatcher<in TRequest, TResponse>
    where TResponse : notnull
{
    string HandlerName { get; }
    Task<Result<TResponse>> Handle(TRequest request);
}
