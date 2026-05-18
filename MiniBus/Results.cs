using System.Diagnostics.CodeAnalysis;

namespace MiniBus;

public enum ResultStatus
{
    Ok,
    NotFound,
    Invalid
}

public class Result<TResponse>
{
    private Result()
    {
        ValidationErrors = new ValidationResult();
    }

    [MemberNotNullWhen(true, nameof(Response))]
    public bool IsSuccess => Status == ResultStatus.Ok;

    public ResultStatus Status { get; init; }

    public TResponse? Response { get; init; }

    public ValidationResult ValidationErrors { get; init; }

    public static Result<TResponse> Success(TResponse response) => new()
    {
        Status = ResultStatus.Ok,
        Response = response
    };

    public static Result<TResponse> NotFound(string? message = null) => new()
    {
        Status = ResultStatus.NotFound,
        ValidationErrors = message is null
            ? new ValidationResult()
            : new ValidationResult { new ValidationError(message) }
    };

    public static Result<TResponse> Invalid(ValidationResult errors) => new()
    {
        Status = ResultStatus.Invalid,
        ValidationErrors = errors
    };
}

public class ValidationResult : List<ValidationError>
{
    public bool IsValid() => Count == 0;
}

public record ValidationError(string Message, string? Code = null);
