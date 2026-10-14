using UrlShortener.BuildingBlocks.Results;
using UrlShortener.Domain;

namespace UrlShortener.Domain.UnitTests.Domain;

public sealed class OriginalUrlTests
{
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com/a/b?x=1&y=2#frag")]
    [InlineData("https://example.com:8080/path")]
    [InlineData("HTTPS://Example.COM/Mixed/Case")]
    [InlineData("https://sub.example.co.uk/%CE%B1?q=%20")]
    [InlineData("http://127.0.0.1:5000/")]
    public void Create_ValidUrl_SucceedsAndPreservesValueAsGiven(string input)
    {
        var result = OriginalUrl.Create(input);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(input);
    }

    [Fact]
    public void Create_UrlOfMaxLength_Succeeds()
    {
        var input = "https://example.com/" + new string('a', DomainRules.MaxUrlLength - "https://example.com/".Length);

        OriginalUrl.Create(input).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_UrlLongerThanMax_Fails()
    {
        var input = "https://example.com/" + new string('a', DomainRules.MaxUrlLength);

        OriginalUrl.Create(input).IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("example.com")]                        // not absolute
    [InlineData("/relative/path")]
    [InlineData("ftp://example.com/file")]             // scheme not http/https
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>1</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("https://user:pass@example.com")]      // credentials in the URL
    [InlineData("https://user@example.com")]
    [InlineData("http://")]                            // no host
    [InlineData("https://exa mple.com")]               // whitespace
    [InlineData("https://example.com/a b")]
    [InlineData("https://example.com/\n")]
    [InlineData(" https://example.com")]               // leading whitespace
    public void Create_InvalidUrl_FailsWithValidationError(string? input)
    {
        var result = OriginalUrl.Create(input);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("OriginalUrl.Invalid");
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        OriginalUrl.Create("https://example.com").Value.ToString().ShouldBe("https://example.com");
    }
}
