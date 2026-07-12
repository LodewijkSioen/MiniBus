using Microsoft.CodeAnalysis;

namespace Caravelle.Generator.Middleware;

/// <summary>Matches handlers whose pipeline can produce an IValidationResult. See <see cref="global::Caravelle.HasValidation"/>.</summary>
internal sealed class HasValidationFilter : IMiddlewareFilterDefinition
{
    public string MetadataName => "HasValidation";
    public string Kind => "HasValidation";

    public MiddlewareFilterDescriptor Parse(INamedTypeSymbol filterType, SymbolDisplayFormat fmt) => new(Kind, null);

    public bool Matches(MiddlewareFilterDescriptor descriptor, MiddlewareMatchContext context) => context.HasValidation;
}
