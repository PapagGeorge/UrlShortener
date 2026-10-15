using Microsoft.EntityFrameworkCore;
using UrlShortener.BuildingBlocks.Logging;
using UrlShortener.BuildingBlocks.Metrics;
using UrlShortener.BuildingBlocks.Messaging;
using UrlShortener.ReadService.Application;
using UrlShortener.ReadService.Application.GetOriginalUrl;
using UrlShortener.ReadService.Application.Ports;
using UrlShortener.ReadService.Infrastructure;
using UrlShortener.ReadService.Infrastructure.Persistence;
using UrlShortener.ServiceDefaults;

namespace UrlShortener.ReadService.Api;

/// <summary>The composition root of the Read Service. Registers only read-side services: no counter, no write handlers, no cleanup job.</summary>
internal static class ReadApiServiceCollectionExtensions
{
    public static IServiceCollection AddReadApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddReadInfrastructure(configuration);

        services.AddSingleton<RedirectMetrics>();

        services.AddScoped<IQueryHandler<GetOriginalUrlQuery, GetOriginalUrlResult>>(sp =>
            new MetricsQueryDecorator<GetOriginalUrlQuery, GetOriginalUrlResult>(
                new LoggingQueryDecorator<GetOriginalUrlQuery, GetOriginalUrlResult>(
                    new GetOriginalUrlQueryHandler(
                        sp.GetRequiredService<IShortUrlCache>(),
                        sp.GetRequiredService<IShortUrlReadRepository>(),
                        sp.GetRequiredService<TimeProvider>(),
                        sp.GetRequiredService<RedirectCachePolicy>(),
                        sp.GetRequiredService<RedirectMetrics>()),
                    sp.GetRequiredService<ILogger<LoggingQueryDecorator<GetOriginalUrlQuery, GetOriginalUrlResult>>>())));

        // Ready = can answer a redirect from the source of truth. Redis is deliberately NOT checked: without it the service degrades to the database.
        services.AddHealthChecks()
            .AddReadinessProbe("postgres", async (sp, ct) =>
            {
                if (!await sp.GetRequiredService<ReadDbContext>().Database.CanConnectAsync(ct))
                {
                    throw new InvalidOperationException("Postgres is not reachable.");
                }
            });

        return services;
    }
}
