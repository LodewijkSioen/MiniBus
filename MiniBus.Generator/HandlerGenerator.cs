using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;

namespace MiniBus.Generator;

internal sealed record LoadInfo(
    bool IsAsync,
    string LoadedFullType);   // non-nullable, global::-prefixed

internal sealed record ValidateInfo(bool IsAsync);

internal sealed record HandlerModel(
    string? Namespace,
    string ClassName,
    string FullClassName,
    string FullRequestType,
    string FullResponseType,
    LoadInfo? Load,
    string HandleCallArgs,
    ValidateInfo? Validate,
    string ValidateCallArgs)
{
    // "global::TestApp.DummyHandler" + "Dispatcher" = "global::TestApp.DummyHandlerDispatcher"
    public string DispatcherFullName => FullClassName + "Dispatcher";
}

[Generator]
public class HandlerGenerator : IIncrementalGenerator
{
    private const string HandlerAttributeFqn = "MiniBus.Convention.HandlerAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var handlerModels = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                HandlerAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => GetHandlerModel(ctx, ct))
            .Where(static m => m is not null);

        // One file per handler: dispatcher class + typed ConventionBus extension method
        context.RegisterSourceOutput(handlerModels, static (spc, model) =>
        {
            if (model is null) return;
            spc.AddSource($"{model.ClassName}Dispatcher.g.cs", GenerateDispatcherSource(model));
        });

