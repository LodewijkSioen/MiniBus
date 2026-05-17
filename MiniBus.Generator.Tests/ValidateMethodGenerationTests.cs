namespace MiniBus.Generator.Tests;

[TestFixture]
public class ValidateMethodGenerationTests
{
    // ── Sync Validate ─────────────────────────────────────────────────────

    [Test]
    public Task SyncValidate_GeneratesCallWithoutAwait()
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

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    // ── Async Validate (Task<ValidationResult>) ───────────────────────────

    [Test]
    public Task AsyncValidate_GeneratesAwaitedCall()
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

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    // ── Validate receives only the loaded entity ──────────────────────────

    [Test]
    public Task ValidateWithLoadedParam_PassesLoaded()
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

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    // ── Validate receives both Request and loaded entity ──────────────────

    [Test]
    public Task ValidateWithBothParams_PassesBoth()
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

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    // ── No Validate → no validation code ─────────────────────────────────

    [Test]
    public Task NoValidateMethod_DoesNotGenerateValidationCode()
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

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    // ── Validate method with wrong return type is ignored ─────────────────

    [Test]
    public Task ValidateWithWrongReturnType_IsIgnored()
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

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    // ── Full pipeline: Load → Validate → Handle ───────────────────────────

    [Test]
    public Task FullPipeline_LoadValidateHandle_GeneratesAllPhases()
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

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }
}
