using System.Linq;
using Microsoft.CodeAnalysis;

namespace Caravelle.Generator.Middleware;

/// <summary>Matches handlers whose class implements T. See <see cref="global::Caravelle.ForInterface{T}"/>.</summary>
internal sealed class ForInterfaceFilter : IMiddlewareFilterDefinition
{
    public string MetadataName => "ForInterface`1";
    public string Kind => "ForInterface";

    public MiddlewareFilterDescriptor Parse(INamedTypeSymbol filterType, SymbolDisplayFormat fmt) =>
        new(Kind, filterType.TypeArguments[0].ToDisplayString(fmt));

    public bool Matches(MiddlewareFilterDescriptor descriptor, MiddlewareMatchContext context) =>
        descriptor.TargetType is not null
            && context.ClassSymbol.AllInterfaces.Any(i => i.ToDisplayString(context.Format) == descriptor.TargetType);
}
