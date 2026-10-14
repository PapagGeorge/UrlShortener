namespace UrlShortener.ReadService.Application.GetOriginalUrl;

/// <param name="ShortCode">The raw path segment. Parsed (and rejected if malformed) by the handler.</param>
public sealed record GetOriginalUrlQuery(string? ShortCode);

public sealed record GetOriginalUrlResult(string OriginalUrl);
