namespace UrlShortener.ReadService.Application;

/// <param name="DefaultTtl">Upper bound for how long a redirect stays cached. A URL that expires sooner is cached only until then.</param>
public sealed record RedirectCachePolicy(TimeSpan DefaultTtl);
