using Microsoft.Extensions.DependencyInjection;

namespace MiniBus.Tests;

// ── Handlers under test ───────────────────────────────────────────────────────

[Handler]
public class SyncValidatingHandler
{
    public record Request(string Value);
    public record Response(string Out);

    public global::MiniBus.ValidationResult Validate(Request request)
    {
        var errors = new global::MiniBus.ValidationResult();
        if (string.IsNullOrEmpty(request.Value))
            errors.Add(new global::MiniBus.ValidationError("Value is required", "REQUIRED"));
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

    public Task<global::MiniBus.ValidationResult> Validate(Request request)
    {
        var errors = new global::MiniBus.ValidationResult();
        if (request.Id <= 0)
            errors.Add(new global::MiniBus.ValidationError("Id must be positive", "POSITIVE"));
        return Task.FromResult(errors);
    }

    public Task<Response> Handle(Request request)
        => Task.FromResult(new Response(request.Id * 2));
}

[Handler]
public class LoadValidateHandler
{
    public record Request(int Id);
    public record Response(string Name);
    public record Entity(int Id, string Name);

    public Task<Entity?> Load(Request request)
        => Task.FromResult<Entity?>(request.Id > 0 ? new Entity(request.Id, "item-" + request.Id) : null);

    public global::MiniBus.ValidationResult Validate(Entity entity)
    {
        var errors = new global::MiniBus.ValidationResult();
        if (entity.Id > 100)
            errors.Add(new global::MiniBus.ValidationError("Id out of range", "OUT_OF_RANGE"));
        return errors;
    }

    public Task<Response> Handle(Entity entity)
        => Task.FromResult(new Response(entity.Name));
}

[Handler]
public class ValidateTupleResultHandler
{
    public record Request(string Value);
    public record Prepared(string Value);
    public record Response(string Value);

    public (global::MiniBus.ValidationResult validation, Prepared prepared) Validate(Request request)
    {
        var errors = new global::MiniBus.ValidationResult();
        if (string.IsNullOrWhiteSpace(request.Value))
            errors.Add(new global::MiniBus.ValidationError("Value is required", "REQUIRED"));

        return (errors, new Prepared(request.Value));
    }

    public Task<Response> Handle(Prepared prepared)
        => Task.FromResult(new Response(prepared.Value.ToUpperInvariant()));
}

[Handler]
public class HandleTupleValidationHandler
{
    public record Request(int Id);
    public record Response(int Value);

    public (Response response, global::MiniBus.ValidationResult validation) Handle(Request request)
    {
        var errors = new global::MiniBus.ValidationResult();
        if (request.Id <= 0)
            errors.Add(new global::MiniBus.ValidationError("Id must be positive", "POSITIVE"));

        return (new Response(request.Id * 3), errors);
    }
}

[Handler]
public class PostValidationHandler
{
    public record Request(string Value);
    public record Response(string Value);
    public record Audit(string Value);

    public Response Handle(Request request)
        => new Response(request.Value.ToUpperInvariant());

    public Audit? AfterLoad(Response response)
        => response.Value == "MISSING" ? null : new Audit(response.Value);

