using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Security.Cryptography;
using System.Text;
using System.Linq;

namespace MiniBus.Generator;

[Generator]
public class HandlerGenerator : IIncrementalGenerator
{
    private const string HandlerAttributeFqn = "MiniBus.HandlerAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var results = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                HandlerAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => HandlerModelFactory.GetHandlerModel(ctx, ct));

        // One file per handler: dispatcher class + typed MiniBus extension method
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

            spc.AddSource("MiniBusRegistrations.g.cs", RegistrationsSourceBuilder.Build(valid, conflicting, excludedDispatcherPairs));
        });
    }

    private static string CreateDispatcherHintName(HandlerModel model)
    {
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(model.FullClassName));
        var chars = new char[8];
        for (var i = 0; i < 4; i++)
        {
            var b = hashBytes[i];
            chars[i * 2] = ToHexChar((b >> 4) & 0xF);
            chars[i * 2 + 1] = ToHexChar(b & 0xF);
        }

        var hash = new string(chars);
        return $"{model.ClassName}Dispatcher_{hash}.g.cs";
    }

    private static char ToHexChar(int value) =>
        (char)(value < 10 ? '0' + value : 'a' + (value - 10));
}
