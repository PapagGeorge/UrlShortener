using UrlShortener.Domain;
using UrlShortener.WriteService.Api.Options;

namespace UrlShortener.WriteService.UnitTests.Api;

public sealed class ShortUrlOptionsTests
{
    private readonly ShortUrlOptionsValidator _sut = new();

    [Theory]
    [InlineData("https://short.example")]
    [InlineData("https://short.example/")]
    [InlineData("http://localhost:8080")]
    [InlineData("https://s.example:8443/")]
    public void PublicBaseUrl_AbsoluteHttpOrHttpsWithoutPath_IsValid(string url)
    {
        _sut.Validate(null, new ShortUrlOptions { PublicBaseUrl = url }).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short.example")]                 // not absolute
    [InlineData("ftp://short.example")]
    [InlineData("https://short.example/sub")]     // a path would break /{code}
    [InlineData("https://short.example/?x=1")]
    [InlineData("https://short.example/#f")]
    [InlineData("https://user:pass@short.example")]
    public void PublicBaseUrl_MissingOrUnsuitable_IsInvalid(string? url)
    {
        var result = _sut.Validate(null, new ShortUrlOptions { PublicBaseUrl = url });

        result.Failed.ShouldBeTrue();
        result.FailureMessage!.ShouldContain("ShortUrl:PublicBaseUrl");
    }

    [Theory]
    [InlineData("https://short.example", "https://short.example/0aZ9xYb")]
    [InlineData("https://short.example/", "https://short.example/0aZ9xYb")]     // never a double slash
    [InlineData("http://localhost:8080", "http://localhost:8080/0aZ9xYb")]
    public void BuildShortUrl_JoinsTheBaseAndTheCodeWithASingleSlash(string baseUrl, string expected)
    {
        var options = new ShortUrlOptions { PublicBaseUrl = baseUrl };

        options.BuildShortUrl(ShortCode.Create("0aZ9xYb").Value).ShouldBe(expected);
    }

    [Theory]
    [InlineData("https://Short.Example/", "short.example")]
    [InlineData("http://localhost:8080", "localhost")]
    public void Host_IsTheLowercaseHostWithoutPort(string baseUrl, string expected)
    {
        new ShortUrlOptions { PublicBaseUrl = baseUrl }.Host.ShouldBe(expected);
    }
}
