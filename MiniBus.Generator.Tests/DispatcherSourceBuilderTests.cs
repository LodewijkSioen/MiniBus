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
        LoadInfo? load = null,
        ValidateInfo? validate = null,
        string handleCallArgs = "request",
        string validateCallArgs = "")
    {
        var prefix = ns is not null ? $"global::{ns}." : "global::";
        return new HandlerModel(
            Namespace: ns,
            ClassName: className,
            FullClassName: $"{prefix}{className}",
            FullRequestType: $"{prefix}{className}.Request",
            FullResponseType: $"{prefix}{className}.Response",
            Load: load,
            HandleCallArgs: handleCallArgs,
            HandleIsAsync: handleIsAsync,
            Validate: validate,
            ValidateCallArgs: validateCallArgs,
            UnsupportedHandleParameters: ImmutableArray<string>.Empty,
            UnsupportedValidateParameters: ImmutableArray<string>.Empty,
            IsGenericHandler: false,
            IsNestedHandler: false,
            Location: Location.None);
    }

    private static LoadInfo ScalarLoad(bool isAsync, bool isNullable) =>
        new LoadInfo(IsAsync: isAsync, IsTuple: false,
            Elements: ImmutableArray.Create(
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
            Model(load: ScalarLoad(true, true), handleCallArgs: "loaded")));

    [Test]
    public Task SyncNullableScalarLoad_AsyncHandle() =>
        Verify(DispatcherSourceBuilder.Build(
            Model(load: ScalarLoad(false, true), handleCallArgs: "loaded")));

    [Test]
    public Task NonNullableLoad_DoesNotGenerateNullCheck() =>
        Verify(DispatcherSourceBuilder.Build(
            Model(load: ScalarLoad(false, false), handleCallArgs: "loaded")));

    // ── Validate ──────────────────────────────────────────────────────────

    [Test]
    public Task SyncLoad_SyncValidate_RequestArg() =>
        Verify(DispatcherSourceBuilder.Build(Model(
            load: ScalarLoad(false, true),
            validate: new ValidateInfo(IsAsync: false),
            handleCallArgs: "loaded",
            validateCallArgs: "request")));

    [Test]
    public Task SyncLoad_SyncValidate_LoadedArg() =>
        Verify(DispatcherSourceBuilder.Build(Model(
            load: ScalarLoad(false, true),
            validate: new ValidateInfo(IsAsync: false),
            handleCallArgs: "loaded",
            validateCallArgs: "loaded")));

    [Test]
    public Task SyncLoad_SyncValidate_BothArgs() =>
        Verify(DispatcherSourceBuilder.Build(Model(
            load: ScalarLoad(false, true),
            validate: new ValidateInfo(IsAsync: false),
            handleCallArgs: "request, loaded",
            validateCallArgs: "request, loaded")));

    [Test]
    public Task AsyncLoad_AsyncValidate_AsyncHandle() =>
        Verify(DispatcherSourceBuilder.Build(Model(
            load: ScalarLoad(true, true),
            validate: new ValidateInfo(IsAsync: true),
            handleCallArgs: "loaded",
            validateCallArgs: "loaded")));

    // ── Tuple load ────────────────────────────────────────────────────────

    [Test]
    public Task TupleLoad_AllNullableNamedElements() =>
        Verify(DispatcherSourceBuilder.Build(Model(
            load: new LoadInfo(IsAsync: false, IsTuple: true,
                Elements: ImmutableArray.Create(
                    new LoadedElement("entity", "global::TestApp.MyHandler.Entity", IsNullable: true),
                    new LoadedElement("config", "global::TestApp.MyHandler.Config", IsNullable: true))),
            handleCallArgs: "entity, config")));

    [Test]
    public Task TupleLoad_PartialNullableUnnamedElements() =>
        Verify(DispatcherSourceBuilder.Build(Model(
            load: new LoadInfo(IsAsync: false, IsTuple: true,
                Elements: ImmutableArray.Create(
                    new LoadedElement("item1", "global::TestApp.MyHandler.Entity", IsNullable: true),
                    new LoadedElement("item2", "global::TestApp.MyHandler.Config", IsNullable: false))),
            handleCallArgs: "item1, item2")));

    [Test]
    public Task TupleLoad_NoNullableElements_NoNullCheck() =>
        Verify(DispatcherSourceBuilder.Build(Model(
            load: new LoadInfo(IsAsync: false, IsTuple: true,
                Elements: ImmutableArray.Create(
                    new LoadedElement("entity", "global::TestApp.MyHandler.Entity", IsNullable: false),
                    new LoadedElement("config", "global::TestApp.MyHandler.Config", IsNullable: false))),
            handleCallArgs: "entity, config")));

    // ── Namespace ─────────────────────────────────────────────────────────

    [Test]
    public Task NoNamespace_GlobalNamespace() =>
        Verify(DispatcherSourceBuilder.Build(Model(ns: null)));
}
