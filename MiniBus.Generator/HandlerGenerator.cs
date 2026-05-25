using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Immutable;
using System.Linq;

namespace MiniBus.Generator;

public sealed record LoadedElement(
    string LocalName,    // local variable name in generated code
    string FullType,     // non-nullable, global::-prefixed
    bool IsNullable,     // whether a null-check should be emitted
    string? NotFoundMessage = null)  // message for Result.NotFound() when null
{
    // For nullable elements, a non-nullable pattern variable captured after the null check
    public string NonNullLocalName => IsNullable ? LocalName + "Value" : LocalName;
}

public sealed record LoadInfo(
    bool IsAsync,
    bool IsTuple,
    ImmutableArray<LoadedElement> Elements);

public sealed record ValidateInfo(bool IsAsync);

public sealed record HandlerModel(
    string? Namespace,
    string ClassName,
    string FullClassName,
    string FullRequestType,
    string FullResponseType,
    LoadInfo? Load,
    string HandleCallArgs,
    bool HandleIsAsync,
    ValidateInfo? Validate,
    string ValidateCallArgs,
    ImmutableArray<string> UnsupportedHandleParameters,
    ImmutableArray<string> UnsupportedValidateParameters,
    bool IsGenericHandler,
    bool IsNestedHandler,
    Location Location)
{
    // "global::TestApp.DummyHandler" + "Dispatcher" = "global::TestApp.DummyHandlerDispatcher"
    public string DispatcherFullName => FullClassName + "Dispatcher";
    public string DispatcherKey => $"{FullRequestType}|{FullResponseType}";
    public bool IsAnyAsync => HandleIsAsync || (Load?.IsAsync ?? false) || (Validate?.IsAsync ?? false);
    public bool HasUnsupportedParameters => !UnsupportedHandleParameters.IsEmpty || !UnsupportedValidateParameters.IsEmpty;
}

[Generator]
public class HandlerGenerator : IIncrementalGenerator
{
    private const string HandlerAttributeFqn = "MiniBus.HandlerAttribute";

    private static readonly DiagnosticDescriptor DuplicateRequestType = new DiagnosticDescriptor(
        id: "MBG001",
        title: "Duplicate request type",
        messageFormat: "Handler '{0}' shares request type '{1}' with another [Handler] class. No typed extension method will be generated for this request type.",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedParameter = new DiagnosticDescriptor(
        id: "MBG002",
        title: "Unsupported handler parameter",
        messageFormat: "Handler '{0}' has unsupported parameter '{1}' in {2}. Parameters must match request type '{3}' or a loaded value type.",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateRequestResponsePair = new DiagnosticDescriptor(
        id: "MBG003",
        title: "Duplicate request/response pair",
        messageFormat: "Handler '{0}' shares request/response pair '{1}' -> '{2}' with another [Handler] class. Dispatcher registration and typed extension method are omitted for this pair.",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor GenericHandlerNotSupported = new DiagnosticDescriptor(
        id: "MBG004",
        title: "Generic handler is not supported",
        messageFormat: "Handler '{0}' is generic. Generic [Handler] classes are not supported by source generation.",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NestedHandlerNotSupported = new DiagnosticDescriptor(
        id: "MBG005",
        title: "Nested handler is not supported",
        messageFormat: "Handler '{0}' is nested. Nested [Handler] classes are not supported by source generation.",
        category: "MiniBus.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var handlerModels = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                HandlerAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => HandlerModelFactory.GetHandlerModel(ctx, ct))
            .Where(static m => m is not null);

        // One file per handler: dispatcher class + typed MiniBus extension method
        context.RegisterSourceOutput(handlerModels, static (spc, model) =>
        {
            if (model is null) return;
            foreach (var unsupported in model.UnsupportedHandleParameters)
                spc.ReportDiagnostic(Diagnostic.Create(UnsupportedParameter, model.Location, model.ClassName, unsupported, "Handle", model.FullRequestType));
            foreach (var unsupported in model.UnsupportedValidateParameters)
                spc.ReportDiagnostic(Diagnostic.Create(UnsupportedParameter, model.Location, model.ClassName, unsupported, "Validate", model.FullRequestType));
            if (model.IsGenericHandler)
                spc.ReportDiagnostic(Diagnostic.Create(GenericHandlerNotSupported, model.Location, model.FullClassName));
            if (model.IsNestedHandler)
                spc.ReportDiagnostic(Diagnostic.Create(NestedHandlerNotSupported, model.Location, model.FullClassName));
            if (model.HasUnsupportedParameters || model.IsGenericHandler || model.IsNestedHandler) return;
            spc.AddSource(CreateDispatcherHintName(model), DispatcherSourceBuilder.Build(model));
        });

        // One file for all handlers: AddGeneratedHandlers() DI registration
        context.RegisterSourceOutput(handlerModels.Collect(), static (spc, models) =>
        {
            var valid = models
                .Where(static m => m is not null)
                .Select(static m => m!)
                .Where(static m => !m.HasUnsupportedParameters && !m.IsGenericHandler && !m.IsNestedHandler)
                .ToArray();
            // Detect handlers that share a request type — extension methods would collide (CS0111)
            var conflicting = new System.Collections.Generic.HashSet<string>();
            foreach (var group in valid.GroupBy(static m => m.FullRequestType).Where(static g => g.Count() > 1))
            {
                conflicting.Add(group.Key);
                foreach (var m in group)
                    spc.ReportDiagnostic(Diagnostic.Create(DuplicateRequestType, m.Location, m.ClassName, m.FullRequestType));
            }

            var excludedDispatcherPairs = new System.Collections.Generic.HashSet<string>();
            foreach (var group in valid.GroupBy(static m => m.DispatcherKey).Where(static g => g.Count() > 1))
            {
                excludedDispatcherPairs.Add(group.Key);
                foreach (var m in group)
                    spc.ReportDiagnostic(Diagnostic.Create(DuplicateRequestResponsePair, m.Location, m.ClassName, m.FullRequestType, m.FullResponseType));
            }

            spc.AddSource("MiniBusRegistrations.g.cs", RegistrationsSourceBuilder.Build(valid, conflicting, excludedDispatcherPairs));
        });
    }

    private static string CreateDispatcherHintName(HandlerModel model)
    {
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(model.FullClassName));
        var chars = new char[8];
        for (var i = 0; i < 4; i++)
        {
            var b = hashBytes[i];
            chars[i * 2] = ToHexChar((b >> 4) & 0xF);
            chars[i * 2 + 1] = ToHexChar(b & 0xF);
        }

        var hash = new string(chars);
        return $"{model.ClassName}Dispatcher_{hash}.g.cs";
    }

    private static char ToHexChar(int value) =>
        (char)(value < 10 ? '0' + value : 'a' + (value - 10));
}
