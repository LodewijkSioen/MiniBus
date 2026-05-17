using MiniBus.Convention;
using Microsoft.Extensions.DependencyInjection;

namespace MiniBus.Tests;

// ── Handlers under test ───────────────────────────────────────────────────────

[Handler]
public class TupleLoadConventionHandler
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

    public global::MiniBus.Convention.ValidationResult Validate(Entity entity, Config config)
    {
        var errors = new global::MiniBus.Convention.ValidationResult();
        if (entity.Id > 100)
            errors.Add(new global::MiniBus.Convention.ValidationError("Entity Id out of range", "RANGE"));
        return errors;
    }

    public Task<Response> Handle(Entity entity, Config config)
        => Task.FromResult(new Response($"{entity.Name}|{config.Setting}"));
}

// ── Tests ─────────────────────────────────────────────────────────────────────

[TestFixture]
public class TestPhase5_IntegrationTupleLoad
{
    [Test]
    public async Task BothValuesLoaded_PassesToHandle()
    {
        using var scope = AppUnderTest.ConventionServices.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ConventionBus>();

        var result = await bus.Handle(new TupleLoadConventionHandler.Request(1, 2));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Response!.Value, Is.EqualTo("entity-1|config-2"));
    }

    [Test]
    public async Task NullEntity_ReturnsNotFound()
    {
        using var scope = AppUnderTest.ConventionServices.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ConventionBus>();

        // EntityId = 0 → entity is null
        var result = await bus.Handle(new TupleLoadConventionHandler.Request(0, 2));

        Assert.That(result.Status, Is.EqualTo(global::MiniBus.Convention.ResultStatus.NotFound));
    }

    [Test]
    public async Task NullConfig_ReturnsNotFound()
    {
        using var scope = AppUnderTest.ConventionServices.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ConventionBus>();

        // ConfigId = 0 → config is null
        var result = await bus.Handle(new TupleLoadConventionHandler.Request(1, 0));

        Assert.That(result.Status, Is.EqualTo(global::MiniBus.Convention.ResultStatus.NotFound));
    }

    [Test]
    public async Task TupleLoadWithValidate_ValidRequest_Succeeds()
    {
        using var scope = AppUnderTest.ConventionServices.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ConventionBus>();

        var result = await bus.Handle(new TupleLoadWithValidateHandler.Request(5, 3));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Response!.Value, Is.EqualTo("e-5|cfg-3"));
    }

    [Test]
    public async Task TupleLoadWithValidate_InvalidEntity_ReturnsInvalid()
    {
        using var scope = AppUnderTest.ConventionServices.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ConventionBus>();

        // EntityId = 999 → load succeeds but validate rejects
        var result = await bus.Handle(new TupleLoadWithValidateHandler.Request(999, 1));

        Assert.That(result.Status, Is.EqualTo(global::MiniBus.Convention.ResultStatus.Invalid));
        Assert.That(result.ValidationErrors[0].Code, Is.EqualTo("RANGE"));
    }
}