    public global::MiniBus.ValidationResult PostValidate(Audit audit)
    {
        var errors = new global::MiniBus.ValidationResult();
        if (audit.Value == "INVALID")
            errors.Add(new global::MiniBus.ValidationError("Value is invalid", "POST_INVALID"));
        return errors;
    }
}

// ── Tests ─────────────────────────────────────────────────────────────────────

[TestFixture]
public class ValidateMethodIntegrationTests
{
    [Test]
    public async Task SyncValidate_ValidRequest_PassesToHandle()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new SyncValidatingHandler.Request("hello"));
        var response = result.Match(r => r, _ => null!);

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Out, Is.EqualTo("HELLO"));
    }

    [Test]
    public async Task SyncValidate_InvalidRequest_ReturnsInvalidWithErrors()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new SyncValidatingHandler.Request(""));
        var errors = result.Match(_ => null!, r => r);

        Assert.That(errors, Is.Not.Null);
        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0].Code, Is.EqualTo("REQUIRED"));
    }

    [Test]
    public async Task SyncValidate_InvalidRequest_Match_UsesInvalidBranch()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new SyncValidatingHandler.Request(""));
        var errorCode = result.Match(
            onSuccess: _ => "OK",
            onInvalid: errors => errors[0].Code);

        Assert.That(errorCode, Is.EqualTo("REQUIRED"));
    }

    [Test]
    public async Task AsyncValidate_ValidRequest_PassesToHandle()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new AsyncValidatingHandler.Request(5));
        var response = result.Match(r => r, _ => null!);

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Value, Is.EqualTo(10));
    }

    [Test]
    public async Task AsyncValidate_InvalidRequest_ReturnsInvalidWithErrors()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new AsyncValidatingHandler.Request(-1));
        var errors = result.Match(_ => null!, r => r);

        Assert.That(errors, Is.Not.Null);
        Assert.That(errors[0].Code, Is.EqualTo("POSITIVE"));
    }

    [Test]
    public async Task LoadValidate_NullLoad_ReturnsNotFoundBeforeValidate()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        // Id = 0 → Load returns null → NotFound, Validate never called
        var result = await bus.Handle(new LoadValidateHandler.Request(0));

        Assert.That(result.Value, Is.TypeOf<global::MiniBus.NotFoundResult>());
    }

    [Test]
    public async Task LoadValidate_NullLoad_Match_UsesNotFoundBranch()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new LoadValidateHandler.Request(0));
        var marker = result.Match(
            onSuccess: _ => "success",
            onInvalid: _ => "invalid",
            onNotFound: _ => "notfound");

        Assert.That(marker, Is.EqualTo("notfound"));
    }

    [Test]
    public async Task LoadValidate_InvalidEntity_ReturnsInvalid()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        // Id = 999 → Load returns entity, but Validate rejects it
        var result = await bus.Handle(new LoadValidateHandler.Request(999));
        var errors = result.Match(_ => null!, r => r, _ => null!);

        Assert.That(errors, Is.Not.Null);
        Assert.That(errors[0].Code, Is.EqualTo("OUT_OF_RANGE"));
    }

    [Test]
    public async Task LoadValidate_ValidEntity_PassesToHandle()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new LoadValidateHandler.Request(42));
        var response = result.Match(r => r, _ => null!, _ => null!);

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Name, Is.EqualTo("item-42"));
    }

    [Test]
    public async Task ValidateTupleResult_InvalidRequest_ReturnsInvalid()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new ValidateTupleResultHandler.Request(""));
        var errors = result.Match(_ => null!, r => r);

        Assert.That(errors, Is.Not.Null);
        Assert.That(errors[0].Code, Is.EqualTo("REQUIRED"));
    }

    [Test]
    public async Task ValidateTupleResult_ValidRequest_PassesToHandle()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new ValidateTupleResultHandler.Request("hello"));
        var response = result.Match(r => r, _ => null!);

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Value, Is.EqualTo("HELLO"));
    }

    [Test]
    public async Task HandleTupleValidation_InvalidRequest_ReturnsInvalid()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new HandleTupleValidationHandler.Request(0));
        var errors = result.Match(_ => null!, r => r);

        Assert.That(errors, Is.Not.Null);
        Assert.That(errors[0].Code, Is.EqualTo("POSITIVE"));
    }

    [Test]
    public async Task HandleTupleValidation_ValidRequest_UsesTupleItem1AsResponse()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new HandleTupleValidationHandler.Request(5));
        var response = result.Match(r => r, _ => null!);

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Value, Is.EqualTo(15));
    }

    [Test]
    public async Task PostValidation_ValidRequest_ReturnsSuccess()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new PostValidationHandler.Request("ok"));
        var response = result.Match(r => r, _ => null!, _ => null!);

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Value, Is.EqualTo("OK"));
    }

    [Test]
    public async Task PostValidation_InvalidPostStep_ReturnsInvalid()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new PostValidationHandler.Request("invalid"));
        var errors = result.Match(_ => null!, r => r, _ => null!);

        Assert.That(errors, Is.Not.Null);
        Assert.That(errors[0].Code, Is.EqualTo("POST_INVALID"));
    }

    [Test]
    public async Task PostValidation_NullPostOutput_ReturnsNotFound()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        var result = await bus.Handle(new PostValidationHandler.Request("missing"));

        Assert.That(result.Value, Is.TypeOf<global::MiniBus.NotFoundResult>());
    }
}
