using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;

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
public class SyncLoadHandler
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

[Handler]
public class NotFoundMessageHandler
{
    public record Request(bool ReturnNull);
    public record Response(string Value);
    public record Loaded(string Value);

    public Task<Loaded?> Load(Request request)
        => Task.FromResult<Loaded?>(request.ReturnNull ? null : new Loaded("loaded!"));

    public Task<Response> Handle([Required(ErrorMessage = "Entity not found")] Loaded loaded)
        => Task.FromResult(new Response(loaded.Value));
}

[Handler]
public class RequiredNoMessageHandler
{
    public record Request(bool ReturnNull);
    public record Response(string Value);
    public record Loaded(string Value);

    public Task<Loaded?> Load(Request request)
        => Task.FromResult<Loaded?>(request.ReturnNull ? null : new Loaded("loaded!"));

    public Task<Response> Handle([Required] Loaded loaded)
        => Task.FromResult(new Response(loaded.Value));
}

// ── Tests ─────────────────────────────────────────────────────────────────────

[TestFixture]
public class LoadMethodIntegrationTests
{
    [Test]
    public async Task LoadReturnsNull_ResultIsNotFound()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new NullableLoadHandler.Request(ReturnNull: true));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Status, Is.EqualTo(global::MiniBus.ResultStatus.NotFound));
    }

    [Test]
    public async Task LoadReturnsValue_HandleReceivesIt()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new NullableLoadHandler.Request(ReturnNull: false));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Response!.Value, Is.EqualTo("loaded!"));
    }

    [Test]
    public async Task HandleReceivesBothRequestAndLoaded()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new LoadWithBothParamsHandler.Request("hello"));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Response!.Combined, Is.EqualTo("hello: world"));
    }

    [Test]
    public async Task SyncLoad_ReturnsNull_IsNotFound()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new SyncLoadHandler.Request(Id: 0));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Status, Is.EqualTo(global::MiniBus.ResultStatus.NotFound));
    }

    [Test]
    public async Task SyncLoad_ReturnsValue_HandleReceivesIt()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new SyncLoadHandler.Request(Id: 4));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Response!.Value, Is.EqualTo(12));
    }

    [Test]
    public async Task LoadReturnsNull_RequiredWithMessage_NotFoundHasMessageAndCode()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new NotFoundMessageHandler.Request(ReturnNull: true));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Status, Is.EqualTo(global::MiniBus.ResultStatus.NotFound));
        Assert.That(result.ValidationErrors, Has.Count.EqualTo(1));
        Assert.That(result.ValidationErrors[0].Message, Is.EqualTo("Entity not found"));
        Assert.That(result.ValidationErrors[0].Code, Is.EqualTo("notfound"));
    }

    [Test]
    public async Task LoadReturnsNull_RequiredWithoutMessage_ValidationErrorsIsEmpty()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new RequiredNoMessageHandler.Request(ReturnNull: true));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Status, Is.EqualTo(global::MiniBus.ResultStatus.NotFound));
        Assert.That(result.ValidationErrors, Has.Count.EqualTo(1));
        Assert.That(result.ValidationErrors[0].Message, Is.EqualTo("loaded cannot be null"));
        Assert.That(result.ValidationErrors[0].Code, Is.EqualTo("notfound"));
    }
}
