using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Caravelle.Generator.Tests;

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
            using Caravelle;
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

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "OrderHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task SyncHandle_HandleIsAsync_IsFalse()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;
            [Handler]
            public class SyncHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public Response Handle(Request request) => new Response("test");
            }
            """;

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "SyncHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task GlobalNamespace_NamespaceIsNull()
    {
        const string source = """
            using Caravelle;
            [Handler]
            public class GlobalHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public System.Threading.Tasks.Task<Response> Handle(Request request)
                    => System.Threading.Tasks.Task.FromResult(new Response("test"));
            }
            """;

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "GlobalHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task MissingHandleMethod_ReturnsNull()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;
            [Handler]
            public class NoHandleHandler
            {
                public record Request(int Id);
                public record Response(string Name);
            }
            """;

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "NoHandleHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task ExecuteAsyncHandleAlias_IsDetectedAsHandleMethod()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;
            [Handler]
            public class ExecuteAsyncHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public System.Threading.Tasks.Task<Response> ExecuteAsync(Request request)
                    => System.Threading.Tasks.Task.FromResult(new Response(request.Id.ToString()));
            }
            """;

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "ExecuteAsyncHandler"), Location.None);

        await Verify(result);
    }

    // ── Load ──────────────────────────────────────────────────────────────

    [Test]
    public async Task AsyncNullableScalarLoad_LoadMethodPhaseIsCorrect()
    {
        const string source = """
            using Caravelle;
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

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "EntityHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task SyncNullableScalarLoad_LoadIsAsyncFalse()
    {
        const string source = """
            using Caravelle;
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

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "SyncLoadHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task NonNullableLoad_ElementIsNotNullable()
    {
        const string source = """
            using Caravelle;
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

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "NonNullLoadHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task LoadMethod_SetsRequestTypeFromLoadParam()
    {
        const string source = """
            using Caravelle;
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

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "LoadRequestHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task NamedTupleLoad_ExtractsElementNamesAndNullability()
    {
        const string source = """
            using Caravelle;
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

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "TupleHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task UnnamedTupleLoad_UsesItem1Item2Names()
    {
        const string source = """
            using Caravelle;
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

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "UnnamedTupleHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task DuplicateTupleElementTypes_ReportsMBG006_AndReturnsNullModel()
    {
        const string source = """
            using Caravelle;
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

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "DuplicateTupleTypesHandler"), Location.None);

        await Verify(result);
    }

    // ── Handle call args ──────────────────────────────────────────────────

    [Test]
    public async Task HandleCallArgs_SubstitutesLoadedParam()
    {
        const string source = """
            using Caravelle;
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

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "LoadedArgHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task HandleCallArgs_BothRequestAndLoaded()
    {
        const string source = """
            using Caravelle;
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

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "BothArgsHandler"), Location.None);

        await Verify(result);
    }

    // ── Validate ──────────────────────────────────────────────────────────

    [Test]
    public async Task SyncValidate_ValidateMethodPhaseIsCorrect()
    {
        const string source = """
            using Caravelle;
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

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "SyncValidateHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task AsyncValidate_IsAsyncTrue()
    {
        const string source = """
            using Caravelle;
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

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "AsyncValidateHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task ValidateCallArgs_WithBothRequestAndLoaded()
    {
        const string source = """
            using Caravelle;
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

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "BothValidateArgsHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task ValidateOrder_RequestOnlyValidate_ComesBeforeLoad()
    {
        const string source = """
            using Caravelle;
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

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "RequestOnlyValidateHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task ValidateWithNonValidationResultReturn_IsIncluded()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;
            [Handler]
            public class NonValidationValidateHandler
            {
                public record Request(string Value);
                public record Response(string Out);
                public string Validate(Request request) => request.Value;
                public System.Threading.Tasks.Task<Response> Handle(string fromValidate)
                    => System.Threading.Tasks.Task.FromResult(new Response(fromValidate));
            }
            """;

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "NonValidationValidateHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task ValidateReturningTupleWithValidationResult_IsIncluded()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;
            [Handler]
            public class ValidateTupleHandler
            {
                public record Request(int Id);
                public record Payload(int Value);
                public record Response(int Value);

                public (ValidationResult validation, Payload payload) Validate(Request request)
                    => (new ValidationResult(), new Payload(request.Id));

                public System.Threading.Tasks.Task<Response> Handle(Payload payload)
                    => System.Threading.Tasks.Task.FromResult(new Response(payload.Value));
            }
            """;

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "ValidateTupleHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task HandleTupleWithValidationResultFirst_IsAllowed()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;
            [Handler]
            public class InvalidHandleTupleHandler
            {
                public record Request(int Id);
                public record Response(string Value);

                public (ValidationResult validation, Response response) Handle(Request request)
                    => (new ValidationResult(), new Response(request.Id.ToString()));
            }
            """;

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "InvalidHandleTupleHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task RequestType_UsesValidateFirstParam_WhenLoadIsMissing()
    {
        const string source = """
            using Caravelle;
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

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "ValidateRequestTypeHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task BeforeNameConventions_AndAsyncAliases_AreIncludedAsPreHandlePhases()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;
            [Handler]
            public class BeforeConventionHandler
            {
                public record Request(string Value);
                public record Entity(string Value);
                public record Prepared(string Value);
                public record Enriched(string Value);
                public record Response(string Value);

                public Entity LoadAsync(Request request) => new Entity(request.Value);

                public Prepared BeforeNormalize(Entity entity) => new Prepared(entity.Value + "-prepared");

                public Enriched NormalizeBeforeAsync(Prepared prepared) => new Enriched(prepared.Value + "-enriched");

                public ValidationResult ValidateAsync(Enriched enriched) => new ValidationResult();

                public Response Handle(Enriched enriched) => new Response(enriched.Value);
            }
            """;

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "BeforeConventionHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task AfterAndPostNameConventions_AreIncludedAsPostHandlePhases()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;
            [Handler]
            public class PostConventionHandler
            {
                public record Request(string Value);
                public record Response(string Value);
                public record Audit(string Value);

                public Response Handle(Request request)
                    => new Response(request.Value);

                public Audit AfterAudit(Response response)
                    => new Audit(response.Value + "-audit");

                public ValidationResult PostValidate(Audit audit)
                    => new ValidationResult();
            }
            """;

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "PostConventionHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task PostOrder_DependencyBased_UsesSameMechanismAsPreHandle()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;
            [Handler]
            public class PostOrderingHandler
            {
                public record Request(string Value);
                public record Response(string Value);
                public record Audit(string Value);
                public record Envelope(string Value);

                public Response Handle(Request request)
                    => new Response(request.Value);

                public Envelope PostWrap(Audit audit)
                    => new Envelope(audit.Value);

                public Audit AfterAudit(Response response)
                    => new Audit(response.Value + "-audit");
            }
            """;

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "PostOrderingHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task StaticHandle_IsStaticTrue_AndHasNoInstanceMethods()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;
            [Handler]
            public class StaticOnlyHandler
            {
                public record Request(int Value);
                public record Response(int Value);
                public static Response Handle(Request request) => new Response(request.Value);
            }
            """;

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "StaticOnlyHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task ConventionBasedDi_UnmatchedParameters_AreMarkedFromServices()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;
            public interface IService;

            [Handler]
            public class ConventionDiHandler
            {
                public record Request(int Value);
                public record Response(int Value);

                public static ValidationResult Validate(Request request, IService? service)
                    => new ValidationResult();

                public static Response Handle(Request request, IService service)
                    => new Response(request.Value);
            }
            """;

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "ConventionDiHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public void CheckEquality()
    {
        const string source = """
                              using Caravelle;
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

        var result1 = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "BothValidateArgsHandler"), Location.None);
        var result2 = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "BothValidateArgsHandler"), Location.None);

        Assert.That(result1, Is.EqualTo(result2));
    }

    // ── Finally ──────────────────────────────────────────────────────────

    [Test]
    public async Task FinallyMethod_IsDetectedAsFinallyPhase()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;
            [Handler]
            public class FinallyHandler
            {
                public record Request(int Id);
                public record Response(string Value);
                public Response Handle(Request request) => new Response("test");
                public void Finally(Request request) { }
            }
            """;

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "FinallyHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task FinallyAsyncMethod_IsAsync()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;
            [Handler]
            public class FinallyAsyncHandler
            {
                public record Request(int Id);
                public record Response(string Value);
                public Response Handle(Request request) => new Response("test");
                public System.Threading.Tasks.Task FinallyAsync(Request request) => System.Threading.Tasks.Task.CompletedTask;
            }
            """;

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "FinallyAsyncHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task FinallyParams_PipelineReturnTypes_AreNotFromServices()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;
            [Handler]
            public class FinallyPipelineParamsHandler
            {
                public record Request(int Id);
                public record Response(string Value);
                public record Entity(int Id, string Name);
                
                public Entity Load(Request request) => new Entity(request.Id, "test");
                public Response Handle(Entity entity) => new Response(entity.Name);
                public void Finally(Request request, Entity? entity) { }
            }
            """;

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "FinallyPipelineParamsHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task FinallyParams_UnknownTypes_AreFromServices()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;
            public interface IService;
            
            [Handler]
            public class FinallyDiParamsHandler
            {
                public record Request(int Id);
                public record Response(string Value);
                public Response Handle(Request request) => new Response("test");
                public void Finally(Request request, IService service) { }
            }
            """;

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "FinallyDiParamsHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task FinallyWithVoidReturn_IsAccepted()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;
            [Handler]
            public class FinallyVoidHandler
            {
                public record Request(int Id);
                public record Response(string Value);
                public Response Handle(Request request) => new Response("test");
                public void Finally(Request request) { }
            }
            """;

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "FinallyVoidHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task FinallyWithTaskReturn_IsAccepted()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;
            [Handler]
            public class FinallyTaskHandler
            {
                public record Request(int Id);
                public record Response(string Value);
                public Response Handle(Request request) => new Response("test");
                public System.Threading.Tasks.Task Finally(Request request) => System.Threading.Tasks.Task.CompletedTask;
            }
            """;

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "FinallyTaskHandler"), Location.None);

        await Verify(result);
    }

    [Test]
    public async Task FinallyWithNonNullablePipelineParam_ReportsMBG010()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;
            [Handler]
            public class FinallyNonNullableHandler
            {
                public record Request(int Id);
                public record Response(string Value);
                public record Entity(int Id);
                
                public Entity? Load(Request request) => null;
                public Response Handle(Entity entity) => new Response("test");
                public void Finally(Entity entity) { }
            }
            """;

        var result = HandlerModelFactory.GetHandlerModel(GetSymbol(source, "FinallyNonNullableHandler"), Location.None);

        await Verify(result);
    }
}

