using UrlShortener.BuildingBlocks.Messaging;
using UrlShortener.ReadService.Application;
using UrlShortener.ReadService.Application.GetOriginalUrl;
using UrlShortener.ServiceDefaults;

namespace UrlShortener.ReadService.Api.Endpoints;

internal static class RedirectEndpoint
{
    /// <summary>
    /// <c>GET /{shortCode}</c> → <c>302</c> (never 301: browsers must keep asking us), <c>404</c> unknown or malformed, <c>410</c> expired.
    /// HEAD is served too, because link previewers and crawlers use it. Thin by design: all rules live in the Application layer.
    /// </summary>
    public static IEndpointRouteBuilder MapRedirect(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/{shortCode}", [HttpMethods.Get, HttpMethods.Head], async (
            string shortCode,
            HttpContext http,
            IQueryHandler<GetOriginalUrlQuery, GetOriginalUrlResult> handler,
            ILogger<GetOriginalUrlQuery> logger,
            CancellationToken ct) =>
        {
            // Never let a browser or proxy keep a redirect (or a 404/410) we may later change.
            http.Response.Headers.CacheControl = "no-store";

            var result = await handler.Handle(new GetOriginalUrlQuery(shortCode), ct);
            if (result.IsFailure)
            {
                return result.Error.ToProblem();
            }

            var url = result.Value.OriginalUrl;
            if (!IsWebUrl(url))
            {
                // Created URLs are always http(s), so this means corrupt data: refuse rather than redirect to javascript:, data: or file:.
                logger.LogError("Refusing to redirect {ShortCode}: the stored URL does not use http or https", shortCode);
                return ReadErrors.NotFound.ToProblem();
            }

            return Results.Redirect(url, permanent: false, preserveMethod: false);
        })
        .WithName("Redirect")
        .WithSummary("Redirects a short code to its original URL.")
        .WithDescription("Always a temporary redirect (302), never 301, so clients keep asking. HEAD is supported. The response is never cacheable (Cache-Control: no-store).")
        .Produces(StatusCodes.Status302Found)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status410Gone)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static bool IsWebUrl(string url) =>
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
}
