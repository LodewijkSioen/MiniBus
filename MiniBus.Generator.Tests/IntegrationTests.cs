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

    [Test]
    public Task MultipleHandlers_GenerateSeparateDispatchersAndSharedRegistrations()
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

    [Test]
    public Task MissingHandleMethod_ProducesNoOutput()
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
}
