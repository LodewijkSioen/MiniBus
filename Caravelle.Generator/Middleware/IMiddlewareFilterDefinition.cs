using Microsoft.CodeAnalysis;

namespace Caravelle.Generator.Middleware;

/// <summary>
/// A single middleware filter shape (e.g. <c>ForInterface&lt;T&gt;</c>), fully self-contained:
/// it recognizes its own type-argument shape, parses itself into an equatable
/// <see cref="MiddlewareFilterDescriptor"/>, and evaluates that descriptor against a
/// handler's <see cref="MiddlewareMatchContext"/>. Adding a new filter kind only requires
/// implementing this interface and registering the instance in
/// <see cref="MiddlewareFilterRegistry.All"/> — no other file needs to change.
/// </summary>
internal interface IMiddlewareFilterDefinition
{
    /// <summary>The Roslyn metadata name of the recognized marker type, e.g. "ForInterface`1" or "AllHandlers".</summary>
    string MetadataName { get; }

    /// <summary>Stable string discriminator stored in <see cref="MiddlewareFilterDescriptor.Kind"/>.</summary>
    string Kind { get; }

    /// <summary>Parses the type argument(s) of the recognized filter marker into a descriptor.</summary>
    MiddlewareFilterDescriptor Parse(INamedTypeSymbol filterType, SymbolDisplayFormat fmt);

    /// <summary>Evaluates whether a parsed descriptor of this kind matches the given handler context.</summary>
    bool Matches(MiddlewareFilterDescriptor descriptor, MiddlewareMatchContext context);
}
