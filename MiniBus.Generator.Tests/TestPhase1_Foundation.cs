namespace MiniBus.Generator.Tests;

/// <summary>
/// Phase 1 — verifies the skeleton generator compiles and produces no output.
/// Real generation tests are added in subsequent phases.
/// </summary>
[TestFixture]
public class TestPhase1_Foundation
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
    public void HandlerAttributePresent_SkeletonStillProducesNoOutput()
    {
        // In Phase 1 the generator is a stub — even a properly attributed class
        // should produce nothing yet.  This test will be updated in Phase 2.
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

        Assert.That(sources, Is.Empty);
        Assert.That(diagnostics, Is.Empty);
    }
}
