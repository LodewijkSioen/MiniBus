using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace MiniBus.Generator;

public sealed record LoadedElement(
    string LocalName,    // local variable name in generated code
    string FullType,     // non-nullable, global::-prefixed
    bool IsNullable);    // whether a null-check should be emitted

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

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var handlerModels = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                HandlerAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => GetHandlerModel(ctx, ct))
            .Where(static m => m is not null);

        // One file per handler: dispatcher class + typed MiniBus extension method
        context.RegisterSourceOutput(handlerModels, static (spc, model) =>
        {
            if (model is null) return;
            foreach (var unsupported in model.UnsupportedHandleParameters)
                spc.ReportDiagnostic(Diagnostic.Create(UnsupportedParameter, model.Location, model.ClassName, unsupported, "Handle", model.FullRequestType));
            foreach (var unsupported in model.UnsupportedValidateParameters)
                spc.ReportDiagnostic(Diagnostic.Create(UnsupportedParameter, model.Location, model.ClassName, unsupported, "Validate", model.FullRequestType));
            if (model.HasUnsupportedParameters) return;
            spc.AddSource($"{model.ClassName}Dispatcher.g.cs", DispatcherSourceBuilder.Build(model));
        });

        // One file for all handlers: AddGeneratedHandlers() DI registration
        context.RegisterSourceOutput(handlerModels.Collect(), static (spc, models) =>
        {
            var valid = models
                .Where(static m => m is not null)
                .Select(static m => m!)
                .Where(static m => !m.HasUnsupportedParameters)
                .ToArray();
            if (valid.Length == 0) return;   // no handlers → no file (avoids noise in tests)

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

    private static HandlerModel? GetHandlerModel(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol) return null;
        ct.ThrowIfCancellationRequested();
        return GetHandlerModel(classSymbol, ctx.TargetNode.GetLocation());
    }

    public static HandlerModel? GetHandlerModel(INamedTypeSymbol classSymbol, Location location)
    {
        // Must have a non-static Handle method with at least one parameter
        var handleMethod = classSymbol.GetMembers("Handle")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static m => !m.IsStatic && m.Parameters.Length >= 1);
        if (handleMethod is null) return null;

        // Unwrap Task<T> → T to determine TResponse
        var returnType = handleMethod.ReturnType;
        ITypeSymbol responseType;
        bool handleIsAsync;
        if (returnType is INamedTypeSymbol namedReturn
            && namedReturn.Name == "Task"
            && namedReturn.TypeArguments.Length == 1)
        {
            responseType = namedReturn.TypeArguments[0];
            handleIsAsync = true;
        }
        else
        {
            responseType = returnType;
            handleIsAsync = false;
        }

        var fmt = SymbolDisplayFormat.FullyQualifiedFormat;

        // ── Detect optional Load method ───────────────────────────────────────
        LoadInfo? loadInfo = null;
        var loadedByFqn = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>(); // fqn → localNames
        static void AddLoadedLocalName(
            System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> map,
            string fqn,
            string localName)
        {
            if (!map.TryGetValue(fqn, out var names))
            {
                names = new System.Collections.Generic.List<string>();
                map[fqn] = names;
            }

            names.Add(localName);
        }

        var loadMethod = classSymbol.GetMembers("Load")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static m => !m.IsStatic && m.Parameters.Length >= 1);

        if (loadMethod is not null)
        {
            var loadReturn = loadMethod.ReturnType;
            bool loadAsync = false;
            ITypeSymbol loadReturnInner = loadReturn;

            // Unwrap Task<(A?,B?)> or Task<T?>
            if (loadReturn is INamedTypeSymbol { Name: "Task" } taskType
                && taskType.TypeArguments.Length == 1)
            {
                loadReturnInner = taskType.TypeArguments[0];
                loadAsync = true;
            }

            if (loadReturnInner is INamedTypeSymbol { IsTupleType: true } tupleType)
            {
                // Tuple load: (A? a, B? b) etc.
                var elements = ImmutableArray.CreateBuilder<LoadedElement>();
                for (int ei = 0; ei < tupleType.TupleElements.Length; ei++)
                {
                    var elem = tupleType.TupleElements[ei];
                    var elemType = elem.Type;
                    bool isNullable = elemType.NullableAnnotation == NullableAnnotation.Annotated;
                    var nonNullType = isNullable
                        ? elemType.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                        : elemType;
                    var fqn = nonNullType.ToDisplayString(fmt);
                    // Camelcase the element name (Item1→item1, entity→entity)
                    var rawName = elem.Name;
                    var localName = rawName.Length > 0 && char.IsUpper(rawName[0])
                        ? char.ToLower(rawName[0]) + rawName.Substring(1)
                        : rawName;
                    elements.Add(new LoadedElement(localName, fqn, isNullable));
                    AddLoadedLocalName(loadedByFqn, fqn, localName);
                }
                loadInfo = new LoadInfo(IsAsync: loadAsync, IsTuple: true, Elements: elements.ToImmutable());
            }
            else
            {
                // Scalar load branch: handles both T and T? return types.
                var isNullable = loadReturnInner.NullableAnnotation == NullableAnnotation.Annotated;
                var nonNullable = isNullable
                    ? loadReturnInner.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                    : loadReturnInner;
                var fqn = nonNullable.ToDisplayString(fmt);
                AddLoadedLocalName(loadedByFqn, fqn, "loaded");
                loadInfo = new LoadInfo(IsAsync: loadAsync, IsTuple: false,
                    Elements: ImmutableArray.Create(new LoadedElement("loaded", fqn, IsNullable: isNullable)));
            }
        }

        // Request type: first param of Load (if present), else first param of Handle
        var requestFqn = (loadMethod is not null
            ? loadMethod.Parameters[0].Type
            : handleMethod.Parameters[0].Type).ToDisplayString(fmt);

        // ── Compute Handle call arguments (matched by type) ───────────────────
        static (System.Collections.Generic.List<string> CallArgs, ImmutableArray<string> Unsupported) BuildCallArgs(
            ImmutableArray<IParameterSymbol> parameters,
            System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> loadedByType,
            string requestType,
            SymbolDisplayFormat format)
        {
            var callArgs = new System.Collections.Generic.List<string>();
            var seenLoadedTypeCount = new System.Collections.Generic.Dictionary<string, int>();
            var unsupported = ImmutableArray.CreateBuilder<string>();
            foreach (var param in parameters)
            {
                var paramFqn = param.Type.ToDisplayString(format);
                if (loadedByType.TryGetValue(paramFqn, out var loadedNames))
                {
                    if (loadedNames.Count == 1)
                    {
                        callArgs.Add(loadedNames[0]);
                        continue;
                    }

                    var byName = loadedNames
                        .Where(n => string.Equals(n, param.Name, global::System.StringComparison.Ordinal))
                        .Distinct()
                        .ToArray();
                    if (byName.Length == 1)
                    {
                        callArgs.Add(byName[0]);
                        continue;
                    }

                    seenLoadedTypeCount.TryGetValue(paramFqn, out var seenCount);
                    callArgs.Add(seenCount < loadedNames.Count ? loadedNames[seenCount] : loadedNames[loadedNames.Count - 1]);
                    seenLoadedTypeCount[paramFqn] = seenCount + 1;
                    continue;
                }

                if (paramFqn == requestType)
                {
                    callArgs.Add("request");
                    continue;
                }

                callArgs.Add("request");
                unsupported.Add($"{param.Name}: {paramFqn}");
            }

            return (callArgs, unsupported.ToImmutable());
        }
        var (handleArgsList, unsupportedHandleParameters) = BuildCallArgs(handleMethod.Parameters, loadedByFqn, requestFqn, fmt);

        // ── Detect optional Validate method ───────────────────────────────────
        ValidateInfo? validateInfo = null;
        var validateArgsList = new System.Collections.Generic.List<string>();
        var unsupportedValidateParameters = ImmutableArray<string>.Empty;

        var validateMethod = classSymbol.GetMembers("Validate")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static m => !m.IsStatic);

        if (validateMethod is not null)
        {
            var validateReturn = validateMethod.ReturnType;
            bool validateAsync = false;
            ITypeSymbol validateReturnInner = validateReturn;

            // Unwrap Task<T>
            if (validateReturn is INamedTypeSymbol { Name: "Task" } vTaskType
                && vTaskType.TypeArguments.Length == 1)
            {
                validateReturnInner = vTaskType.TypeArguments[0];
                validateAsync = true;
            }

            // Must return ValidationResult
            if (validateReturnInner.ToDisplayString(fmt) == "global::MiniBus.ValidationResult")
            {
                validateInfo = new ValidateInfo(IsAsync: validateAsync);
                var (args, unsupported) = BuildCallArgs(validateMethod.Parameters, loadedByFqn, requestFqn, fmt);
                validateArgsList = args;
                unsupportedValidateParameters = unsupported;
            }
        }

        var ns = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : classSymbol.ContainingNamespace.ToDisplayString();

        return new HandlerModel(
            Namespace: ns,
            ClassName: classSymbol.Name,
            FullClassName: classSymbol.ToDisplayString(fmt),
            FullRequestType: requestFqn,
            FullResponseType: responseType.ToDisplayString(fmt),
            Load: loadInfo,
            HandleCallArgs: string.Join(", ", handleArgsList),
            HandleIsAsync: handleIsAsync,
            Validate: validateInfo,
            ValidateCallArgs: string.Join(", ", validateArgsList),
            UnsupportedHandleParameters: unsupportedHandleParameters,
            UnsupportedValidateParameters: unsupportedValidateParameters,
            Location: location);
    }
}
