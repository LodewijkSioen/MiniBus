using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace MiniBus.Generator;

public static class HandlerModelFactory
{
    public static HandlerModel? GetHandlerModel(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol) return null;
        ct.ThrowIfCancellationRequested();
        return GetHandlerModel(classSymbol, ctx.TargetNode.GetLocation());
    }

    public static HandlerModel? GetHandlerModel(INamedTypeSymbol classSymbol, Location location)
    {
        var fmt = SymbolDisplayFormat.FullyQualifiedFormat;

        var isGenericHandler = classSymbol.Arity > 0 || HasGenericContainingType(classSymbol.ContainingType);
        var isNestedHandler = classSymbol.ContainingType is not null;

        // Find all three methods upfront
        var handleMethod = classSymbol.GetMembers("Handle")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static m => !m.IsStatic && m.Parameters.Length >= 1);
        if (handleMethod is null) return null;

        var loadMethod = classSymbol.GetMembers("Load")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static m => !m.IsStatic && m.Parameters.Length >= 1);

        var validateMethod = classSymbol.GetMembers("Validate")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static m => !m.IsStatic);

        // Unwrap Handle return type to determine TResponse
        var (responseType, handleIsAsync) = UnwrapTask(handleMethod.ReturnType);

        // Extract Load info and build the loaded-by-type lookup
        var (loadInfo, loadedByFqn) = ExtractLoadInfo(loadMethod, fmt);

        // Request type: first param of Load (if present), else first param of Validate (if present), else first param of Handle
        var requestTypeSymbol = loadMethod is not null
            ? loadMethod.Parameters[0].Type
            : validateMethod is { Parameters.Length: >= 1 }
                ? validateMethod.Parameters[0].Type
                : handleMethod.Parameters[0].Type;
        var requestFqn = requestTypeSymbol.ToDisplayString(fmt);

        // Build call args for Handle
        var (handleArgsList, unsupportedHandleParameters) = BuildCallArgs(handleMethod.Parameters, loadedByFqn, requestFqn, fmt);

        // Detect optional Validate method (must return ValidationResult)
        ValidateInfo? validateInfo = null;
        var validateArgsList = new List<string>();
        var unsupportedValidateParameters = ImmutableArray<string>.Empty;

        if (validateMethod is not null)
        {
            var (validateReturnInner, validateAsync) = UnwrapTask(validateMethod.ReturnType);
            if (validateReturnInner.ToDisplayString(fmt) == "global::MiniBus.ValidationResult")
            {
                validateInfo = new ValidateInfo(IsAsync: validateAsync);
                var (args, unsupported) = BuildCallArgs(validateMethod.Parameters, loadedByFqn, requestFqn, fmt);
                validateArgsList = args;
                unsupportedValidateParameters = unsupported;
            }
        }

        // Enrich nullable LoadedElements with [Required] error messages from Handle + Validate parameters
        if (loadInfo is not null)
        {
            var allMethodParams = validateMethod is not null
                ? handleMethod.Parameters.AddRange(validateMethod.Parameters)
                : handleMethod.Parameters;
            loadInfo = EnrichWithNotFoundMessages(loadInfo, allMethodParams, fmt);
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
            IsGenericHandler: isGenericHandler,
            IsNestedHandler: isNestedHandler,
            Location: location);
    }

    private static (ITypeSymbol Inner, bool IsAsync) UnwrapTask(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol { Name: "Task" } task && task.TypeArguments.Length == 1)
            return (task.TypeArguments[0], true);
        return (returnType, false);
    }

    private static (LoadInfo? LoadInfo, Dictionary<string, List<string>> LoadedByFqn) ExtractLoadInfo(
        IMethodSymbol? loadMethod,
        SymbolDisplayFormat fmt)
    {
        var loadedByFqn = new Dictionary<string, List<string>>();
        if (loadMethod is null)
            return (null, loadedByFqn);

        var (loadReturnInner, loadAsync) = UnwrapTask(loadMethod.ReturnType);

        if (loadReturnInner is INamedTypeSymbol { IsTupleType: true } tupleType)
        {
            // Tuple load: (A? a, B? b) etc.
            var elements = ImmutableArray.CreateBuilder<LoadedElement>();
            foreach (var elem in tupleType.TupleElements)
            {
                var elemType = elem.Type;
                bool isNullable = elemType.NullableAnnotation == NullableAnnotation.Annotated;
                var nonNullType = isNullable
                    ? elemType.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                    : elemType;
                var fqn = nonNullType.ToDisplayString(fmt);
                // Camelcase the element name (Item1->item1, Entity->entity)
                var rawName = elem.Name;
                var localName = rawName.Length > 0 && char.IsUpper(rawName[0])
                    ? char.ToLower(rawName[0]) + rawName.Substring(1)
                    : rawName;
                var loadedElem = new LoadedElement(localName, fqn, isNullable);
                elements.Add(loadedElem);
                AddToLoadedByFqn(loadedByFqn, fqn, loadedElem.NonNullLocalName);
            }
            return (new LoadInfo(IsAsync: loadAsync, IsTuple: true, Elements: elements.ToImmutable()), loadedByFqn);
        }

        // Scalar load: handles both T and T? return types
        var scalarIsNullable = loadReturnInner.NullableAnnotation == NullableAnnotation.Annotated;
        var nonNullable = scalarIsNullable
            ? loadReturnInner.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            : loadReturnInner;
        var scalarFqn = nonNullable.ToDisplayString(fmt);
        var scalarLoadedElem = new LoadedElement("loaded", scalarFqn, IsNullable: scalarIsNullable);
        AddToLoadedByFqn(loadedByFqn, scalarFqn, scalarLoadedElem.NonNullLocalName);
        return (new LoadInfo(IsAsync: loadAsync, IsTuple: false, Elements: ImmutableArray.Create(scalarLoadedElem)), loadedByFqn);
    }

    private static void AddToLoadedByFqn(
        Dictionary<string, List<string>> map,
        string fqn,
        string localName)
    {
        if (!map.TryGetValue(fqn, out var names))
        {
            names = new List<string>();
            map[fqn] = names;
        }
        names.Add(localName);
    }

    private static (List<string> CallArgs, ImmutableArray<string> Unsupported) BuildCallArgs(
        ImmutableArray<IParameterSymbol> parameters,
        Dictionary<string, List<string>> loadedByType,
        string requestType,
        SymbolDisplayFormat format)
    {
        var callArgs = new List<string>();
        var seenLoadedTypeCount = new Dictionary<string, int>();
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
                    .Where(n => string.Equals(n, param.Name, StringComparison.Ordinal))
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

            unsupported.Add($"{param.Name}: {paramFqn}");
        }

        return (callArgs, unsupported.ToImmutable());
    }

    private static LoadInfo EnrichWithNotFoundMessages(
        LoadInfo loadInfo,
        ImmutableArray<IParameterSymbol> parameters,
        SymbolDisplayFormat fmt)
    {
        var enriched = ImmutableArray.CreateBuilder<LoadedElement>();
        var anyEnriched = false;
        foreach (var elem in loadInfo.Elements)
        {
            if (!elem.IsNullable)
            {
                enriched.Add(elem);
                continue;
            }
            var msg = GetRequiredMessage(parameters, elem.FullType, fmt);
            if (msg is not null)
            {
                enriched.Add(elem with { NotFoundMessage = msg });
                anyEnriched = true;
            }
            else
            {
                enriched.Add(elem);
            }
        }
        return anyEnriched ? new LoadInfo(loadInfo.IsAsync, loadInfo.IsTuple, enriched.ToImmutable()) : loadInfo;
    }

    private static string? GetRequiredMessage(
        ImmutableArray<IParameterSymbol> parameters,
        string loadedFqn,
        SymbolDisplayFormat format)
    {
        foreach (var param in parameters)
        {
            if (param.Type.ToDisplayString(format) != loadedFqn) continue;
            var req = param.GetAttributes().FirstOrDefault(static a =>
                a.AttributeClass?.Name == "RequiredAttribute"
                && a.AttributeClass.ContainingNamespace?.ToDisplayString()
                    == "System.ComponentModel.DataAnnotations");
            if (req is null) continue;
            var msgArg = req.NamedArguments
                .FirstOrDefault(static kv => kv.Key == "ErrorMessage");
            return msgArg.Value.Value as string;
        }
        return null;
    }

    private static bool HasGenericContainingType(INamedTypeSymbol? typeSymbol)
    {
        var current = typeSymbol;
        while (current is not null)
        {
            if (current.Arity > 0) return true;
            current = current.ContainingType;
        }

        return false;
    }
}
