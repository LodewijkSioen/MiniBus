using Microsoft.CodeAnalysis;

namespace Caravelle.Generator.Middleware;

/// <summary>Matches handlers whose pipeline can short-circuit with a NotFoundResult. See <see cref="global::Caravelle.HasNotFound"/>.</summary>
internal sealed class HasNotFoundFilter : IMiddlewareFilterDefinition
{
    public string MetadataName => "HasNotFound";
    public string Kind => "HasNotFound";

    public MiddlewareFilterDescriptor Parse(INamedTypeSymbol filterType, SymbolDisplayFormat fmt) => new(Kind, null);

    public bool Matches(MiddlewareFilterDescriptor descriptor, MiddlewareMatchContext context) => context.HasNotFound;
}
