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
    public void AsyncHandle_ExtractsAllProperties()
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

        var model = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "OrderHandler"), Location.None);

        Assert.Multiple(() =>
        {
            Assert.That(model, Is.Not.Null);
            Assert.That(model!.ClassName, Is.EqualTo("OrderHandler"));
            Assert.That(model.Namespace, Is.EqualTo("TestApp"));
            Assert.That(model.FullClassName, Is.EqualTo("global::TestApp.OrderHandler"));
            Assert.That(model.FullRequestType, Is.EqualTo("global::TestApp.OrderHandler.Request"));
            Assert.That(model.FullResponseType, Is.EqualTo("global::TestApp.OrderHandler.Response"));
            Assert.That(model.Phases.GetRequiredPhase<HandleMethodPhase>().IsAsync, Is.True);
            Assert.That(model.Phases.GetRequiredPhase<HandleMethodPhase>().CallArgs, Is.EqualTo("request"));
            Assert.That(model.Phases.TryGetPhase<LoadMethodPhase>(), Is.Null);
            Assert.That(model.Phases.TryGetPhase<ValidateMethodPhase>(), Is.Null);
        });
    }

    [Test]
    public void SyncHandle_HandleIsAsync_IsFalse()
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

        var model = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "SyncHandler"), Location.None);

        Assert.That(model!.Phases.GetRequiredPhase<HandleMethodPhase>().IsAsync, Is.False);
    }

    [Test]
    public void GlobalNamespace_NamespaceIsNull()
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

        var model = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "GlobalHandler"), Location.None);

        Assert.That(model!.Namespace, Is.Null);
    }

    [Test]
    public void MissingHandleMethod_ReturnsNull()
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

        var model = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "NoHandleHandler"), Location.None);

        Assert.That(model, Is.Null);
    }

    // ── Load ──────────────────────────────────────────────────────────────

    [Test]
    public void AsyncNullableScalarLoad_LoadMethodPhaseIsCorrect()
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

        var model = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "EntityHandler"), Location.None);

        Assert.Multiple(() =>
        {
            Assert.That(model!.Phases.TryGetPhase<LoadMethodPhase>(), Is.Not.Null);
            Assert.That(model.Phases.TryGetPhase<LoadMethodPhase>()!.IsAsync, Is.True);
            Assert.That(model.Phases.TryGetPhase<LoadMethodPhase>()!.IsTuple, Is.False);
            Assert.That(model.Phases.TryGetPhase<LoadMethodPhase>()!.Elements, Has.Length.EqualTo(1));
            Assert.That(model.Phases.TryGetPhase<LoadMethodPhase>()!.Elements[0].LocalName, Is.EqualTo("loaded"));
            Assert.That(model.Phases.TryGetPhase<LoadMethodPhase>()!.Elements[0].IsNullable, Is.True);
        });
    }

    [Test]
    public void SyncNullableScalarLoad_LoadIsAsyncFalse()
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

        var model = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "SyncLoadHandler"), Location.None);

        Assert.Multiple(() =>
        {
            Assert.That(model!.Phases.TryGetPhase<LoadMethodPhase>()!.IsAsync, Is.False);
            Assert.That(model.Phases.TryGetPhase<LoadMethodPhase>()!.Elements[0].IsNullable, Is.True);
        });
    }

    [Test]
    public void NonNullableLoad_ElementIsNotNullable()
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

        var model = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "NonNullLoadHandler"), Location.None);

        Assert.Multiple(() =>
        {
            Assert.That(model!.Phases.TryGetPhase<LoadMethodPhase>(), Is.Not.Null);
            Assert.That(model.Phases.TryGetPhase<LoadMethodPhase>()!.IsTuple, Is.False);
            Assert.That(model.Phases.TryGetPhase<LoadMethodPhase>()!.Elements, Has.Length.EqualTo(1));
            Assert.That(model.Phases.TryGetPhase<LoadMethodPhase>()!.Elements[0].LocalName, Is.EqualTo("loaded"));
            Assert.That(model.Phases.TryGetPhase<LoadMethodPhase>()!.Elements[0].IsNullable, Is.False);
            Assert.That(model.Phases.GetRequiredPhase<HandleMethodPhase>().CallArgs, Is.EqualTo("loaded"));
        });
    }

    [Test]
    public void LoadMethod_SetsRequestTypeFromLoadParam()
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

        var model = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "LoadRequestHandler"), Location.None);

        Assert.That(model!.FullRequestType, Is.EqualTo("global::TestApp.LoadRequestHandler.Query"));
    }

    [Test]
    public void NamedTupleLoad_ExtractsElementNamesAndNullability()
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

        var model = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "TupleHandler"), Location.None);

        Assert.Multiple(() =>
        {
            Assert.That(model!.Phases.TryGetPhase<LoadMethodPhase>()!.IsTuple, Is.True);
            Assert.That(model.Phases.TryGetPhase<LoadMethodPhase>()!.Elements, Has.Length.EqualTo(2));
            Assert.That(model.Phases.TryGetPhase<LoadMethodPhase>()!.Elements[0].LocalName, Is.EqualTo("entity"));
            Assert.That(model.Phases.TryGetPhase<LoadMethodPhase>()!.Elements[0].IsNullable, Is.True);
            Assert.That(model.Phases.TryGetPhase<LoadMethodPhase>()!.Elements[1].LocalName, Is.EqualTo("config"));
            Assert.That(model.Phases.TryGetPhase<LoadMethodPhase>()!.Elements[1].IsNullable, Is.True);
        });
    }

    [Test]
    public void UnnamedTupleLoad_UsesItem1Item2Names()
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

        var model = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "UnnamedTupleHandler"), Location.None);

        Assert.Multiple(() =>
        {
            Assert.That(model!.Phases.TryGetPhase<LoadMethodPhase>()!.IsTuple, Is.True);
            Assert.That(model.Phases.TryGetPhase<LoadMethodPhase>()!.Elements[0].LocalName, Is.EqualTo("item1"));
            Assert.That(model.Phases.TryGetPhase<LoadMethodPhase>()!.Elements[1].LocalName, Is.EqualTo("item2"));
        });
    }

    [Test]
    public void DuplicateTupleElementTypes_MapByParameterPosition()
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

        var model = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "DuplicateTupleTypesHandler"), Location.None);

        Assert.Multiple(() =>
        {
            Assert.That(model!.Phases.GetRequiredPhase<HandleMethodPhase>().CallArgs, Is.EqualTo("primary, secondary"));
            Assert.That(model.Phases.TryGetPhase<ValidateMethodPhase>()!.CallArgs, Is.EqualTo("primary, secondary"));
        });
    }

    // ── Handle call args ──────────────────────────────────────────────────

    [Test]
    public void HandleCallArgs_SubstitutesLoadedParam()
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

        var model = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "LoadedArgHandler"), Location.None);

        Assert.That(model!.Phases.GetRequiredPhase<HandleMethodPhase>().CallArgs, Is.EqualTo("loadedValue"));
    }

    [Test]
    public void HandleCallArgs_BothRequestAndLoaded()
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

        var model = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "BothArgsHandler"), Location.None);

        Assert.That(model!.Phases.GetRequiredPhase<HandleMethodPhase>().CallArgs, Is.EqualTo("request, loadedValue"));
    }

    // ── Validate ──────────────────────────────────────────────────────────

    [Test]
    public void SyncValidate_ValidateMethodPhaseIsCorrect()
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

        var model = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "SyncValidateHandler"), Location.None);

        Assert.Multiple(() =>
        {
            Assert.That(model!.Phases.TryGetPhase<ValidateMethodPhase>(), Is.Not.Null);
            Assert.That(model.Phases.TryGetPhase<ValidateMethodPhase>()!.IsAsync, Is.False);
            Assert.That(model.Phases.TryGetPhase<ValidateMethodPhase>()!.CallArgs, Is.EqualTo("request"));
        });
    }

    [Test]
    public void AsyncValidate_IsAsyncTrue()
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

        var model = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "AsyncValidateHandler"), Location.None);

        Assert.That(model!.Phases.TryGetPhase<ValidateMethodPhase>()!.IsAsync, Is.True);
    }

    [Test]
    public void ValidateCallArgs_WithBothRequestAndLoaded()
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

        var model = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "BothValidateArgsHandler"), Location.None);

        Assert.Multiple(() =>
        {
            Assert.That(model!.Phases.TryGetPhase<ValidateMethodPhase>()!.CallArgs, Is.EqualTo("request, loadedValue"));
            Assert.That(model.Phases.TryGetPhase<LoadMethodPhase>(), Is.Not.Null);
            Assert.That(model.Phases.TryGetPhase<ValidateMethodPhase>(), Is.Not.Null);
            Assert.That(model.Phases.TryGetPhase<LoadMethodPhase>()!.Order, Is.LessThan(model.Phases.TryGetPhase<ValidateMethodPhase>()!.Order));
        });
    }

    [Test]
    public void ValidateOrder_RequestOnlyValidate_ComesBeforeLoad()
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

        var model = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "RequestOnlyValidateHandler"), Location.None);

        Assert.Multiple(() =>
        {
            Assert.That(model, Is.Not.Null);
            Assert.That(model!.Phases.TryGetPhase<ValidateMethodPhase>()!.CallArgs, Is.EqualTo("request"));
            Assert.That(model.Phases.TryGetPhase<LoadMethodPhase>(), Is.Not.Null);
            Assert.That(model.Phases.TryGetPhase<ValidateMethodPhase>(), Is.Not.Null);
            Assert.That(model.Phases.TryGetPhase<ValidateMethodPhase>()!.Order, Is.LessThan(model.Phases.TryGetPhase<LoadMethodPhase>()!.Order));
        });
    }

    [Test]
    public void ValidateWithWrongReturnType_IsIgnored()
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

        var model = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "WrongValidateHandler"), Location.None);

        Assert.That(model!.Phases.TryGetPhase<ValidateMethodPhase>(), Is.Null);
    }

    [Test]
    public void RequestType_UsesValidateFirstParam_WhenLoadIsMissing()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;
            [Handler]
            public class ValidateRequestTypeHandler
            {
                public record Query(string Value);
                public record Command(string Value);
                public record Response(string Out);
                public ValidationResult Validate(Query query) => new ValidationResult();
                public System.Threading.Tasks.Task<Response> Handle(Command command)
                    => System.Threading.Tasks.Task.FromResult(new Response(command.Value));
            }
            """;

        var model = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "ValidateRequestTypeHandler"), Location.None);

        Assert.Multiple(() =>
        {
            Assert.That(model, Is.Not.Null);
            Assert.That(model!.FullRequestType, Is.EqualTo("global::TestApp.ValidateRequestTypeHandler.Query"));
            Assert.That(model.Phases.GetRequiredPhase<HandleMethodPhase>().UnsupportedParameters, Has.Length.EqualTo(1));
            Assert.That(model.Phases.GetRequiredPhase<HandleMethodPhase>().UnsupportedParameters[0], Is.EqualTo("command: global::TestApp.ValidateRequestTypeHandler.Command"));
        });
    }
}

