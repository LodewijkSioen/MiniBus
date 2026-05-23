using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace MiniBus.Tests;

// ── Handler under test ─────────────────────────────────────────────────────────

[Handler]
public class ThrowingHandler
{
    public record Request();
    public record Response();

    public Task<Response> Handle(Request request) =>
        throw new InvalidOperationException("test error");
}

// ── Tests ──────────────────────────────────────────────────────────────────────

[TestFixture]
public class TracingTests
{
    private List<Activity> _activities = null!;
    private ActivityListener _listener = null!;

    [SetUp]
    public void SetUp()
    {
        _activities = [];
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "MiniBus",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _activities.Add(activity)
        };
        ActivitySource.AddActivityListener(_listener);
    }

    [TearDown]
    public void TearDown()
    {
        _listener.Dispose();
    }

    [Test]
    public async Task SuccessfulDispatch_ActivityHasCorrectNameAndTags()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        await bus.Handle(new SimpleHandler.Request(5));

        Assert.That(_activities, Has.Count.EqualTo(1));
        var activity = _activities[0];
        Assert.That(activity.OperationName, Is.EqualTo("minibus.dispatch SimpleHandler"));
        Assert.That(activity.GetTagItem("minibus.request.type"), Is.EqualTo(typeof(SimpleHandler.Request).FullName));
        Assert.That(activity.GetTagItem("minibus.response.type"), Is.EqualTo(typeof(SimpleHandler.Response).FullName));
        Assert.That(activity.GetTagItem("minibus.result.status"), Is.EqualTo("Ok"));
        Assert.That(activity.Status, Is.EqualTo(ActivityStatusCode.Unset));
    }

    [Test]
    public async Task NotFoundResult_SpanStatusIsNotError()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        await bus.Handle(new NullableLoadHandler.Request(ReturnNull: true));

        Assert.That(_activities, Has.Count.EqualTo(1));
        var activity = _activities[0];
        Assert.That(activity.GetTagItem("minibus.result.status"), Is.EqualTo("NotFound"));
        Assert.That(activity.Status, Is.EqualTo(ActivityStatusCode.Unset));
    }

    [Test]
    public async Task InvalidResult_SpanStatusIsNotError()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        await bus.Handle(new SyncValidatingHandler.Request(Value: ""));

        Assert.That(_activities, Has.Count.EqualTo(1));
        var activity = _activities[0];
        Assert.That(activity.GetTagItem("minibus.result.status"), Is.EqualTo("Invalid"));
        Assert.That(activity.Status, Is.EqualTo(ActivityStatusCode.Unset));
    }

    [Test]
    public void UnhandledException_SetsErrorStatusAndRecordsExceptionEvent()
    {
        using var scope = AppUnderTest.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();

        Assert.ThrowsAsync<InvalidOperationException>(
            () => bus.Handle(new ThrowingHandler.Request()));

        Assert.That(_activities, Has.Count.EqualTo(1));
        var activity = _activities[0];
        Assert.That(activity.Status, Is.EqualTo(ActivityStatusCode.Error));

        var exceptionEvent = activity.Events.FirstOrDefault(e => e.Name == "exception");
        Assert.That(exceptionEvent, Is.Not.EqualTo(default(ActivityEvent)));
        Assert.That(
            exceptionEvent.Tags.FirstOrDefault(t => t.Key == "exception.type").Value,
            Is.EqualTo(typeof(InvalidOperationException).FullName));
        Assert.That(
            exceptionEvent.Tags.FirstOrDefault(t => t.Key == "exception.message").Value,
            Is.EqualTo("test error"));
        Assert.That(
            exceptionEvent.Tags.FirstOrDefault(t => t.Key == "exception.stacktrace").Value,
            Is.Not.Null.And.Not.Empty);
    }
}
