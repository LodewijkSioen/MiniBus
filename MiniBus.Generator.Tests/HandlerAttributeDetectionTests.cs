namespace MiniBus.Generator.Tests;

/// <summary>
/// Verifies that the generator correctly triggers (or skips) based on the [Handler] attribute.
/// </summary>
[TestFixture]
public class HandlerAttributeDetectionTests
{
    [Test]
    public void EmptySource_ProducesNoOutput()
    {
        var (sources, diagnostics) = GeneratorTestHelper.Run(string.Empty);

        Assert.That(sources, Is.Empty);
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void ClassWithoutHandlerAttribute_ProducesNoOutput()
    {
        const string source = """
            namespace TestApp;
            public class MyClass { }
            """;

        var (sources, _) = GeneratorTestHelper.Run(source);

        Assert.That(sources, Is.Empty);
    }

    [Test]
    public void HandlerAttributePresent_GeneratesOutput()
    {
        const string source = """
            using MiniBus.Convention;
            namespace TestApp;

            [Handler]
            public class DummyHandler
            {
                public record Request;
                public record Response;
                public System.Threading.Tasks.Task<Response> Handle(Request request)
                    => System.Threading.Tasks.Task.FromResult(new Response());
            }
            """;

        var (sources, diagnostics) = GeneratorTestHelper.Run(source);

        Assert.That(sources, Is.Not.Empty);
        Assert.That(diagnostics, Is.Empty);
    }
}
