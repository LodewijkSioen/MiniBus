using Microsoft.Extensions.DependencyInjection;

namespace MiniBus.Tests;

[SetUpFixture]
public class AppUnderTest
{
    public static ServiceProvider Services { get; private set; } = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddMinibus(GetType().Assembly);

        Services = serviceCollection.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        Services.Dispose();
    }
}