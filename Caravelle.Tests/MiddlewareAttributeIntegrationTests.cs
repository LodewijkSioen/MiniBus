using Microsoft.Extensions.DependencyInjection;

namespace Caravelle.Tests;

// ── Global (AllHandlers) middleware: also proves the outer/inner onion ordering
// relative to inherited base-class phases ────────────────────────────────────

public abstract class OnionInheritedBase
{
    public void BeforeInheritedOnion(IMiddlewareOrderProbe probe) => probe.Record("InheritedBefore");
    public void AfterInheritedOnion(IMiddlewareOrderProbe probe) => probe.Record("InheritedAfter");
}

[Middleware<AllHandlers>]
public class OnionMiddleware
{
    public void BeforeOnionMiddleware(IMiddlewareOrderProbe probe) => probe.Record("MiddlewareBefore");
    public void AfterOnionMiddleware(IMiddlewareOrderProbe probe) => probe.Record("MiddlewareAfter");
}

[Handler]
public class OnionOrderingHandler : OnionInheritedBase
{
    public record Request(int Id);
    public record Response(int Id);
    public Response Handle(Request request) => new Response(request.Id);
}

[Handler]
public class UnrelatedGlobalMiddlewareHandler
{
    public record Request(string Name);
    public record Response(string Name);
    public Response Handle(Request request) => new Response(request.Name);
}

// ── ForInterface ───────────────────────────────────────────────────────────

public interface IAdminHandlerMarker;

[Middleware<ForInterface<IAdminHandlerMarker>>]
public class AdminOnlyMiddleware
{
    public void BeforeAdminCheck(IMiddlewareOrderProbe probe) => probe.Record("AdminCheck");
}

[Handler]
public class AdminOnlyHandler : IAdminHandlerMarker
{
    public record Request(int Id);
    public record Response(int Id);
    public Response Handle(Request request) => new Response(request.Id);
}

[Handler]
public class NonAdminHandler
{
    public record Request(int Id);
    public record Response(int Id);
    public Response Handle(Request request) => new Response(request.Id);
}

// ── ForReturnType / ForRequestType / ForVariable ────────────────────────────

public interface ITaggedResponseMarker;
public interface ITaggedRequestMarker;
public interface ITaggedEntityMarker;

[Middleware<ForReturnType<ITaggedResponseMarker>>]
public class ResponseTaggingMiddleware
{
    public void BeforeResponseTagCheck(IMiddlewareOrderProbe probe) => probe.Record("ResponseTagCheck");
}

[Handler]
public class TaggedResponseHandler
{
    public record Request(int Id);
    public record Response(int Id) : ITaggedResponseMarker;
    public Response Handle(Request request) => new Response(request.Id);
}

[Middleware<ForRequestType<ITaggedRequestMarker>>]
public class RequestTaggingMiddleware
{
    public void BeforeRequestTagCheck(IMiddlewareOrderProbe probe) => probe.Record("RequestTagCheck");
}

[Handler]
public class TaggedRequestHandler
{
    public record Request(int Id) : ITaggedRequestMarker;
    public record Response(int Id);
    public Response Handle(Request request) => new Response(request.Id);
}

[Middleware<ForVariable<ITaggedEntityMarker>>]
public class EntityTaggingMiddleware
{
    public void BeforeEntityTagCheck(IMiddlewareOrderProbe probe) => probe.Record("EntityTagCheck");
}

[Handler]
public class LoadingEntityHandler
{
    public record Request(int Id);
    public record Response(int Id);
    public record Entity(int Id) : ITaggedEntityMarker;
    public Entity Load(Request request) => new Entity(request.Id);
    public Response Handle(Entity entity) => new Response(entity.Id);
}

// ── ForNamespaceOf / ForAssemblyOf ───────────────────────────────────────────

public record NamespaceAndAssemblyMarker;

[Middleware<ForNamespaceOf<NamespaceAndAssemblyMarker>>]
public class NamespaceMiddleware
{
    public void BeforeNamespaceCheck(IMiddlewareOrderProbe probe) => probe.Record("NamespaceCheck");
}

[Middleware<ForAssemblyOf<NamespaceAndAssemblyMarker>>]
public class AssemblyMiddleware
{
    public void BeforeAssemblyCheck(IMiddlewareOrderProbe probe) => probe.Record("AssemblyCheck");
}

[Handler]
public class NamespaceAndAssemblyHandler
{
    public record Request(int Id);
    public record Response(int Id);
    public Response Handle(Request request) => new Response(request.Id);
}

// ── ForAttribute ─────────────────────────────────────────────────────────────

public class AuditedAttribute : Attribute;

