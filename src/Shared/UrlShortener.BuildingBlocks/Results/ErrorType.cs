namespace UrlShortener.BuildingBlocks.Results;

/// <summary>The category of an expected failure. Each Api maps it to an HTTP status code.</summary>
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Gone,
    Failure,
}
