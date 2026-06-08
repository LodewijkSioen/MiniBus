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
    public Task FullPipeline_BeforeNamingConventions_AsyncHandle()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;

            [Handler]
            public class BeforeConventionPipelineHandler
            {
                public record Request(int Id);
                public record Entity(int Id, string Name);
                public record Prepared(string Name);
                public record Response(string Name);

                public Entity BeforeLoad(Request request)
                    => new Entity(request.Id, "item");

                public Prepared NormalizeBefore(Entity entity)
                    => new Prepared(entity.Name);

                public ValidationResult Validate(Prepared prepared)
                    => new ValidationResult();

                public System.Threading.Tasks.Task<Response> Handle(Prepared prepared)
                    => System.Threading.Tasks.Task.FromResult(new Response(prepared.Name));
            }
            """;

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    [Test]
    public Task FullPipeline_ExecuteAsyncHandleAlias()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;

            [Handler]
            public class ExecuteAsyncPipelineHandler
            {
                public record Request(int Id);
                public record Entity(int Id, string Name);
                public record Response(string Name);

                public Entity Load(Request request)
                    => new Entity(request.Id, "item");

                public ValidationResult Validate(Entity entity)
                    => new ValidationResult();

                public System.Threading.Tasks.Task<Response> ExecuteAsync(Entity entity)
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
    public Task MissingHandleMethodAliases_ProducesNoOutput()
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
    public void UnmatchedHandleParameter_IsResolvedFromServices_AndDispatcherIsGenerated()
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

        Assert.That(result.GeneratedSources.Any(s => s.Contains("InvalidHandleHandlerDispatcher", StringComparison.Ordinal)), Is.True);
        var dispatcher = result.GeneratedSources.Single(s => s.Contains("class InvalidHandleHandlerDispatcher", StringComparison.Ordinal));
        Assert.That(dispatcher.Contains("GetRequiredService<global::TestApp.InvalidHandleHandler.Other>()", StringComparison.Ordinal), Is.True);
    }

    [Test]
    public void UnmatchedValidateParameter_IsResolvedFromServices_AndDispatcherIsGenerated()
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

        Assert.That(result.GeneratedSources.Any(s => s.Contains("InvalidValidateHandlerDispatcher", StringComparison.Ordinal)), Is.True);
        var dispatcher = result.GeneratedSources.Single(s => s.Contains("class InvalidValidateHandlerDispatcher", StringComparison.Ordinal));
        Assert.That(dispatcher.Contains("GetRequiredService<global::TestApp.InvalidValidateHandler.Other>()", StringComparison.Ordinal), Is.True);
    }

    [Test]
    public void DuplicateSameTypeLoadOutputs_ReportsMBG006_AndSkipsDispatcherGeneration()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;

            [Handler]
            public class DuplicateTypeFlowHandler
            {
                public record Request(int Id);
                public record Response(string Value);
                public record Entity(string Value);

                public (Entity primary, Entity secondary) Load(Request request)
                    => (new Entity("a"), new Entity("b"));

                public ValidationResult Validate(Entity first, Entity second)
                    => new ValidationResult();

                public Response Handle(Entity first, Entity second)
                    => new Response(first.Value + second.Value);
            }
            """;

        var result = GeneratorTestHelper.Run(source);

        Assert.That(result.Diagnostics.Any(d => d.Id == "MBG006"), Is.True);
        Assert.That(result.GeneratedSources.Any(s => s.Contains("DuplicateTypeFlowHandlerDispatcher", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public void WrongValidateReturnType_IsIgnored_AndDispatcherIsGenerated()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;

            [Handler]
            public class WrongValidateReturnHandler
            {
                public record Request(int Id);
                public record Response(string Value);

                public string Validate(Request request) => "ignored";

                public Response Handle(Request request)
                    => new Response(request.Id.ToString());
            }
            """;

        var result = GeneratorTestHelper.Run(source);

        Assert.That(result.Diagnostics.Any(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error), Is.False);
        Assert.That(result.GeneratedSources.Any(s => s.Contains("WrongValidateReturnHandlerDispatcher", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void VoidLoad_ReportsMBG007_AndSkipsDispatcherGeneration()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;

            [Handler]
            public class VoidLoadHandler
            {
                public record Request(int Id);
                public record Response(string Value);

                public void Load(Request request) { }

                public Response Handle(Request request)
                    => new Response(request.Id.ToString());
            }
            """;

        var result = GeneratorTestHelper.Run(source);

        Assert.That(result.Diagnostics.Any(d => d.Id == "MBG007"), Is.True);
        Assert.That(result.GeneratedSources.Any(s => s.Contains("VoidLoadHandlerDispatcher", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public void MultipleInvalidPreHandleReturns_ReportMBG007ForEach_AndSkipDispatcherGeneration()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;

            [Handler]
            public class InvalidPreReturnsHandler
            {
                public record Request(int Id);
                public record Response(string Value);

                public void BeforeLoad(Request request) { }

                public System.Threading.Tasks.Task NormalizeBefore(Request request)
                    => System.Threading.Tasks.Task.CompletedTask;

                public Response Handle(Request request)
                    => new Response(request.Id.ToString());
            }
            """;

        var result = GeneratorTestHelper.Run(source);

        var unsupportedReturnDiagnostics = result.Diagnostics
            .Where(d => d.Id == "MBG007")
            .Select(d => d.GetMessage())
            .ToArray();

        Assert.That(unsupportedReturnDiagnostics.Any(message => message.Contains("BeforeLoad", StringComparison.Ordinal)), Is.True);
        Assert.That(unsupportedReturnDiagnostics.Any(message => message.Contains("NormalizeBefore", StringComparison.Ordinal)), Is.True);
        Assert.That(result.GeneratedSources.Any(s => s.Contains("InvalidPreReturnsHandlerDispatcher", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public void TaskHandleWithoutResult_ReportsMBG007_AndSkipsDispatcherGeneration()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;

            [Handler]
            public class TaskHandleHandler
            {
                public record Request(int Id);

                public System.Threading.Tasks.Task Handle(Request request)
                    => System.Threading.Tasks.Task.CompletedTask;
            }
            """;

        var result = GeneratorTestHelper.Run(source);

        Assert.That(result.Diagnostics.Any(d => d.Id == "MBG007"), Is.True);
        Assert.That(result.GeneratedSources.Any(s => s.Contains("TaskHandleHandlerDispatcher", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public void CyclicLoadValidateDependencies_ReportMBG008_AndSkipDispatcherGeneration()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;

            [Handler]
            public class CyclicPipelineHandler
            {
                public record Request(int Id);
                public record Response(string Value);
                public record Entity(int Id);

                public Entity? Load(ValidationResult validation)
                    => null;

                public ValidationResult Validate(Entity entity)
                    => new ValidationResult();

                public Response Handle(Entity entity)
                    => new Response(entity.Id.ToString());
            }
            """;

        var result = GeneratorTestHelper.Run(source);

        Assert.That(result.Diagnostics.Any(d => d.Id == "MBG008"), Is.True);
        Assert.That(result.GeneratedSources.Any(s => s.Contains("CyclicPipelineHandlerDispatcher", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public void DuplicateRequestType_Diagnostics_AreDeterministicallyOrdered()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;

            public record SharedRequest(int Id);

            [Handler]
            public class ZetaHandler
            {
                public record Response;
                public Response Handle(SharedRequest request) => new Response();
            }

            [Handler]
            public class AlphaHandler
            {
                public record Response;
                public Response Handle(SharedRequest request) => new Response();
            }

            [Handler]
            public class BetaHandler
            {
                public record Response;
                public Response Handle(SharedRequest request) => new Response();
            }
            """;

        var result = GeneratorTestHelper.Run(source);

        var mbg001Messages = result.Diagnostics
            .Where(d => d.Id == "MBG001")
            .Select(d => d.GetMessage())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.That(mbg001Messages, Has.Length.EqualTo(3));
        Assert.That(mbg001Messages[0], Does.Contain("AlphaHandler"));
        Assert.That(mbg001Messages[1], Does.Contain("BetaHandler"));
        Assert.That(mbg001Messages[2], Does.Contain("ZetaHandler"));
    }

    [Test]
    public void GenericHandler_ReportsMBG003_AndSkipsDispatcherGeneration()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;

            [Handler]
            public class GenericHandler<T>
            {
                public record Request(int Id);
                public record Response(string Name);
                public Response Handle(Request request) => new Response(request.Id.ToString());
            }
            """;

        var result = GeneratorTestHelper.Run(source);

        Assert.That(result.Diagnostics.Select(d => d.Id), Does.Contain("MBG003"));
        Assert.That(result.GeneratedSources.Any(s => s.Contains("GenericHandlerDispatcher", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public void SameClassNameInDifferentNamespaces_GeneratesBothDispatchers()
    {
        const string source = """
            using MiniBus;

            namespace TestApp.One
            {
                [Handler]
                public class SameNameHandler
                {
                    public record Request(int Id);
                    public record Response(string Name);
                    public Response Handle(Request request) => new Response(request.Id.ToString());
                }
            }

            namespace TestApp.Two
            {
                [Handler]
                public class SameNameHandler
                {
                    public record Request(int Id);
                    public record Response(string Name);
                    public Response Handle(Request request) => new Response(request.Id.ToString());
                }
            }
            """;

        var result = GeneratorTestHelper.Run(source);

        Assert.That(result.Diagnostics.Any(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error), Is.False);
        Assert.That(result.GeneratedSources.Count(s => s.Contains("class SameNameHandlerDispatcher", StringComparison.Ordinal)), Is.EqualTo(2));
        Assert.That(result.GeneratedSources.Any(s => s.Contains("namespace TestApp.One", StringComparison.Ordinal) && s.Contains("class SameNameHandlerDispatcher", StringComparison.Ordinal)), Is.True);
        Assert.That(result.GeneratedSources.Any(s => s.Contains("namespace TestApp.Two", StringComparison.Ordinal) && s.Contains("class SameNameHandlerDispatcher", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void NestedHandler_ReportsMBG004_AndSkipsDispatcherGeneration()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;

            public class Container
            {
                [Handler]
                public class NestedHandler
                {
                    public record Request(int Id);
                    public record Response(string Name);
                    public Response Handle(Request request) => new Response(request.Id.ToString());
                }
            }
            """;

        var result = GeneratorTestHelper.Run(source);

        Assert.That(result.Diagnostics.Select(d => d.Id), Does.Contain("MBG004"));
        Assert.That(result.GeneratedSources.Any(s => s.Contains("NestedHandlerDispatcher", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public void DuplicateRequestResponsePair_ReportsMBG002_AndSkipsDuplicateDispatcherRegistrations()
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

        Assert.That(result.Diagnostics.Any(d => d.Id == "MBG002"), Is.True);
        var registration = result.GeneratedSources.Single(s => s.Contains("class GeneratedHandlerRegistrations", StringComparison.Ordinal));
        Assert.That(registration.Contains("IDispatcher<\n                    global::TestApp.SharedRequest,\n                    global::TestApp.SharedResponse>", StringComparison.Ordinal), Is.False);
    }

    [Test]
    public void ParameterlessLoad_WithFullyMatchedHandleInput_ReportsMBG005_AndSkipsDispatcherGeneration()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;

            [Handler]
            public class NoRequestTypeHandler
            {
                public record Response(string Value);
                public record Entity(string Value);

                public Entity Load() => new Entity("value");

                public Response Handle(Entity entity) => new Response(entity.Value);
            }
            """;

        var result = GeneratorTestHelper.Run(source);

        Assert.That(result.Diagnostics.Any(d => d.Id == "MBG005"), Is.True);
        Assert.That(result.GeneratedSources.Any(s => s.Contains("NoRequestTypeHandlerDispatcher", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public Task StaticHandler_WithDiParameters_GeneratesServiceProviderOnlyDispatcher()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;

            public interface IClock;

            [Handler]
            public class StaticConventionHandler
            {
                public record Request(int Value);
                public record Response(int Value);

                public static ValidationResult Validate(Request request, IClock? clock)
                    => new ValidationResult();

                public static Response Handle(Request request, IClock clock)
                    => new Response(request.Value);
            }
            """;

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    [Test]
    public Task MixedStaticAndInstance_WithDiParameters_GeneratesHandlerAndServiceProvider()
    {
        const string source = """
            using MiniBus;
            namespace TestApp;

            public interface IClock;

            [Handler]
            public class MixedConventionHandler
            {
                public record Request(int Value);
                public record Response(int Value);

                public static ValidationResult Validate(Request request, IClock clock)
                    => new ValidationResult();

                public Response Handle(Request request)
                    => new Response(request.Value);
            }
            """;

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }
}
