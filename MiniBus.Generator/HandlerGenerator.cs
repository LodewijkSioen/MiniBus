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
    int Order,
    ImmutableArray<LoadedElement> Elements);

public sealed record ValidateInfo(bool IsAsync, int Order);

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
                spc.ReportDiagnostic(Diagnostics.UnsupportedParameter(
                    location: model.Location,
                    handlerName: model.ClassName,
                    parameterNameAndType: unsupported,
                    methodName: "Handle",
                    requestType: model.FullRequestType));
            foreach (var unsupported in model.UnsupportedValidateParameters)
                spc.ReportDiagnostic(Diagnostics.UnsupportedParameter(
                    location: model.Location,
                    handlerName: model.ClassName,
                    parameterNameAndType: unsupported,
                    methodName: "Validate",
                    requestType: model.FullRequestType));
            if (model.IsGenericHandler)
                spc.ReportDiagnostic(Diagnostics.GenericHandlerNotSupported(
                    location: model.Location,
                    fullHandlerName: model.FullClassName));
            if (model.IsNestedHandler)
                spc.ReportDiagnostic(Diagnostics.NestedHandlerNotSupported(
                    location: model.Location,
                    fullHandlerName: model.FullClassName));
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
                    spc.ReportDiagnostic(Diagnostics.DuplicateRequestType(
                        location: m.Location,
                        handlerName: m.ClassName,
                        requestType: m.FullRequestType));
            }

            var excludedDispatcherPairs = new System.Collections.Generic.HashSet<string>();
            foreach (var group in valid.GroupBy(static m => m.DispatcherKey).Where(static g => g.Count() > 1))
            {
                excludedDispatcherPairs.Add(group.Key);
                foreach (var m in group)
                    spc.ReportDiagnostic(Diagnostics.DuplicateRequestResponsePair(
                        location: m.Location,
                        handlerName: m.ClassName,
                        requestType: m.FullRequestType,
                        responseType: m.FullResponseType));
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
