using System.Linq;
using Microsoft.CodeAnalysis;

namespace Caravelle.Generator.Middleware;

/// <summary>
/// Matches handlers decorated with TAttribute. See
/// <see cref="global::Caravelle.ForAttribute{TAttribute}"/>.
/// </summary>
internal sealed class ForAttributeFilter : IMiddlewareFilterDefinition
{
    public string MetadataName => "ForAttribute`1";
    public string Kind => "ForAttribute";

    public MiddlewareFilterDescriptor Parse(INamedTypeSymbol filterType, SymbolDisplayFormat fmt) =>
        new(Kind, filterType.TypeArguments[0].ToDisplayString(fmt));

    public bool Matches(MiddlewareFilterDescriptor descriptor, MiddlewareMatchContext context)
    {
        if (descriptor.TargetType is null)
        {
            return false;
        }

        return context.ClassSymbol.GetAttributes().Any(attribute =>
        {
            var attributeType = attribute.AttributeClass;
            while (attributeType is not null)
            {
                if (attributeType.ToDisplayString(context.Format) == descriptor.TargetType)
                {
                    return true;
                }
                attributeType = attributeType.BaseType;
            }
            return false;
        });
    }
}
