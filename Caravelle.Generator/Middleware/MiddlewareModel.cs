using Caravelle.Generator.Handler;

namespace Caravelle.Generator.Middleware;

/// <summary>
/// A parsed, fully-equatable representation of a <c>TFilter</c> type argument passed to
/// <c>MiddlewareAttribute&lt;TFilter&gt;</c>. Leaf filters (e.g. <c>ForInterface&lt;T&gt;</c>)
/// carry their type argument in <see cref="TargetType"/>. <see cref="Kind"/> is the
/// <see cref="IMiddlewareFilterDefinition.Kind"/> of the recognized filter, or
/// <see cref="MiddlewareFilterRegistry.UnrecognizedKind"/> if the filter type wasn't recognized.
/// </summary>
public sealed record MiddlewareFilterDescriptor(
    string Kind,
    string? TargetType);

public sealed record MiddlewareModel(
    string? Namespace,
    string ClassName,
    string FullClassName,
    EquatableArray<MethodPhase> PreHandlePhases,
    EquatableArray<MethodPhase> PostHandlePhases,
    EquatableArray<MethodPhase> FinallyPhases,
    EquatableArray<MiddlewareFilterDescriptor> Filters);

public sealed record MiddlewareResult(MiddlewareModel? Model, EquatableArray<DiagnosticInfo> Diagnostics);
