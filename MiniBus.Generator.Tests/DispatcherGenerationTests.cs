namespace MiniBus.Generator.Tests;

[TestFixture]
public class DispatcherGenerationTests
{
    // ── Dispatcher generation ──────────────────────────────────────────────

    [Test]
    public Task ValidHandler_GeneratesTwoFiles_DispatcherAndRegistrations()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class SimpleHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public System.Threading.Tasks.Task<Response> Handle(Request request)
                    => System.Threading.Tasks.Task.FromResult(new Response("test"));
            }
            """;

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    [Test]
    public Task ValidHandler_DispatcherFile_ImplementsIConventionHandler()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class OrderHandler
            {
                public record Request(int OrderId);
                public record Response(string Status);
                public System.Threading.Tasks.Task<Response> Handle(Request request)
                    => System.Threading.Tasks.Task.FromResult(new Response("ok"));
            }
            """;

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    [Test]
    public Task ValidHandler_RegistrationsFile_ContainsTypedExtensionMethod()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class ExtHandler
            {
                public record Request;
                public record Response;
                public System.Threading.Tasks.Task<Response> Handle(Request request)
                    => System.Threading.Tasks.Task.FromResult(new Response());
            }
            """;

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    [Test]
    public Task ValidHandler_RegistrationsFile_ContainsAddGeneratedHandlers()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class RegHandler
            {
                public record Request;
                public record Response;
                public System.Threading.Tasks.Task<Response> Handle(Request request)
                    => System.Threading.Tasks.Task.FromResult(new Response());
            }
            """;

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    // ── Sync Handle ────────────────────────────────────────────────────

    [Test]
    public Task SyncHandle_GeneratesCallWithoutAwait()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class SyncHandleHandler
            {
                public record Request(int Value);
                public record Response(int Result);

                public Response Handle(Request request)
                    => new Response(request.Value * 2);
            }
            """;

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    // ── Negative cases ─────────────────────────────────────────────────────

    [Test]
    public Task Handler_NoNestedRequestType_UsesFirstHandleParamAsRequestType()
    {
        // No nested Request type: the first param of Handle determines the request type.
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class NoRequestHandler
            {
                public record Response(string Name);
                public System.Threading.Tasks.Task<Response> Handle(string input)
                    => System.Threading.Tasks.Task.FromResult(new Response(input));
            }
            """;

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    [Test]
    public Task Handler_MissingHandleMethod_ProducesNoDispatcher()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class NoHandleHandler
            {
                public record Request(int Id);
                public record Response(string Name);
            }
            """;

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    // ── Multiple handlers ──────────────────────────────────────────────────

    [Test]
    public Task MultipleHandlers_GeneratesSeparateDispatchersAndSharedRegistrations()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class HandlerA
            {
                public record Request;
                public record Response;
                public System.Threading.Tasks.Task<Response> Handle(Request r)
                    => System.Threading.Tasks.Task.FromResult(new Response());
            }

            [Handler]
            public class HandlerB
            {
                public record Request;
                public record Response;
                public System.Threading.Tasks.Task<Response> Handle(Request r)
                    => System.Threading.Tasks.Task.FromResult(new Response());
            }
            """;

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    [Test]
    public Task DuplicateRequestType_ReportsMBG001Warning_AndOmitsExtensionMethod()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            public record SharedRequest(int Id);

            [Handler]
            public class HandlerOne
            {
                public record Response;
                public System.Threading.Tasks.Task<Response> Handle(SharedRequest request)
                    => System.Threading.Tasks.Task.FromResult(new Response());
            }

            [Handler]
            public class HandlerTwo
            {
                public record Response;
                public System.Threading.Tasks.Task<Response> Handle(SharedRequest request)
                    => System.Threading.Tasks.Task.FromResult(new Response());
            }
            """;

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }
}
