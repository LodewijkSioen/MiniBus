namespace MiniBus.Generator.Tests;

[TestFixture]
public class ValidateMethodGenerationTests
{
    // ── Sync Validate ─────────────────────────────────────────────────────

    [Test]
    public void SyncValidate_GeneratesCallWithoutAwait()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class SyncValidateHandler
            {
                public record Request(string Value);
                public record Response(string Out);

                public ValidationResult Validate(Request request)
                    => new ValidationResult();

                public System.Threading.Tasks.Task<Response> Handle(Request request)
                    => System.Threading.Tasks.Task.FromResult(new Response(request.Value));
            }
            """;

        var (sources, diagnostics) = GeneratorTestHelper.Run(source);

        Assert.That(diagnostics, Is.Empty);
        var dispatcher = sources.Single(s => s.Contains("class SyncValidateHandlerDispatcher"));
        Assert.That(dispatcher, Does.Not.Contain("await _handler.Validate"));
        Assert.That(dispatcher, Does.Contain("_handler.Validate(request)"));
        Assert.That(dispatcher, Does.Contain("validationResult.IsValid()"));
        Assert.That(dispatcher, Does.Contain("Invalid(validationResult)"));
    }

    // ── Async Validate (Task<ValidationResult>) ───────────────────────────

    [Test]
    public void AsyncValidate_GeneratesAwaitedCall()
    {
        const string source = """
            using MiniBus.Convention;
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

        var (sources, diagnostics) = GeneratorTestHelper.Run(source);

        Assert.That(diagnostics, Is.Empty);
        var dispatcher = sources.Single(s => s.Contains("class AsyncValidateHandlerDispatcher"));
        Assert.That(dispatcher, Does.Contain("await _handler.Validate(request)"));
        Assert.That(dispatcher, Does.Contain("validationResult.IsValid()"));
        Assert.That(dispatcher, Does.Contain("Invalid(validationResult)"));
    }

    // ── Validate receives only the loaded entity ──────────────────────────

    [Test]
    public void ValidateWithLoadedParam_PassesLoaded()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class LoadedValidateHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public record Entity(string Name);

                public System.Threading.Tasks.Task<Entity?> Load(Request request)
                    => System.Threading.Tasks.Task.FromResult<Entity?>(new Entity("ok"));

                public ValidationResult Validate(Entity entity)
                    => new ValidationResult();

                public System.Threading.Tasks.Task<Response> Handle(Entity entity)
                    => System.Threading.Tasks.Task.FromResult(new Response(entity.Name));
            }
            """;

        var (sources, _) = GeneratorTestHelper.Run(source);

        var dispatcher = sources.Single(s => s.Contains("class LoadedValidateHandlerDispatcher"));
        Assert.That(dispatcher, Does.Contain("_handler.Validate(loaded)"));
    }

    // ── Validate receives both Request and loaded entity ──────────────────

    [Test]
    public void ValidateWithBothParams_PassesBoth()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class BothParamValidateHandler
            {
                public record Request(string Prefix);
                public record Response(string Value);
                public record Entity(string Data);

                public System.Threading.Tasks.Task<Entity?> Load(Request request)
                    => System.Threading.Tasks.Task.FromResult<Entity?>(new Entity("data"));

                public ValidationResult Validate(Request request, Entity entity)
                    => new ValidationResult();

                public System.Threading.Tasks.Task<Response> Handle(Entity entity)
                    => System.Threading.Tasks.Task.FromResult(new Response(entity.Data));
            }
            """;

        var (sources, _) = GeneratorTestHelper.Run(source);

        var dispatcher = sources.Single(s => s.Contains("class BothParamValidateHandlerDispatcher"));
        Assert.That(dispatcher, Does.Contain("_handler.Validate(request, loaded)"));
    }

    // ── No Validate → no validation code ─────────────────────────────────

    [Test]
    public void NoValidateMethod_DoesNotGenerateValidationCode()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class NoValidateHandler
            {
                public record Request(int Id);
                public record Response(int Value);

                public System.Threading.Tasks.Task<Response> Handle(Request request)
                    => System.Threading.Tasks.Task.FromResult(new Response(request.Id));
            }
            """;

        var (sources, _) = GeneratorTestHelper.Run(source);

        var dispatcher = sources.Single(s => s.Contains("class NoValidateHandlerDispatcher"));
        Assert.That(dispatcher, Does.Not.Contain("validationResult"));
        Assert.That(dispatcher, Does.Not.Contain("_handler.Validate"));
    }

    // ── Validate method with wrong return type is ignored ─────────────────

    [Test]
    public void ValidateWithWrongReturnType_IsIgnored()
    {
        // A Validate method returning bool (not ValidationResult) must be ignored.
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class WrongValidateHandler
            {
                public record Request(int Id);
                public record Response(int Value);

                public bool Validate(Request request) => true;

                public System.Threading.Tasks.Task<Response> Handle(Request request)
                    => System.Threading.Tasks.Task.FromResult(new Response(request.Id));
            }
            """;

        var (sources, _) = GeneratorTestHelper.Run(source);

        var dispatcher = sources.Single(s => s.Contains("class WrongValidateHandlerDispatcher"));
        Assert.That(dispatcher, Does.Not.Contain("validationResult"));
    }

    // ── Full pipeline: Load → Validate → Handle ───────────────────────────

    [Test]
    public void FullPipeline_LoadValidateHandle_GeneratesAllPhases()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class FullPipelineHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public record Entity(int Id, string Name);

                public System.Threading.Tasks.Task<Entity?> Load(Request request)
                    => System.Threading.Tasks.Task.FromResult<Entity?>(new Entity(request.Id, "item"));

                public ValidationResult Validate(Entity entity)
                    => new ValidationResult();

                public System.Threading.Tasks.Task<Response> Handle(Entity entity)
                    => System.Threading.Tasks.Task.FromResult(new Response(entity.Name));
            }
            """;

        var (sources, diagnostics) = GeneratorTestHelper.Run(source);

        Assert.That(diagnostics, Is.Empty);
        var dispatcher = sources.Single(s => s.Contains("class FullPipelineHandlerDispatcher"));

        // Load phase
        Assert.That(dispatcher, Does.Contain("await _handler.Load(request)"));
        Assert.That(dispatcher, Does.Contain("if (loaded is null)"));
        Assert.That(dispatcher, Does.Contain("NotFound()"));

        // Validate phase
        Assert.That(dispatcher, Does.Contain("_handler.Validate(loaded)"));
        Assert.That(dispatcher, Does.Contain("!validationResult.IsValid()"));
        Assert.That(dispatcher, Does.Contain("Invalid(validationResult)"));

        // Handle phase
        Assert.That(dispatcher, Does.Contain("_handler.Handle(loaded)"));
        Assert.That(dispatcher, Does.Contain("Success(response)"));
    }
}
