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
}
