using UrlShortener.BuildingBlocks.Results;

namespace UrlShortener.Domain.UnitTests.BuildingBlocks;

public sealed class ResultTests
{
    private static readonly Error SomeError = new("Some.Error", "Something went wrong", ErrorType.Conflict);

    [Fact]
    public void Success_WithValue_IsSuccessAndExposesValue()
    {
        var result = Result<int>.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Failure_WithError_IsFailureAndExposesError()
    {
        var result = Result<int>.Failure(SomeError);

        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SomeError);
    }

    [Fact]
    public void Value_OnFailure_Throws()
    {
        var result = Result<int>.Failure(SomeError);

        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Error_OnSuccess_Throws()
    {
        var result = Result<int>.Success(1);

        Should.Throw<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void ImplicitConversion_FromValue_IsSuccess()
    {
        Result<string> result = "ok";

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("ok");
    }

    [Fact]
    public void ImplicitConversion_FromError_IsFailure()
    {
        Result<string> result = SomeError;

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SomeError);
    }

    [Fact]
    public void Success_WithNullValue_Throws()
    {
        Should.Throw<ArgumentNullException>(() => Result<string>.Success(null!));
    }
}

public sealed class ErrorTests
{
    [Fact]
    public void Error_WithoutDetails_HasNullDetails()
    {
        new Error("Some.Error", "desc", ErrorType.Failure).Details.ShouldBeNull();
    }

    [Fact]
    public void Error_WithDetails_ExposesFieldMessages()
    {
        var details = new Dictionary<string, string[]> { ["OriginalUrl"] = ["must be a URL"] };

        var error = new Error("Validation.Failed", "invalid", ErrorType.Validation, details);

        error.Details.ShouldNotBeNull()["OriginalUrl"].ShouldBe(["must be a URL"]);
    }
}
