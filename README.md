# MiniBus

MiniBus is a small mediator-style helper around dependency injection.

## Registration

Register MiniBus and scan assemblies for handlers:

```csharp
using MiniBus;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddMinibus(typeof(SomeRequest).Assembly);
var provider = services.BuildServiceProvider();
```

`AddMinibus(...)` registers:
- all `IHandler<TRequest, TResponse>` implementations as **scoped**
- `MiniBus` as **transient**

## Requests and handlers

```csharp
public record SomeRequest(int Id) : IRequest<SomeResponse>;
public record SomeResponse(string Name);

public sealed class SomeHandler : IHandler<SomeRequest, SomeResponse>
{
    public Task<SomeResponse> Handle(SomeRequest request)
        => Task.FromResult(new SomeResponse($"Item {request.Id}"));
}
```

Call the bus:

```csharp
var bus = provider.GetRequiredService<MiniBus.MiniBus>();
Result<SomeResponse> result = await bus.Handle<SomeRequest, SomeResponse>(new(1));
```

## Pipeline behavior

Before calling `IHandler<TRequest, TResponse>.Handle(...)`, MiniBus optionally runs:

1. `ILoader<TRequest>.Load(...)`
2. `IValidator<TRequest>.Validate(...)`
3. `IAsyncValidator<TRequest>.Validate(...)`

Short-circuit behavior:
- `LoadResult.NotFound(message)` -> `ResultStatus.NotFound`
- non-empty `ValidationResult` -> `ResultStatus.Invalid`
- otherwise -> `ResultStatus.Ok` with `Response`

## Result model

`Result<TResponse>` contains:
- `Status` (`Ok`, `NotFound`, `Invalid`)
- `Response` (when successful)
- `ValidationResult` (validation errors / not-found message)

`IsSuccess` is true when `Status == ResultStatus.Ok`.

## Loader nullability helper

For loader implementations, there is a helper extension:

```csharp
bool isLoaded = this.TryAssign(candidateValue, ref targetField);
```

`TryAssign` returns `true` and assigns when the candidate value is not null.
