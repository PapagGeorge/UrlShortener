using Microsoft.AspNetCore.Http;
using UrlShortener.BuildingBlocks.Results;

namespace UrlShortener.ServiceDefaults;

public static class ErrorHttpExtensions
{
    /// <summary>Maps an expected failure to an RFC 9457 problem. The stable <c>Error.Code</c> is exposed as the <c>code</c> extension.</summary>
    public static IResult ToProblem(this Error error)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };

        if (error is { Type: ErrorType.Validation, Details: { } details })
        {
            return Results.ValidationProblem(
                details.ToDictionary(pair => pair.Key, pair => pair.Value),
                detail: error.Description,
                extensions: extensions);
        }

        return Results.Problem(detail: error.Description, statusCode: StatusCodeFor(error.Type), extensions: extensions);
    }

    private static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Gone => StatusCodes.Status410Gone,
        _ => StatusCodes.Status500InternalServerError,
    };
}
