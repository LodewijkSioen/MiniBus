namespace MiniBus.Generator.Tests;

[TestFixture]
public class TestPhase3_LoadMethod
{
    // ── Async Load (Task<T?>) ──────────────────────────────────────────────

    [Test]
    public void AsyncLoad_GeneratesAwaitedNullCheck()
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

        var (sources, diagnostics) = GeneratorTestHelper.Run(source);

        Assert.That(diagnostics, Is.Empty);
        var dispatcher = sources.Single(s => s.Contains("class EntityHandlerDispatcher"));
        Assert.That(dispatcher, Does.Contain("await _handler.Load(request)"));
        Assert.That(dispatcher, Does.Contain("if (loaded is null)"));
        Assert.That(dispatcher, Does.Contain("NotFound()"));
        Assert.That(dispatcher, Does.Contain("_handler.Handle(loaded)"));
    }

    // ── Sync Load (T?) ────────────────────────────────────────────────────

    [Test]
    public void SyncLoad_GeneratesNullCheckWithoutAwait()
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

        var (sources, _) = GeneratorTestHelper.Run(source);

        var dispatcher = sources.Single(s => s.Contains("class SyncLoadHandler"));
        // No 'await' before the Load call
        Assert.That(dispatcher, Does.Not.Contain("await _handler.Load"));
        Assert.That(dispatcher, Does.Contain("_handler.Load(request)"));
        Assert.That(dispatcher, Does.Contain("if (loaded is null)"));
    }

    // ── Handle receives both Request and loaded value ─────────────────────

    [Test]
    public void HandleWithRequestAndLoadedParams_PassesBoth()
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

        var (sources, _) = GeneratorTestHelper.Run(source);

        var dispatcher = sources.Single(s => s.Contains("class BothParamsHandlerDispatcher"));
        Assert.That(dispatcher, Does.Contain("Handle(request, loaded)"));
    }

    [Test]
    public void HandleWithLoadedThenRequestParams_PassesInDeclaredOrder()
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

        var (sources, _) = GeneratorTestHelper.Run(source);

        var dispatcher = sources.Single(s => s.Contains("class ReversedParamsHandlerDispatcher"));
        Assert.That(dispatcher, Does.Contain("Handle(loaded, request)"));
    }

    // ── No Load → no null-check ───────────────────────────────────────────

    [Test]
    public void NoLoadMethod_DoesNotGenerateNullCheck()
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

        var (sources, _) = GeneratorTestHelper.Run(source);

        var dispatcher = sources.Single(s => s.Contains("class NoLoadHandlerDispatcher"));
        Assert.That(dispatcher, Does.Not.Contain("loaded"));
        Assert.That(dispatcher, Does.Not.Contain("NotFound"));
    }

    // ── Non-nullable Load return → treated as plain call, no null-check ───

    [Test]
    public void LoadReturningNonNullableType_DoesNotGenerateNullCheck()
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

        var (sources, _) = GeneratorTestHelper.Run(source);

        var dispatcher = sources.Single(s => s.Contains("class NonNullableLoadHandlerDispatcher"));
        Assert.That(dispatcher, Does.Not.Contain("if (loaded is null)"));
    }
}
