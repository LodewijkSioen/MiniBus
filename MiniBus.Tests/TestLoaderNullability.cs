using Microsoft.Extensions.DependencyInjection;

namespace MiniBus.Tests;

public class TestLoaderNullability
{
    [Test]
    public async Task TestLoadingNull()
    {
        using var scope = AppUnderTest.Services.CreateScope();

        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();
        var result = await bus.Handle<TestLoaderNullabilityHandler.Request, TestLoaderNullabilityHandler.Response>(new(true));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Status, Is.EqualTo(ResultStatus.NotFound));
    }

    [Test]
    public async Task TestNotLoadingNull()
    {
        using var scope = AppUnderTest.Services.CreateScope();

        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();
        var result = await bus.Handle<TestLoaderNullabilityHandler.Request, TestLoaderNullabilityHandler.Response>(new(false));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Response!.Count, Is.EqualTo(1));
    }
}

public class TestLoaderNullabilityHandler :
    IHandler<TestLoaderNullabilityHandler.Request, TestLoaderNullabilityHandler.Response>,
    ILoader<TestLoaderNullabilityHandler.Request>
{
    public record Response(int Count);
    public record Request(bool LoadsNull) : IRequest<Response>;

    private record Loaded(int Count = 1);

    private Loaded _loaded = null!;

    public Task<LoadResult> Load(Request request)
    {
        var isLoaded = this.TryAssign(request.LoadsNull ? null : new Loaded(), ref _loaded);

        return Task.FromResult(isLoaded ? LoadResult.Ok : LoadResult.NotFound("not found"));
    }

    public Task<Response> Handle(Request request)
    {
        return Task.FromResult(new Response(_loaded.Count));
    }
}