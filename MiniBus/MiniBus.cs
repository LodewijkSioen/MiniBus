using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace MiniBus;

public static class MiniBusExtensions
{
    /// <summary>
    /// Add the MiniBus to your servicecollection.
    /// - Handlers are scoped (one per httprequest)
    /// - Bus is transient
    /// </summary>
    public static IServiceCollection AddMinibus(this IServiceCollection services, params Assembly[] assemblies)
    {
        services.Scan(s => s
            .FromAssemblies(assemblies)
            .AddClasses(c => c.AssignableTo(typeof(IHandler<,>)))
            .As(t => t.GetInterfaces().Where(i => i.GetGenericTypeDefinition() == typeof(IHandler<,>)))
            .WithScopedLifetime()
        );
        services.AddTransient<MiniBus>();

        return services;
    }
}

/// <summary>
/// Cheap Mediater knockoff to quickly cleanup your controllers
/// </summary>
public class MiniBus(IServiceProvider services)
{
    public async Task<Result<TResponse>> Handle<TRequest, TResponse>(TRequest request)
        where TRequest : IRequest<TResponse>
    {
        var handler = services.GetRequiredService<IHandler<TRequest, TResponse>>();

        if (handler is ILoader<TRequest> loader)
        {
            var loadResult = await loader.Load(request);
            if (loadResult.IsNotFound)
            {
                return Result<TResponse>.NotFound(loadResult.Message);
            }
        }

        if (handler is IValidator<TRequest> validator)
        {
            var result = validator.Validate(request);
            if (!result.IsValid())
            {
                return Result<TResponse>.Invalid(result);
            }
        }

        if (handler is IAsyncValidator<TRequest> asyncValidator)
        {
            var result = await asyncValidator.Validate(request);
            if (!result.IsValid())
            {
                return Result<TResponse>.Invalid(result);
            }
        }

        var response = await handler.Handle(request);
        return Result<TResponse>.Success(response);
    }
}

/// <summary>
/// Use this interface on your handlers to load additional data.
/// </summary>
/// <returns>If you return a LoadResult with an ErrorValue, the execution of the handler will stop and a NotFound is generated</returns>
public interface ILoader<in TRequest>
{
    public Task<LoadResult> Load(TRequest request);
}

public record LoadResult
{
    [MemberNotNullWhen(true, nameof(Message))]
    public bool IsNotFound => Message is not null;

    public string? Message { get; private init; }

    public static LoadResult NotFound(string message) => new()
    {
        Message = message
    };

    public static readonly LoadResult Ok = new();
}

/// <summary>
/// Use this interface to validate your request asynchronously
/// </summary>
/// <returns>If a non-empty ValidationResult is returned, the execution of the handlers will stop and a BadRequest is generated.</returns>
public interface IAsyncValidator<in TRequest>
{
    public Task<ValidationResult> Validate(TRequest request);
}

/// <summary>
/// Use this interface to validate your request
/// </summary>
/// <returns>If a non-empty ValidationResult is returned, the execution of the handlers will stop and a BadRequest is generated.</returns>
public interface IValidator<in TRequest>
{
    public ValidationResult Validate(TRequest request);
}

public class ValidationResult : List<ValidationError>
{
    public bool IsValid() => Count == 0;
}

public record ValidationError(string Message, string? Code = null);

/// <summary>
/// The handler interface. This is the workhorse that will handle your request and return your response.
/// </summary>
public interface IHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public Task<TResponse> Handle(TRequest request);
}

// ReSharper disable once UnusedTypeParameter
/// <summary>
/// TResponse is a marker to ensure the correct response type
/// </summary>
public interface IRequest<out TResponse>;

public enum ResultStatus
{
    Ok,
    NotFound,
    Invalid
}

/// <summary>
/// Response object will wrap the result of the handler. If something went wrong during loading or validation,
/// this will be reflected in the `Status` enum
/// </summary>
public class Result<TResponse>
{
    private Result()
    {
        ValidationResult = [];
    }

    [MemberNotNullWhen(true, nameof(Response))]
    public bool IsSuccess => Status == ResultStatus.Ok;

    public ResultStatus Status { get; init; }

    public TResponse? Response { get; init; }

    public ValidationResult ValidationResult { get; init; }

    internal static Result<TResponse> Success(TResponse response)
    {
        return new()
        {
            Status = ResultStatus.Ok,
            Response = response
        };
    }

    internal static Result<TResponse> Invalid(ValidationResult result)
    {
        return new()
        {
            Status = ResultStatus.Invalid,
            ValidationResult = result
        };
    }

    internal static Result<TResponse> NotFound(string message)
    {
        return new()
        {
            Status = ResultStatus.NotFound,
            ValidationResult = [new(message)]
        };
    }
}


public static class MinibusExtentions
{
    extension<TRequest>(ILoader<TRequest> source)
    {
        /// <summary>
        /// This function will help with nullability in loader code
        /// </summary>
        public bool TryAssign<T>(T? value, ref T assignment)
        {
            if (value is not null)
            {
                assignment = value;
                return true;
            }

            return false;
        }
    }
}