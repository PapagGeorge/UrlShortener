using UrlShortener.ReadService.Application;
using UrlShortener.ReadService.Infrastructure.Caching;

namespace UrlShortener.ReadService.UnitTests.Caching;

public sealed class CacheEntryFormatTests
{
    private static readonly DateTimeOffset Expiry = new(2027, 3, 4, 5, 6, 7, 890, TimeSpan.Zero);

    [Fact]
    public void RoundTrip_WithExpiration_PreservesUrlAndInstantToTheMillisecond()
    {
        var target = new RedirectTarget("https://example.com/a?x=1&y=2#frag", Expiry);

        var parsed = CacheEntryFormat.TryParse(CacheEntryFormat.Serialize(target), out var result);

        parsed.ShouldBeTrue();
        result!.OriginalUrl.ShouldBe(target.OriginalUrl);
        result.ExpiresAt.ShouldBe(Expiry);
    }

    [Fact]
    public void RoundTrip_WithoutExpiration_KeepsItNull()
    {
        var parsed = CacheEntryFormat.TryParse(CacheEntryFormat.Serialize(new RedirectTarget("https://example.com", null)), out var result);

        parsed.ShouldBeTrue();
        result!.ExpiresAt.ShouldBeNull();
    }

    [Fact]
    public void RoundTrip_UrlContainingThePipeSeparator_IsNotTruncated()
    {
        var target = new RedirectTarget("https://example.com/a|b|c?q=1|2", Expiry);

        CacheEntryFormat.TryParse(CacheEntryFormat.Serialize(target), out var result).ShouldBeTrue();

        result!.OriginalUrl.ShouldBe("https://example.com/a|b|c?q=1|2");
    }

    [Fact]
    public void Serialize_UsesTheDocumentedCompactLayout()
    {
        CacheEntryFormat.Serialize(new RedirectTarget("https://e.com", null)).ShouldBe("-|https://e.com");
        CacheEntryFormat.Serialize(new RedirectTarget("https://e.com", DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_123)))
            .ShouldBe("1700000000123|https://e.com");
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-separator")]
    [InlineData("abc|https://e.com")]        // expiry is not a number
    [InlineData("-|")]                       // empty URL
    [InlineData("123|")]
    [InlineData("|https://e.com")]
    public void TryParse_CorruptValue_ReturnsFalse(string value)
    {
        CacheEntryFormat.TryParse(value, out var result).ShouldBeFalse();
        result.ShouldBeNull();
    }

    [Fact]
    public void TryParse_NullValue_ReturnsFalse()
    {
        CacheEntryFormat.TryParse(null, out _).ShouldBeFalse();
    }
}