[Middleware<ForAttribute<AuditedAttribute>>]
public class AttributeBasedMiddleware
{
    public void BeforeAttributeCheck(IMiddlewareOrderProbe probe) => probe.Record("AttributeCheck");
}

[Audited]
[Handler]
public class AttributeTaggedHandler
{
    public record Request(int Id);
    public record Response(int Id);
    public Response Handle(Request request) => new Response(request.Id);
}

// ── ForHandler ───────────────────────────────────────────────────────────────

[Handler]
public class SpecificallyTargetedHandler
{
    public record Request(int Id);
    public record Response(int Id);
    public Response Handle(Request request) => new Response(request.Id);
}

[Middleware<ForHandler<SpecificallyTargetedHandler>>]
public class SpecificHandlerMiddleware
{
    public void BeforeSpecificCheck(IMiddlewareOrderProbe probe) => probe.Record("SpecificCheck");
}

[Handler]
public class NotSpecificallyTargetedHandler
{
    public record Request(int Id);
    public record Response(int Id);
    public Response Handle(Request request) => new Response(request.Id);
}

// ── HasValidation / HasNotFound ──────────────────────────────────────────────

[Middleware<HasValidation>]
public class ValidationAwareMiddleware
{
    public void BeforeValidationAwareCheck(IMiddlewareOrderProbe probe) => probe.Record("ValidationAwareCheck");
}

[Handler]
public class HasValidationHandler
{
    public record Request(int Id);
    public record Response(int Id);
    public ValidationResult Validate(Request request) => new ValidationResult();
    public Response Handle(Request request) => new Response(request.Id);
}

[Handler]
public class HasNoValidationHandler
{
    public record Request(int Id);
    public record Response(int Id);
    public Response Handle(Request request) => new Response(request.Id);
}

[Middleware<HasNotFound>]
public class NotFoundAwareMiddleware
{
    public void BeforeNotFoundAwareCheck(IMiddlewareOrderProbe probe) => probe.Record("NotFoundAwareCheck");
}

[Handler]
public class HasNullableLoadHandler
{
    public record Request(int Id);
    public record Response(int Id);
    public record Entity(int Id);
    public Entity? Load(Request request) => request.Id > 0 ? new Entity(request.Id) : null;
    public Response Handle(Entity entity) => new Response(entity.Id);
}

// ── DI lifetime: a middleware-resolved scoped service must be the same instance
// visible elsewhere in the same scope (proves AddTransient<Middleware> still
// resolves its own dependencies against the ambient scope) ──────────────────

public interface IScopeCheckHandlerMarker;

[Middleware<ForInterface<IScopeCheckHandlerMarker>>]
public class ScopeCheckingMiddleware
{
    public void BeforeScopeCheck(IScopeProbe scopeProbe, IMiddlewareOrderProbe orderProbe) =>
        orderProbe.Record($"MiddlewareScope:{scopeProbe.ScopeId}");
}

[Handler]
public class ScopeCheckHandler : IScopeCheckHandlerMarker
{
    public record Request(int Id);
    public record Response(Guid ScopeId);
    public Response Handle(Request request, IScopeProbe scopeProbe) => new Response(scopeProbe.ScopeId);
}

[TestFixture]
public class MiddlewareAttributeIntegrationTests
{
    [Test]
    public async Task AllHandlersMiddleware_AppliesToUnrelatedHandler()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();
        var probe = scope.ServiceProvider.GetRequiredService<IMiddlewareOrderProbe>();

        await bus.Handle(new UnrelatedGlobalMiddlewareHandler.Request("hello"));

