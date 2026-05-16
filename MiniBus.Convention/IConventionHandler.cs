namespace MiniBus.Convention;

public interface IConventionHandler<TRequest, TResponse>
{
    Task<Result<TResponse>> Handle(TRequest request);
}
