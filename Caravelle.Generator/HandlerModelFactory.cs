using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Caravelle.Generator;

public sealed record Result(HandlerModel? Model, EquatableArray<DiagnosticInfo> Diagnostics);

public sealed record LocalVariable(string LocalName, string FullType, bool CheckNullability, string? IfNullErrorMessage);

public sealed record ResultValueType(string FullType, bool RequiresNullCheck);

public sealed record HandlerModel(
    string? Namespace,
    string ClassName,
    string FullClassName,
    string FullRequestType,
    string FullResponseType,
    EquatableArray<ResultValueType> ResultValueTypes,
    EquatableArray<MethodPhase> Phases,
    EquatableArray<LocalVariable> LocalVariables,
    EquatableArray<MethodPhase> FinallyPhases)
{
    // "global::TestApp.DummyHandler" + "Dispatcher" = "global::TestApp.DummyHandlerDispatcher"
    public string DispatcherFullName => FullClassName + "Dispatcher";
    public string DispatcherKey => $"{FullRequestType}|{FullResponseType}";
    public bool IsAnyAsync => Phases.Any(p => p.IsAsync) || FinallyPhases.Any(p => p.IsAsync);
    public bool HasInstanceMethods => Phases.Any(p => !p.IsStatic) || FinallyPhases.Any(p => !p.IsStatic);
    public bool HasFromServicesParameters => Phases.Any(p => p.Parameters.Any(ip => ip.IsFromServices)) || FinallyPhases.Any(p => p.Parameters.Any(ip => ip.IsFromServices));
    public bool HasSingleResultType => ResultValueTypes.Count == 1;
    public string ResultTypeName => HasSingleResultType ? ResultValueTypes[0].FullType : DispatcherFullName + ".Result";
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

        // Pre-handle/post-handle/finally methods are discovered across the base-type chain
        // (own class first, up to but excluding System.Object) so shared middleware logic
        // can live on a common base class and be reused across multiple [Handler] classes.
        var inheritanceChain = GetInheritanceChain(classSymbol);

        // Onion ordering: inherited Before-phase methods run before own-class ones when
        // there is no type dependency between them, so candidates are farthest-ancestor-first.
        var preHandleCandidates = CollectPhaseMethods(inheritanceChain, IsPreHandleMethodName)
            .OrderByDescending(static c => c.Depth)
            .ToArray();

        // Onion ordering: inherited After-phase methods run after own-class ones, so
        // candidates stay in the natural own-class-first order produced by CollectPhaseMethods.
        var postHandleCandidates = CollectPhaseMethods(inheritanceChain, IsPostHandleMethodName)
            .ToArray();

        // Finally: each class in the chain may contribute at most one Finally method (a
        // class can only declare one of "Finally"/"FinallyAsync"). All discovered Finally
        // methods run in the generated finally block, own class first and ancestors last —
        // mirroring try/finally stack-unwind order, consistent with the After onion ordering.
        var finallyCandidates = CollectFinallyMethods(inheritanceChain);

        var inaccessibleInheritedMethodDiagnostics = preHandleCandidates
            .Concat(postHandleCandidates)
            .Concat(finallyCandidates)
            .Where(static c => c.Depth > 0 && IsInaccessibleFromDispatcher(c.Method.DeclaredAccessibility))
            .Select(c => Diagnostics.InheritedMethodNotAccessible(
                location: location,
                handlerName: classSymbol.ToDisplayString(fmt),
                methodName: c.Method.Name,
                declaringTypeName: c.Method.ContainingType.ToDisplayString(fmt)))
            .ToArray();
        if (inaccessibleInheritedMethodDiagnostics.Length > 0)
        {
            return new(null, new(inaccessibleInheritedMethodDiagnostics));
        }

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

        var unsupportedPreHandleMethodDiagnostics = preHandleCandidates
            .Select(static c => c.Method)
            .Where(static m => !IsSupportedHandlerMethodReturnType(m))
            .Select(m => Diagnostics.UnsupportedMethodReturnType(
                location: location,
                handlerName: classSymbol.ToDisplayString(fmt),
                returnType: m.ReturnType.ToDisplayString(fmt),
                methodName: m.Name))
            .ToArray();
        var unsupportedPostHandleMethodDiagnostics = postHandleCandidates
            .Select(static c => c.Method)
            .Where(static m => !IsSupportedHandlerMethodReturnType(m))
            .Select(m => Diagnostics.UnsupportedMethodReturnType(
                location: location,
                handlerName: classSymbol.ToDisplayString(fmt),
                returnType: m.ReturnType.ToDisplayString(fmt),
                methodName: m.Name))
            .ToArray();
        var unsupportedFinallyMethodDiagnostics = finallyCandidates
            .Select(static c => c.Method)
            .Where(static m => !IsSupportedFinallyReturnType(m))
            .Select(m => Diagnostics.UnsupportedMethodReturnType(
                location: location,
                handlerName: classSymbol.ToDisplayString(fmt),
                returnType: m.ReturnType.ToDisplayString(fmt),
                methodName: m.Name))
            .ToArray();
        if (unsupportedPreHandleMethodDiagnostics.Length > 0 || unsupportedPostHandleMethodDiagnostics.Length > 0 || unsupportedFinallyMethodDiagnostics.Length > 0)
        {
            return new(null, new(unsupportedPreHandleMethodDiagnostics
                .Concat(unsupportedPostHandleMethodDiagnostics)
                .Concat(unsupportedFinallyMethodDiagnostics)
                .ToArray()));
        }

        var handlePhase = new MethodPhase(PhaseType.Handle, handleMethod, fmt);
        var preHandlePhaseCandidates = preHandleCandidates
            .Select(c => (Phase: new MethodPhase(PhaseType.Before, c.Method, fmt), c.Depth))
            .ToList();
        var postHandlePhaseCandidates = postHandleCandidates
            .Select(c => (Phase: new MethodPhase(PhaseType.After, c.Method, fmt), c.Depth))
            .ToList();

        if (!TryOrderPhases(preHandlePhaseCandidates.Select(static c => c.Phase), out var orderedPrePhases, out var cycleMethods))
        {
            return new(null, new([
                Diagnostics.CyclicPhaseDependency(
                    location: location,
                    handlerName: classSymbol.ToDisplayString(fmt),
                    methodNames: cycleMethods!)
            ]));
        }

        if (!TryOrderPhases(postHandlePhaseCandidates.Select(static c => c.Phase), out var orderedPostPhases, out cycleMethods))
        {
            return new(null, new([
                Diagnostics.CyclicPhaseDependency(
                    location: location,
                    handlerName: classSymbol.ToDisplayString(fmt),
                    methodNames: cycleMethods!)
            ]));
        }

        var orderedMethods = new EquatableArray<MethodPhase>(orderedPrePhases
            .Append(handlePhase)
            .Concat(orderedPostPhases));

        


        // Only the concrete handler class's own phases (never inherited ones) may define
        // the request type: Handle is never inherited, and restricting to depth-0
        // pre/post-handle phases here avoids an inherited, DI-only middleware method being
        // mistaken for the request-defining parameter.
        var ownClassPhases = new HashSet<MethodPhase>(preHandlePhaseCandidates
            .Where(static c => c.Depth == 0)
            .Select(static c => c.Phase)
            .Concat(postHandlePhaseCandidates
                .Where(static c => c.Depth == 0)
                .Select(static c => c.Phase))
            .Append(handlePhase));

        if (!InferRequestType(orderedMethods, ownClassPhases, out var requestType))
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

        var resultValueTypes = BuildResultValueTypes(handlePhase, orderedMethods, localVariables);

        var knownPipelineTypes = localVariables
            .Select(static local => local.FullType);

        orderedMethods = new(orderedMethods
            .Select(phase => MarkFromServices(phase, knownPipelineTypes)));

        // Build and validate Finally phases (one per class in the chain that declares one)
        var finallyPhases = finallyCandidates
            .Select(c => MarkFromServices(new MethodPhase(PhaseType.Finally, c.Method, fmt), knownPipelineTypes))
            .ToList();

        if (finallyPhases.Count > 0)
        {
            var pipelineReturnTypes = new HashSet<string>(orderedMethods
                .SelectMany(static phase => phase.Returns)
                .Select(static element => element.FullType),
                StringComparer.Ordinal);

            // Validate that Finally parameters matching pipeline returns are nullable (MBG010)
            var finallyDiagnostics = new List<DiagnosticInfo>();
            foreach (var finallyPhase in finallyPhases)
            {
                foreach (var parameter in finallyPhase.Parameters)
                {
                    if (!parameter.IsFromServices && pipelineReturnTypes.Contains(parameter.FullType) && !parameter.IsNullable)
                    {
                        finallyDiagnostics.Add(Diagnostics.FinallyParameterMustBeNullable(
                            location: location,
                            handlerName: classSymbol.ToDisplayString(fmt),
                            parameterName: parameter.LocalName,
                            parameterType: parameter.FullType));
                    }
                }
            }

            if (finallyDiagnostics.Count > 0)
            {
                return new(null, new(finallyDiagnostics.ToArray()));
            }
        }

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
                ResultValueTypes: resultValueTypes,
                Phases: orderedMethods,
                LocalVariables: localVariables,
                FinallyPhases: new(finallyPhases)),
            EquatableArray<DiagnosticInfo>.Empty);
    }

    private static EquatableArray<ResultValueType> BuildResultValueTypes(
        MethodPhase handlePhase,
        EquatableArray<MethodPhase> phases,
        EquatableArray<LocalVariable> localVariables)
    {
        var types = new List<ResultValueType>
        {
            new(handlePhase.Returns[0].FullType, handlePhase.Returns[0].RequiresNullCheck)
        };

        foreach (var returnType in phases
            .SelectMany(static phase => phase.Returns)
            .Where(static result => result.IsResultType)
            .Select(static result => new ResultValueType(result.FullType, result.RequiresNullCheck)))
        {
            if (!types.Any(type => type.FullType.Equals(returnType.FullType, StringComparison.Ordinal)))
            {
                types.Add(returnType);
            }
        }

        if (localVariables.Any(static local => local.CheckNullability)
            && !types.Any(type => type.FullType.Equals("global::Caravelle.NotFoundResult", StringComparison.Ordinal)))
        {
            types.Add(new("global::Caravelle.NotFoundResult", true));
        }

        return new(types);
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

    private static bool IsSupportedFinallyReturnType(IMethodSymbol method)
    {
        var returnType = method.ReturnType;
        
        // Finally supports void or non-generic Task only
        if (returnType.SpecialType == SpecialType.System_Void)
        {
            return true;
        }

        if (returnType is INamedTypeSymbol { Name: "Task", TypeArguments.Length: 0 })
        {
            return true;
        }

        return false;
    }

    private static List<INamedTypeSymbol> GetInheritanceChain(INamedTypeSymbol classSymbol)
    {
        var chain = new List<INamedTypeSymbol> { classSymbol };
        var current = classSymbol.BaseType;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            chain.Add(current);
            current = current.BaseType;
        }

        return chain;
    }

    private static List<(IMethodSymbol Method, int Depth)> CollectPhaseMethods(
        IReadOnlyList<INamedTypeSymbol> inheritanceChain,
        Func<string, bool> nameMatches)
    {
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<(IMethodSymbol Method, int Depth)>();

        for (var depth = 0; depth < inheritanceChain.Count; depth++)
        {
            var methods = inheritanceChain[depth].GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Ordinary)
                .Where(m => nameMatches(m.Name))
                .OrderBy(m => m.Locations.FirstOrDefault(l => l.IsInSource)?.SourceSpan.Start ?? int.MaxValue)
                .ThenBy(m => m.Name, StringComparer.Ordinal);

            // Dedupe across the chain by method name: the shallowest (most-derived)
            // declaration wins, matching real C# override/hiding semantics since
            // `_handler.MethodName(...)` can only ever bind to one implementation.
            foreach (var method in methods)
            {
                if (seenNames.Add(method.Name))
                {
                    result.Add((method, depth));
                }
            }
        }

        return result;
    }

    private static bool IsInaccessibleFromDispatcher(Accessibility accessibility) =>
        accessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal;

    private static List<(IMethodSymbol Method, int Depth)> CollectFinallyMethods(
        IReadOnlyList<INamedTypeSymbol> inheritanceChain)
    {
        var result = new List<(IMethodSymbol Method, int Depth)>();

        for (var depth = 0; depth < inheritanceChain.Count; depth++)
        {
            // A single class can only contribute one Finally method (it matches either
            // "Finally" or "FinallyAsync", not both), so pick at most one per depth.
            var method = inheritanceChain[depth].GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Ordinary)
                .Where(m => IsFinallyMethodName(m.Name))
                .OrderBy(m => m.Locations.FirstOrDefault(l => l.IsInSource)?.SourceSpan.Start ?? int.MaxValue)
                .ThenBy(m => m.Name, StringComparer.Ordinal)
                .FirstOrDefault();

            if (method is not null)
            {
                result.Add((method, depth));
            }
        }

        return result;
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

    private static bool IsPostHandleMethodName(string methodName)
    {
        return methodName.StartsWith("After", StringComparison.Ordinal)
            || methodName.StartsWith("Post", StringComparison.Ordinal);
    }

    private static bool IsFinallyMethodName(string methodName)
    {
        return methodName == "Finally"
            || methodName == "FinallyAsync";
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
        ISet<MethodPhase> ownClassPhases,
        out string? requestType)
    {
        var availableOutputs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var phase in orderedMethods)
        {
            // Only the concrete handler class's own phases may introduce the request type;
            // inherited phases still contribute their outputs to the availability set below,
            // but their parameters are never treated as the request-defining parameter.
            if (ownClassPhases.Contains(phase))
            {
                foreach (var inputType in phase.Parameters)
                {
                    if (!availableOutputs.Contains(inputType.FullType))
                    {
                        requestType = inputType.FullType;
                        return true;
                    }
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
    
}
