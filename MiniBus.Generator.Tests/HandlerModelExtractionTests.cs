using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MiniBus.Generator.Tests;

[TestFixture]
public class HandlerModelExtractionTests
{
    // ── Helper ────────────────────────────────────────────────────────────

    private static INamedTypeSymbol GetSymbol(string source, string className)
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
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var classSyntax = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == className);

        return (INamedTypeSymbol)semanticModel.GetDeclaredSymbol(classSyntax)!;
    }

    // ── Handle ────────────────────────────────────────────────────────────

    [Test]
    public async Task AsyncHandle_ExtractsAllProperties()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;
            [Handler]
            public class OrderHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public System.Threading.Tasks.Task<Response> Handle(Request request)
                    => System.Threading.Tasks.Task.FromResult(new Response("test"));
            }
            """;

        HandlerModelFactory.TryGetHandlerModel(GetSymbol(source, "OrderHandler"), Location.None, out var model, out var diagnostics);

        await Verify((model, diagnostics));
    }

    [Test]
    public async Task SyncHandle_HandleIsAsync_IsFalse()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;
            [Handler]
            public class SyncHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public Response Handle(Request request) => new Response("test");
            }
            """;

        HandlerModelFactory.TryGetHandlerModel(GetSymbol(source, "SyncHandler"), Location.None, out var model, out var diagnostics);

        await Verify((model, diagnostics));
    }

    [Test]
    public async Task GlobalNamespace_NamespaceIsNull()
    {
        const string source = """
            using MiniBus;
            [Handler]
            public class GlobalHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public System.Threading.Tasks.Task<Response> Handle(Request request)
                    => System.Threading.Tasks.Task.FromResult(new Response("test"));
            }
            """;

        HandlerModelFactory.TryGetHandlerModel(GetSymbol(source, "GlobalHandler"), Location.None, out var model, out var diagnostics);

        await Verify((model, diagnostics));
    }

    [Test]
    public async Task MissingHandleMethod_ReturnsNull()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;
            [Handler]
            public class NoHandleHandler
            {
                public record Request(int Id);
                public record Response(string Name);
            }
            """;

        HandlerModelFactory.TryGetHandlerModel(GetSymbol(source, "NoHandleHandler"), Location.None, out var model, out var diagnostics);

        await Verify((model, diagnostics));
    }

    // ── Load ──────────────────────────────────────────────────────────────

    [Test]
    public async Task AsyncNullableScalarLoad_LoadMethodPhaseIsCorrect()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;
            [Handler]
            public class EntityHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public record Entity(string Name);
                public System.Threading.Tasks.Task<Entity?> Load(Request request)
                    => System.Threading.Tasks.Task.FromResult<Entity?>(null);
                public System.Threading.Tasks.Task<Response> Handle(Entity entity)
                    => System.Threading.Tasks.Task.FromResult(new Response(entity.Name));
            }
            """;

        HandlerModelFactory.TryGetHandlerModel(GetSymbol(source, "EntityHandler"), Location.None, out var model, out var diagnostics);

        await Verify((model, diagnostics));
    }

    [Test]
    public async Task SyncNullableScalarLoad_LoadIsAsyncFalse()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;
            [Handler]
            public class SyncLoadHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public record Entity(string Name);
                public Entity? Load(Request request) => null;
                public System.Threading.Tasks.Task<Response> Handle(Entity entity)
                    => System.Threading.Tasks.Task.FromResult(new Response(entity.Name));
            }
            """;

        HandlerModelFactory.TryGetHandlerModel(GetSymbol(source, "SyncLoadHandler"), Location.None, out var model, out var diagnostics);

        await Verify((model, diagnostics));
    }

    [Test]
    public async Task NonNullableLoad_ElementIsNotNullable()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;
            [Handler]
            public class NonNullLoadHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public record Entity(string Name);
                public Entity Load(Request request) => new Entity("test");
                public System.Threading.Tasks.Task<Response> Handle(Entity entity)
                    => System.Threading.Tasks.Task.FromResult(new Response(entity.Name));
            }
            """;

        HandlerModelFactory.TryGetHandlerModel(GetSymbol(source, "NonNullLoadHandler"), Location.None, out var model, out var diagnostics);

        await Verify((model, diagnostics));
    }

    [Test]
    public async Task LoadMethod_SetsRequestTypeFromLoadParam()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;
            [Handler]
            public class LoadRequestHandler
            {
                public record Query(int Id);
                public record Response(string Name);
                public record Entity(string Name);
                public System.Threading.Tasks.Task<Entity?> Load(Query query)
                    => System.Threading.Tasks.Task.FromResult<Entity?>(null);
                public System.Threading.Tasks.Task<Response> Handle(Entity entity)
                    => System.Threading.Tasks.Task.FromResult(new Response(entity.Name));
            }
            """;

        HandlerModelFactory.TryGetHandlerModel(GetSymbol(source, "LoadRequestHandler"), Location.None, out var model, out var diagnostics);

        await Verify((model, diagnostics));
    }

    [Test]
    public async Task NamedTupleLoad_ExtractsElementNamesAndNullability()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;
            [Handler]
            public class TupleHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public record Entity(string Name);
                public record Config(string Value);
                public (Entity? entity, Config? config) Load(Request request)
                    => (null, null);
                public System.Threading.Tasks.Task<Response> Handle(Entity entity, Config config)
                    => System.Threading.Tasks.Task.FromResult(new Response(entity.Name));
            }
            """;

        HandlerModelFactory.TryGetHandlerModel(GetSymbol(source, "TupleHandler"), Location.None, out var model, out var diagnostics);

        await Verify((model, diagnostics));
    }

    [Test]
    public async Task UnnamedTupleLoad_UsesItem1Item2Names()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;
            [Handler]
            public class UnnamedTupleHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public record Entity(string Name);
                public record Config(string Value);
                public (Entity?, Config?) Load(Request request) => (null, null);
                public System.Threading.Tasks.Task<Response> Handle(Entity entity, Config config)
                    => System.Threading.Tasks.Task.FromResult(new Response(entity.Name));
            }
            """;

        HandlerModelFactory.TryGetHandlerModel(GetSymbol(source, "UnnamedTupleHandler"), Location.None, out var model, out var diagnostics);

        await Verify((model, diagnostics));
    }

    [Test]
    public async Task DuplicateTupleElementTypes_MapByParameterPosition()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;
            [Handler]
            public class DuplicateTupleTypesHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public record Entity(string Value);
                public (Entity primary, Entity secondary) Load(Request request)
                    => (new Entity("a"), new Entity("b"));
                public ValidationResult Validate(Entity first, Entity second)
                    => new ValidationResult();
                public System.Threading.Tasks.Task<Response> Handle(Entity first, Entity second)
                    => System.Threading.Tasks.Task.FromResult(new Response(first.Value + second.Value));
            }
            """;

        HandlerModelFactory.TryGetHandlerModel(GetSymbol(source, "DuplicateTupleTypesHandler"), Location.None, out var model, out var diagnostics);

        await Verify((model, diagnostics));
    }

    // ── Handle call args ──────────────────────────────────────────────────

    [Test]
    public async Task HandleCallArgs_SubstitutesLoadedParam()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;
            [Handler]
            public class LoadedArgHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public record Entity(string Name);
                public System.Threading.Tasks.Task<Entity?> Load(Request request)
                    => System.Threading.Tasks.Task.FromResult<Entity?>(null);
                public System.Threading.Tasks.Task<Response> Handle(Entity entity)
                    => System.Threading.Tasks.Task.FromResult(new Response(entity.Name));
            }
            """;

        HandlerModelFactory.TryGetHandlerModel(GetSymbol(source, "LoadedArgHandler"), Location.None, out var model, out var diagnostics);

        await Verify((model, diagnostics));
    }

    [Test]
    public async Task HandleCallArgs_BothRequestAndLoaded()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;
            [Handler]
            public class BothArgsHandler
            {
                public record Request(string Prefix);
                public record Response(string Value);
                public record Entity(string Data);
                public System.Threading.Tasks.Task<Entity?> Load(Request request)
                    => System.Threading.Tasks.Task.FromResult<Entity?>(null);
                public System.Threading.Tasks.Task<Response> Handle(Request request, Entity entity)
                    => System.Threading.Tasks.Task.FromResult(new Response(request.Prefix));
            }
            """;

        HandlerModelFactory.TryGetHandlerModel(GetSymbol(source, "BothArgsHandler"), Location.None, out var model, out var diagnostics);

        await Verify((model, diagnostics));
    }

    // ── Validate ──────────────────────────────────────────────────────────

    [Test]
    public async Task SyncValidate_ValidateMethodPhaseIsCorrect()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;
            [Handler]
            public class SyncValidateHandler
            {
                public record Request(string Value);
                public record Response(string Out);
                public ValidationResult Validate(Request request) => new ValidationResult();
                public System.Threading.Tasks.Task<Response> Handle(Request request)
                    => System.Threading.Tasks.Task.FromResult(new Response(request.Value));
            }
            """;

        HandlerModelFactory.TryGetHandlerModel(GetSymbol(source, "SyncValidateHandler"), Location.None, out var model, out var diagnostics);

        await Verify((model, diagnostics));
    }

    [Test]
    public async Task AsyncValidate_IsAsyncTrue()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;
            [Handler]
            public class AsyncValidateHandler
            {
                public record Request(int Id);
                public record Response(int Value);
                public System.Threading.Tasks.Task<ValidationResult> Validate(Request request)
                    => System.Threading.Tasks.Task.FromResult(new ValidationResult());
                public System.Threading.Tasks.Task<Response> Handle(Request request)
                    => System.Threading.Tasks.Task.FromResult(new Response(request.Id));
            }
            """;

        HandlerModelFactory.TryGetHandlerModel(GetSymbol(source, "AsyncValidateHandler"), Location.None, out var model, out var diagnostics);

        await Verify((model, diagnostics));
    }

    [Test]
    public async Task ValidateCallArgs_WithBothRequestAndLoaded()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;
            [Handler]
            public class BothValidateArgsHandler
            {
                public record Request(string Prefix);
                public record Response(string Value);
                public record Entity(string Data);
                public System.Threading.Tasks.Task<Entity?> Load(Request request)
                    => System.Threading.Tasks.Task.FromResult<Entity?>(null);
                public ValidationResult Validate(Request request, Entity entity)
                    => new ValidationResult();
                public System.Threading.Tasks.Task<Response> Handle(Entity entity)
                    => System.Threading.Tasks.Task.FromResult(new Response(entity.Data));
            }
            """;

        HandlerModelFactory.TryGetHandlerModel(GetSymbol(source, "BothValidateArgsHandler"), Location.None, out var model, out var diagnostics);

        await Verify((model, diagnostics));
    }

    [Test]
    public async Task ValidateOrder_RequestOnlyValidate_ComesBeforeLoad()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;
            [Handler]
            public class RequestOnlyValidateHandler
            {
                public record Request(string Prefix);
                public record Response(string Value);
                public record Entity(string Data);
                public System.Threading.Tasks.Task<Entity?> Load(Request request)
                    => System.Threading.Tasks.Task.FromResult<Entity?>(null);
                public ValidationResult Validate(Request request)
                    => new ValidationResult();
                public System.Threading.Tasks.Task<Response> Handle(Entity entity)
                    => System.Threading.Tasks.Task.FromResult(new Response(entity.Data));
            }
            """;

        HandlerModelFactory.TryGetHandlerModel(GetSymbol(source, "RequestOnlyValidateHandler"), Location.None, out var model, out var diagnostics);

        await Verify((model, diagnostics));
    }

    [Test]
    public async Task ValidateWithWrongReturnType_IsIgnored()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;
            [Handler]
            public class WrongValidateHandler
            {
                public record Request(string Value);
                public record Response(string Out);
                public string Validate(Request request) => "not a ValidationResult";
                public System.Threading.Tasks.Task<Response> Handle(Request request)
                    => System.Threading.Tasks.Task.FromResult(new Response(request.Value));
            }
            """;

        HandlerModelFactory.TryGetHandlerModel(GetSymbol(source, "WrongValidateHandler"), Location.None, out var model, out var diagnostics);

        await Verify((model, diagnostics));
    }

    [Test]
    public async Task RequestType_UsesValidateFirstParam_WhenLoadIsMissing()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;
            [Handler]
            public class ValidateRequestTypeHandler
            {
                public record Unknown(string Value);
                public record Command(string Value);
                public record Response(string Out);
                public ValidationResult Validate(Command command) => new ValidationResult();
                public System.Threading.Tasks.Task<Response> Handle(Command command, Unknown unknown)
                    => System.Threading.Tasks.Task.FromResult(new Response(command.Value));
            }
            """;

        HandlerModelFactory.TryGetHandlerModel(GetSymbol(source, "ValidateRequestTypeHandler"), Location.None, out var model, out var diagnostics);

        await Verify((model, diagnostics));
    }
}

