using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Caravelle.Generator;

public sealed record DiagnosticInfo
{
    // Explicit constructor to convert Location into LocationInfo
    public DiagnosticInfo(DiagnosticDescriptor descriptor, Location? location, string[] messageArgs)
    {
        Descriptor = descriptor;
        Location = location is not null ? LocationInfo.CreateFrom(location) : null;
        MessageArgs = new(messageArgs);
    }

    public DiagnosticDescriptor Descriptor { get; }
    public LocationInfo? Location { get; }
    public EquatableArray<string> MessageArgs { get; }

    public Diagnostic ToDiagnostic()
    {
        return Diagnostic.Create(Descriptor, Location?.ToLocation(), messageArgs: MessageArgs.ToArray<object?>());
    }
}

public record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public Location ToLocation()
        => Location.Create(FilePath, TextSpan, LineSpan);

    public static LocationInfo? CreateFrom(SyntaxNode node)
        => CreateFrom(node.GetLocation());

    public static LocationInfo? CreateFrom(Location location)
    {
        if (location.SourceTree is null)
        {
            return null;
        }

        return new(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }
}

internal static class Diagnostics
{
    private static readonly DiagnosticDescriptor DuplicateRequestTypeDescriptor = new(
        id: "MBG001",
        title: "Duplicate request type",
        messageFormat: "Handler '{0}' shares request type '{1}' with another [Handler] class. No typed extension method will be generated for this request type.",
        category: "Caravelle.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateRequestResponsePairDescriptor = new(
        id: "MBG002",
        title: "Duplicate request/response pair",
        messageFormat: "Handler '{0}' shares request/response pair '{1}' -> '{2}' with another [Handler] class. Dispatcher registration and typed extension method are omitted for this pair.",
        category: "Caravelle.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor GenericHandlerNotSupportedDescriptor = new(
        id: "MBG003",
        title: "Generic handler is not supported",
        messageFormat: "Handler '{0}' is generic. Generic [Handler] classes are not supported by source generation.",
        category: "Caravelle.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NestedHandlerNotSupportedDescriptor = new(
        id: "MBG004",
        title: "Nested handler is not supported",
        messageFormat: "Handler '{0}' is nested. Nested [Handler] classes are not supported by source generation.",
        category: "Caravelle.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RequestTypeCannotBeInferredDescriptor = new(
        id: "MBG005",
        title: "Request type cannot be inferred",
        messageFormat: "Handler '{0}' request type cannot be inferred because all ordered method parameters match prior method outputs",
        category: "Caravelle.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateLocalVariableTypeDescriptor = new(
        id: "MBG006",
        title: "Duplicate local variable type",
        messageFormat: "Handler '{0}' produces duplicate local variable type '{1}'. A handler can only contain one local variable per type.",
        category: "Caravelle.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedMethodReturnTypeDescriptor = new(
        id: "MBG007",
        title: "Unsupported handler method return type",
        messageFormat: "Handler '{0}' has unsupported return type '{1}' on method '{2}'",
        category: "Caravelle.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor CyclicPhaseDependencyDescriptor = new(
        id: "MBG008",
        title: "Cyclic pipeline dependencies",
        messageFormat: "Handler '{0}' has cyclic dependencies between pipeline methods: {1}",
        category: "Caravelle.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InheritedMethodNotAccessibleDescriptor = new(
        id: "MBG009",
        title: "Inherited pipeline method is not accessible",
        messageFormat: "Handler '{0}' inherits pipeline method '{1}' from '{2}', but it is not accessible from the generated dispatcher. Inherited pipeline methods must be public or internal.",
        category: "Caravelle.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor FinallyParameterMustBeNullableDescriptor = new(
        id: "MBG010",
        title: "Finally parameter must be nullable",
        messageFormat: "Handler '{0}' has Finally method with non-nullable parameter '{1}' of type '{2}' that matches a pipeline return type. Parameters matching pipeline returns must be nullable.",
        category: "Caravelle.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor GenericMiddlewareNotSupportedDescriptor = new(
        id: "MBG011",
        title: "Generic middleware is not supported",
        messageFormat: "Middleware '{0}' is generic. Generic [Middleware] classes are not supported by source generation.",
        category: "Caravelle.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NestedMiddlewareNotSupportedDescriptor = new(
        id: "MBG012",
        title: "Nested middleware is not supported",
        messageFormat: "Middleware '{0}' is nested. Nested [Middleware] classes are not supported by source generation.",
        category: "Caravelle.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnrecognizedMiddlewareFilterDescriptor = new(
        id: "MBG013",
        title: "Unrecognized middleware filter",
        messageFormat: "Middleware '{0}' uses unrecognized filter type '{1}'. Only the filter types shipped by Caravelle are supported; this filter is ignored and will never match a handler.",
        category: "Caravelle.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MiddlewareMatchedNoHandlersDescriptor = new(
        id: "MBG014",
        title: "Middleware matched no handlers",
        messageFormat: "Middleware '{0}' does not match any [Handler] class in the compilation. Its pipeline methods will never run.",
        category: "Caravelle.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MiddlewareResolutionDidNotConvergeDescriptor = new(
        id: "MBG015",
        title: "Middleware resolution did not converge",
        messageFormat: "Handler '{0}' middleware matching did not converge within the expected number of passes. The generated pipeline may be missing applicable middleware; please report this as a bug.",
        category: "Caravelle.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static DiagnosticInfo DuplicateRequestType(
        Location location,
        string handlerName,
        string requestType) =>
        new(
            descriptor: DuplicateRequestTypeDescriptor,
            location: location,
            messageArgs: [handlerName, requestType]);

