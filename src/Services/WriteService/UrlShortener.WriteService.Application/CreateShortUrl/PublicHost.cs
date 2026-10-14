namespace UrlShortener.WriteService.Application.CreateShortUrl;

/// <summary>The host our short URLs are served from (e.g. <c>short.example</c>). Used to reject links that point back at this service.</summary>
public sealed record PublicHost(string Value);
