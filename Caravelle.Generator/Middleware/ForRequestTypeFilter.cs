using Microsoft.CodeAnalysis;

namespace Caravelle.Generator.Middleware;

/// <summary>
/// Matches handlers whose inferred request type is T, or a type assignable to it.
/// See <see cref="global::Caravelle.ForRequestType{T}"/>.
/// </summary>
internal sealed class ForRequestTypeFilter : IMiddlewareFilterDefinition
{
    public string MetadataName => "ForRequestType`1";
    public string Kind => "ForRequestType";

    public MiddlewareFilterDescriptor Parse(INamedTypeSymbol filterType, SymbolDisplayFormat fmt) =>
        new(Kind, filterType.TypeArguments[0].ToDisplayString(fmt));

    public bool Matches(MiddlewareFilterDescriptor descriptor, MiddlewareMatchContext context) =>
        descriptor.TargetType is not null && context.RequestTypeClosure.Contains(descriptor.TargetType);
}
