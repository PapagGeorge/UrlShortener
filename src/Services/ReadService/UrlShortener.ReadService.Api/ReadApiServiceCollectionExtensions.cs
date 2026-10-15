using UrlShortener.BuildingBlocks.Logging;
using UrlShortener.BuildingBlocks.Metrics;
using UrlShortener.BuildingBlocks.Messaging;
using UrlShortener.ReadService.Application;
using UrlShortener.ReadService.Application.GetOriginalUrl;
using UrlShortener.ReadService.Application.Ports;
using UrlShortener.ReadService.Infrastructure;

namespace UrlShortener.ReadService.Api;

/// <summary>The composition root of the Read Service. Registers only read-side services: no counter, no write handlers, no cleanup job.</summary>
internal static class ReadApiServiceCollectionExtensions
{
    public static IServiceCollection AddReadApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddReadInfrastructure(configuration);

        services.AddScoped<IQueryHandler<GetOriginalUrlQuery, GetOriginalUrlResult>>(sp =>
            new MetricsQueryDecorator<GetOriginalUrlQuery, GetOriginalUrlResult>(
                new LoggingQueryDecorator<GetOriginalUrlQuery, GetOriginalUrlResult>(
                    new GetOriginalUrlQueryHandler(
                        sp.GetRequiredService<IShortUrlCache>(),
                        sp.GetRequiredService<IShortUrlReadRepository>(),
                        sp.GetRequiredService<TimeProvider>(),
                        sp.GetRequiredService<RedirectCachePolicy>()),
                    sp.GetRequiredService<ILogger<LoggingQueryDecorator<GetOriginalUrlQuery, GetOriginalUrlResult>>>())));

        return services;
    }
}
