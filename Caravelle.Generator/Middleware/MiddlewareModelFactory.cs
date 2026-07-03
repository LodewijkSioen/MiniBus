using Caravelle.Generator.Handler;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace Caravelle.Generator.Middleware;

public static class MiddlewareModelFactory
{
    public static MiddlewareResult GetMiddlewareModel(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol)
        {
            return new(null, EquatableArray<DiagnosticInfo>.Empty);
        }
        ct.ThrowIfCancellationRequested();

        return GetMiddlewareModel(classSymbol, ctx.Attributes, ctx.TargetNode.GetLocation());
    }

    public static MiddlewareResult GetMiddlewareModel(
        INamedTypeSymbol classSymbol,
        ImmutableArray<AttributeData> middlewareAttributes,
        Location location)
    {
        var fmt = SymbolDisplayFormat.FullyQualifiedFormat;

        var isGenericMiddleware = classSymbol.Arity > 0 || Helpers.HasGenericContainingType(classSymbol.ContainingType);
        var isNestedMiddleware = classSymbol.ContainingType is not null;

        if (isGenericMiddleware)
        {
            return new(null, new(
            [
                Diagnostics.GenericMiddlewareNotSupported(
                    location: location,
                    fullMiddlewareName: classSymbol.ToDisplayString(fmt))
            ]));
        }
        if (isNestedMiddleware)
        {
            return new(null, new(
            [
                Diagnostics.NestedMiddlewareNotSupported(
                    location: location,
                    fullMiddlewareName: classSymbol.ToDisplayString(fmt))
            ]));
        }

        var inheritanceChain = Helpers.GetInheritanceChain(classSymbol);

        var preHandleCandidates = Helpers.CollectPhaseMethods(inheritanceChain, Helpers.IsPreHandleMethodName)
            .OrderByDescending(static c => c.Depth)
            .ToArray();
        var postHandleCandidates = Helpers.CollectPhaseMethods(inheritanceChain, Helpers.IsPostHandleMethodName)
            .ToArray();
        var finallyCandidates = Helpers.CollectFinallyMethods(inheritanceChain);

        // Before/after-handle middleware methods may return void, a non-generic Task, or a
        // value — only a handler's own Handle entry method is required to produce a response
        // value. Finally methods keep the stricter void/Task-only rule (MBG007), matching
        // handler Finally methods.
        var unsupportedReturnTypeDiagnostics = finallyCandidates.Select(static c => c.Method)
            .Where(static m => !Helpers.IsSupportedFinallyReturnType(m))
            .Select(m => Diagnostics.UnsupportedMethodReturnType(
                location: location,
                handlerName: classSymbol.ToDisplayString(fmt),
                returnType: m.ReturnType.ToDisplayString(fmt),
                methodName: m.Name))
            .ToArray();
        if (unsupportedReturnTypeDiagnostics.Length > 0)
        {
            return new(null, new(unsupportedReturnTypeDiagnostics));
        }

        var preHandlePhases = preHandleCandidates
            .Select(c => new MethodPhase(PhaseType.Before, c.Method, fmt))
            .ToArray();

        var postHandlePhases = postHandleCandidates
            .Select(c => new MethodPhase(PhaseType.After, c.Method, fmt))
            .ToArray();

        var finallyPhases = finallyCandidates
            .Select(c => new MethodPhase(PhaseType.Finally, c.Method, fmt))
            .ToArray();

        var diagnostics = new List<DiagnosticInfo>();
        var filters = new List<MiddlewareFilterDescriptor>();

        foreach (var attribute in middlewareAttributes)
        {
            if (attribute.AttributeClass is not { TypeArguments.Length: 1 } attributeClass)
            {
                continue;
            }

            var unrecognized = new List<string>();
            var descriptor = ParseFilter(attributeClass.TypeArguments[0], fmt, unrecognized);
            filters.Add(descriptor);

            foreach (var unrecognizedTypeName in unrecognized)
            {
                diagnostics.Add(Diagnostics.UnrecognizedMiddlewareFilter(
                    location: location,
                    fullMiddlewareName: classSymbol.ToDisplayString(fmt),
                    filterTypeName: unrecognizedTypeName));
            }
        }

        var ns = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : classSymbol.ContainingNamespace.ToDisplayString();

        var model = new MiddlewareModel(
            Namespace: ns,
            ClassName: classSymbol.Name,
            FullClassName: classSymbol.ToDisplayString(fmt),
            PreHandlePhases: new(preHandlePhases),
            PostHandlePhases: new(postHandlePhases),
            FinallyPhases: new(finallyPhases),
            Filters: new(filters));

        return new(model, new(diagnostics));
    }

    /// <summary>
    /// Aggregates all discovered <c>[Middleware&lt;TFilter&gt;]</c> results, de-duplicating
    /// by class (a partial class could otherwise be discovered once per declaration) and
    /// de-duplicating diagnostics.
    /// </summary>
    public static (EquatableArray<MiddlewareModel> Models, EquatableArray<DiagnosticInfo> Diagnostics) Merge(
        IEnumerable<MiddlewareResult> results)
    {
        var modelsByClass = new Dictionary<string, MiddlewareModel>(StringComparer.Ordinal);
        var diagnostics = new List<DiagnosticInfo>();

        foreach (var result in results)
        {
            foreach (var diagnostic in result.Diagnostics)
            {
                if (!diagnostics.Contains(diagnostic))
                {
                    diagnostics.Add(diagnostic);
                }
            }

            if (result.Model is { } model)
            {
                modelsByClass[model.FullClassName] = model;
            }
        }

        return (
            new(modelsByClass.Values.OrderBy(static m => m.FullClassName, StringComparer.Ordinal).ToArray()),
            new(diagnostics));
    }

    private static MiddlewareFilterDescriptor ParseFilter(ITypeSymbol filterType, SymbolDisplayFormat fmt, List<string> unrecognized)
    {
        if (filterType is not INamedTypeSymbol named || !IsRecognizedFilterType(named, out var definition))
        {
            var typeName = filterType.ToDisplayString(fmt);
            unrecognized.Add(typeName);
            return new(MiddlewareFilterRegistry.UnrecognizedKind, typeName);
        }

        return definition!.Parse(named, fmt);
    }

    // Restricted to types declared directly in the Caravelle namespace so a user's own
    // unrelated "ForInterface" type elsewhere is never mistaken for a recognized filter shape.
    private static bool IsRecognizedFilterType(INamedTypeSymbol named, out IMiddlewareFilterDefinition? definition)
    {
        definition = null;

        if (named.ContainingType is not null)
        {
            return false;
        }

        if (named.ContainingNamespace is not { IsGlobalNamespace: false } ns
            || ns.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != "global::Caravelle")
        {
            return false;
        }

        return MiddlewareFilterRegistry.TryGetByMetadataName(named.MetadataName, out definition);
    }
}