        Assert.That(probe.Order, Does.Contain("MiddlewareBefore"));
        Assert.That(probe.Order, Does.Contain("MiddlewareAfter"));
    }

    [Test]
    public async Task AllHandlersMiddleware_RunsOutermost_BeforeFirstAfterLast()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();
        var probe = scope.ServiceProvider.GetRequiredService<IMiddlewareOrderProbe>();

        await bus.Handle(new OnionOrderingHandler.Request(1));

        // OnionOrderingHandler also happens to match other broadly-scoped filters declared
        // in this file (ForNamespaceOf/ForAssemblyOf apply project-wide) — assert relative
        // ordering of the four onion markers rather than an exact sequence, so those
        // unrelated matches don't break this.
        var order = probe.Order.ToList();
        var middlewareBeforeIndex = order.IndexOf("MiddlewareBefore");
        var inheritedBeforeIndex = order.IndexOf("InheritedBefore");
        var inheritedAfterIndex = order.IndexOf("InheritedAfter");
        var middlewareAfterIndex = order.IndexOf("MiddlewareAfter");

        Assert.That(middlewareBeforeIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(inheritedBeforeIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(inheritedAfterIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(middlewareAfterIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(middlewareBeforeIndex, Is.LessThan(inheritedBeforeIndex), "middleware Before should run before the inherited Before phase");
        Assert.That(inheritedAfterIndex, Is.LessThan(middlewareAfterIndex), "middleware After should run after the inherited After phase");
    }

    [Test]
    public async Task ForInterface_AppliesOnlyToImplementingHandler()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();
        var probe = scope.ServiceProvider.GetRequiredService<IMiddlewareOrderProbe>();

        await bus.Handle(new AdminOnlyHandler.Request(1));
        await bus.Handle(new NonAdminHandler.Request(1));

        Assert.That(probe.Order.Count(step => step == "AdminCheck"), Is.EqualTo(1));
    }

    [Test]
    public async Task ForReturnType_AppliesToHandlerWithAssignableResponse()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();
        var probe = scope.ServiceProvider.GetRequiredService<IMiddlewareOrderProbe>();

        await bus.Handle(new TaggedResponseHandler.Request(1));

        Assert.That(probe.Order, Does.Contain("ResponseTagCheck"));
    }

    [Test]
    public async Task ForRequestType_AppliesToHandlerWithAssignableRequest()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();
        var probe = scope.ServiceProvider.GetRequiredService<IMiddlewareOrderProbe>();

        await bus.Handle(new TaggedRequestHandler.Request(1));

        Assert.That(probe.Order, Does.Contain("RequestTagCheck"));
    }

    [Test]
    public async Task ForVariable_AppliesToHandlerWithAssignableLocalVariable()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();
        var probe = scope.ServiceProvider.GetRequiredService<IMiddlewareOrderProbe>();

        await bus.Handle(new LoadingEntityHandler.Request(1));

        Assert.That(probe.Order, Does.Contain("EntityTagCheck"));
    }

    [Test]
    public async Task ForNamespaceOfAndForAssemblyOf_ApplyToHandlerInSameNamespaceAndAssembly()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();
        var probe = scope.ServiceProvider.GetRequiredService<IMiddlewareOrderProbe>();

        await bus.Handle(new NamespaceAndAssemblyHandler.Request(1));

        Assert.That(probe.Order, Does.Contain("NamespaceCheck"));
        Assert.That(probe.Order, Does.Contain("AssemblyCheck"));
    }

    [Test]
    public async Task ForAttribute_AppliesToDecoratedHandler()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();
        var probe = scope.ServiceProvider.GetRequiredService<IMiddlewareOrderProbe>();

        await bus.Handle(new AttributeTaggedHandler.Request(1));

        Assert.That(probe.Order, Does.Contain("AttributeCheck"));
    }

    [Test]
    public async Task ForHandler_AppliesOnlyToNamedHandler()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();
        var probe = scope.ServiceProvider.GetRequiredService<IMiddlewareOrderProbe>();

        await bus.Handle(new SpecificallyTargetedHandler.Request(1));
        await bus.Handle(new NotSpecificallyTargetedHandler.Request(1));

        Assert.That(probe.Order.Count(step => step == "SpecificCheck"), Is.EqualTo(1));
    }

    [Test]
    public async Task HasValidation_AppliesOnlyToHandlerWithValidatePhase()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();
        var probe = scope.ServiceProvider.GetRequiredService<IMiddlewareOrderProbe>();

        await bus.Handle(new HasValidationHandler.Request(1));
        await bus.Handle(new HasNoValidationHandler.Request(1));

        Assert.That(probe.Order.Count(step => step == "ValidationAwareCheck"), Is.EqualTo(1));
    }

    [Test]
    public async Task HasNotFound_AppliesToHandlerWithNullableLoad()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();
        var probe = scope.ServiceProvider.GetRequiredService<IMiddlewareOrderProbe>();

        await bus.Handle(new HasNullableLoadHandler.Request(1));

        Assert.That(probe.Order, Does.Contain("NotFoundAwareCheck"));
    }

    [Test]
    public async Task Middleware_ResolvesScopedDependency_FromTheSameAmbientScope()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();
        var probe = scope.ServiceProvider.GetRequiredService<IMiddlewareOrderProbe>();
        var expectedScopeId = scope.ServiceProvider.GetRequiredService<IScopeProbe>().ScopeId;

        var result = await bus.Handle(new ScopeCheckHandler.Request(1));

        Assert.That(result, Is.EqualTo(new ScopeCheckHandler.Response(expectedScopeId)));
        Assert.That(probe.Order, Does.Contain($"MiddlewareScope:{expectedScopeId}"));
    }
}
