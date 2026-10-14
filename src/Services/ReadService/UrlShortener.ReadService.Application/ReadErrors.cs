using UrlShortener.BuildingBlocks.Results;

namespace UrlShortener.ReadService.Application;

public static class ReadErrors
{
    /// <summary>Unknown <em>or malformed</em> code: both are the same to a caller.</summary>
    public static readonly Error NotFound = new("ShortUrl.NotFound", "The short URL does not exist.", ErrorType.NotFound);

    public static readonly Error Expired = new("ShortUrl.Expired", "The short URL has expired.", ErrorType.Gone);
}
