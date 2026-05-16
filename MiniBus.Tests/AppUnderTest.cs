using Microsoft.Extensions.DependencyInjection;
using MiniBus.Convention;

namespace MiniBus.Tests;

[SetUpFixture]
public class AppUnderTest
{
    public static ServiceProvider Services { get; private set; } = null!;
    public static ServiceProvider ConventionServices { get; private set; } = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddMinibus(GetType().Assembly);
        Services = serviceCollection.BuildServiceProvider();

        var conventionCollection = new ServiceCollection();
        conventionCollection.AddConventionBus();
        conventionCollection.AddGeneratedHandlers();
        ConventionServices = conventionCollection.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        Services.Dispose();
        ConventionServices.Dispose();
    }
}
