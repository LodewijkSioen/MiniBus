using Microsoft.CodeAnalysis;

namespace MiniBus.Generator.Tests;

[TestFixture]
public class RegistrationsSourceBuilderTests
{
    // ── Helper ────────────────────────────────────────────────────────────

    private static HandlerModel MakeModel(
        string className,
        string requestType,
        string responseType,
        bool hasInstanceMethods = true) =>
        new HandlerModel(
            Namespace: "TestApp",
            ClassName: className,
            FullClassName: $"global::TestApp.{className}",
            FullRequestType: requestType,
            FullResponseType: responseType,
            Phases: new EquatableArray<MethodPhase>(new[]
            {
                new MethodPhase(
                    type: PhaseType.Handle,
                    methodName: "Handle",
                    isAsync: false,
                    isStatic: !hasInstanceMethods,
                    parameters: EquatableArray<InputParameter>.Empty,
                    returns: EquatableArray<ReturnElement>.Empty)
            }),
            Location: Location.None,
            LocalVariables: EquatableArray<LocalVariable>.Empty);

    // ── Tests ─────────────────────────────────────────────────────────────

    [Test]
    public Task SingleModel_NoConflicts() =>
        Verify(RegistrationsSourceBuilder.Build(
            new[] { MakeModel("OrderHandler", "global::TestApp.OrderHandler.Request", "global::TestApp.OrderHandler.Response") },
            new HashSet<string>(),
            new HashSet<string>()));

    [Test]
    public Task MultipleModels_NoConflicts() =>
        Verify(RegistrationsSourceBuilder.Build(
            new[]
            {
                MakeModel("OrderHandler", "global::TestApp.OrderHandler.Request", "global::TestApp.OrderHandler.Response"),
                MakeModel("UserHandler", "global::TestApp.UserHandler.Request", "global::TestApp.UserHandler.Response"),
            },
            new HashSet<string>(),
            new HashSet<string>()));

    [Test]
    public Task SomeConflicting_OmitsExtensionMethodForConflicts() =>
        Verify(RegistrationsSourceBuilder.Build(
            new[]
            {
                MakeModel("HandlerOne",   "global::TestApp.SharedRequest",       "global::TestApp.HandlerOne.Response"),
                MakeModel("HandlerTwo",   "global::TestApp.SharedRequest",       "global::TestApp.HandlerTwo.Response"),
                MakeModel("HandlerThree", "global::TestApp.HandlerThree.Request", "global::TestApp.HandlerThree.Response"),
            },
            new HashSet<string> { "global::TestApp.SharedRequest" },
            new HashSet<string>()));

    [Test]
    public Task AllConflicting_MiniBusExtensionsIsEmpty() =>
        Verify(RegistrationsSourceBuilder.Build(
            new[]
            {
                MakeModel("HandlerOne", "global::TestApp.SharedRequest", "global::TestApp.HandlerOne.Response"),
                MakeModel("HandlerTwo", "global::TestApp.SharedRequest", "global::TestApp.HandlerTwo.Response"),
            },
            new HashSet<string> { "global::TestApp.SharedRequest" },
            new HashSet<string>()));

    [Test]
    public void DuplicateRequestResponsePair_OmitsDispatcherRegistrations()
    {
        var generated = RegistrationsSourceBuilder.Build(
            new[]
            {
                MakeModel("HandlerOne", "global::TestApp.SharedRequest", "global::TestApp.SharedResponse"),
                MakeModel("HandlerTwo", "global::TestApp.SharedRequest", "global::TestApp.SharedResponse"),
                MakeModel("HandlerThree", "global::TestApp.HandlerThree.Request", "global::TestApp.HandlerThree.Response"),
            },
            new HashSet<string>(),
            new HashSet<string> { "global::TestApp.SharedRequest|global::TestApp.SharedResponse" });

        Assert.That(generated.Contains("global::TestApp.HandlerOneDispatcher", StringComparison.Ordinal), Is.False);
        Assert.That(generated.Contains("global::TestApp.HandlerTwoDispatcher", StringComparison.Ordinal), Is.False);
        Assert.That(generated.Contains("global::TestApp.HandlerThreeDispatcher", StringComparison.Ordinal), Is.True);
    }

    [Test]
    public void StaticOnlyHandler_OmitsHandlerRegistration_ButKeepsDispatcherRegistration()
    {
        var generated = RegistrationsSourceBuilder.Build(
            new[]
            {
                MakeModel("StaticOnlyHandler", "global::TestApp.StaticOnlyHandler.Request", "global::TestApp.StaticOnlyHandler.Response", hasInstanceMethods: false),
            },
            new HashSet<string>(),
            new HashSet<string>());

        Assert.That(generated.Contains("services.AddScoped<global::TestApp.StaticOnlyHandler>();", StringComparison.Ordinal), Is.False);
        Assert.That(generated.Contains("global::TestApp.StaticOnlyHandlerDispatcher", StringComparison.Ordinal), Is.True);
    }
}
