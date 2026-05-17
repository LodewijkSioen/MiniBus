namespace MiniBus.Generator.Tests;

/// <summary>
/// Verifies that the generator correctly triggers (or skips) based on the [Handler] attribute.
/// </summary>
[TestFixture]
public class HandlerAttributeDetectionTests
{
    [Test]
    public Task EmptySource_ProducesNoOutput()
    {
        var driver = GeneratorTestHelper.RunDriver(string.Empty);
        return Verify(driver);
    }

    [Test]
    public Task ClassWithoutHandlerAttribute_ProducesNoOutput()
    {
        const string source = """
            namespace TestApp;
            public class MyClass { }
            """;

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }

    [Test]
    public Task HandlerAttributePresent_GeneratesOutput()
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

        var driver = GeneratorTestHelper.RunDriver(source);
        return Verify(driver);
    }
}
