using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace MiniBus.Generator;

public sealed record LocalVariable(string LocalName, string FullType, bool CheckNullability, string? IfNullErrorMessage);

public sealed record HandlerModel(
    string? Namespace,
    string ClassName,
    string FullClassName,
    string FullRequestType,
    string FullResponseType,
    ImmutableArray<MethodPhase> Phases,
    ImmutableArray<LocalVariable> LocalVariables,
    Location Location)
{
    // "global::TestApp.DummyHandler" + "Dispatcher" = "global::TestApp.DummyHandlerDispatcher"
    public string DispatcherFullName => FullClassName + "Dispatcher";
    public string DispatcherKey => $"{FullRequestType}|{FullResponseType}";
    public bool IsAnyAsync => Phases.Any(p => p.IsAsync);
    //public bool HasUnsupportedParameters => Phases.Any.HasUnsupportedParameters;
}

public static class HandlerModelFactory
{
    public static (HandlerModel? model, IEnumerable<Diagnostic> diagnostics) GetHandlerModel(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol)
        {
            return (null, []);
        }
        ct.ThrowIfCancellationRequested();
        
        TryGetHandlerModel(classSymbol, ctx.TargetNode.GetLocation(), out var handlerModel, out var diagnostics);
        return (handlerModel, diagnostics);
    }

    public static bool TryGetHandlerModel(INamedTypeSymbol classSymbol, Location location, out HandlerModel? handlerModel, out IEnumerable<Diagnostic> diagnostics)
    { 
        var fmt = SymbolDisplayFormat.FullyQualifiedFormat;

        var isGenericHandler = classSymbol.Arity > 0 || HasGenericContainingType(classSymbol.ContainingType);
        var isNestedHandler = classSymbol.ContainingType is not null;

        if (isGenericHandler)
        {
            handlerModel = null;
            diagnostics =
            [
                Diagnostics.GenericHandlerNotSupported(
                    location: location,
                    fullHandlerName: classSymbol.ToDisplayString(fmt))
            ];
            return false;
        }
        if (isNestedHandler)
        {
            handlerModel = null;
            diagnostics =
            [
                Diagnostics.NestedHandlerNotSupported(
                    location: location,
                    fullHandlerName: classSymbol.ToDisplayString(fmt))
            ];
            return false;
        }



        var handleMethod = classSymbol.GetMembers("Handle")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static m => !m.IsStatic && m.Parameters.Length >= 1);
        if (handleMethod is null)
        {
            handlerModel = null;
            diagnostics = [];
            return false;
        }

        var loadMethod = classSymbol.GetMembers("Load")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static m => !m.IsStatic);

        var validateMethod = classSymbol.GetMembers("Validate")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => !m.IsStatic && IsSupportedValidateMethod(m, fmt));

        var handlePhase = new MethodPhase(PhaseType.Handle, handleMethod, fmt);
        var loadPhase = loadMethod is null ? null : new MethodPhase(PhaseType.Before, loadMethod, fmt);
        var validatePhase = validateMethod is null ? null : new MethodPhase(PhaseType.Before, validateMethod, fmt);

        var preHandleCandidates = ImmutableArray.CreateBuilder<MethodPhase>();
        if (loadPhase is not null)
            preHandleCandidates.Add(loadPhase);
        if (validatePhase is not null)
            preHandleCandidates.Add(validatePhase);

        var orderedMethods = OrderPhases(preHandleCandidates.ToImmutable(), handlePhase);

        


        if (!InferRequestType(orderedMethods, out var requestType))
        {
            diagnostics =
            [
                Diagnostics.RequestTypeCannotBeInferred(
                    location: location,
                    fullHandlerName: classSymbol.ToDisplayString(fmt))];
            handlerModel = null;
            return false;
        }

        var responseType = handlePhase.Returns[0].FullType;

        var returnVariables = BuildReturnVariables(orderedMethods);
        var localVariables = ImmutableArray.Create<LocalVariable>([
            ..returnVariables,
            new("request", requestType!, false, null)
        ]);
        if (!ValidateUniqueLocalVariableTypes(classSymbol.Name, location, localVariables, out var duplicateLocalTypeDiagnostics))
        {
            diagnostics = [..duplicateLocalTypeDiagnostics];
            handlerModel = null;
            return false;
        }

        if (!ValidateVariables(classSymbol.Name, location, localVariables, orderedMethods, out var invalidVariables))
        {
            diagnostics = [..invalidVariables];
            handlerModel = null;
            return false;
        }

        var ns = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : classSymbol.ContainingNamespace.ToDisplayString();


        diagnostics = [];
        handlerModel = new(
            Namespace: ns,
            ClassName: classSymbol.Name,
            FullClassName: classSymbol.ToDisplayString(fmt),
            FullRequestType: requestType!,
            FullResponseType: responseType,
            Phases: orderedMethods,
            LocalVariables: localVariables,
            Location: location);
        return true;
    }

    private static bool ValidateVariables(string handlerName, Location location, ImmutableArray<LocalVariable> localVariables, ImmutableArray<MethodPhase> phases, out IEnumerable<Diagnostic> diagnostics)
    {
        var diag = new List<Diagnostic>();
        foreach (var phase in phases)
        {
            foreach (var parameter in phase.Parameters)
            {
                if (!localVariables.Any(l => l.FullType == parameter.FullType))
                {
                    diag.Add(Diagnostics.UnsupportedParameter(location, handlerName, parameter.LocalName, phase.MethodName, parameter.FullType));
                }
            }
        }

        diagnostics = diag;
        return !diagnostics.Any();
    }

    private static bool ValidateUniqueLocalVariableTypes(
        string handlerName,
        Location location,
        ImmutableArray<LocalVariable> localVariables,
        out IEnumerable<Diagnostic> diagnostics)
    {
        var duplicateTypeDiagnostics = localVariables
            .GroupBy(static local => local.FullType)
            .Where(static group => group.Count() > 1)
            .Select(group => Diagnostics.DuplicateLocalVariableType(location, handlerName, group.Key))
            .ToList();

        diagnostics = duplicateTypeDiagnostics;
        return duplicateTypeDiagnostics.Count == 0;
    }

    private static ImmutableArray<MethodPhase> OrderPhases(
        ImmutableArray<MethodPhase> phases, MethodPhase handlePhase)
    {
        var pending = phases
            .Select((phase, index) => new PendingPhase(phase, index))
            .ToList();
        var ordered =  ImmutableArray.CreateBuilder<MethodPhase>(pending.Count + 1);

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

        return ordered.ToImmutable();
    }

    private static IEnumerable<LocalVariable> BuildReturnVariables(ImmutableArray<MethodPhase> phases)
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

        // TODO
        //foreach (var phase in model.Phases)
        //{
        //    foreach (var unsupported in phase.UnsupportedParameters)
        //        spc.ReportDiagnostic(Diagnostics.UnsupportedParameter(
        //            location: model.Location,
        //            handlerName: model.ClassName,
        //            parameterNameAndType: unsupported,
        //            methodName: phase.MethodName,
        //            requestType: model.FullRequestType));
        //}
    }

    private static bool DependsOn(MethodPhase candidate, MethodPhase dependency)
    {
        return candidate.Parameters.Any(c => dependency.Returns.Any(d => d.FullType == c.FullType));
    }

    private static bool HasOutputs(MethodPhase phase) => !phase.Returns.IsDefaultOrEmpty;

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
