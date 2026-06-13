using Microsoft.Extensions.DependencyInjection;

namespace MiniBus.Tests;

[Handler]
public class FinallyPipelineHandler
{
    public record Request(string Mode);
    public record Entity(string Mode);
    public record Response(string Message);

    public Entity? Load(Request request)
        => request.Mode == "notfound" ? null : new Entity(request.Mode);

    public global::MiniBus.ValidationResult Validate(Entity entity)
    {
        var validation = new global::MiniBus.ValidationResult();
        if (entity.Mode == "invalid")
            validation.Add(new global::MiniBus.ValidationError("invalid mode", "INVALID_MODE"));
        return validation;
    }

    public Response Handle(Entity entity)
    {
        if (entity.Mode == "throw")
            throw new InvalidOperationException("boom");

        return new Response("ok");
    }

    public void Finally(
        Request request,
        Entity? entity,
        global::MiniBus.ValidationResult? validation,
        Response? response,
        IFinallyExecutionProbe probe)
    {
        probe.Record(new FinallyObservation(
            Mode: request.Mode,
            RequestSeen: true,
            EntitySeen: entity is not null,
            ValidationSeen: validation is not null,
            ValidationWasValid: validation?.IsValid() ?? false,
            ResponseSeen: response is not null));
    }
}

[TestFixture]
public class FinallyMethodIntegrationTests
{
    [Test]
    public async Task Finally_Runs_WhenDispatchSucceeds()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();
        var probe = scope.ServiceProvider.GetRequiredService<IFinallyExecutionProbe>();

        var result = await bus.Handle(new FinallyPipelineHandler.Request("ok"));

        Assert.That(result.Value, Is.TypeOf<FinallyPipelineHandler.Response>());
        Assert.That(probe.Observations, Has.Count.EqualTo(1));

        var call = probe.Observations[0];
        Assert.That(call.Mode, Is.EqualTo("ok"));
        Assert.That(call.RequestSeen, Is.True);
        Assert.That(call.EntitySeen, Is.True);
        Assert.That(call.ValidationSeen, Is.True);
        Assert.That(call.ValidationWasValid, Is.True);
        Assert.That(call.ResponseSeen, Is.True);
    }

    [Test]
    public async Task Finally_Runs_WhenDispatchReturnsNotFound()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();
        var probe = scope.ServiceProvider.GetRequiredService<IFinallyExecutionProbe>();

        var result = await bus.Handle(new FinallyPipelineHandler.Request("notfound"));

        Assert.That(result.Value, Is.TypeOf<global::MiniBus.NotFoundResult>());
        Assert.That(probe.Observations, Has.Count.EqualTo(1));

        var call = probe.Observations[0];
        Assert.That(call.Mode, Is.EqualTo("notfound"));
        Assert.That(call.RequestSeen, Is.True);
        Assert.That(call.EntitySeen, Is.False);
        Assert.That(call.ValidationSeen, Is.False);
        Assert.That(call.ResponseSeen, Is.False);
    }

    [Test]
    public async Task Finally_Runs_WhenDispatchReturnsInvalid()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();
        var probe = scope.ServiceProvider.GetRequiredService<IFinallyExecutionProbe>();

        var result = await bus.Handle(new FinallyPipelineHandler.Request("invalid"));

        Assert.That(result.Value, Is.TypeOf<global::MiniBus.ValidationResult>());
        Assert.That(probe.Observations, Has.Count.EqualTo(1));

        var call = probe.Observations[0];
        Assert.That(call.Mode, Is.EqualTo("invalid"));
        Assert.That(call.RequestSeen, Is.True);
        Assert.That(call.EntitySeen, Is.True);
        Assert.That(call.ValidationSeen, Is.True);
        Assert.That(call.ValidationWasValid, Is.False);
        Assert.That(call.ResponseSeen, Is.False);
    }

    [Test]
    public void Finally_Runs_WhenDispatchThrows()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();
        var probe = scope.ServiceProvider.GetRequiredService<IFinallyExecutionProbe>();

        Assert.ThrowsAsync<InvalidOperationException>(
            () => bus.Handle(new FinallyPipelineHandler.Request("throw")));

        Assert.That(probe.Observations, Has.Count.EqualTo(1));

        var call = probe.Observations[0];
        Assert.That(call.Mode, Is.EqualTo("throw"));
        Assert.That(call.RequestSeen, Is.True);
        Assert.That(call.EntitySeen, Is.True);
        Assert.That(call.ValidationSeen, Is.True);
        Assert.That(call.ValidationWasValid, Is.True);
        Assert.That(call.ResponseSeen, Is.False);
    }
}
