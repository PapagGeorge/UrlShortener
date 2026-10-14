using UrlShortener.Domain;

namespace UrlShortener.WriteService.Application.CreateShortUrl;

/// <summary>The Api turns the code into a full URL with its configured public base URL.</summary>
public sealed record CreateShortUrlResult(ShortCode ShortCode);
