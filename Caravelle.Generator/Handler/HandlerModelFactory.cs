using Caravelle.Generator.Middleware;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Caravelle.Generator.Handler;

public static class HandlerModelFactory
{
    public static Result GetHandlerModel(GeneratorAttributeSyntaxContext ctx, EquatableArray<MiddlewareModel> allMiddleware, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol)
        {
            return new(null, EquatableArray<DiagnosticInfo>.Empty);
        }
        ct.ThrowIfCancellationRequested();

        return GetHandlerModel(classSymbol, allMiddleware, ctx.TargetNode.GetLocation());
    }

    public static Result GetHandlerModel(INamedTypeSymbol classSymbol, Location location) =>
        GetHandlerModel(classSymbol, EquatableArray<MiddlewareModel>.Empty, location);

    public static Result GetHandlerModel(INamedTypeSymbol classSymbol, EquatableArray<MiddlewareModel> allMiddleware, Location location)
    { 
        var fmt = SymbolDisplayFormat.FullyQualifiedFormat;

        var isGenericHandler = classSymbol.Arity > 0 || Helpers.HasGenericContainingType(classSymbol.ContainingType);
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
        var inheritanceChain = Helpers.GetInheritanceChain(classSymbol);

        // Onion ordering: inherited Before-phase methods run before own-class ones when
        // there is no type dependency between them, so candidates are farthest-ancestor-first.
        var preHandleCandidates = Helpers.CollectPhaseMethods(inheritanceChain, Helpers.IsPreHandleMethodName)
            .OrderByDescending(static c => c.Depth)
            .ToArray();

        // Onion ordering: inherited After-phase methods run after own-class ones, so
        // candidates stay in the natural own-class-first order produced by CollectPhaseMethods.
        var postHandleCandidates = Helpers.CollectPhaseMethods(inheritanceChain, Helpers.IsPostHandleMethodName)
            .ToArray();

        // Finally: each class in the chain may contribute at most one Finally method (a
        // class can only declare one of "Finally"/"FinallyAsync"). All discovered Finally
        // methods run in the generated finally block, own class first and ancestors last —
        // mirroring try/finally stack-unwind order, consistent with the After onion ordering.
        var finallyCandidates = Helpers.CollectFinallyMethods(inheritanceChain);

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

        // Before/after-handle methods may return void, a non-generic Task, or a value —
        // only the Handle entry method itself is required to produce a response value.
        var unsupportedFinallyMethodDiagnostics = finallyCandidates
            .Select(static c => c.Method)
            .Where(static m => !Helpers.IsSupportedFinallyReturnType(m))
            .Select(m => Diagnostics.UnsupportedMethodReturnType(
                location: location,
                handlerName: classSymbol.ToDisplayString(fmt),
                returnType: m.ReturnType.ToDisplayString(fmt),
                methodName: m.Name))
            .ToArray();
        if (unsupportedFinallyMethodDiagnostics.Length > 0)
        {
            return new(null, new(unsupportedFinallyMethodDiagnostics));
        }

        var handlePhase = new MethodPhase(PhaseType.Handle, handleMethod, fmt);
        var preHandlePhaseCandidates = preHandleCandidates
            .Select(c => (Phase: new MethodPhase(PhaseType.Before, c.Method, fmt), c.Depth))
            .ToList();
        var postHandlePhaseCandidates = postHandleCandidates
            .Select(c => (Phase: new MethodPhase(PhaseType.After, c.Method, fmt), c.Depth))
            .ToList();
        var finallyPhaseList = finallyCandidates
            .Select(c => new MethodPhase(PhaseType.Finally, c.Method, fmt))
            .ToList();

        // Only the concrete handler class's own phases (never inherited or middleware ones)
        // may define the request type: Handle is never inherited, and restricting to depth-0
        // pre/post-handle phases here avoids an inherited/middleware, DI-only method being
        // mistaken for the request-defining parameter.
        var ownClassPhases = new HashSet<MethodPhase>(preHandlePhaseCandidates
            .Where(static c => c.Depth == 0)
            .Select(static c => c.Phase)
            .Concat(postHandlePhaseCandidates
                .Where(static c => c.Depth == 0)
                .Select(static c => c.Phase))
            .Append(handlePhase));

        // Type-closure index for assignability-based middleware filters (ForReturnType,
        // ForRequestType, ForVariable), built once from the handler's own + inherited raw
        // method symbols since those are the only ones still available as live symbols at
        // this point. Types introduced only by an already-matched middleware are not in this
        // index and fall back to exact-string matching in BuildMatchSnapshot — a known,
        // documented limitation of cross-middleware assignability matching.
        var typeClosureIndex = MethodPhase.BuildTypeClosureIndex(
            new[] { handleMethod }
                .Concat(preHandleCandidates.Select(static c => c.Method))
                .Concat(postHandleCandidates.Select(static c => c.Method))
                .Concat(finallyCandidates.Select(static c => c.Method)),
            fmt);

        // ── Fixed-point middleware matching ──────────────────────────────────────
        // Each pass evaluates every not-yet-matched middleware's filter against a snapshot
        // of the handler's currently-merged pipeline (own class + inherited base classes +
        // any middleware matched in an earlier pass), and merges all newly-matching
        // middleware at once (batched, not one at a time) so the result is deterministic
        // regardless of discovery order. The merged set only ever grows, so this converges
        // within allMiddleware.Count passes; the iteration cap is a defensive backstop.
        var matchedMiddleware = new List<MiddlewareModel>();
        var remainingMiddleware = new List<MiddlewareModel>(allMiddleware);
        var maxIterations = allMiddleware.Count + 1;
        var exceededIterationCap = false;

        for (var iteration = 0; remainingMiddleware.Count > 0; iteration++)
        {
            if (iteration >= maxIterations)
            {
                exceededIterationCap = true;
                break;
            }

            var snapshot = BuildMatchSnapshot(
                classSymbol, fmt, handlePhase, preHandlePhaseCandidates, postHandlePhaseCandidates, matchedMiddleware, typeClosureIndex);

            var newlyMatched = remainingMiddleware
                .Where(m => m.Filters.Any(f => MiddlewareFilterMatcher.Matches(f, snapshot)))
                .ToList();

            if (newlyMatched.Count == 0)
            {
                break;
            }

            matchedMiddleware.AddRange(newlyMatched);
            remainingMiddleware.RemoveAll(newlyMatched.Contains);
        }

        // Fold matched middleware phases in deterministically (by class name, independent of
        // which pass matched them). Before-phases are prepended — middleware is the outermost
        // onion layer and runs first when untied by a type dependency; After/Finally phases
        // are appended — middleware runs last, mirroring the existing "ancestors last"
        // Finally/After onion convention for inherited base-class phases.
        var orderedMatchedMiddleware = matchedMiddleware
            .OrderBy(static m => m.FullClassName, StringComparer.Ordinal)
            .ToList();

        var combinedPreForOrdering = orderedMatchedMiddleware
            .SelectMany(static m => m.PreHandlePhases.Select(p => p with { OwnerTypeFullName = m.FullClassName }))
            .Concat(preHandlePhaseCandidates.Select(static c => c.Phase))
            .ToList();
        var combinedPostForOrdering = postHandlePhaseCandidates.Select(static c => c.Phase)
            .Concat(orderedMatchedMiddleware.SelectMany(static m => m.PostHandlePhases.Select(p => p with { OwnerTypeFullName = m.FullClassName })))
            .ToList();
        finallyPhaseList.AddRange(orderedMatchedMiddleware.SelectMany(static m => m.FinallyPhases.Select(p => p with { OwnerTypeFullName = m.FullClassName })));

        if (!TryOrderPhases(combinedPreForOrdering, out var orderedPrePhases, out var cycleMethods))
        {
            return new(null, new([
                Diagnostics.CyclicPhaseDependency(
                    location: location,
                    handlerName: classSymbol.ToDisplayString(fmt),
                    methodNames: cycleMethods!)
            ]));
        }

        if (!TryOrderPhases(combinedPostForOrdering, out var orderedPostPhases, out cycleMethods))
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

        // Build and validate Finally phases (one per class in the chain that declares one,
        // plus any matched middleware's Finally phase)
        var finallyPhases = finallyPhaseList
            .Select(phase => MarkFromServices(phase, knownPipelineTypes))
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

        var resultDiagnostics = exceededIterationCap
            ? new EquatableArray<DiagnosticInfo>([
                Diagnostics.MiddlewareResolutionDidNotConverge(location, classSymbol.ToDisplayString(fmt))
            ])
            : EquatableArray<DiagnosticInfo>.Empty;

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
                FinallyPhases: new(finallyPhases),
                MatchedMiddlewareClassNames: new(orderedMatchedMiddleware.Select(static m => m.FullClassName))),
            resultDiagnostics);
    }

    /// <summary>
    /// Builds a best-effort snapshot of the handler's currently-merged pipeline shape for
    /// middleware filter matching. Request-type inference here is a lighter-weight
    /// approximation of <see cref="InferRequestType"/> (it doesn't require a fully
    /// dependency-ordered phase list), and HasNotFound is approximated as "any nullable
    /// return element" rather than the precise nullable-vs-claimed-by-parameter logic used
    /// for code generation — both are reasonable approximations for deciding whether a
    /// middleware filter applies, and the authoritative values are computed once more,
    /// precisely, after the fixed point converges.
    /// </summary>
    private static MiddlewareMatchContext BuildMatchSnapshot(
        INamedTypeSymbol classSymbol,
        SymbolDisplayFormat fmt,
        MethodPhase handlePhase,
        List<(MethodPhase Phase, int Depth)> preHandlePhaseCandidates,
        List<(MethodPhase Phase, int Depth)> postHandlePhaseCandidates,
        List<MiddlewareModel> matchedMiddleware,
        Dictionary<string, HashSet<string>> typeClosureIndex)
    {
        HashSet<string> GetClosure(string typeName) =>
            typeClosureIndex.TryGetValue(typeName, out var closure)
                ? closure
                : new HashSet<string>(StringComparer.Ordinal) { typeName };

        var ownAndInheritedPhases = preHandlePhaseCandidates.Select(static c => c.Phase)
            .Append(handlePhase)
            .Concat(postHandlePhaseCandidates.Select(static c => c.Phase))
            .ToList();

        var middlewarePhases = matchedMiddleware
            .SelectMany(static m => m.PreHandlePhases.Concat(m.PostHandlePhases))
            .ToList();

        // Best-effort request type: the first parameter (in declaration order across
        // pre-handle, handle, then post-handle phases) whose type isn't already produced as
        // a return by an earlier phase. Middleware phases aren't considered here since only
        // the handler's own phases may define the request type (mirrors InferRequestType).
        var produced = new HashSet<string>(StringComparer.Ordinal);
        string? requestType = null;
        foreach (var phase in ownAndInheritedPhases)
        {
            if (requestType is null)
            {
                foreach (var parameter in phase.Parameters)
                {
                    if (!produced.Contains(parameter.FullType))
                    {
                        requestType = parameter.FullType;
                        break;
                    }
                }
            }

            foreach (var returnElement in phase.Returns)
                produced.Add(returnElement.FullType);
        }

        var returnClosure = new HashSet<string>(StringComparer.Ordinal);
        returnClosure.UnionWith(GetClosure(handlePhase.Returns[0].FullType));

        var variableClosure = new HashSet<string>(StringComparer.Ordinal);
        var hasValidation = false;
        var hasNotFound = false;

        foreach (var element in ownAndInheritedPhases.SelectMany(static p => p.Returns).Concat(middlewarePhases.SelectMany(static p => p.Returns)))
        {
            variableClosure.UnionWith(GetClosure(element.FullType));

            if (element.IsResultType)
            {
                hasValidation = true;
                returnClosure.UnionWith(GetClosure(element.FullType));
            }

            if (element.IsNullable)
            {
                hasNotFound = true;
            }
        }

        if (hasNotFound)
        {
            returnClosure.Add("global::Caravelle.NotFoundResult");
            returnClosure.Add("global::Caravelle.IValidationResult");
        }

        var requestClosure = requestType is not null
            ? GetClosure(requestType)
            : new HashSet<string>(StringComparer.Ordinal);
        if (requestType is not null)
        {
            variableClosure.UnionWith(requestClosure);
        }

        return new MiddlewareMatchContext(classSymbol, fmt, returnClosure, requestClosure, variableClosure, hasValidation, hasNotFound);
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

    internal static bool IsSupportedHandlerMethodReturnType(IMethodSymbol method)
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

    private static bool IsInaccessibleFromDispatcher(Accessibility accessibility) =>
        accessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal;

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

    private static bool IsHandleMethodName(string methodName)
    {
        return methodName 
            is "Handle" 
            or "HandleAsync" 
            or "Execute" 
            or "ExecuteAsync";
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
}
