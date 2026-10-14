namespace UrlShortener.WriteService.Application.DeleteExpiredShortUrls;

/// <param name="BatchSize">Rows deleted per round trip, so one huge DELETE never locks the table for long.</param>
public sealed record DeleteExpiredShortUrlsCommand(int BatchSize = 1000);
