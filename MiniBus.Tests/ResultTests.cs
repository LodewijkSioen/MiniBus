namespace MiniBus.Tests;

[TestFixture]
public class ResultTests
{
    [Test]
    public void Invalid_NullErrors_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Result<int>.Invalid(null!));
    }

    [Test]
    public void Success_NullReference_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Result<string>.Success(null!));
    }

    [Test]
    public void Success_ValueType_DoesNotThrow()
    {
        var result = Result<int>.Success(42);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Response, Is.EqualTo(42));
    }

    [Test]
    public void NotFound_WithoutMessage_HasNoValidationErrors()
    {
        var result = Result<string>.NotFound();

        Assert.That(result.Status, Is.EqualTo(ResultStatus.NotFound));
        Assert.That(result.ValidationErrors, Has.Count.EqualTo(0));
    }

    [Test]
    public void NotFound_WithMessage_AddsNotFoundValidationError()
    {
        var result = Result<string>.NotFound("missing");

        Assert.That(result.Status, Is.EqualTo(ResultStatus.NotFound));
        Assert.That(result.ValidationErrors, Has.Count.EqualTo(1));
        Assert.That(result.ValidationErrors[0].Message, Is.EqualTo("missing"));
        Assert.That(result.ValidationErrors[0].Code, Is.EqualTo("notfound"));
    }

    [Test]
    public void Invalid_WithErrors_ReturnsInvalidResultWithSameErrorsInstance()
    {
        var errors = new ValidationResult
        {
            new ValidationError("bad", "BAD")
        };

        var result = Result<string>.Invalid(errors);

        Assert.That(result.Status, Is.EqualTo(ResultStatus.Invalid));
        Assert.That(ReferenceEquals(result.ValidationErrors, errors), Is.True);
    }

    [Test]
    public void ValidationResult_IsValid_ReturnsTrueWhenEmpty()
    {
        var errors = new ValidationResult();

        Assert.That(errors.IsValid(), Is.True);
    }

    [Test]
    public void ValidationResult_IsValid_ReturnsFalseWhenContainingErrors()
    {
        var errors = new ValidationResult
        {
            new ValidationError("bad")
        };

        Assert.That(errors.IsValid(), Is.False);
    }
}
