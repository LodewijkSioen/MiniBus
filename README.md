# MiniBus

MiniBus is a small Mediator-style helper to move request handling logic out of
controllers and into dedicated handlers.

## What it does

- Registers handlers with DI through `AddMinibus(...)`
- Executes optional loading and validation before handling
- Returns a `Result<TResponse>` with status information (`Ok`, `NotFound`,
  `Invalid`)

## Quick start

```csharp
using MiniBus;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddMinibus(typeof(SomeRequest).Assembly);
```

Create a request + handler:

```csharp
public record SomeRequest(int Id) : IRequest<SomeResponse>;
public record SomeResponse(string Name);

public class SomeHandler : IHandler<SomeRequest, SomeResponse>
{
    public Task<SomeResponse> Handle(SomeRequest request)
    {
        return Task.FromResult(new SomeResponse("MiniBus"));
    }
}
```

Handle a request:

```csharp
var bus = provider.GetRequiredService<MiniBus.MiniBus>();
var result = await bus.Handle<SomeRequest, SomeResponse>(new SomeRequest(1));

if (result.IsSuccess)
{
    Console.WriteLine(result.Response!.Name);
}
```

## Optional pipeline interfaces

Handlers can also implement:

- `ILoader<TRequest>`: load extra data before handling
- `IValidator<TRequest>`: synchronous validation
- `IAsyncValidator<TRequest>`: asynchronous validation

If loading returns `LoadResult.NotFound(...)`, execution stops with
`ResultStatus.NotFound`.

If validation returns errors, execution stops with `ResultStatus.Invalid`.
