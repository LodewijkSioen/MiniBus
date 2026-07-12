using System.Linq;

namespace Caravelle.Generator.Handler;

public sealed record Result(HandlerModel? Model, EquatableArray<DiagnosticInfo> Diagnostics);

public sealed record LocalVariable(string LocalName, string FullType, bool CheckNullability, string? IfNullErrorMessage);

public sealed record ResultValueType(string FullType, bool RequiresNullCheck);


public sealed record HandlerModel(
    string? Namespace,
    string ClassName,
    string FullClassName,
    string FullRequestType,
    string FullResponseType,
    EquatableArray<ResultValueType> ResultValueTypes,
    EquatableArray<MethodPhase> Phases,
    EquatableArray<LocalVariable> LocalVariables,
    EquatableArray<MethodPhase> FinallyPhases,
    EquatableArray<string> MatchedMiddlewareClassNames)
{
    // "global::TestApp.DummyHandler" + "Dispatcher" = "global::TestApp.DummyHandlerDispatcher"
    public string DispatcherFullName => FullClassName + "Dispatcher";
    public string DispatcherKey => $"{FullRequestType}|{FullResponseType}";
    public bool IsAnyAsync => Phases.Any(p => p.IsAsync) || FinallyPhases.Any(p => p.IsAsync);
    // Only own-class/inherited phases (OwnerTypeFullName == null) need the _handler field —
    // middleware-owned phases are called through their own dedicated field instead.
    public bool HasInstanceMethods =>
        Phases.Any(p => p.OwnerTypeFullName is null && !p.IsStatic)
        || FinallyPhases.Any(p => p.OwnerTypeFullName is null && !p.IsStatic);
    public bool HasFromServicesParameters => Phases.Any(p => p.Parameters.Any(ip => ip.IsFromServices)) || FinallyPhases.Any(p => p.Parameters.Any(ip => ip.IsFromServices));
    public bool HasSingleResultType => ResultValueTypes.Count == 1;
    public string ResultTypeName => HasSingleResultType ? ResultValueTypes[0].FullType : DispatcherFullName + ".Result";
}