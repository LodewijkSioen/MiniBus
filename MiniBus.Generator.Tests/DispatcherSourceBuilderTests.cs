using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace MiniBus.Generator.Tests;

[TestFixture]
public class DispatcherSourceBuilderTests
{
    // ── Helpers ────────────────────────────────────────────────────────────

    private static HandlerModel Model(
        string? ns = "TestApp",
        string className = "MyHandler",
        bool handleIsAsync = true,
        LoadMethodPhase? load = null,
        (bool IsAsync, int Order)? validate = null,
        string handleCallArgs = "request",
        string validateCallArgs = "")
    {
        var prefix = ns is not null ? $"global::{ns}." : "global::";
        var handle = new HandleMethodPhase(handleIsAsync, $"{prefix}{className}.Response", handleCallArgs, ImmutableArray<string>.Empty);
        ValidateMethodPhase? validateInfo = validate is null
            ? null
            : new ValidateMethodPhase(validate.Value.IsAsync, validate.Value.Order, validateCallArgs, ImmutableArray<string>.Empty);
        return new HandlerModel(
            Namespace: ns,
            ClassName: className,
            FullClassName: $"{prefix}{className}",
            FullRequestType: $"{prefix}{className}.Request",
            FullResponseType: $"{prefix}{className}.Response",
                Phases: HandlerPhases.From(handle, load, validateInfo),
            IsGenericHandler: false,
            IsNestedHandler: false,
            Location: Location.None);
    }

    private static LoadMethodPhase ScalarLoad(bool isAsync, bool isNullable) =>
        new LoadMethodPhase(isAsync: isAsync, isTuple: false, order: 0,
            elements: ImmutableArray.Create(
                new LoadedElement("loaded", "global::TestApp.MyHandler.Entity", isNullable)));

    // ── Handle async / sync ───────────────────────────────────────────────

    [Test]
    public Task SyncHandle_NoLoadNoValidate_WithNamespace() =>
        Verify(DispatcherSourceBuilder.Build(Model(handleIsAsync: false)));

    [Test]
    public Task AsyncHandle_NoLoadNoValidate_WithNamespace() =>
        Verify(DispatcherSourceBuilder.Build(Model()));

    // ── Scalar load ───────────────────────────────────────────────────────

    [Test]
    public Task AsyncNullableScalarLoad_AsyncHandle() =>
        Verify(DispatcherSourceBuilder.Build(
            Model(load: ScalarLoad(true, true), handleCallArgs: "loadedValue")));

    [Test]
    public Task SyncNullableScalarLoad_AsyncHandle() =>
        Verify(DispatcherSourceBuilder.Build(
            Model(load: ScalarLoad(false, true), handleCallArgs: "loadedValue")));

    [Test]
    public Task NonNullableLoad_DoesNotGenerateNullCheck() =>
        Verify(DispatcherSourceBuilder.Build(
            Model(load: ScalarLoad(false, false), handleCallArgs: "loaded")));

    // ── Validate ──────────────────────────────────────────────────────────

    [Test]
    public Task SyncLoad_SyncValidate_RequestArg()
    {
        var load = ScalarLoad(false, true);
        load.Order = 1;

        return Verify(DispatcherSourceBuilder.Build(Model(
            load: load,
            validate: (IsAsync: false, Order: 0),
            handleCallArgs: "loadedValue",
            validateCallArgs: "request")));
    }

    [Test]
    public Task SyncLoad_SyncValidate_LoadedArg() =>
        Verify(DispatcherSourceBuilder.Build(Model(
            load: ScalarLoad(false, true),
            validate: (IsAsync: false, Order: 1),
            handleCallArgs: "loadedValue",
            validateCallArgs: "loadedValue")));

    [Test]
    public Task SyncLoad_SyncValidate_BothArgs() =>
        Verify(DispatcherSourceBuilder.Build(Model(
            load: ScalarLoad(false, true),
            validate: (IsAsync: false, Order: 1),
            handleCallArgs: "request, loadedValue",
            validateCallArgs: "request, loadedValue")));

    [Test]
    public Task AsyncLoad_AsyncValidate_AsyncHandle() =>
        Verify(DispatcherSourceBuilder.Build(Model(
            load: ScalarLoad(true, true),
            validate: (IsAsync: true, Order: 1),
            handleCallArgs: "loadedValue",
            validateCallArgs: "loadedValue")));

    // ── Tuple load ────────────────────────────────────────────────────────

    [Test]
    public Task TupleLoad_AllNullableNamedElements() =>
        Verify(DispatcherSourceBuilder.Build(Model(
            load: new LoadMethodPhase(isAsync: false, isTuple: true,
                order: 0,
                elements: ImmutableArray.Create(
                    new LoadedElement("entity", "global::TestApp.MyHandler.Entity", IsNullable: true),
                    new LoadedElement("config", "global::TestApp.MyHandler.Config", IsNullable: true))),
            handleCallArgs: "entityValue, configValue")));

    [Test]
    public Task TupleLoad_PartialNullableUnnamedElements() =>
        Verify(DispatcherSourceBuilder.Build(Model(
            load: new LoadMethodPhase(isAsync: false, isTuple: true,
                order: 0,
                elements: ImmutableArray.Create(
                    new LoadedElement("item1", "global::TestApp.MyHandler.Entity", IsNullable: true),
                    new LoadedElement("item2", "global::TestApp.MyHandler.Config", IsNullable: false))),
            handleCallArgs: "item1Value, item2")));

    [Test]
    public Task TupleLoad_NoNullableElements_NoNullCheck() =>
        Verify(DispatcherSourceBuilder.Build(Model(
            load: new LoadMethodPhase(isAsync: false, isTuple: true,
                order: 0,
                elements: ImmutableArray.Create(
                    new LoadedElement("entity", "global::TestApp.MyHandler.Entity", IsNullable: false),
                    new LoadedElement("config", "global::TestApp.MyHandler.Config", IsNullable: false))),
            handleCallArgs: "entity, config")));

    // ── NotFound message ──────────────────────────────────────────────────

    [Test]
    public Task ScalarLoad_WithNotFoundMessage() =>
        Verify(DispatcherSourceBuilder.Build(
            Model(load: new LoadMethodPhase(isAsync: false, isTuple: false,
                order: 0,
                elements: ImmutableArray.Create(
                    new LoadedElement("loaded", "global::TestApp.MyHandler.Entity", IsNullable: true, NotFoundMessage: "Entity not found"))),
            handleCallArgs: "loadedValue")));

    [Test]
    public Task TupleLoad_PerElementNotFoundMessages() =>
        Verify(DispatcherSourceBuilder.Build(Model(
            load: new LoadMethodPhase(isAsync: false, isTuple: true,
                order: 0,
                elements: ImmutableArray.Create(
                    new LoadedElement("entity", "global::TestApp.MyHandler.Entity", IsNullable: true, NotFoundMessage: "Entity not found"),
                    new LoadedElement("config", "global::TestApp.MyHandler.Config", IsNullable: true, NotFoundMessage: "Config not found"))),
            handleCallArgs: "entityValue, configValue")));

    // ── Namespace ─────────────────────────────────────────────────────────

    [Test]
    public Task NoNamespace_GlobalNamespace() =>
        Verify(DispatcherSourceBuilder.Build(Model(ns: null)));
}
