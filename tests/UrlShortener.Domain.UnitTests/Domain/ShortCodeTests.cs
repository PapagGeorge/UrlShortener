using UrlShortener.BuildingBlocks.Results;
using UrlShortener.Domain;

namespace UrlShortener.Domain.UnitTests.Domain;

public sealed class ShortCodeTests
{
    [Theory]
    [InlineData("0aZ9xYb")]                          // generated: exactly 7, base62
    [InlineData("0000000")]
    [InlineData("zzzzzzz")]
    [InlineData("my-alias")]                         // alias: 8 chars
    [InlineData("my_alias_1")]
    [InlineData("AbCdEfGh-_")]
    [InlineData("123456789012345678901234567890")]  // alias: 30 chars
    public void TryParse_ValidCode_ReturnsTrueAndPreservesValue(string input)
    {
        var parsed = ShortCode.TryParse(input, out var code);

        parsed.ShouldBeTrue();
        code.Value.ShouldBe(input);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abcdef")]                            // 6: too short
    [InlineData("abc-def")]                           // 7 with '-': generated codes are base62 only
    [InlineData("abc_def")]
    [InlineData("1234567890123456789012345678901")]   // 31: too long
    [InlineData("my alias")]                          // space
    [InlineData("abc/defg")]
    [InlineData("αααααααα")]                          // non-ASCII letters
    [InlineData("abcdefg\n")]
    public void TryParse_InvalidCode_ReturnsFalse(string? input)
    {
        var parsed = ShortCode.TryParse(input, out _);

        parsed.ShouldBeFalse();
    }

    [Fact]
    public void Create_ValidCode_Succeeds()
    {
        var result = ShortCode.Create("my-alias");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe("my-alias");
    }

    [Fact]
    public void Create_InvalidCode_FailsWithValidationError()
    {
        var result = ShortCode.Create("nope");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ShortCode.Invalid");
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Equality_IsCaseSensitive()
    {
        ShortCode.Create("Abcdefgh").Value.ShouldNotBe(ShortCode.Create("abcdefgh").Value);
        ShortCode.Create("Abcdefgh").Value.ShouldBe(ShortCode.Create("Abcdefgh").Value);
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        ShortCode.Create("my-alias").Value.ToString().ShouldBe("my-alias");
    }

    [Theory]
    [InlineData("swagger", true)]
    [InlineData("OpenApi", true)]
    [InlineData("0aZ9xYb", false)]
    public void IsReserved_MatchesReservedWordsCaseInsensitively(string input, bool expected)
    {
        ShortCode.Create(input).Value.IsReserved.ShouldBe(expected);
    }
}
