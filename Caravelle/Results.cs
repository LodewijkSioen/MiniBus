namespace Caravelle;

public interface IValidationResult
{
    bool IsValid();
}

public class ValidationResult<TError> : List<TError>, IValidationResult
{
    public bool IsValid() => Count == 0;
}

public class ValidationResult : ValidationResult<ValidationError>
{
}

public sealed record NotFoundResult(string Message) : IValidationResult
{
    public bool IsValid() => false;
}

public record ValidationError(string Message, string? Code = null);
