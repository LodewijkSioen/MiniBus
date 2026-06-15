namespace MiniBus;

public interface IDispatcher<in TRequest, TResult>
{
    string HandlerName { get; }
    Task<TResult> Handle(TRequest request);
}

// This can be removed once Union types are in C#
public interface IDispatchResult
{
    object? Value { get; }
}