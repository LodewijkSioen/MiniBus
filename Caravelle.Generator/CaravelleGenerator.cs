using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using Caravelle.Generator.Middleware;
using Caravelle.Generator.SourceBuilders;
using Caravelle.Generator.Handler;

namespace Caravelle.Generator;

[Generator]
public class CaravelleGenerator : IIncrementalGenerator
{
    private const string HandlerAttributeFqn = "Caravelle.HandlerAttribute";
    private const string MiddlewareAttributeFqn = "Caravelle.MiddlewareAttribute`1";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var allMiddleware = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                MiddlewareAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => MiddlewareModelFactory.GetMiddlewareModel(ctx, ct))
            .Collect()
            .Select(static (results, _) => MiddlewareModelFactory.Merge(results));

        var results = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                HandlerAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => ctx)
            .Combine(allMiddleware)
            .Select(static (pair, ct) => HandlerModelFactory.GetHandlerModel(pair.Left, pair.Right.Models, ct));

        // No source is emitted for middleware classes themselves yet — discovery-time
        // diagnostics (generic/nested middleware, unrecognized filters) are reported here;
        // matching middleware phases into handler dispatchers happens in a later phase.
        context.RegisterSourceOutput(allMiddleware, static (spc, result) =>
        {
            foreach (var diagnostic in result.Diagnostics)
                spc.ReportDiagnostic(diagnostic.ToDiagnostic());
        });

        // A middleware that never matched any handler across the whole compilation is
        // almost certainly a mistake (typo'd filter, moved/renamed target type, etc.) —
        // warn once per unmatched middleware (MBG014).
        context.RegisterSourceOutput(results.Collect().Combine(allMiddleware), static (spc, pair) =>
        {
            var matchedNames = new System.Collections.Generic.HashSet<string>(
                pair.Left
                    .Where(static r => r.Model is not null)
                    .SelectMany(static r => r.Model!.MatchedMiddlewareClassNames),
                System.StringComparer.Ordinal);

            foreach (var middleware in pair.Right.Models)
            {
                if (!matchedNames.Contains(middleware.FullClassName))
                {
                    spc.ReportDiagnostic(Diagnostics.MiddlewareMatchedNoHandlers(
                        location: Location.None,
                        fullMiddlewareName: middleware.FullClassName).ToDiagnostic());
                }
            }
        });

        // One file per handler: dispatcher class + typed Caravelle extension method
        context.RegisterSourceOutput(results, static (spc, result) =>
        {
            foreach (var diagnostic in result.Diagnostics)
                spc.ReportDiagnostic(diagnostic.ToDiagnostic());
            if (result.Model is not {  } model)
            {
                return;
            }

            spc.AddSource(CreateDispatcherHintName(model), DispatcherSourceBuilder.Build(model));
        });

        // One file for all handlers: AddGeneratedHandlers() DI registration
        context.RegisterSourceOutput(results.Collect(), static (spc, result) =>
        {
            var valid = result
                .Where(static m => m.Model is not null)
                .Select(static m => m.Model!)
                .OrderBy(static m => m.FullClassName, System.StringComparer.Ordinal)
                .ToArray();

            // Detect handlers that share a request type — extension methods would collide (CS0111)
            var conflicting = new System.Collections.Generic.HashSet<string>();
            foreach (var group in valid
                .GroupBy(static m => m.FullRequestType)
                .Where(static g => g.Count() > 1)
                .OrderBy(static g => g.Key, System.StringComparer.Ordinal))
            {
                conflicting.Add(group.Key);
                foreach (var m in group.OrderBy(static m => m.FullClassName, System.StringComparer.Ordinal))
                    spc.ReportDiagnostic(Diagnostics.DuplicateRequestType(
                        location: Location.None,
                        handlerName: m.ClassName,
                        requestType: m.FullRequestType).ToDiagnostic());
            }

            var excludedDispatcherPairs = new System.Collections.Generic.HashSet<string>();
            foreach (var group in valid
                .GroupBy(static m => m.DispatcherKey)
                .Where(static g => g.Count() > 1)
                .OrderBy(static g => g.Key, System.StringComparer.Ordinal))
            {
                excludedDispatcherPairs.Add(group.Key);
                foreach (var m in group.OrderBy(static m => m.FullClassName, System.StringComparer.Ordinal))
                    spc.ReportDiagnostic(Diagnostics.DuplicateRequestResponsePair(
                        location: Location.None,
                        handlerName: m.ClassName,
                        requestType: m.FullRequestType,
                        responseType: m.FullResponseType).ToDiagnostic());
            }

            spc.AddSource("CaravelleRegistrations.g.cs", RegistrationsSourceBuilder.Build(valid, conflicting, excludedDispatcherPairs));
        });
    }

    private static string CreateDispatcherHintName(HandlerModel model)
    {
        return $"{model.ClassName}Dispatcher_{Helpers.GetHashForTypeName(model.FullClassName)}.g.cs";
    }
}
