namespace MiniBus.Generator.Tests;

/// <summary>
/// End-to-end tests that run the full source generator pipeline via
/// <see cref="GeneratorTestHelper.RunDriver"/> and snapshot the complete output.
/// Permutation coverage (all HandlerModel combinations) lives in
/// DispatcherSourceBuilderTests and RegistrationsSourceBuilderTests.
/// </summary>
[TestFixture]
public class IntegrationTests
{
    [Test]
    public Task FullPipeline_AsyncLoad_Validate_AsyncHandle()
    {
        const string source = """
            using MiniBus;
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

    [Test]
    public Task MultipleHandlers_GenerateSeparateDispatchersAndSharedRegistrations()
    {
        const string source = """
            using MiniBus;
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
            using MiniBus;
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

    [Test]
    public Task MissingHandleMethod_ProducesNoOutput()
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

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    [Test]
    public void UnsupportedHandleParameter_ReportsMBG002_AndSkipsDispatcherGeneration()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;

            [Handler]
            public class InvalidHandleHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public record Other(int Id);
                public Response Handle(Request request, Other other) => new Response(other.Id.ToString());
            }
            """;

        var result = GeneratorTestHelper.Run(source);

        Assert.That(result.Diagnostics.Any(d => d.Id == "MBG002"), Is.True);
        Assert.That(result.GeneratedSources.Any(s => s.Contains("InvalidHandleHandlerDispatcher", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public void UnsupportedValidateParameter_ReportsMBG002_AndSkipsDispatcherGeneration()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;

            [Handler]
            public class InvalidValidateHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public record Other(int Id);
                public ValidationResult Validate(Request request, Other other) => new ValidationResult();
                public Response Handle(Request request) => new Response(request.Id.ToString());
            }
            """;

        var result = GeneratorTestHelper.Run(source);

        Assert.That(result.Diagnostics.Any(d => d.Id == "MBG002"), Is.True);
        Assert.That(result.GeneratedSources.Any(s => s.Contains("InvalidValidateHandlerDispatcher", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public void DuplicateRequestResponsePair_ReportsMBG003_AndSkipsDuplicateDispatcherRegistrations()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;

            public record SharedRequest(int Id);
            public record SharedResponse(string Name);

            [Handler]
            public class HandlerOne
            {
                public SharedResponse Handle(SharedRequest request) => new("A");
            }

            [Handler]
            public class HandlerTwo
            {
                public SharedResponse Handle(SharedRequest request) => new("B");
            }
            """;

        var result = GeneratorTestHelper.Run(source);

        Assert.That(result.Diagnostics.Any(d => d.Id == "MBG003"), Is.True);
        var registration = result.GeneratedSources.Single(s => s.Contains("class GeneratedHandlerRegistrations", StringComparison.Ordinal));
        Assert.That(registration.Contains("IDispatcher<\n                    global::TestApp.SharedRequest,\n                    global::TestApp.SharedResponse>", StringComparison.Ordinal), Is.False);
    }
}
