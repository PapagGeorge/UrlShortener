using FluentValidation;
using Microsoft.Extensions.Time.Testing;
using UrlShortener.WriteService.Application.CreateShortUrl;

namespace UrlShortener.WriteService.UnitTests.Application;

public sealed class CreateShortUrlCommandValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private const string LongUrl = "https://example.com/some/very/long/url";

    private readonly CreateShortUrlCommandValidator _sut = new(new FakeTimeProvider(Now));

    [Fact]
    public void Validate_UrlOnly_IsValid()
    {
        _sut.Validate(new CreateShortUrlCommand(LongUrl, null, null)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_AllFieldsValid_IsValid()
    {
        _sut.Validate(new CreateShortUrlCommand(LongUrl, "my-custom-alias", Now.AddMinutes(1))).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("example.com")]
    [InlineData("javascript:alert(1)")]
    public void Validate_InvalidUrl_FailsOnOriginalUrl(string? url)
    {
        var result = _sut.Validate(new CreateShortUrlCommand(url, null, null));

        var error = result.Errors.ShouldHaveSingleItem();
        error.PropertyName.ShouldBe("OriginalUrl");
        error.ErrorCode.ShouldBe("OriginalUrl.Invalid");
    }

    [Theory]
    [InlineData("short", "Alias.Invalid")]
    [InlineData("has space!!", "Alias.Invalid")]
    [InlineData("swagger", "Alias.Reserved")]
    public void Validate_InvalidAlias_FailsOnAliasWithTheDomainErrorCode(string alias, string expectedCode)
    {
        var result = _sut.Validate(new CreateShortUrlCommand(LongUrl, alias, null));

        var error = result.Errors.ShouldHaveSingleItem();
        error.PropertyName.ShouldBe("Alias");
        error.ErrorCode.ShouldBe(expectedCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void Validate_ExpirationNotInTheFuture_FailsOnExpiresAt(int offsetSeconds)
    {
        var result = _sut.Validate(new CreateShortUrlCommand(LongUrl, null, Now.AddSeconds(offsetSeconds)));

        var error = result.Errors.ShouldHaveSingleItem();
        error.PropertyName.ShouldBe("ExpiresAt");
        error.ErrorCode.ShouldBe("ShortUrl.ExpirationInPast");
    }

    [Fact]
    public void Validate_EverythingWrong_ReportsEveryField()
    {
        var result = _sut.Validate(new CreateShortUrlCommand("nope", "x", Now.AddDays(-1)));

        result.Errors.Select(e => e.PropertyName).ShouldBe(["OriginalUrl", "Alias", "ExpiresAt"], ignoreOrder: true);
    }
}
