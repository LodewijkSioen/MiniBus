using Microsoft.CodeAnalysis;

namespace Caravelle.Generator.Middleware;

/// <summary>
/// Matches handlers whose pipeline has a local variable of type T, or a type assignable to
/// it, flowing between pre-handle/handle/post-handle methods. See
/// <see cref="global::Caravelle.ForVariable{T}"/>.
/// </summary>
internal sealed class ForVariableFilter : IMiddlewareFilterDefinition
{
    public string MetadataName => "ForVariable`1";
    public string Kind => "ForVariable";

    public MiddlewareFilterDescriptor Parse(INamedTypeSymbol filterType, SymbolDisplayFormat fmt) =>
        new(Kind, filterType.TypeArguments[0].ToDisplayString(fmt));

    public bool Matches(MiddlewareFilterDescriptor descriptor, MiddlewareMatchContext context) =>
        descriptor.TargetType is not null && context.VariableTypeClosure.Contains(descriptor.TargetType);
}
