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
        serviceCollection.AddMiniBusCore();
        serviceCollection.AddScoped<IScopeProbe, ScopeProbe>();
        serviceCollection.AddGeneratedHandlers();
        Services = serviceCollection.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        Services.Dispose();
    }
}

public interface IScopeProbe
{
    Guid ScopeId { get; }
}

public sealed class ScopeProbe : IScopeProbe
{
    public Guid ScopeId { get; } = Guid.NewGuid();
}