        // One file for all handlers: AddGeneratedHandlers() DI registration
        context.RegisterSourceOutput(handlerModels.Collect(), static (spc, models) =>
        {
            var valid = models
                .Where(static m => m is not null)
                .Select(static m => m!)
                .ToArray();
            if (valid.Length == 0) return;   // no handlers → no file (avoids noise in tests)
            spc.AddSource("ConventionRegistrations.g.cs", GenerateRegistrationsSource(valid));
        });
    }

    private static HandlerModel? GetHandlerModel(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol) return null;
        ct.ThrowIfCancellationRequested();

        // Must have a non-static Handle method with at least one parameter
        var handleMethod = classSymbol.GetMembers("Handle")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static m => !m.IsStatic && m.Parameters.Length >= 1);
        if (handleMethod is null) return null;

        // Unwrap Task<T> → T to determine TResponse
        var returnType = handleMethod.ReturnType;
        ITypeSymbol responseType;
        if (returnType is INamedTypeSymbol namedReturn
            && namedReturn.Name == "Task"
            && namedReturn.TypeArguments.Length == 1)
        {
            responseType = namedReturn.TypeArguments[0];
        }
        else
        {
            responseType = returnType;
        }

        var fmt = SymbolDisplayFormat.FullyQualifiedFormat;

        // ── Detect optional Load method ───────────────────────────────────────
        LoadInfo? loadInfo = null;
        string? loadedFqn = null;

        var loadMethod = classSymbol.GetMembers("Load")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static m => !m.IsStatic && m.Parameters.Length >= 1);

        if (loadMethod is not null)
        {
            var loadReturn = loadMethod.ReturnType;
            bool loadAsync = false;
            ITypeSymbol loadReturnInner = loadReturn;

            // Unwrap Task<T?>
            if (loadReturn is INamedTypeSymbol { Name: "Task" } taskType
                && taskType.TypeArguments.Length == 1)
            {
                loadReturnInner = taskType.TypeArguments[0];
                loadAsync = true;
            }

            // Nullable reference type (T?) → the non-null T is what gets loaded
            if (loadReturnInner.NullableAnnotation == NullableAnnotation.Annotated)
            {
                var nonNullable = loadReturnInner.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
                loadedFqn = nonNullable.ToDisplayString(fmt);
                loadInfo = new LoadInfo(IsAsync: loadAsync, LoadedFullType: loadedFqn);
            }
        }

        // Request type: first param of Load (if present), else first param of Handle
        var requestFqn = (loadMethod is not null
            ? loadMethod.Parameters[0].Type
            : handleMethod.Parameters[0].Type).ToDisplayString(fmt);

        // ── Compute Handle call arguments (matched by type) ───────────────────
        var handleArgsList = new System.Collections.Generic.List<string>();
        foreach (var param in handleMethod.Parameters)
        {
            var paramFqn = param.Type.ToDisplayString(fmt);
            if (loadedFqn is not null && paramFqn == loadedFqn)
                handleArgsList.Add("loaded");
            else
                handleArgsList.Add("request");
        }

        // ── Detect optional Validate method ───────────────────────────────────
        ValidateInfo? validateInfo = null;
        var validateArgsList = new System.Collections.Generic.List<string>();

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
            if (validateReturnInner.ToDisplayString(fmt) == "global::MiniBus.Convention.ValidationResult")
            {
                validateInfo = new ValidateInfo(IsAsync: validateAsync);
                foreach (var param in validateMethod.Parameters)
                {
                    var paramFqn = param.Type.ToDisplayString(fmt);
                    if (loadedFqn is not null && paramFqn == loadedFqn)
                        validateArgsList.Add("loaded");
                    else
                        validateArgsList.Add("request");
                }
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
            Validate: validateInfo,
            ValidateCallArgs: string.Join(", ", validateArgsList));
    }

    private static string GenerateDispatcherSource(HandlerModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        // ── Dispatcher class ─────────────────────────────────────────────────
        var inNs = model.Namespace is not null;
        var i = inNs ? "    " : "";     // indent when inside a namespace block

        if (inNs) { sb.AppendLine($"namespace {model.Namespace}"); sb.AppendLine("{"); }

        sb.AppendLine($"{i}public class {model.ClassName}Dispatcher");
        sb.AppendLine($"{i}    : global::MiniBus.Convention.IConventionHandler<");
        sb.AppendLine($"{i}        {model.FullRequestType},");
        sb.AppendLine($"{i}        {model.FullResponseType}>");
        sb.AppendLine($"{i}{{");
        sb.AppendLine($"{i}    private readonly {model.FullClassName} _handler;");
        sb.AppendLine();
        sb.AppendLine($"{i}    public {model.ClassName}Dispatcher({model.FullClassName} handler)");
        sb.AppendLine($"{i}    {{");
        sb.AppendLine($"{i}        _handler = handler;");
        sb.AppendLine($"{i}    }}");
        sb.AppendLine();
        sb.AppendLine($"{i}    public async global::System.Threading.Tasks.Task<");
        sb.AppendLine($"{i}        global::MiniBus.Convention.Result<{model.FullResponseType}>>");
        sb.AppendLine($"{i}        Handle({model.FullRequestType} request)");
        sb.AppendLine($"{i}    {{");

        // ── Load phase ────────────────────────────────────────────────────────
        if (model.Load is { } load)
        {
            var awaitPrefix = load.IsAsync ? "await " : "";
            sb.AppendLine($"{i}        var loaded = {awaitPrefix}_handler.Load(request);");
            sb.AppendLine($"{i}        if (loaded is null)");
            sb.AppendLine($"{i}            return global::MiniBus.Convention.Result<{model.FullResponseType}>.NotFound();");
            sb.AppendLine();
        }

        // ── Validate phase ────────────────────────────────────────────────────
        if (model.Validate is { } validate)
        {
            var validateAwait = validate.IsAsync ? "await " : "";
            sb.AppendLine($"{i}        var validationResult = {validateAwait}_handler.Validate({model.ValidateCallArgs});");
            sb.AppendLine($"{i}        if (!validationResult.IsValid())");
            sb.AppendLine($"{i}            return global::MiniBus.Convention.Result<{model.FullResponseType}>.Invalid(validationResult);");
            sb.AppendLine();
        }

        // ── Handle phase ──────────────────────────────────────────────────────
        sb.AppendLine($"{i}        var response = await _handler.Handle({model.HandleCallArgs});");
        sb.AppendLine($"{i}        return global::MiniBus.Convention.Result<{model.FullResponseType}>.Success(response);");
        sb.AppendLine($"{i}    }}");
        sb.AppendLine($"{i}}}");

        if (inNs) sb.AppendLine("}");

        // ── Typed ConventionBus extension method ─────────────────────────────
        sb.AppendLine();
        sb.AppendLine("namespace MiniBus.Convention");
        sb.AppendLine("{");
        sb.AppendLine($"    public static class {model.ClassName}Extensions");
        sb.AppendLine("    {");
        sb.AppendLine($"        public static global::System.Threading.Tasks.Task<global::MiniBus.Convention.Result<{model.FullResponseType}>>");
        sb.AppendLine($"            Handle(this global::MiniBus.Convention.ConventionBus bus, {model.FullRequestType} request)");
        sb.AppendLine($"            => bus.Handle<{model.FullRequestType}, {model.FullResponseType}>(request);");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GenerateRegistrationsSource(HandlerModel[] models)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("namespace MiniBus.Convention");
        sb.AppendLine("{");
        sb.AppendLine("    public static class GeneratedHandlerRegistrations");
        sb.AppendLine("    {");
        sb.AppendLine("        public static IServiceCollection AddGeneratedHandlers(");
        sb.AppendLine("            this IServiceCollection services)");
        sb.AppendLine("        {");

        foreach (var model in models)
        {
            sb.AppendLine($"            services.AddScoped<{model.FullClassName}>();");
            sb.AppendLine($"            services.AddScoped<");
            sb.AppendLine($"                global::MiniBus.Convention.IConventionHandler<");
            sb.AppendLine($"                    {model.FullRequestType},");
            sb.AppendLine($"                    {model.FullResponseType}>,");
            sb.AppendLine($"                {model.DispatcherFullName}>();");
        }

        sb.AppendLine("            return services;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}

