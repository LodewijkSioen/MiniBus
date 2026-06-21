namespace Caravelle.Tests;

[TestFixture]
public class ResultTests
{
    [Test]
    public void ValidationResult_ImplementsIValidationResult()
    {
        var errors = new ValidationResult();

        Assert.That(errors, Is.AssignableTo<IValidationResult>());
    }

    [Test]
    public void ValidationResultOfT_ImplementsIValidationResult()
    {
        var errors = new ValidationResult<ValidationError>();

        Assert.That(errors, Is.AssignableTo<IValidationResult>());
    }

    [Test]
    public void NotFoundResult_ImplementsIValidationResult()
    {
        var notFound = new NotFoundResult("missing");

        Assert.That(notFound, Is.AssignableTo<IValidationResult>());
    }

    [Test]
    public void NotFoundResult_IsValid_ReturnsFalse()
    {
        var notFound = new NotFoundResult("missing");

        Assert.That(notFound.IsValid(), Is.False);
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

    [Test]
    public void NotFoundResult_Message_IsStored()
    {
        var notFound = new NotFoundResult("missing");

        Assert.That(notFound.Message, Is.EqualTo("missing"));
    }
}
