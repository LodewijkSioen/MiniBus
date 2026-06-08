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
        serviceCollection.AddScoped<IFinallyExecutionProbe, FinallyExecutionProbe>();
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

public interface IFinallyExecutionProbe
{
    IReadOnlyList<FinallyObservation> Observations { get; }
    void Record(FinallyObservation observation);
}

public sealed record FinallyObservation(
    string Mode,
    bool RequestSeen,
    bool EntitySeen,
    bool ValidationSeen,
    bool ValidationWasValid,
    bool ResponseSeen);

public sealed class FinallyExecutionProbe : IFinallyExecutionProbe
{
    private readonly List<FinallyObservation> _observations = [];

    public IReadOnlyList<FinallyObservation> Observations => _observations;

    public void Record(FinallyObservation observation)
    {
        _observations.Add(observation);
    }
}
