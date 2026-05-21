using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MiniBus.Generator.Tests;

internal static class GeneratorTestHelper
{
    /// <summary>
    /// Compiles <paramref name="source"/> with MiniBus available,
    /// runs <see cref="HandlerGenerator"/> against it, and returns the generated
    /// source texts and any generator-emitted diagnostics.
    /// </summary>
    internal static (IReadOnlyList<string> GeneratedSources, IReadOnlyList<Diagnostic> Diagnostics) Run(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        // Collect all assemblies already loaded in this test process — this includes
        // System.Runtime, Task, etc. — then add MiniBus explicitly.
        var references = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        var path = typeof(HandlerAttribute).Assembly.Location;
        if (!references.Any(r => r.Display == path))
            references.Add(MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            "GeneratorTestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var generator = new HandlerGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);

        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out _,
            out var generatorDiagnostics
        );

        var runResult = driver.GetRunResult();
        var generatedSources = runResult.GeneratedTrees
            .Select(t => t.GetText().ToString())
            .ToArray();

        var diagnostics = runResult.Results
            .SelectMany(r => r.Diagnostics)
            .Concat(generatorDiagnostics)
            .ToArray();

        return (generatedSources, diagnostics);
    }

    /// <summary>
    /// Compiles <paramref name="source"/> with MiniBus available,
    /// runs <see cref="HandlerGenerator"/> against it, and returns the
    /// <see cref="GeneratorDriver"/> for use with Verify snapshot assertions.
    /// </summary>
    internal static GeneratorDriver RunDriver(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        var path = typeof(HandlerAttribute).Assembly.Location;
        if (!references.Any(r => r.Display == path))
            references.Add(MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            "GeneratorTestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var generator = new HandlerGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        return driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
    }
}
