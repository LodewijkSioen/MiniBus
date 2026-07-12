using Microsoft.CodeAnalysis;

namespace Caravelle.Generator.Middleware;

/// <summary>
/// Matches handlers whose result can be T (or a type assignable to it), including the
/// success response type and any validation/not-found payload types.
/// See <see cref="global::Caravelle.ForReturnType{T}"/>.
/// </summary>
internal sealed class ForReturnTypeFilter : IMiddlewareFilterDefinition
{
    public string MetadataName => "ForReturnType`1";
    public string Kind => "ForReturnType";

    public MiddlewareFilterDescriptor Parse(INamedTypeSymbol filterType, SymbolDisplayFormat fmt) =>
        new(Kind, filterType.TypeArguments[0].ToDisplayString(fmt));

    public bool Matches(MiddlewareFilterDescriptor descriptor, MiddlewareMatchContext context) =>
        descriptor.TargetType is not null && context.ReturnTypeClosure.Contains(descriptor.TargetType);
}
