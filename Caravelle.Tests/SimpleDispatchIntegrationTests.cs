using Microsoft.Extensions.DependencyInjection;

namespace Caravelle.Tests;

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
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();

        var result = await bus.Handle(new SimpleHandler.Request(21));
        var response = result.Match(response => response);

        Assert.That(response, Is.Not.Null);
        Assert.That(response.DoubledValue, Is.EqualTo(42));
    }

    [Test]
    public async Task TypedExtensionMethod_ResolvesWithoutExplicitTypeArgs()
    {
        // The generated Handle(this Caravelle, Request) extension means
        // the caller never needs to specify TResponse.
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();

        var result = await bus.Handle(new SimpleHandler.Request(5));
        var response = result.Match(response => response);

        Assert.That(response, Is.Not.Null);
        Assert.That(response.DoubledValue, Is.EqualTo(10));
    }

    [Test]
    public async Task Match_OnAsyncHandleSuccess_UsesSuccessBranch()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();

        var result = await bus.Handle(new SimpleHandler.Request(8));
        var value = result.Match(onSuccess: response => response.DoubledValue);

        Assert.That(value, Is.EqualTo(16));
    }

    [Test]
    public void HandlerAndDispatcher_AreRegisteredAsTransient()
    {
        // Re-resolving within the same scope returns different transient instances.
        using var scope = AppUnderTest.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var h1 = sp.GetRequiredService<SyncSimpleHandler>();
        var h2 = sp.GetRequiredService<SyncSimpleHandler>();
        var d1 = sp.GetRequiredService<global::Caravelle.IDispatcher<SyncSimpleHandler.Request, SyncSimpleHandlerDispatcher.Result>>();
        var d2 = sp.GetRequiredService<global::Caravelle.IDispatcher<SyncSimpleHandler.Request, SyncSimpleHandlerDispatcher.Result>>();

        Assert.That(ReferenceEquals(h1, h2), Is.False, "Handler should be transient");
        Assert.That(ReferenceEquals(d1, d2), Is.False, "Dispatcher should be transient");
    }

    [Test]
    public async Task SyncHandle_ReturnsSuccess()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();

        var result = await bus.Handle(new SyncSimpleHandler.Request(7));
        var response = result.Match(response => response);

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Result, Is.EqualTo(21));
    }

    [Test]
    public async Task Match_OnSyncHandleSuccess_UsesSuccessBranch()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();

        var result = await bus.Handle(new SyncSimpleHandler.Request(7));
        var value = result.Match(onSuccess: response => response.Result);

        Assert.That(value, Is.EqualTo(21));
    }
}
