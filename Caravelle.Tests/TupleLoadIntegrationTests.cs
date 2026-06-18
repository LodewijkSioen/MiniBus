using Microsoft.Extensions.DependencyInjection;

namespace Caravelle.Tests;

// ── Handlers under test ───────────────────────────────────────────────────────

[Handler]
public class TupleLoadHandler
{
    public record Request(int EntityId, int ConfigId);
    public record Response(string Value);
    public record Entity(string Name);
    public record Config(string Setting);

    public Task<(Entity? entity, Config? config)> Load(Request request)
    {
        Entity? entity = request.EntityId > 0 ? new Entity("entity-" + request.EntityId) : null;
        Config? config = request.ConfigId > 0 ? new Config("config-" + request.ConfigId) : null;
        return Task.FromResult<(Entity?, Config?)>((entity, config));
    }

    public Task<Response> Handle(Entity entity, Config config)
        => Task.FromResult(new Response($"{entity.Name}|{config.Setting}"));
}

[Handler]
public class TupleLoadWithValidateHandler
{
    public record Request(int EntityId, int ConfigId);
    public record Response(string Value);
    public record Entity(int Id, string Name);
    public record Config(string Setting);

    public Task<(Entity? entity, Config? config)> Load(Request request)
    {
        Entity? entity = request.EntityId > 0 ? new Entity(request.EntityId, "e-" + request.EntityId) : null;
        Config? config = request.ConfigId > 0 ? new Config("cfg-" + request.ConfigId) : null;
        return Task.FromResult<(Entity?, Config?)>((entity, config));
    }

    public global::Caravelle.ValidationResult Validate(Entity entity, Config config)
    {
        var errors = new global::Caravelle.ValidationResult();
        if (entity.Id > 100)
            errors.Add(new global::Caravelle.ValidationError("Entity Id out of range", "RANGE"));
        return errors;
    }

    public Task<Response> Handle(Entity entity, Config config)
        => Task.FromResult(new Response($"{entity.Name}|{config.Setting}"));
}

// ── Tests ─────────────────────────────────────────────────────────────────────

[TestFixture]
public class TupleLoadIntegrationTests
{
    [Test]
    public async Task BothValuesLoaded_PassesToHandle()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();

        var result = await bus.Handle(new TupleLoadHandler.Request(1, 2));
        var response = result.Match(r => r, _ => null!);

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Value, Is.EqualTo("entity-1|config-2"));
    }

    [Test]
    public async Task NullEntity_ReturnsNotFound()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();

        // EntityId = 0 → entity is null
        var result = await bus.Handle(new TupleLoadHandler.Request(0, 2));

        Assert.That(result.Value, Is.TypeOf<global::Caravelle.NotFoundResult>());
    }

    [Test]
    public async Task NullConfig_ReturnsNotFound()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();

        // ConfigId = 0 → config is null
        var result = await bus.Handle(new TupleLoadHandler.Request(1, 0));

        Assert.That(result.Value, Is.TypeOf<global::Caravelle.NotFoundResult>());
    }

    [Test]
    public async Task TupleLoadWithValidate_ValidRequest_Succeeds()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();

        var result = await bus.Handle(new TupleLoadWithValidateHandler.Request(5, 3));
        var response = result.Match(r => r, _ => null!, _ => null!);

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Value, Is.EqualTo("e-5|cfg-3"));
    }

    [Test]
    public async Task TupleLoadWithValidate_InvalidEntity_ReturnsInvalid()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();

        // EntityId = 999 → load succeeds but validate rejects
        var result = await bus.Handle(new TupleLoadWithValidateHandler.Request(999, 1));
        var errors = result.Match(_ => null!, r => r, _ => null!);

        Assert.That(errors, Is.Not.Null);
        Assert.That(errors[0].Code, Is.EqualTo("RANGE"));
    }
}
