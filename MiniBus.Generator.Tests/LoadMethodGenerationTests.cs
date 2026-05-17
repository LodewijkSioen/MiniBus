namespace MiniBus.Generator.Tests;

[TestFixture]
public class LoadMethodGenerationTests
{
    // ── Async Load (Task<T?>) ──────────────────────────────────────────────

    [Test]
    public Task AsyncLoad_GeneratesAwaitedNullCheck()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class EntityHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public record Entity(int Id, string Name);

                public System.Threading.Tasks.Task<Entity?> Load(Request request)
                    => System.Threading.Tasks.Task.FromResult<Entity?>(new Entity(request.Id, "test"));

                public System.Threading.Tasks.Task<Response> Handle(Entity entity)
                    => System.Threading.Tasks.Task.FromResult(new Response(entity.Name));
            }
            """;

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    // ── Sync Load (T?) ────────────────────────────────────────────────────

    [Test]
    public Task SyncLoad_GeneratesNullCheckWithoutAwait()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class SyncLoadHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public record Entity(string Name);

                public Entity? Load(Request request)
                    => request.Id == 0 ? null : new Entity("ok");

                public System.Threading.Tasks.Task<Response> Handle(Entity entity)
                    => System.Threading.Tasks.Task.FromResult(new Response(entity.Name));
            }
            """;

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    // ── Handle receives both Request and loaded value ─────────────────────

    [Test]
    public Task HandleWithRequestAndLoadedParams_PassesBoth()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class BothParamsHandler
            {
                public record Request(string Prefix);
                public record Response(string Value);
                public record Loaded(string Data);

                public System.Threading.Tasks.Task<Loaded?> Load(Request request)
                    => System.Threading.Tasks.Task.FromResult<Loaded?>(new Loaded("data"));

                public System.Threading.Tasks.Task<Response> Handle(Request request, Loaded loaded)
                    => System.Threading.Tasks.Task.FromResult(new Response($"{request.Prefix}:{loaded.Data}"));
            }
            """;

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    [Test]
    public Task HandleWithLoadedThenRequestParams_PassesInDeclaredOrder()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class ReversedParamsHandler
            {
                public record Request(string Prefix);
                public record Response(string Value);
                public record Loaded(string Data);

                public System.Threading.Tasks.Task<Loaded?> Load(Request request)
                    => System.Threading.Tasks.Task.FromResult<Loaded?>(new Loaded("data"));

                public System.Threading.Tasks.Task<Response> Handle(Loaded loaded, Request request)
                    => System.Threading.Tasks.Task.FromResult(new Response(loaded.Data));
            }
            """;

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    // ── No Load → no null-check ───────────────────────────────────────────

    [Test]
    public Task NoLoadMethod_DoesNotGenerateNullCheck()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class NoLoadHandler
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

    // ── Non-nullable Load return → treated as plain call, no null-check ───

    [Test]
    public Task LoadReturningNonNullableType_DoesNotGenerateNullCheck()
    {
        // A Load method returning Task<Entity> (not Task<Entity?>) should not
        // generate a null-check — the generator only reacts to nullable returns.
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class NonNullableLoadHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public record Entity(string Name);

                public System.Threading.Tasks.Task<Entity> Load(Request request)
                    => System.Threading.Tasks.Task.FromResult(new Entity("ok"));

                public System.Threading.Tasks.Task<Response> Handle(Request request)
                    => System.Threading.Tasks.Task.FromResult(new Response("ok"));
            }
            """;

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }
}
