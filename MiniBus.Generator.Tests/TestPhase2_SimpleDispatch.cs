namespace MiniBus.Generator.Tests;

[TestFixture]
public class TestPhase2_SimpleDispatch
{
    // ── Dispatcher generation ──────────────────────────────────────────────

    [Test]
    public void ValidHandler_GeneratesTwoFiles_DispatcherAndRegistrations()
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

        var (sources, diagnostics) = GeneratorTestHelper.Run(source);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(sources, Has.Count.EqualTo(2));
    }

    [Test]
    public void ValidHandler_DispatcherFile_ImplementsIConventionHandler()
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

        var (sources, _) = GeneratorTestHelper.Run(source);

        var dispatcher = sources.Single(s => s.Contains("class OrderHandlerDispatcher"));
        Assert.That(dispatcher, Does.Contain("IConventionHandler"));
        Assert.That(dispatcher, Does.Contain("OrderHandler.Request"));
        Assert.That(dispatcher, Does.Contain("OrderHandler.Response"));
        Assert.That(dispatcher, Does.Contain("Result"));
        Assert.That(dispatcher, Does.Contain("Success(response)"));
    }

    [Test]
    public void ValidHandler_DispatcherFile_ContainsTypedExtensionMethod()
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

        var (sources, _) = GeneratorTestHelper.Run(source);

        var dispatcher = sources.Single(s => s.Contains("class ExtHandlerDispatcher"));
        Assert.That(dispatcher, Does.Contain("ConventionBus bus"));
        Assert.That(dispatcher, Does.Contain("ExtHandlerExtensions"));
    }

    [Test]
    public void ValidHandler_RegistrationsFile_ContainsAddGeneratedHandlers()
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

        var (sources, _) = GeneratorTestHelper.Run(source);

        var registrations = sources.Single(s => s.Contains("AddGeneratedHandlers"));
        Assert.That(registrations, Does.Contain("RegHandler"));
        Assert.That(registrations, Does.Contain("RegHandlerDispatcher"));
        Assert.That(registrations, Does.Contain("AddScoped"));
    }

    // ── Sync Handle ────────────────────────────────────────────────────

    [Test]
    public void SyncHandle_GeneratesCallWithoutAwait()
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

        var (sources, diagnostics) = GeneratorTestHelper.Run(source);

        Assert.That(diagnostics, Is.Empty);
        var dispatcher = sources.Single(s => s.Contains("class SyncHandleHandlerDispatcher"));
        Assert.That(dispatcher, Does.Not.Contain("await _handler.Handle"));
        Assert.That(dispatcher, Does.Contain("_handler.Handle(request)"));
    }

    // ── Negative cases ─────────────────────────────────────────────────────

    [Test]
    public void Handler_NoNestedRequestType_UsesFirstHandleParamAsRequestType()
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

        var (sources, _) = GeneratorTestHelper.Run(source);

        var dispatcher = sources.Single(s => s.Contains("class NoRequestHandlerDispatcher"));
        Assert.That(dispatcher, Does.Contain("Handle(string request)"));
    }

    [Test]
    public void Handler_MissingHandleMethod_ProducesNoDispatcher()
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

        var (sources, _) = GeneratorTestHelper.Run(source);

        Assert.That(sources.Any(s => s.Contains("NoHandleHandlerDispatcher")), Is.False);
    }

    // ── Multiple handlers ──────────────────────────────────────────────────

    [Test]
    public void MultipleHandlers_GeneratesSeparateDispatchersAndSharedRegistrations()
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

        var (sources, diagnostics) = GeneratorTestHelper.Run(source);

        Assert.That(diagnostics, Is.Empty);
        // Two dispatcher files + one registrations file
        Assert.That(sources, Has.Count.EqualTo(3));
        Assert.That(sources.Any(s => s.Contains("HandlerADispatcher")), Is.True);
        Assert.That(sources.Any(s => s.Contains("HandlerBDispatcher")), Is.True);

        var registrations = sources.Single(s => s.Contains("AddGeneratedHandlers"));
        Assert.That(registrations, Does.Contain("HandlerA"));
        Assert.That(registrations, Does.Contain("HandlerB"));
    }
}
