namespace MiniBus;

public interface IDispatcher<in TRequest, TResponse>
{
    Task<Result<TResponse>> Handle(TRequest request);
}
