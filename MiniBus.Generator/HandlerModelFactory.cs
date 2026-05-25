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

        var handleMethod = classSymbol.GetMembers("Handle")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static m => !m.IsStatic && m.Parameters.Length >= 1);
        if (handleMethod is null) return null;

        var loadMethod = classSymbol.GetMembers("Load")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static m => !m.IsStatic);

        var rawValidateMethod = classSymbol.GetMembers("Validate")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static m => !m.IsStatic);

        var handlePhase = new HandleMethodPhase(handleMethod, fmt);
        var loadPhase = loadMethod is null ? null : new LoadMethodPhase(loadMethod, fmt);

        ValidateMethodPhase? validatePhase = null;
        if (rawValidateMethod is not null)
        {
            var candidate = new ValidateMethodPhase(rawValidateMethod, fmt);
            if (candidate.ReturnsValidationResult)
                validatePhase = candidate;
        }

        var preHandleCandidates = ImmutableArray.CreateBuilder<IPreHandlePhaseInfo>();
        if (loadPhase is not null)
            preHandleCandidates.Add(loadPhase);
        if (validatePhase is not null)
            preHandleCandidates.Add(validatePhase);

        var orderedPreHandle = OrderPreHandlePhases(preHandleCandidates.ToImmutable());
        var orderedMethods = orderedPreHandle.Cast<IMethodPhaseInfo>()
            .Append(handlePhase)
            .ToArray();

        var requestType = InferRequestType(orderedMethods, out var inferredRequestType);
        var extractionDiagnostics = ImmutableArray<Diagnostic>.Empty;
        if (!requestType)
        {
            extractionDiagnostics = ImmutableArray.Create(
                Diagnostics.RequestTypeCannotBeInferred(
                    location: location,
                    fullHandlerName: classSymbol.ToDisplayString(fmt)));
            inferredRequestType = FallbackRequestType(handleMethod, loadMethod, rawValidateMethod, fmt);
        }

        BindCallArguments(orderedPreHandle, handlePhase, inferredRequestType!, fmt);

        if (loadPhase is not null)
        {
            var allMethodParams = validatePhase is not null
                ? handleMethod.Parameters.AddRange(validatePhase.Parameters)
                : handleMethod.Parameters;
            loadPhase.Elements = EnrichWithNotFoundMessages(loadPhase.Elements, allMethodParams, fmt);
        }

        var ns = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : classSymbol.ContainingNamespace.ToDisplayString();

        return new HandlerModel(
            Namespace: ns,
            ClassName: classSymbol.Name,
            FullClassName: classSymbol.ToDisplayString(fmt),
            FullRequestType: inferredRequestType!,
            FullResponseType: handlePhase.FullResponseType,
            Phases: HandlerPhases.From(handlePhase, loadPhase, validatePhase),
            IsGenericHandler: isGenericHandler,
            IsNestedHandler: isNestedHandler,
            Location: location)
        {
            ExtractionDiagnostics = extractionDiagnostics
        };
    }

    private static ImmutableArray<IPreHandlePhaseInfo> OrderPreHandlePhases(
        ImmutableArray<IPreHandlePhaseInfo> phases)
    {
        if (phases.IsDefaultOrEmpty)
            return ImmutableArray<IPreHandlePhaseInfo>.Empty;

        var pending = phases
            .Select((phase, index) => new PendingPhase(phase, index))
            .ToList();
        var ordered = new List<IPreHandlePhaseInfo>(pending.Count);

        while (pending.Count > 0)
        {
            var ready = pending
                .Where(candidate => !pending.Any(other =>
                    !ReferenceEquals(candidate, other) && DependsOn(candidate.Phase, other.Phase)))
                .OrderBy(candidate => HasOutputs(candidate.Phase))
                .ThenBy(candidate => candidate.Phase.TieBreak)
                .ThenBy(candidate => candidate.SourceIndex)
                .ToList();

            var next = ready.Count > 0
                ? ready[0]
                : pending
                    .OrderBy(candidate => HasOutputs(candidate.Phase))
                    .ThenBy(candidate => candidate.Phase.TieBreak)
                    .ThenBy(candidate => candidate.SourceIndex)
                    .First();

            pending.Remove(next);
            ordered.Add(next.Phase);
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i;
        }

        return ordered.ToImmutableArray();
    }

    private static bool DependsOn(IPreHandlePhaseInfo candidate, IPreHandlePhaseInfo dependency)
    {
        if (candidate is not IMethodPhaseInfo candidateMethod || dependency is not IMethodPhaseInfo dependencyMethod)
            return false;

        return candidateMethod.InputTypeFqns.Any(dependencyMethod.OutputTypeFqns.Contains);
    }

    private static bool HasOutputs(IPreHandlePhaseInfo phase) =>
        phase is IMethodPhaseInfo methodPhase && !methodPhase.OutputTypeFqns.IsDefaultOrEmpty;

    private sealed record PendingPhase(IPreHandlePhaseInfo Phase, int SourceIndex);

    private static bool InferRequestType(
        IEnumerable<IMethodPhaseInfo> orderedMethods,
        out string? requestType)
    {
        var availableOutputs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var phase in orderedMethods)
        {
            foreach (var inputType in phase.InputTypeFqns)
            {
                if (!availableOutputs.Contains(inputType))
                {
                    requestType = inputType;
                    return true;
                }
            }

            foreach (var outputType in phase.OutputTypeFqns)
                availableOutputs.Add(outputType);
        }

        requestType = null;
        return false;
    }

    private static string FallbackRequestType(
        IMethodSymbol handleMethod,
        IMethodSymbol? loadMethod,
        IMethodSymbol? validateMethod,
        SymbolDisplayFormat fmt)
    {
        var requestTypeSymbol = loadMethod is { Parameters.Length: >= 1 }
            ? loadMethod.Parameters[0].Type
            : validateMethod is { Parameters.Length: >= 1 }
                ? validateMethod.Parameters[0].Type
                : handleMethod.Parameters[0].Type;

        return requestTypeSymbol.ToDisplayString(fmt);
    }

    private static void BindCallArguments(
        ImmutableArray<IPreHandlePhaseInfo> orderedPreHandle,
        HandleMethodPhase handlePhase,
        string requestType,
        SymbolDisplayFormat format)
    {
        var loadedByType = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var preHandle in orderedPreHandle)
        {
            if (preHandle is LoadMethodPhase loadPhase)
            {
                var (loadCallArgs, loadUnsupported) = BuildCallArgs(loadPhase.Parameters, loadedByType, requestType, format);
                loadPhase.CallArgs = string.Join(", ", loadCallArgs);
                loadPhase.UnsupportedParameters = loadUnsupported;

                foreach (var element in loadPhase.Elements)
                    AddToLoadedByType(loadedByType, element.FullType, element.NonNullLocalName);

                continue;
            }

            if (preHandle is ValidateMethodPhase validatePhase)
            {
                var (validateCallArgs, validateUnsupported) = BuildCallArgs(validatePhase.Parameters, loadedByType, requestType, format);
                validatePhase.CallArgs = string.Join(", ", validateCallArgs);
                validatePhase.UnsupportedParameters = validateUnsupported;
            }
        }

        var (handleCallArgs, handleUnsupported) = BuildCallArgs(handlePhase.Parameters, loadedByType, requestType, format);
        handlePhase.CallArgs = string.Join(", ", handleCallArgs);
        handlePhase.UnsupportedParameters = handleUnsupported;
    }

    private static (List<string> CallArgs, ImmutableArray<string> Unsupported) BuildCallArgs(
        ImmutableArray<IParameterSymbol> parameters,
        Dictionary<string, List<string>> loadedByType,
        string requestType,
        SymbolDisplayFormat format)
    {
        var callArgs = new List<string>();
        var seenLoadedTypeCount = new Dictionary<string, int>(StringComparer.Ordinal);
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

    private static void AddToLoadedByType(
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

    private static ImmutableArray<LoadedElement> EnrichWithNotFoundMessages(
        ImmutableArray<LoadedElement> elements,
        ImmutableArray<IParameterSymbol> parameters,
        SymbolDisplayFormat fmt)
    {
        var enriched = ImmutableArray.CreateBuilder<LoadedElement>();
        foreach (var element in elements)
        {
            if (!element.IsNullable)
            {
                enriched.Add(element);
                continue;
            }

            var message = GetRequiredMessage(parameters, element.FullType, fmt);
            enriched.Add(message is null ? element : element with { NotFoundMessage = message });
        }

        return enriched.ToImmutable();
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
