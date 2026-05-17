namespace MiniBus.Generator.Tests;

[TestFixture]
public class TupleLoadGenerationTests
{
    // ── Named tuple elements ──────────────────────────────────────────────

    [Test]
    public Task NamedTupleElements_GeneratesDeconstructionWithElementNames()
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

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    // ── Unnamed tuple elements fall back to item1/item2 ───────────────────

    [Test]
    public Task UnnamedTupleElements_GeneratesItem1Item2Names()
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

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    // ── All nullable → combined null-check ───────────────────────────────

    [Test]
    public Task AllNullableElements_GeneratesCombinedNullCheck()
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

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    // ── Partially nullable → only nullable elements in check ─────────────

    [Test]
    public Task PartiallyNullableElements_ChecksOnlyNullableOnes()
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

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    // ── No nullable tuple elements → no null-check ────────────────────────

    [Test]
    public Task NoNullableElements_DoesNotGenerateNullCheck()
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

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    // ── Async tuple load generates await ─────────────────────────────────

    [Test]
    public Task AsyncTupleLoad_GeneratesAwaitedDeconstruction()
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

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }
}
