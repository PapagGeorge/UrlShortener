using UrlShortener.BuildingBlocks.Results;
using UrlShortener.Domain;

namespace UrlShortener.Domain.UnitTests.Domain;

public sealed class AliasTests
{
    [Theory]
    [InlineData("my-alias")]                           // 8: minimum
    [InlineData("My_Custom-Alias_2026")]
    [InlineData("123456789012345678901234567890")]     // 30: maximum
    public void Create_ValidAlias_Succeeds(string input)
    {
        var result = Alias.Create(input);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Code.Value.ShouldBe(input);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abcdefg")]                            // 7: reserved for generated codes
    [InlineData("1234567890123456789012345678901")]    // 31
    [InlineData("has space")]
    [InlineData("slash/alias")]
    [InlineData("dot.alias.x")]
    public void Create_InvalidAlias_FailsWithValidationError(string? input)
    {
        var result = Alias.Create(input);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Alias.Invalid");
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Theory]
    [InlineData("swagger")]
    [InlineData("ADMIN")]
    [InlineData("Metrics")]
    public void Create_ReservedWord_FailsWithReservedError(string input)
    {
        var result = Alias.Create(input);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Alias.Reserved");
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Alias_NeverCollidesWithGeneratedCodeLength()
    {
        DomainRules.AliasMinLength.ShouldBeGreaterThan(DomainRules.GeneratedCodeLength);
    }
}
