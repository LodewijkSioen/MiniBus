using Microsoft.Extensions.DependencyInjection;

namespace MiniBus.Tests;

// ── SyncHandler under test ────────────────────────────────────────────────────────

[Handler]
public class SimpleHandler
{
    public record Request(int Value);
    public record Response(int DoubledValue);

    public Task<Response> Handle(Request request)
        => Task.FromResult(new Response(request.Value * 2));
}

[Handler]
public class SyncSimpleHandler
{
    public record Request(int Value);
    public record Response(int Result);

    public Response Handle(Request request)
        => new Response(request.Value * 3);
}

// ── Tests ─────────────────────────────────────────────────────────────────────

[TestFixture]
public class SimpleDispatchIntegrationTests
{
    [Test]
    public async Task HappyPath_HandleOnly_ReturnsSuccess()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new SimpleHandler.Request(21));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Response!.DoubledValue, Is.EqualTo(42));
    }

    [Test]
    public async Task TypedExtensionMethod_ResolvesWithoutExplicitTypeArgs()
    {
        // The generated Handle(this ConventionBus, Request) extension means
        // the caller never needs to specify TResponse.
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new SimpleHandler.Request(5));

        Assert.That(result.Response!.DoubledValue, Is.EqualTo(10));
    }

    [Test]
    public void HandlerAndDispatcher_AreRegisteredAsScoped()
    {
        // Re-resolving within the same scope returns the same instance.
        using var scope = AppUnderTest.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var h1 = sp.GetRequiredService<SyncSimpleHandler>();
        var h2 = sp.GetRequiredService<SyncSimpleHandler>();

        Assert.That(ReferenceEquals(h1, h2), Is.True, "Handler should be scoped");
    }

    [Test]
    public async Task SyncHandle_ReturnsSuccess()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new SyncSimpleHandler.Request(7));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Response!.Result, Is.EqualTo(21));
    }
}
