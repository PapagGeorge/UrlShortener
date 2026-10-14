namespace UrlShortener.WriteService.Application.CreateShortUrl;

/// <param name="OriginalUrl">The long URL. Raw input: validated by the validator and again by the Domain value objects.</param>
/// <param name="Alias">Optional custom alias (8-30 characters).</param>
/// <param name="ExpiresAt">Optional expiration; must be in the future.</param>
public sealed record CreateShortUrlCommand(string? OriginalUrl, string? Alias, DateTimeOffset? ExpiresAt);
