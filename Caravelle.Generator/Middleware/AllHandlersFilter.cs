using Microsoft.CodeAnalysis;

namespace Caravelle.Generator.Middleware;

/// <summary>Matches every handler in the compilation. See <see cref="global::Caravelle.AllHandlers"/>.</summary>
internal sealed class AllHandlersFilter : IMiddlewareFilterDefinition
{
    public string MetadataName => "AllHandlers";
    public string Kind => "AllHandlers";

    public MiddlewareFilterDescriptor Parse(INamedTypeSymbol filterType, SymbolDisplayFormat fmt) => new(Kind, null);

    public bool Matches(MiddlewareFilterDescriptor descriptor, MiddlewareMatchContext context) => true;
}
