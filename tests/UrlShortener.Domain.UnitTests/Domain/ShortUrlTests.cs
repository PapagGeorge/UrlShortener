using UrlShortener.BuildingBlocks.Results;
using UrlShortener.Domain;

namespace UrlShortener.Domain.UnitTests.Domain;

public sealed class ShortUrlTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly ShortCode Code = ShortCode.Create("0aZ9xYb").Value;
    private static readonly OriginalUrl Url = OriginalUrl.Create("https://example.com/long").Value;

    [Fact]
    public void Create_WithoutExpiration_SetsFields()
    {
        var result = ShortUrl.Create(Code, Url, Now, expiresAt: null, createdBy: "giorgos");

        result.IsSuccess.ShouldBeTrue();
        var shortUrl = result.Value;
        shortUrl.ShortCode.ShouldBe(Code);
        shortUrl.OriginalUrl.ShouldBe(Url);
        shortUrl.CreatedAt.ShouldBe(Now);
        shortUrl.ExpiresAt.ShouldBeNull();
        shortUrl.CreatedBy.ShouldBe("giorgos");
    }

    [Fact]
    public void Create_WithFutureExpiration_Succeeds()
    {
        var expiresAt = Now.AddDays(1);

        var result = ShortUrl.Create(Code, Url, Now, expiresAt);

        result.Value.ExpiresAt.ShouldBe(expiresAt);
        result.Value.CreatedBy.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]     // exactly now
    [InlineData(-1)]    // one second ago
    [InlineData(-86400)]
    public void Create_ExpirationNotInTheFuture_FailsWithValidationError(int offsetSeconds)
    {
        var result = ShortUrl.Create(Code, Url, Now, Now.AddSeconds(offsetSeconds));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ShortUrl.ExpirationInPast");
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void IsExpired_WithoutExpiration_IsNeverExpired()
    {
        var shortUrl = ShortUrl.Create(Code, Url, Now, null).Value;

        shortUrl.IsExpired(Now.AddYears(100)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(-1, false)]   // one second before expiry
    [InlineData(0, true)]     // exactly at expiry: expired
    [InlineData(1, true)]
    public void IsExpired_ComparesAgainstExpiresAt(int secondsFromExpiry, bool expected)
    {
        var expiresAt = Now.AddHours(1);
        var shortUrl = ShortUrl.Create(Code, Url, Now, expiresAt).Value;

        shortUrl.IsExpired(expiresAt.AddSeconds(secondsFromExpiry)).ShouldBe(expected);
    }

    [Fact]
    public void Create_ExpirationWithNonUtcOffset_IsStoredAsUtcSameInstant()
    {
        // Npgsql's timestamptz accepts only offset 0, so the Domain normalizes instead of failing at the database.
        var expiresAt = new DateTimeOffset(2026, 10, 15, 14, 0, 0, TimeSpan.FromHours(2));

        var shortUrl = ShortUrl.Create(Code, Url, Now, expiresAt).Value;

        shortUrl.ExpiresAt!.Value.Offset.ShouldBe(TimeSpan.Zero);
        shortUrl.ExpiresAt.Value.ShouldBe(expiresAt);
    }

    [Fact]
    public void Create_CreatedAtWithNonUtcOffset_IsStoredAsUtc()
    {
        var now = new DateTimeOffset(2026, 10, 14, 15, 0, 0, TimeSpan.FromHours(3));

        var shortUrl = ShortUrl.Create(Code, Url, now, null).Value;

        shortUrl.CreatedAt.Offset.ShouldBe(TimeSpan.Zero);
        shortUrl.CreatedAt.ShouldBe(now);
    }
}