    public static DiagnosticInfo DuplicateRequestResponsePair(
        Location location,
        string handlerName,
        string requestType,
        string responseType) =>
        new (
            descriptor: DuplicateRequestResponsePairDescriptor,
            location: location,
            messageArgs: [handlerName, requestType, responseType]);

    public static DiagnosticInfo GenericHandlerNotSupported(
        Location location,
        string fullHandlerName) =>
        new (
            descriptor: GenericHandlerNotSupportedDescriptor,
            location: location,
            messageArgs: [fullHandlerName]);

    public static DiagnosticInfo NestedHandlerNotSupported(
        Location location,
        string fullHandlerName) =>
        new (
            descriptor: NestedHandlerNotSupportedDescriptor,
            location: location,
            messageArgs: [fullHandlerName]);

    public static DiagnosticInfo RequestTypeCannotBeInferred(
        Location location,
        string fullHandlerName) =>
        new (
            descriptor: RequestTypeCannotBeInferredDescriptor,
            location: location,
            messageArgs: [fullHandlerName]);

    public static DiagnosticInfo DuplicateLocalVariableType(
        Location location,
        string handlerName,
        string duplicateType) =>
        new (
            descriptor: DuplicateLocalVariableTypeDescriptor,
            location: location,
            messageArgs: [handlerName, duplicateType]);

    public static DiagnosticInfo UnsupportedMethodReturnType(
        Location location,
        string handlerName,
        string returnType,
        string methodName) =>
        new (
            descriptor: UnsupportedMethodReturnTypeDescriptor,
            location: location,
            messageArgs: [handlerName, returnType, methodName]);

    public static DiagnosticInfo CyclicPhaseDependency(
        Location location,
        string handlerName,
        string methodNames) =>
        new (
            descriptor: CyclicPhaseDependencyDescriptor,
            location: location,
            messageArgs: [handlerName, methodNames]);

    public static DiagnosticInfo InheritedMethodNotAccessible(
        Location location,
        string handlerName,
        string methodName,
        string declaringTypeName) =>
        new (
            descriptor: InheritedMethodNotAccessibleDescriptor,
            location: location,
            messageArgs: [handlerName, methodName, declaringTypeName]);

    public static DiagnosticInfo FinallyParameterMustBeNullable(
        Location location,
        string handlerName,
        string parameterName,
        string parameterType) =>
        new (
            descriptor: FinallyParameterMustBeNullableDescriptor,
            location: location,
            messageArgs: [handlerName, parameterName, parameterType]);

    public static DiagnosticInfo GenericMiddlewareNotSupported(
        Location location,
        string fullMiddlewareName) =>
        new (
            descriptor: GenericMiddlewareNotSupportedDescriptor,
            location: location,
            messageArgs: [fullMiddlewareName]);

    public static DiagnosticInfo NestedMiddlewareNotSupported(
        Location location,
        string fullMiddlewareName) =>
        new (
            descriptor: NestedMiddlewareNotSupportedDescriptor,
            location: location,
            messageArgs: [fullMiddlewareName]);

    public static DiagnosticInfo UnrecognizedMiddlewareFilter(
        Location location,
        string fullMiddlewareName,
        string filterTypeName) =>
        new (
            descriptor: UnrecognizedMiddlewareFilterDescriptor,
            location: location,
            messageArgs: [fullMiddlewareName, filterTypeName]);

    public static DiagnosticInfo MiddlewareMatchedNoHandlers(
        Location location,
        string fullMiddlewareName) =>
        new (
            descriptor: MiddlewareMatchedNoHandlersDescriptor,
            location: location,
            messageArgs: [fullMiddlewareName]);

    public static DiagnosticInfo MiddlewareResolutionDidNotConverge(
        Location location,
        string fullHandlerName) =>
        new (
            descriptor: MiddlewareResolutionDidNotConvergeDescriptor,
            location: location,
            messageArgs: [fullHandlerName]);
}
