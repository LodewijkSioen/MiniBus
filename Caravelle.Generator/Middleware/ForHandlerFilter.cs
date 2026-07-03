using Microsoft.CodeAnalysis;

namespace Caravelle.Generator.Middleware;

/// <summary>Matches only the specific handler THandler. See <see cref="global::Caravelle.ForHandler{THandler}"/>.</summary>
internal sealed class ForHandlerFilter : IMiddlewareFilterDefinition
{
    public string MetadataName => "ForHandler`1";
    public string Kind => "ForHandler";

    public MiddlewareFilterDescriptor Parse(INamedTypeSymbol filterType, SymbolDisplayFormat fmt) =>
        new(Kind, filterType.TypeArguments[0].ToDisplayString(fmt));

    public bool Matches(MiddlewareFilterDescriptor descriptor, MiddlewareMatchContext context) =>
        descriptor.TargetType is not null && context.ClassSymbol.ToDisplayString(context.Format) == descriptor.TargetType;
}
