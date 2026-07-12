namespace Caravelle.Generator.Tests;

[TestFixture]
public class MiddlewareDiscoveryTests
{
    [Test]
    public void GenericMiddleware_ReportsMBG011()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;

            [Middleware<AllHandlers>]
            public class GenericMiddleware<T>
            {
                public void BeforeLog() { }
            }
            """;

        var result = GeneratorTestHelper.Run(source);

        Assert.That(result.Diagnostics.Any(d => d.Id == "MBG011"), Is.True);
    }

    [Test]
    public void NestedMiddleware_ReportsMBG012()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;

            public class Container
            {
                [Middleware<AllHandlers>]
                public class NestedMiddleware
                {
                    public void BeforeLog() { }
                }
            }
            """;

        var result = GeneratorTestHelper.Run(source);

        Assert.That(result.Diagnostics.Any(d => d.Id == "MBG012"), Is.True);
    }

    [Test]
    public void UnrecognizedFilterType_ReportsMBG013Warning()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;

            public class CustomFilter : IMiddlewareFilter;

            [Middleware<CustomFilter>]
            public class CustomFilterMiddleware
            {
                public string BeforeLog() => "logged";
            }
            """;

        var result = GeneratorTestHelper.Run(source);

        // GeneratorTestHelper.Run concatenates per-result and driver-level diagnostics,
        // so a given diagnostic can legitimately appear more than once — assert presence
        // and severity rather than uniqueness.
        Assert.That(result.Diagnostics.Any(d => d.Id == "MBG013"), Is.True);
        Assert.That(result.Diagnostics.First(d => d.Id == "MBG013").Severity, Is.EqualTo(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning));
    }

    [Test]
    public void RecognizedFilters_ReportNoDiagnostics()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;

            public interface IAdminHandler;

            [Middleware<ForInterface<IAdminHandler>>]
            public class RecognizedFilterMiddleware
            {
                public string BeforeLog() => "logged";
            }

            [Handler]
            public class SimpleHandler : IAdminHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public Response Handle(Request request) => new Response("test");
            }
            """;

        var result = GeneratorTestHelper.Run(source);

        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void AllHandlersFilter_ReportsNoDiagnostics()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;

            [Middleware<AllHandlers>]
            public class GloballyAppliedMiddleware
            {
                public string BeforeLog() => "logged";
            }

            [Handler]
            public class SimpleHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public Response Handle(Request request) => new Response("test");
            }
            """;

        var result = GeneratorTestHelper.Run(source);

        Assert.That(result.Diagnostics, Is.Empty);
    }
}
