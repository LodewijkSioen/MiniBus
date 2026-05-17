using Microsoft.CodeAnalysis;
using MiniBus.Generator;

namespace MiniBus.Generator.Tests;

[TestFixture]
public class RegistrationsSourceBuilderTests
{
    // ── Helper ────────────────────────────────────────────────────────────

    private static HandlerModel MakeModel(
        string className,
        string requestType,
        string responseType) =>
        new HandlerModel(
            Namespace: "TestApp",
            ClassName: className,
            FullClassName: $"global::TestApp.{className}",
            FullRequestType: requestType,
            FullResponseType: responseType,
            Load: null,
            HandleCallArgs: "request",
            HandleIsAsync: true,
            Validate: null,
            ValidateCallArgs: "",
            Location: Location.None);

    // ── Tests ─────────────────────────────────────────────────────────────

    [Test]
    public Task SingleModel_NoConflicts() =>
        Verify(RegistrationsSourceBuilder.Build(
            new[] { MakeModel("OrderHandler", "global::TestApp.OrderHandler.Request", "global::TestApp.OrderHandler.Response") },
            new HashSet<string>()));

    [Test]
    public Task MultipleModels_NoConflicts() =>
        Verify(RegistrationsSourceBuilder.Build(
            new[]
            {
                MakeModel("OrderHandler", "global::TestApp.OrderHandler.Request", "global::TestApp.OrderHandler.Response"),
                MakeModel("UserHandler", "global::TestApp.UserHandler.Request", "global::TestApp.UserHandler.Response"),
            },
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
            new HashSet<string> { "global::TestApp.SharedRequest" }));

    [Test]
    public Task AllConflicting_ConventionBusExtensionsIsEmpty() =>
        Verify(RegistrationsSourceBuilder.Build(
            new[]
            {
                MakeModel("HandlerOne", "global::TestApp.SharedRequest", "global::TestApp.HandlerOne.Response"),
                MakeModel("HandlerTwo", "global::TestApp.SharedRequest", "global::TestApp.HandlerTwo.Response"),
            },
            new HashSet<string> { "global::TestApp.SharedRequest" }));
}
