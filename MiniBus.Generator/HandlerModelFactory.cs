using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MiniBus.Generator;

public sealed record Result(HandlerModel? Model, EquatableArray<DiagnosticInfo> Diagnostics);

public sealed record LocalVariable(string LocalName, string FullType, bool CheckNullability, string? IfNullErrorMessage);

public sealed record HandlerModel(
    string? Namespace,
    string ClassName,
    string FullClassName,
    string FullRequestType,
    string FullResponseType,
    EquatableArray<MethodPhase> Phases,
    EquatableArray<LocalVariable> LocalVariables)
{
    // "global::TestApp.DummyHandler" + "Dispatcher" = "global::TestApp.DummyHandlerDispatcher"
    public string DispatcherFullName => FullClassName + "Dispatcher";
    public string DispatcherKey => $"{FullRequestType}|{FullResponseType}";
    public bool IsAnyAsync => Phases.Any(p => p.IsAsync);
    public bool HasInstanceMethods => Phases.Any(p => !p.IsStatic);
    public bool HasFromServicesParameters => Phases.Any(p => p.Parameters.Any(ip => ip.IsFromServices));
}

public static class HandlerModelFactory
{
    public static Result GetHandlerModel(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol)
        {
            return new(null, EquatableArray<DiagnosticInfo>.Empty);
        }
        ct.ThrowIfCancellationRequested();

