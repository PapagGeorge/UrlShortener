using Microsoft.Extensions.Options;
using UrlShortener.Domain;

namespace UrlShortener.WriteService.Api.Options;

/// <summary>Where short URLs are served from, e.g. <c>https://short.example</c>.</summary>
public sealed class ShortUrlOptions
{
    public const string SectionName = "ShortUrl";

    public string? PublicBaseUrl { get; init; }

    /// <summary>The lowercase host (no port); used to reject URLs that point back at this service.</summary>
    public string Host => new Uri(PublicBaseUrl!).Host.ToLowerInvariant();

    public string BuildShortUrl(ShortCode code) => $"{PublicBaseUrl!.TrimEnd('/')}/{code.Value}";
}

public sealed class ShortUrlOptionsValidator : IValidateOptions<ShortUrlOptions>
{
    public ValidateOptionsResult Validate(string? name, ShortUrlOptions options)
    {
        const string Message =
            "ShortUrl:PublicBaseUrl must be an absolute http(s) URL with no path, query, fragment or credentials (e.g. https://short.example).";

        if (!Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.AbsolutePath != "/"
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || uri.UserInfo.Length > 0)
        {
            return ValidateOptionsResult.Fail(Message);
        }

        return ValidateOptionsResult.Success;
    }
}
