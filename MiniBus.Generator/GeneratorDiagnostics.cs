using Microsoft.CodeAnalysis;

namespace MiniBus.Generator;

public static class GeneratorDiagnostics
{
    private static readonly DiagnosticDescriptor DuplicateRequestTypeDescriptor = new DiagnosticDescriptor(
        id: "MBG001",
        title: "Duplicate request type",
        messageFormat: "Handler '{0}' shares request type '{1}' with another [Handler] class. No typed extension method will be generated for this request type.",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedParameterDescriptor = new DiagnosticDescriptor(
        id: "MBG002",
        title: "Unsupported handler parameter",
        messageFormat: "Handler '{0}' has unsupported parameter '{1}' in {2}. Parameters must match request type '{3}' or a loaded value type.",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateRequestResponsePairDescriptor = new DiagnosticDescriptor(
        id: "MBG003",
        title: "Duplicate request/response pair",
        messageFormat: "Handler '{0}' shares request/response pair '{1}' -> '{2}' with another [Handler] class. Dispatcher registration and typed extension method are omitted for this pair.",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor GenericHandlerNotSupportedDescriptor = new DiagnosticDescriptor(
        id: "MBG004",
        title: "Generic handler is not supported",
        messageFormat: "Handler '{0}' is generic. Generic [Handler] classes are not supported by source generation.",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NestedHandlerNotSupportedDescriptor = new DiagnosticDescriptor(
        id: "MBG005",
        title: "Nested handler is not supported",
        messageFormat: "Handler '{0}' is nested. Nested [Handler] classes are not supported by source generation.",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static Diagnostic DuplicateRequestType(
        Location location,
        string handlerName,
        string requestType) =>
        Diagnostic.Create(
            descriptor: DuplicateRequestTypeDescriptor,
            location: location,
            messageArgs: new object?[] { handlerName, requestType });

    public static Diagnostic UnsupportedParameter(
        Location location,
        string handlerName,
        string parameterNameAndType,
        string methodName,
        string requestType) =>
        Diagnostic.Create(
            descriptor: UnsupportedParameterDescriptor,
            location: location,
            messageArgs: new object?[] { handlerName, parameterNameAndType, methodName, requestType });

    public static Diagnostic DuplicateRequestResponsePair(
        Location location,
        string handlerName,
        string requestType,
        string responseType) =>
        Diagnostic.Create(
            descriptor: DuplicateRequestResponsePairDescriptor,
            location: location,
            messageArgs: new object?[] { handlerName, requestType, responseType });

    public static Diagnostic GenericHandlerNotSupported(
        Location location,
        string fullHandlerName) =>
        Diagnostic.Create(
            descriptor: GenericHandlerNotSupportedDescriptor,
            location: location,
            messageArgs: new object?[] { fullHandlerName });

    public static Diagnostic NestedHandlerNotSupported(
        Location location,
        string fullHandlerName) =>
        Diagnostic.Create(
            descriptor: NestedHandlerNotSupportedDescriptor,
            location: location,
            messageArgs: new object?[] { fullHandlerName });
}
