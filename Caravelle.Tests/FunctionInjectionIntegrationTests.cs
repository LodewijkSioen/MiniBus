using Microsoft.Extensions.DependencyInjection;

namespace Caravelle.Tests;

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

    public static global::Caravelle.ValidationResult Validate(Request request, IScopeProbe scopeProbe)
    {
        var errors = new global::Caravelle.ValidationResult();
        if (request.ExpectedScopeId != scopeProbe.ScopeId)
            errors.Add(new global::Caravelle.ValidationError("Wrong scope", "SCOPE_MISMATCH"));
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
            var bus = firstScope.ServiceProvider.GetRequiredService<Bus>();
            var firstResult = await bus.Handle(new StaticDiHandler.Request());
            var secondResult = await bus.Handle(new StaticDiHandler.Request());
            var firstResponse = firstResult;
            var secondResponse = secondResult;

            Assert.That(firstResponse, Is.Not.Null);
            Assert.That(secondResponse, Is.Not.Null);
            Assert.That(firstResponse.ScopeId, Is.EqualTo(secondResponse!.ScopeId));

            firstScopeId = firstResponse.ScopeId;
        }

        using (var secondScope = AppUnderTest.Services.CreateScope())
        {
            var bus = secondScope.ServiceProvider.GetRequiredService<Bus>();
            var result = await bus.Handle(new StaticDiHandler.Request());
            var response = result;

            Assert.That(response, Is.Not.Null);
            secondScopeId = response!.ScopeId;
        }

        Assert.That(secondScopeId, Is.Not.EqualTo(firstScopeId));
    }

    [Test]
    public async Task MixedStaticAndInstanceHandler_UsesSameScopedServiceAcrossPhases()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Bus>();
        var expectedScopeId = scope.ServiceProvider.GetRequiredService<IScopeProbe>().ScopeId;

        var result = await bus.Handle(new MixedStaticInstanceDiHandler.Request(expectedScopeId));
        var response = result.Match(r => r, _ => null!);

        Assert.That(response, Is.Not.Null);
        Assert.That(response.ScopeId, Is.EqualTo(expectedScopeId));
    }
}
