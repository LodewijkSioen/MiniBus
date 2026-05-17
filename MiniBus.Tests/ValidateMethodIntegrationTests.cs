using MiniBus.Convention;
using Microsoft.Extensions.DependencyInjection;

namespace MiniBus.Tests;

// ── Handlers under test ───────────────────────────────────────────────────────

[Handler]
public class SyncValidatingHandler
{
    public record Request(string Value);
    public record Response(string Out);

    public global::MiniBus.Convention.ValidationResult Validate(Request request)
    {
        var errors = new global::MiniBus.Convention.ValidationResult();
        if (string.IsNullOrEmpty(request.Value))
            errors.Add(new global::MiniBus.Convention.ValidationError("Value is required", "REQUIRED"));
        return errors;
    }

    public Task<Response> Handle(Request request)
        => Task.FromResult(new Response(request.Value.ToUpper()));
}

[Handler]
public class AsyncValidatingHandler
{
    public record Request(int Id);
    public record Response(int Value);

    public Task<global::MiniBus.Convention.ValidationResult> Validate(Request request)
    {
        var errors = new global::MiniBus.Convention.ValidationResult();
        if (request.Id <= 0)
            errors.Add(new global::MiniBus.Convention.ValidationError("Id must be positive", "POSITIVE"));
        return Task.FromResult(errors);
    }

    public Task<Response> Handle(Request request)
        => Task.FromResult(new Response(request.Id * 2));
}

[Handler]
public class LoadValidateConventionHandler
{
    public record Request(int Id);
    public record Response(string Name);
    public record Entity(int Id, string Name);

    public Task<Entity?> Load(Request request)
        => Task.FromResult<Entity?>(request.Id > 0 ? new Entity(request.Id, "item-" + request.Id) : null);

    public global::MiniBus.Convention.ValidationResult Validate(Entity entity)
    {
        var errors = new global::MiniBus.Convention.ValidationResult();
        if (entity.Id > 100)
            errors.Add(new global::MiniBus.Convention.ValidationError("Id out of range", "OUT_OF_RANGE"));
        return errors;
    }

    public Task<Response> Handle(Entity entity)
        => Task.FromResult(new Response(entity.Name));
}

// ── Tests ─────────────────────────────────────────────────────────────────────

[TestFixture]
public class ValidateMethodIntegrationTests
{
    [Test]
    public async Task SyncValidate_ValidRequest_PassesToHandle()
    {
        using var scope = AppUnderTest.ConventionServices.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ConventionBus>();

        var result = await bus.Handle(new SyncValidatingHandler.Request("hello"));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Response!.Out, Is.EqualTo("HELLO"));
    }

    [Test]
    public async Task SyncValidate_InvalidRequest_ReturnsInvalidWithErrors()
    {
        using var scope = AppUnderTest.ConventionServices.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ConventionBus>();

        var result = await bus.Handle(new SyncValidatingHandler.Request(""));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Status, Is.EqualTo(global::MiniBus.Convention.ResultStatus.Invalid));
        Assert.That(result.ValidationErrors, Has.Count.EqualTo(1));
        Assert.That(result.ValidationErrors[0].Code, Is.EqualTo("REQUIRED"));
    }

    [Test]
    public async Task AsyncValidate_ValidRequest_PassesToHandle()
    {
        using var scope = AppUnderTest.ConventionServices.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ConventionBus>();

        var result = await bus.Handle(new AsyncValidatingHandler.Request(5));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Response!.Value, Is.EqualTo(10));
    }

    [Test]
    public async Task AsyncValidate_InvalidRequest_ReturnsInvalidWithErrors()
    {
        using var scope = AppUnderTest.ConventionServices.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ConventionBus>();

        var result = await bus.Handle(new AsyncValidatingHandler.Request(-1));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Status, Is.EqualTo(global::MiniBus.Convention.ResultStatus.Invalid));
        Assert.That(result.ValidationErrors[0].Code, Is.EqualTo("POSITIVE"));
    }

    [Test]
    public async Task LoadValidate_NullLoad_ReturnsNotFoundBeforeValidate()
    {
        using var scope = AppUnderTest.ConventionServices.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ConventionBus>();

        // Id = 0 → Load returns null → NotFound, Validate never called
        var result = await bus.Handle(new LoadValidateConventionHandler.Request(0));

        Assert.That(result.Status, Is.EqualTo(global::MiniBus.Convention.ResultStatus.NotFound));
    }

    [Test]
    public async Task LoadValidate_InvalidEntity_ReturnsInvalid()
    {
        using var scope = AppUnderTest.ConventionServices.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ConventionBus>();

        // Id = 999 → Load returns entity, but Validate rejects it
        var result = await bus.Handle(new LoadValidateConventionHandler.Request(999));

        Assert.That(result.Status, Is.EqualTo(global::MiniBus.Convention.ResultStatus.Invalid));
        Assert.That(result.ValidationErrors[0].Code, Is.EqualTo("OUT_OF_RANGE"));
    }

    [Test]
    public async Task LoadValidate_ValidEntity_PassesToHandle()
    {
        using var scope = AppUnderTest.ConventionServices.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ConventionBus>();

        var result = await bus.Handle(new LoadValidateConventionHandler.Request(42));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Response!.Name, Is.EqualTo("item-42"));
    }
}
