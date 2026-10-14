using UrlShortener.BuildingBlocks.Results;

namespace UrlShortener.WriteService.Application;

/// <summary>Expected failures specific to the write side.</summary>
public static class WriteErrors
{
    public static readonly Error AliasTaken = new(
        "ShortUrl.AliasTaken",
        "This alias is already in use.",
        ErrorType.Conflict);

    public static readonly Error CodeGenerationFailed = new(
        "ShortUrl.CodeGenerationFailed",
        "Could not generate a unique short code. Please try again.",
        ErrorType.Failure);
}
