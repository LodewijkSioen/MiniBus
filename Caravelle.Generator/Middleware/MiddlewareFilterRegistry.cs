using System;
using System.Collections.Generic;
using System.Linq;

namespace Caravelle.Generator.Middleware;

/// <summary>
/// The explicit set of recognized middleware filter shapes. Adding a new filter kind requires
/// only implementing <see cref="IMiddlewareFilterDefinition"/> and adding the instance to
/// <see cref="All"/> below — no other file needs to change.
/// </summary>
internal static class MiddlewareFilterRegistry
{
    /// <summary>Kind used for a <c>TFilter</c> type argument that isn't a recognized filter shape.</summary>
    public const string UnrecognizedKind = "Unrecognized";

    public static readonly IReadOnlyList<IMiddlewareFilterDefinition> All =
    [
        new AllHandlersFilter(),
        new ForInterfaceFilter(),
        new ForReturnTypeFilter(),
        new ForRequestTypeFilter(),
        new ForVariableFilter(),
        new ForNamespaceOfFilter(),
        new ForAssemblyOfFilter(),
        new ForAttributeFilter(),
        new ForHandlerFilter(),
        new HasValidationFilter(),
        new HasNotFoundFilter()
    ];

    private static readonly Dictionary<string, IMiddlewareFilterDefinition> ByMetadataName =
        All.ToDictionary(static f => f.MetadataName, StringComparer.Ordinal);

    private static readonly Dictionary<string, IMiddlewareFilterDefinition> ByKind =
        All.ToDictionary(static f => f.Kind, StringComparer.Ordinal);

    public static bool TryGetByMetadataName(string metadataName, out IMiddlewareFilterDefinition? definition) =>
        ByMetadataName.TryGetValue(metadataName, out definition);

    public static IMiddlewareFilterDefinition? GetByKind(string kind) =>
        ByKind.TryGetValue(kind, out var definition) ? definition : null;
}
