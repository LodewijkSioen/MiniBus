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



        var handleMethod = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(static m => m.MethodKind == MethodKind.Ordinary)
            .Where(static m => IsHandleMethodName(m.Name))
            .OrderBy(static m => m.Locations.FirstOrDefault(static l => l.IsInSource)?.SourceSpan.Start ?? int.MaxValue)
            .ThenBy(static m => m.Name, StringComparer.Ordinal)
            .FirstOrDefault(static m => m.Parameters.Length >= 1);
        if (handleMethod is null)
        {
            return new(null, EquatableArray<DiagnosticInfo>.Empty);
        }

        var preHandleMethods = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(static m => m.MethodKind == MethodKind.Ordinary)
            .Where(static m => IsPreHandleMethodName(m.Name))
            .OrderBy(static m => m.Locations.FirstOrDefault(static l => l.IsInSource)?.SourceSpan.Start ?? int.MaxValue)
            .ThenBy(static m => m.Name, StringComparer.Ordinal)
            .ToArray();

        if (!IsSupportedHandlerMethodReturnType(handleMethod))
        {
            return new(null, new([
                Diagnostics.UnsupportedMethodReturnType(
                    location: location,
                    handlerName: classSymbol.ToDisplayString(fmt),
                    returnType: handleMethod.ReturnType.ToDisplayString(fmt),
                    methodName: handleMethod.Name)
            ]));
        }

        if (HasInvalidHandleTupleResponse(handleMethod, fmt))
        {
            return new(null, new([
                Diagnostics.InvalidHandleTupleResponse(
                    location: location,
                    handlerName: classSymbol.ToDisplayString(fmt),
                    returnType: handleMethod.ReturnType.ToDisplayString(fmt))
            ]));
        }

        var unsupportedPreHandleMethodDiagnostics = preHandleMethods
            .Where(static m => !IsSupportedHandlerMethodReturnType(m))
            .Select(m => Diagnostics.UnsupportedMethodReturnType(
                location: location,
                handlerName: classSymbol.ToDisplayString(fmt),
                returnType: m.ReturnType.ToDisplayString(fmt),
                methodName: m.Name))
            .ToArray();
        if (unsupportedPreHandleMethodDiagnostics.Length > 0)
        {
            return new(null, new(unsupportedPreHandleMethodDiagnostics));
        }

        var handlePhase = new MethodPhase(PhaseType.Handle, handleMethod, fmt);
        var preHandleCandidates = preHandleMethods
            .Select(m => new MethodPhase(PhaseType.Before, m, fmt))
            .ToList();

        if (!TryOrderPhases(preHandleCandidates, handlePhase, out var orderedMethods, out var cycleMethods))
        {
            return new(null, new([
                Diagnostics.CyclicPhaseDependency(
                    location: location,
                    handlerName: classSymbol.ToDisplayString(fmt),
                    methodNames: cycleMethods!)
            ]));
        }

        


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

    private static bool TryOrderPhases(
        IEnumerable<MethodPhase> phases,
        MethodPhase handlePhase,
        out EquatableArray<MethodPhase> orderedPhases,
        out string? cycleMethodNames)
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

            if (ready.Count == 0)
            {
                cycleMethodNames = string.Join(", ",
                    pending
                        .OrderBy(candidate => candidate.SourceIndex)
                        .Select(candidate => candidate.Phase.MethodName));
                orderedPhases = EquatableArray<MethodPhase>.Empty;
                return false;
            }

            var next = ready[0];

            pending.Remove(next);
            ordered.Add(next.Phase);
        }

        ordered.Add(handlePhase);
        orderedPhases = new(ordered);
        cycleMethodNames = null;
        return true;
    }

    private static bool IsSupportedHandlerMethodReturnType(IMethodSymbol method)
    {
        var returnType = method.ReturnType;
        if (returnType.SpecialType == SpecialType.System_Void)
        {
            return false;
        }

        if (returnType is INamedTypeSymbol { Name: "Task", TypeArguments.Length: 0 })
        {
            return false;
        }

        return true;
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

    private static bool IsPreHandleMethodName(string methodName)
    {
        return methodName == "Load"
            || methodName == "LoadAsync"
            || methodName == "Validate"
            || methodName == "ValidateAsync"
            || methodName.StartsWith("Before", StringComparison.Ordinal)
            || methodName.EndsWith("Before", StringComparison.Ordinal)
            || methodName.EndsWith("BeforeAsync", StringComparison.Ordinal);
    }

    private static bool IsHandleMethodName(string methodName)
    {
        return methodName == "Handle"
            || methodName == "HandleAsync"
            || methodName == "Execute"
            || methodName == "ExecuteAsync";
    }

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

    private static bool HasInvalidHandleTupleResponse(IMethodSymbol method, SymbolDisplayFormat format)
    {
        var returnType = UnwrapTask(method.ReturnType);
        if (returnType is not INamedTypeSymbol { IsTupleType: true } tupleType)
        {
            return false;
        }

        if (tupleType.TupleElements.Length == 0)
        {
            return false;
        }

        return IsValidationResultType(tupleType.TupleElements[0].Type, format);
    }

    private static ITypeSymbol UnwrapTask(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol { Name: "Task", TypeArguments.Length: 1 } taskType)
        {
            return taskType.TypeArguments[0];
        }

        return returnType;
    }

    private static bool IsValidationResultType(ITypeSymbol type, SymbolDisplayFormat format)
    {
        var nonNullableType = type.NullableAnnotation == NullableAnnotation.Annotated
            ? type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            : type;
        return nonNullableType.ToDisplayString(format) == "global::MiniBus.ValidationResult";
    }
}
