using Caravelle.Generator.Handler;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace Caravelle.Generator.Middleware;

/// <summary>
/// A snapshot of a handler's currently-known pipeline shape, used to evaluate
/// <see cref="MiddlewareFilterDescriptor"/>s against it. Rebuilt fresh on each pass of
/// the fixed-point middleware matching loop in <see cref="HandlerModelFactory"/> as
/// the merged phase set grows — never cached or persisted.
/// </summary>
internal sealed record MiddlewareMatchContext(
    INamedTypeSymbol ClassSymbol,
    SymbolDisplayFormat Format,
    HashSet<string> ReturnTypeClosure,
    HashSet<string> RequestTypeClosure,
    HashSet<string> VariableTypeClosure,
    bool HasValidation,
    bool HasNotFound);

internal static class MiddlewareFilterMatcher
{
    public static bool Matches(MiddlewareFilterDescriptor filter, MiddlewareMatchContext context)
    {
        var definition = MiddlewareFilterRegistry.GetByKind(filter.Kind);
        return definition is not null && definition.Matches(filter, context);
    }
}
