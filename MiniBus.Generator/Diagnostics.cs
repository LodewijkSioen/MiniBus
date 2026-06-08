using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace MiniBus.Generator;

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
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateRequestResponsePairDescriptor = new(
        id: "MBG002",
        title: "Duplicate request/response pair",
        messageFormat: "Handler '{0}' shares request/response pair '{1}' -> '{2}' with another [Handler] class. Dispatcher registration and typed extension method are omitted for this pair.",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor GenericHandlerNotSupportedDescriptor = new(
        id: "MBG003",
        title: "Generic handler is not supported",
        messageFormat: "Handler '{0}' is generic. Generic [Handler] classes are not supported by source generation.",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NestedHandlerNotSupportedDescriptor = new(
        id: "MBG004",
        title: "Nested handler is not supported",
        messageFormat: "Handler '{0}' is nested. Nested [Handler] classes are not supported by source generation.",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RequestTypeCannotBeInferredDescriptor = new(
        id: "MBG005",
        title: "Request type cannot be inferred",
        messageFormat: "Handler '{0}' request type cannot be inferred because all ordered method parameters match prior method outputs",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateLocalVariableTypeDescriptor = new(
        id: "MBG006",
        title: "Duplicate local variable type",
        messageFormat: "Handler '{0}' produces duplicate local variable type '{1}'. A handler can only contain one local variable per type.",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedMethodReturnTypeDescriptor = new(
        id: "MBG007",
        title: "Unsupported handler method return type",
        messageFormat: "Handler '{0}' has unsupported return type '{1}' on method '{2}'",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor CyclicPhaseDependencyDescriptor = new(
        id: "MBG008",
        title: "Cyclic pipeline dependencies",
        messageFormat: "Handler '{0}' has cyclic dependencies between pipeline methods: {1}",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidHandleTupleResponseDescriptor = new(
        id: "MBG009",
        title: "Invalid Handle tuple response shape",
        messageFormat: "Handler '{0}' has invalid Handle return type '{1}'. When Handle returns a tuple, the first tuple element cannot be ValidationResult.",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
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

    public static DiagnosticInfo InvalidHandleTupleResponse(
        Location location,
        string handlerName,
        string returnType) =>
        new (
            descriptor: InvalidHandleTupleResponseDescriptor,
            location: location,
            messageArgs: [handlerName, returnType]);
}
