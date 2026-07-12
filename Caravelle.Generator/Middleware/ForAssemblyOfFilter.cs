using Microsoft.CodeAnalysis;

namespace Caravelle.Generator.Middleware;

/// <summary>
/// Matches handlers declared in the same assembly as T. See
/// <see cref="global::Caravelle.ForAssemblyOf{T}"/>.
/// </summary>
internal sealed class ForAssemblyOfFilter : IMiddlewareFilterDefinition
{
    public string MetadataName => "ForAssemblyOf`1";
    public string Kind => "ForAssemblyOf";

    // ForAssemblyOf<T> matches on T's containing assembly, not T's own type name, so the
    // target captured here is that projection instead of T's display string.
    public MiddlewareFilterDescriptor Parse(INamedTypeSymbol filterType, SymbolDisplayFormat fmt)
    {
        var type = filterType.TypeArguments[0];
        var name = type.ContainingAssembly?.Identity.Name ?? string.Empty;
        return new(Kind, name);
    }

    public bool Matches(MiddlewareFilterDescriptor descriptor, MiddlewareMatchContext context)
    {
        var actual = context.ClassSymbol.ContainingAssembly?.Identity.Name ?? string.Empty;
        return actual == (descriptor.TargetType ?? string.Empty);
    }
}
