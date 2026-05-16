using Microsoft.Extensions.DependencyInjection;

namespace MiniBus.Tests;

public class TestMiniBus
{
    [Test]
    public async Task TestHappy()
    {
        using var scope = AppUnderTest.Services.CreateScope();

        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();
        var result = await bus.Handle<TestHandler.Request, TestHandler.Response>(new(1));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Response!.WasLoaded, Is.True);
    }

    [Test]
    public async Task TestNotFound()
    {
        using var scope = AppUnderTest.Services.CreateScope();

        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();
        var result = await bus.Handle<TestHandler.Request, TestHandler.Response>(new(666));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Status, Is.EqualTo(ResultStatus.NotFound));
    }

    [Test]
    public async Task TestInvalid()
    {
        using var scope = AppUnderTest.Services.CreateScope();

        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();
        var result = await bus.Handle<TestHandler.Request, TestHandler.Response>(new(-1));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Status, Is.EqualTo(ResultStatus.Invalid));
        Assert.That(result.ValidationResult, Is.EqualTo([
            new ValidationError("Cannot be less than null")
        ]));
    }

    [Test]
    public async Task TestInvalidAsync()
    {
        using var scope = AppUnderTest.Services.CreateScope();

        var bus = scope.ServiceProvider.GetRequiredService<MiniBus>();
        var result = await bus.Handle<TestHandler.Request, TestHandler.Response>(new(101));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Status, Is.EqualTo(ResultStatus.Invalid));
        Assert.That(result.ValidationResult, Is.EqualTo([
            new ValidationError("Cannot be less than null, but async")
        ]));
    }
}

public class TestHandler : 
    IHandler<TestHandler.Request, TestHandler.Response>, 
    ILoader<TestHandler.Request>,
    IValidator<TestHandler.Request>,
    IAsyncValidator<TestHandler.Request>
{
    public record Request(int Counter) : IRequest<Response>;
    public record Response(bool WasLoaded);

    private bool _isLoaded;

    public Task<LoadResult> Load(Request request)
    {
        _isLoaded = true;
        return Task.FromResult(request.Counter == 666 ? LoadResult.NotFound("fail") : LoadResult.Ok);
    }

    public Task<ValidationResult> Validate(Request request)
    {
        ValidationResult errors = request.Counter > 100
            ? [new("Cannot be less than null, but async")]
            : [];
        return Task.FromResult(errors);
    }

    ValidationResult IValidator<Request>.Validate(Request request)
    {
        return request.Counter < 0
            ? [new("Cannot be less than null")]
            : [];
    }

    public Task<Response> Handle(Request request)
    {
        return Task.FromResult(new Response(_isLoaded));
    }
}