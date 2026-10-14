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
        });

        return app;
    }
}
