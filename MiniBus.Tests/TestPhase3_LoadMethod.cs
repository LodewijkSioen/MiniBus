using MiniBus.Convention;
using Microsoft.Extensions.DependencyInjection;

namespace MiniBus.Tests;

// ── Handlers under test ───────────────────────────────────────────────────────

[Handler]
public class NullableLoadHandler
{
    public record Request(bool ReturnNull);
    public record Response(string Value);
    public record Loaded(string Value);

    public Task<Loaded?> Load(Request request)
        => Task.FromResult<Loaded?>(request.ReturnNull ? null : new Loaded("loaded!"));

    public Task<Response> Handle(Loaded loaded)
        => Task.FromResult(new Response(loaded.Value));
}

[Handler]
public class LoadWithBothParamsHandler
{
    public record Request(string Prefix);
    public record Response(string Combined);
    public record Loaded(string Data);

    public Task<Loaded?> Load(Request request)
        => Task.FromResult<Loaded?>(new Loaded("world"));

    public Task<Response> Handle(Request request, Loaded loaded)
        => Task.FromResult(new Response($"{request.Prefix}: {loaded.Data}"));
}

[Handler]
public class SyncLoadConventionHandler
{
    public record Request(int Id);
    public record Response(int Value);
    public record Loaded(int Value);

    // Sync Load (returns T? not Task<T?>)
    public Loaded? Load(Request request)
        => request.Id == 0 ? null : new Loaded(request.Id * 3);

    public Task<Response> Handle(Loaded loaded)
        => Task.FromResult(new Response(loaded.Value));
}

// ── Tests ─────────────────────────────────────────────────────────────────────

[TestFixture]
public class TestPhase3_IntegrationLoadMethod
{
    [Test]
    public async Task LoadReturnsNull_ResultIsNotFound()
    {
        using var scope = AppUnderTest.ConventionServices.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ConventionBus>();

        var result = await bus.Handle(new NullableLoadHandler.Request(ReturnNull: true));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Status, Is.EqualTo(global::MiniBus.Convention.ResultStatus.NotFound));
    }

    [Test]
    public async Task LoadReturnsValue_HandleReceivesIt()
    {
        using var scope = AppUnderTest.ConventionServices.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ConventionBus>();

        var result = await bus.Handle(new NullableLoadHandler.Request(ReturnNull: false));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Response!.Value, Is.EqualTo("loaded!"));
    }

    [Test]
    public async Task HandleReceivesBothRequestAndLoaded()
    {
        using var scope = AppUnderTest.ConventionServices.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ConventionBus>();

        var result = await bus.Handle(new LoadWithBothParamsHandler.Request("hello"));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Response!.Combined, Is.EqualTo("hello: world"));
    }

    [Test]
    public async Task SyncLoad_ReturnsNull_IsNotFound()
    {
        using var scope = AppUnderTest.ConventionServices.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ConventionBus>();

        var result = await bus.Handle(new SyncLoadConventionHandler.Request(Id: 0));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Status, Is.EqualTo(global::MiniBus.Convention.ResultStatus.NotFound));
    }

    [Test]
    public async Task SyncLoad_ReturnsValue_HandleReceivesIt()
    {
        using var scope = AppUnderTest.ConventionServices.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ConventionBus>();

        var result = await bus.Handle(new SyncLoadConventionHandler.Request(Id: 4));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Response!.Value, Is.EqualTo(12));
    }
}
