using Microsoft.CodeAnalysis;

namespace MiniBus.Generator;

public static class Diagnostics
{
    private static readonly DiagnosticDescriptor DuplicateRequestTypeDescriptor = new(
        id: "MBG001",
        title: "Duplicate request type",
        messageFormat: "Handler '{0}' shares request type '{1}' with another [Handler] class. No typed extension method will be generated for this request type.",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedParameterDescriptor = new(
        id: "MBG002",
        title: "Unsupported handler parameter",
        messageFormat: "Handler '{0}' has unsupported parameter '{1}' in {2}. Parameters must match type '{3}'.",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateRequestResponsePairDescriptor = new(
        id: "MBG003",
        title: "Duplicate request/response pair",
        messageFormat: "Handler '{0}' shares request/response pair '{1}' -> '{2}' with another [Handler] class. Dispatcher registration and typed extension method are omitted for this pair.",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor GenericHandlerNotSupportedDescriptor = new(
        id: "MBG004",
        title: "Generic handler is not supported",
        messageFormat: "Handler '{0}' is generic. Generic [Handler] classes are not supported by source generation.",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NestedHandlerNotSupportedDescriptor = new(
        id: "MBG005",
        title: "Nested handler is not supported",
        messageFormat: "Handler '{0}' is nested. Nested [Handler] classes are not supported by source generation.",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RequestTypeCannotBeInferredDescriptor = new(
        id: "MBG006",
        title: "Request type cannot be inferred",
        messageFormat: "Handler '{0}' request type cannot be inferred because all ordered method parameters match prior method outputs",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static Diagnostic DuplicateRequestType(
        Location location,
        string handlerName,
        string requestType) =>
        Diagnostic.Create(
            descriptor: DuplicateRequestTypeDescriptor,
            location: location,
            messageArgs: [handlerName, requestType]);

    public static Diagnostic UnsupportedParameter(
        Location location,
        string handlerName,
        string parameterNameAndType,
        string methodName,
        string requestType) =>
        Diagnostic.Create(
            descriptor: UnsupportedParameterDescriptor,
            location: location,
            messageArgs: [handlerName, parameterNameAndType, methodName, requestType]);

    public static Diagnostic DuplicateRequestResponsePair(
        Location location,
        string handlerName,
        string requestType,
        string responseType) =>
        Diagnostic.Create(
            descriptor: DuplicateRequestResponsePairDescriptor,
            location: location,
            messageArgs: [handlerName, requestType, responseType]);

    public static Diagnostic GenericHandlerNotSupported(
        Location location,
        string fullHandlerName) =>
        Diagnostic.Create(
            descriptor: GenericHandlerNotSupportedDescriptor,
            location: location,
            messageArgs: [fullHandlerName]);

    public static Diagnostic NestedHandlerNotSupported(
        Location location,
        string fullHandlerName) =>
        Diagnostic.Create(
            descriptor: NestedHandlerNotSupportedDescriptor,
            location: location,
            messageArgs: [fullHandlerName]);

    public static Diagnostic RequestTypeCannotBeInferred(
        Location location,
        string fullHandlerName) =>
        Diagnostic.Create(
            descriptor: RequestTypeCannotBeInferredDescriptor,
            location: location,
            messageArgs: [fullHandlerName]);
}
