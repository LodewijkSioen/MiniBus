using Microsoft.Extensions.DependencyInjection;

namespace Caravelle.Tests;

// ── Shared middleware infrastructure ──────────────────────────────────────────

public interface IMiddlewareOrderProbe
{
    IReadOnlyList<string> Order { get; }
    void Record(string step);
}

public sealed class MiddlewareOrderProbe : IMiddlewareOrderProbe
{
    private readonly List<string> _order = [];

    public IReadOnlyList<string> Order => _order;

    public void Record(string step) => _order.Add(step);
}

public record AuditMarker;
public record OwnAuditMarker;

/// <summary>
/// Reusable post-handle "audit" middleware. Applied to two unrelated handlers with
/// completely different Request/Response types via plain class inheritance, proving
/// a single implementation can be shared without any generator-side wiring.
/// </summary>
public abstract class AuditMiddlewareBase
{
    public AuditMarker AfterAudit(IMiddlewareOrderProbe probe)
    {
        probe.Record("BaseAfter");
        return new AuditMarker();
    }
}

[Handler]
public class FirstAuditedHandler : AuditMiddlewareBase
{
    public record Request(string Value);
    public record Response(string Value);

    public Response Handle(Request request) => new Response(request.Value);

    public OwnAuditMarker AfterOwn(Response response, IMiddlewareOrderProbe probe)
    {
        probe.Record("OwnAfter");
        return new OwnAuditMarker();
    }
}

[Handler]
public class SecondAuditedHandler : AuditMiddlewareBase
{
    public record Request(int Value);
    public record Response(int Value);

    public Response Handle(Request request) => new Response(request.Value * 2);
}

// ── Shared validation middleware for a common request type ───────────────────

public record ValidatedRequest(string Value);
public record ValidatedResponse(string Value);

public abstract class InheritedValidationMiddlewareBase
{
    public global::Caravelle.ValidationResult Validate(ValidatedRequest request)
    {
        var errors = new global::Caravelle.ValidationResult();
        if (string.IsNullOrEmpty(request.Value))
            errors.Add(new global::Caravelle.ValidationError("Value is required", "REQUIRED"));
        return errors;
    }
}

[Handler]
public class InheritedValidationHandler : InheritedValidationMiddlewareBase
{
    public ValidatedResponse Handle(ValidatedRequest request) => new ValidatedResponse(request.Value.ToUpper());
}

[TestFixture]
public class MiddlewareInheritanceIntegrationTests
{
    [Test]
    public async Task AuditMiddleware_IsReusedAcrossDifferentHandlers_RecordsBaseAfterForBoth()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();
        var probe = scope.ServiceProvider.GetRequiredService<IMiddlewareOrderProbe>();

        var firstResult = await bus.Handle(new FirstAuditedHandler.Request("hello"));
        var secondResult = await bus.Handle(new SecondAuditedHandler.Request(21));

        Assert.That(firstResult, Is.EqualTo(new FirstAuditedHandler.Response("hello")));
        Assert.That(secondResult, Is.EqualTo(new SecondAuditedHandler.Response(42)));
        Assert.That(probe.Order.Count(step => step == "BaseAfter"), Is.EqualTo(2));
    }

    [Test]
    public async Task OwnAndInheritedAfterMethods_ExecuteInOwnFirstAncestorLastOrder()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();
        var probe = scope.ServiceProvider.GetRequiredService<IMiddlewareOrderProbe>();

        await bus.Handle(new FirstAuditedHandler.Request("hello"));

        Assert.That(probe.Order, Is.EqualTo(new[] { "OwnAfter", "BaseAfter" }));
    }

    [Test]
    public async Task InheritedValidateMethod_InvalidRequest_ReturnsInvalid()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();

        var result = await bus.Handle(new ValidatedRequest(""));

        var validationResult = result.Value as global::Caravelle.ValidationResult;
        Assert.That(validationResult, Is.Not.Null);
        Assert.That(validationResult!.IsValid(), Is.False);
    }

    [Test]
    public async Task InheritedValidateMethod_ValidRequest_PassesToHandle()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();

        var result = await bus.Handle(new ValidatedRequest("hello"));

        Assert.That(result.Value, Is.EqualTo(new ValidatedResponse("HELLO")));
    }
}
