using Microsoft.Extensions.DependencyInjection;

namespace MiniBus.Tests;

[Handler]
public class StaticDiHandler
{
    public record Request;
    public record Response(Guid ScopeId);

    public static Response Handle(Request request, IScopeProbe scopeProbe)
        => new(scopeProbe.ScopeId);
}

[Handler]
public class MixedStaticInstanceDiHandler
{
    private readonly IScopeProbe _scopeProbe;

    public MixedStaticInstanceDiHandler(IScopeProbe scopeProbe)
    {
        _scopeProbe = scopeProbe;
    }

    public record Request(Guid ExpectedScopeId);
    public record Response(Guid ScopeId);

    public static global::MiniBus.ValidationResult Validate(Request request, IScopeProbe scopeProbe)
    {
        var errors = new global::MiniBus.ValidationResult();
        if (request.ExpectedScopeId != scopeProbe.ScopeId)
            errors.Add(new global::MiniBus.ValidationError("Wrong scope", "SCOPE_MISMATCH"));
        return errors;
    }

    public Response Handle(Request request)
        => new(_scopeProbe.ScopeId);
}

[TestFixture]
public class FunctionInjectionIntegrationTests
{
    [Test]
    public async Task StaticHandler_ResolvesScopedServiceFromCurrentScope()
    {
        Guid firstScopeId;
        Guid secondScopeId;

        using (var firstScope = AppUnderTest.Services.CreateScope())
        {
            var bus = firstScope.ServiceProvider.GetRequiredService<MiniBus>();
            var firstResult = await bus.Handle(new StaticDiHandler.Request());
            var secondResult = await bus.Handle(new StaticDiHandler.Request());

            Assert.That(firstResult.IsSuccess, Is.True);
            Assert.That(secondResult.IsSuccess, Is.True);
            Assert.That(firstResult.Response!.ScopeId, Is.EqualTo(secondResult.Response!.ScopeId));

            firstScopeId = firstResult.Response.ScopeId;
        }

        using (var secondScope = AppUnderTest.Services.CreateScope())
        {
            var bus = secondScope.ServiceProvider.GetRequiredService<MiniBus>();
            var result = await bus.Handle(new StaticDiHandler.Request());

            Assert.That(result.IsSuccess, Is.True);
            secondScopeId = result.Response!.ScopeId;
        }

        Assert.That(secondScopeId, Is.Not.EqualTo(firstScopeId));
    }

    [Test]
    public async Task MixedStaticAndInstanceHandler_UsesSameScopedServiceAcrossPhases()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();
        var expectedScopeId = scope.ServiceProvider.GetRequiredService<IScopeProbe>().ScopeId;

        var result = await bus.Handle(new MixedStaticInstanceDiHandler.Request(expectedScopeId));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Response!.ScopeId, Is.EqualTo(expectedScopeId));
    }
}
