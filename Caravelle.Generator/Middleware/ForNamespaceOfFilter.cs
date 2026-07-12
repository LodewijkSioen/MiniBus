using Microsoft.CodeAnalysis;

namespace Caravelle.Generator.Middleware;

/// <summary>
/// Matches handlers declared in the same namespace as T. See
/// <see cref="global::Caravelle.ForNamespaceOf{T}"/>.
/// </summary>
internal sealed class ForNamespaceOfFilter : IMiddlewareFilterDefinition
{
    public string MetadataName => "ForNamespaceOf`1";
    public string Kind => "ForNamespaceOf";

    // ForNamespaceOf<T> matches on T's namespace, not T's own type name, so the target
    // captured here is that projection instead of T's display string.
    public MiddlewareFilterDescriptor Parse(INamedTypeSymbol filterType, SymbolDisplayFormat fmt)
    {
        var type = filterType.TypeArguments[0];
        var name = type.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString(fmt)
            : string.Empty;
        return new(Kind, name);
    }

    public bool Matches(MiddlewareFilterDescriptor descriptor, MiddlewareMatchContext context)
    {
        var ns = context.ClassSymbol.ContainingNamespace;
        var actual = ns is { IsGlobalNamespace: false } ? ns.ToDisplayString(context.Format) : string.Empty;
        return actual == (descriptor.TargetType ?? string.Empty);
    }
}
