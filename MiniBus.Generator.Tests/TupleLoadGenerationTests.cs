namespace MiniBus.Generator.Tests;

[TestFixture]
public class TupleLoadGenerationTests
{
    // ── Named tuple elements ──────────────────────────────────────────────

    [Test]
    public void NamedTupleElements_GeneratesDeconstructionWithElementNames()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class NamedTupleHandler
            {
                public record Request(int Id);
                public record Response(string Out);
                public record Entity(string Name);
                public record Config(string Value);

                public System.Threading.Tasks.Task<(Entity? entity, Config? config)> Load(Request request)
                    => System.Threading.Tasks.Task.FromResult<(Entity?, Config?)>((new Entity("e"), new Config("c")));

                public System.Threading.Tasks.Task<Response> Handle(Entity entity, Config config)
                    => System.Threading.Tasks.Task.FromResult(new Response(entity.Name));
            }
            """;

        var (sources, diagnostics) = GeneratorTestHelper.Run(source);

        Assert.That(diagnostics, Is.Empty);
        var dispatcher = sources.Single(s => s.Contains("class NamedTupleHandlerDispatcher"));
        Assert.That(dispatcher, Does.Contain("var (entity, config) = "));
        Assert.That(dispatcher, Does.Contain("_handler.Handle(entity, config)"));
    }

    // ── Unnamed tuple elements fall back to item1/item2 ───────────────────

    [Test]
    public void UnnamedTupleElements_GeneratesItem1Item2Names()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class UnnamedTupleHandler
            {
                public record Request(int Id);
                public record Response(string Out);
                public record Entity(string Name);
                public record Config(string Value);

                public System.Threading.Tasks.Task<(Entity?, Config?)> Load(Request request)
                    => System.Threading.Tasks.Task.FromResult<(Entity?, Config?)>((new Entity("e"), new Config("c")));

                public System.Threading.Tasks.Task<Response> Handle(Entity item1, Config item2)
                    => System.Threading.Tasks.Task.FromResult(new Response(item1.Name));
            }
            """;

        var (sources, _) = GeneratorTestHelper.Run(source);

        var dispatcher = sources.Single(s => s.Contains("class UnnamedTupleHandlerDispatcher"));
        Assert.That(dispatcher, Does.Contain("var (item1, item2) = "));
        Assert.That(dispatcher, Does.Contain("_handler.Handle(item1, item2)"));
    }

    // ── All nullable → combined null-check ───────────────────────────────

    [Test]
    public void AllNullableElements_GeneratesCombinedNullCheck()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class AllNullableHandler
            {
                public record Request(int Id);
                public record Response(string Out);
                public record A(string X);
                public record B(string Y);

                public System.Threading.Tasks.Task<(A? a, B? b)> Load(Request request)
                    => System.Threading.Tasks.Task.FromResult<(A?, B?)>((new A("x"), new B("y")));

                public System.Threading.Tasks.Task<Response> Handle(A a, B b)
                    => System.Threading.Tasks.Task.FromResult(new Response(a.X));
            }
            """;

        var (sources, _) = GeneratorTestHelper.Run(source);

        var dispatcher = sources.Single(s => s.Contains("class AllNullableHandlerDispatcher"));
        Assert.That(dispatcher, Does.Contain("a is null || b is null"));
        Assert.That(dispatcher, Does.Contain("NotFound()"));
    }

    // ── Partially nullable → only nullable elements in check ─────────────

    [Test]
    public void PartiallyNullableElements_ChecksOnlyNullableOnes()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class PartialNullableHandler
            {
                public record Request(int Id);
                public record Response(string Out);
                public record A(string X);
                public record B(string Y);

                public System.Threading.Tasks.Task<(A? a, B b)> Load(Request request)
                    => System.Threading.Tasks.Task.FromResult<(A?, B)>((new A("x"), new B("y")));

                public System.Threading.Tasks.Task<Response> Handle(A a, B b)
                    => System.Threading.Tasks.Task.FromResult(new Response(a.X));
            }
            """;

        var (sources, _) = GeneratorTestHelper.Run(source);

        var dispatcher = sources.Single(s => s.Contains("class PartialNullableHandlerDispatcher"));
        Assert.That(dispatcher, Does.Contain("a is null"));
        Assert.That(dispatcher, Does.Not.Contain("b is null"));
    }

    // ── No nullable tuple elements → no null-check ────────────────────────

    [Test]
    public void NoNullableElements_DoesNotGenerateNullCheck()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class NonNullableTupleHandler
            {
                public record Request(int Id);
                public record Response(string Out);
                public record A(string X);
                public record B(string Y);

                public System.Threading.Tasks.Task<(A a, B b)> Load(Request request)
                    => System.Threading.Tasks.Task.FromResult<(A, B)>((new A("x"), new B("y")));

                public System.Threading.Tasks.Task<Response> Handle(A a, B b)
                    => System.Threading.Tasks.Task.FromResult(new Response(a.X));
            }
            """;

        var (sources, _) = GeneratorTestHelper.Run(source);

        var dispatcher = sources.Single(s => s.Contains("class NonNullableTupleHandlerDispatcher"));
        Assert.That(dispatcher, Does.Contain("var (a, b) = "));
        Assert.That(dispatcher, Does.Not.Contain("is null"));
        Assert.That(dispatcher, Does.Not.Contain("NotFound"));
    }

    // ── Async tuple load generates await ─────────────────────────────────

    [Test]
    public void AsyncTupleLoad_GeneratesAwaitedDeconstruction()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class AsyncTupleHandler
            {
                public record Request(int Id);
                public record Response(string Out);
                public record Entity(string Name);
                public record Config(string Value);

                public System.Threading.Tasks.Task<(Entity? entity, Config? config)> Load(Request request)
                    => System.Threading.Tasks.Task.FromResult<(Entity?, Config?)>((new Entity("e"), new Config("c")));

                public System.Threading.Tasks.Task<Response> Handle(Entity entity, Config config)
                    => System.Threading.Tasks.Task.FromResult(new Response(entity.Name));
            }
            """;

        var (sources, _) = GeneratorTestHelper.Run(source);

        var dispatcher = sources.Single(s => s.Contains("class AsyncTupleHandlerDispatcher"));
        Assert.That(dispatcher, Does.Contain("await _handler.Load(request)"));
        Assert.That(dispatcher, Does.Contain("var (entity, config) ="));
    }
}