        return GetHandlerModel(classSymbol, ctx.TargetNode.GetLocation());
    }

    public static Result GetHandlerModel(INamedTypeSymbol classSymbol, Location location)
    { 
        var fmt = SymbolDisplayFormat.FullyQualifiedFormat;

        var isGenericHandler = classSymbol.Arity > 0 || HasGenericContainingType(classSymbol.ContainingType);
        var isNestedHandler = classSymbol.ContainingType is not null;

        if (isGenericHandler)
        {

            return new(null, new(
            [
                Diagnostics.GenericHandlerNotSupported(
                    location: location,
                    fullHandlerName: classSymbol.ToDisplayString(fmt))
            ]));
        }
        if (isNestedHandler)
        {
            return new(null, new(
            [
                Diagnostics.NestedHandlerNotSupported(
                    location: location,
                    fullHandlerName: classSymbol.ToDisplayString(fmt))
            ]));
        }



        var handleMethod = classSymbol.GetMembers("Handle")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static m => m.Parameters.Length >= 1);
        if (handleMethod is null)
        {
            return new(null, EquatableArray<DiagnosticInfo>.Empty);
        }

        var loadMethod = classSymbol.GetMembers("Load")
            .OfType<IMethodSymbol>()
            .FirstOrDefault();

        var validateMethod = classSymbol.GetMembers("Validate")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => IsSupportedValidateMethod(m, fmt));

        var handlePhase = new MethodPhase(PhaseType.Handle, handleMethod, fmt);
        var loadPhase = loadMethod is null ? null : new MethodPhase(PhaseType.Before, loadMethod, fmt);
        var validatePhase = validateMethod is null ? null : new MethodPhase(PhaseType.Before, validateMethod, fmt);

        var preHandleCandidates = new List<MethodPhase>();
        if (loadPhase is not null)
            preHandleCandidates.Add(loadPhase);
        if (validatePhase is not null)
            preHandleCandidates.Add(validatePhase);

        var orderedMethods = OrderPhases(preHandleCandidates, handlePhase);

        


        if (!InferRequestType(orderedMethods, out var requestType))
        {
            return new(null, new([
                Diagnostics.RequestTypeCannotBeInferred(
                    location: location,
                    fullHandlerName: classSymbol.ToDisplayString(fmt))
            ]));
        }

        var responseType = handlePhase.Returns[0].FullType;

        var returnVariables = BuildReturnVariables(orderedMethods);
        var localVariables = new EquatableArray<LocalVariable>(returnVariables
            .Append(new("request", requestType!, false, null)));
        if (!ValidateUniqueLocalVariableTypes(classSymbol.Name, location, localVariables, out var duplicateLocalTypeDiagnostics))
        {
            return new(null, new(duplicateLocalTypeDiagnostics));
        }

        var knownPipelineTypes = localVariables
            .Select(static local => local.FullType);

        orderedMethods = new(orderedMethods
            .Select(phase => MarkFromServices(phase, knownPipelineTypes)));

        var ns = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : classSymbol.ContainingNamespace.ToDisplayString();

        return new(
            new(
                Namespace: ns,
                ClassName: classSymbol.Name,
                FullClassName: classSymbol.ToDisplayString(fmt),
                FullRequestType: requestType!,
                FullResponseType: responseType,
                Phases: orderedMethods,
                LocalVariables: localVariables),
            EquatableArray<DiagnosticInfo>.Empty);
    }

    private static bool ValidateUniqueLocalVariableTypes(
        string handlerName,
        Location location,
        EquatableArray<LocalVariable> localVariables,
        out EquatableArray<DiagnosticInfo> diagnostics)
    {
        var duplicateTypeDiagnostics = localVariables
            .GroupBy(static local => local.FullType)
            .Where(static group => group.Count() > 1)
            .Select(group => Diagnostics.DuplicateLocalVariableType(location, handlerName, group.Key))
            .ToList();

        diagnostics = new(duplicateTypeDiagnostics);
        return duplicateTypeDiagnostics.Count == 0;
    }

    private static EquatableArray<MethodPhase> OrderPhases(
        IEnumerable<MethodPhase> phases, MethodPhase handlePhase)
    {
        var pending = phases
            .Select((phase, index) => new PendingPhase(phase, index))
            .ToList();
        var ordered =  new List<MethodPhase>();

        while (pending.Count > 0)
        {
            var ready = pending
                .Where(candidate => !pending.Any(other =>
                    !ReferenceEquals(candidate, other) && DependsOn(candidate.Phase, other.Phase)))
                .OrderBy(candidate => HasOutputs(candidate.Phase))
                //.ThenBy(candidate => candidate.Phase.TieBreak)
                .ThenBy(candidate => candidate.SourceIndex)
                .ToList();

            var next = ready.Count > 0
                ? ready[0]
                : pending
                    .OrderBy(candidate => HasOutputs(candidate.Phase))
                    //.ThenBy(candidate => candidate.Phase.TieBreak)
                    .ThenBy(candidate => candidate.SourceIndex)
                    .First();

            pending.Remove(next);
            ordered.Add(next.Phase);
        }

        ordered.Add(handlePhase);

        return new(ordered);
    }

    private static IEnumerable<LocalVariable> BuildReturnVariables(EquatableArray<MethodPhase> phases)
    {
        var allReturns = phases.SelectMany(p => p.Returns);
        var allParams = phases.SelectMany(p => p.Parameters).GroupBy(p => p.FullType).ToDictionary(
            p => p.Key, 
            p => new
            {
                IsNullable = p.All(n => n.IsNullable),
                p.FirstOrDefault(n => !string.IsNullOrEmpty(n.NotNullMessage))?.NotNullMessage
            });

        foreach (var returnValue in allReturns)
        {
            var checkNullability = false;
            string? ifNullErrorMessage = null;

            if (allParams.TryGetValue(returnValue.FullType, out var parameter))
            {
                checkNullability = returnValue.IsNullable && !parameter.IsNullable;
                ifNullErrorMessage = parameter.NotNullMessage;
            }

            yield return new(
                returnValue.NonNullLocalName,
                returnValue.FullType,
                checkNullability,
                ifNullErrorMessage);
        }
    }

    private static MethodPhase MarkFromServices(MethodPhase phase, IEnumerable<string> knownPipelineTypes)
    {
        return phase with
        {
            Parameters = new(phase.Parameters
                .Select(parameter => parameter with
                {
                    IsFromServices = !knownPipelineTypes.Contains(parameter.FullType)
                }))
        };
    }

    private static bool DependsOn(MethodPhase candidate, MethodPhase dependency)
    {
        return candidate.Parameters
            .Any(c => dependency.Returns.Any(d => d.FullType == c.FullType));
    }

    private static bool HasOutputs(MethodPhase phase) => phase.Returns.Count > 0;

    private sealed record PendingPhase(MethodPhase Phase, int SourceIndex);

    private static bool InferRequestType(
        IEnumerable<MethodPhase> orderedMethods,
        out string? requestType)
    {
        var availableOutputs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var phase in orderedMethods)
        {
            foreach (var inputType in phase.Parameters)
            {
                if (!availableOutputs.Contains(inputType.FullType))
                {
                    requestType = inputType.FullType;
                    return true;
                }
            }

            foreach (var outputType in phase.Returns)
                availableOutputs.Add(outputType.FullType);
        }

        requestType = null;
        return false;
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

    private static bool IsSupportedValidateMethod(IMethodSymbol method, SymbolDisplayFormat format)
    {
        var returnType = method.ReturnType;
        if (returnType is INamedTypeSymbol { Name: "Task", TypeArguments.Length: 1 } taskType)
        {
            returnType = taskType.TypeArguments[0];
        }

        return returnType.ToDisplayString(format) == "global::MiniBus.ValidationResult";
    }
}
