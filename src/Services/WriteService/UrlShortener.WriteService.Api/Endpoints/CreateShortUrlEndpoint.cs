using Microsoft.Extensions.Options;
using UrlShortener.BuildingBlocks.Messaging;
using UrlShortener.ServiceDefaults;
using UrlShortener.WriteService.Api.Options;
using UrlShortener.WriteService.Application.CreateShortUrl;

namespace UrlShortener.WriteService.Api.Endpoints;

public sealed record CreateShortUrlRequest(string? OriginalUrl, string? Alias, DateTimeOffset? ExpiresAt);

public sealed record CreateShortUrlResponse(string ShortUrl);

internal static class CreateShortUrlEndpoint
{
    /// <summary>Thin by design: request → command → handler → HTTP. All rules live in the Application and Domain layers.</summary>
    public static IEndpointRouteBuilder MapCreateShortUrl(this IEndpointRouteBuilder app)
    {
        app.MapPost("/urls", async (
            CreateShortUrlRequest request,
            ICommandHandler<CreateShortUrlCommand, CreateShortUrlResult> handler,
            IOptions<ShortUrlOptions> options,
            CancellationToken ct) =>
        {
            var result = await handler.Handle(new CreateShortUrlCommand(request.OriginalUrl, request.Alias, request.ExpiresAt), ct);

            if (result.IsFailure)
            {
                return result.Error.ToProblem();
            }

            var shortUrl = options.Value.BuildShortUrl(result.Value.ShortCode);
            return Results.Created(shortUrl, new CreateShortUrlResponse(shortUrl));
        })
        .WithName("CreateShortUrl")
        .WithSummary("Creates a short URL.")
        .WithDescription(
            "Generates a 7-character code, or uses the optional alias (8-30 characters of A-Z, a-z, 0-9, _ and -, case-sensitive). " +
            "An optional expiresAt must be in the future; after it the short URL answers 410 Gone. Only http and https URLs are accepted.")
        .Produces<CreateShortUrlResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }
}
