using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using UrlShortener.ReadService.Application;
using UrlShortener.ReadService.Application.Ports;
using UrlShortener.ReadService.Infrastructure.Caching;
using UrlShortener.ReadService.Infrastructure.Persistence;

namespace UrlShortener.ReadService.Infrastructure;

internal static class ReadInfrastructureOptions
{
    /// <summary>
    /// AbortOnConnectFail=false: the service starts and serves redirects from the database even while Redis is down.
    /// Short timeouts keep a sick Redis from slowing redirects.
    /// </summary>
    public static ConfigurationOptions RedisOptions(string connectionString, ReadCacheOptions cache)
    {
        var options = ConfigurationOptions.Parse(connectionString);
        var timeout = (int)cache.OperationTimeout.TotalMilliseconds;
        options.AbortOnConnectFail = false;
        options.AsyncTimeout = timeout;
        options.SyncTimeout = timeout;
        options.ConnectTimeout = Math.Min(timeout * 10, 2000);
        return options;
    }
}

public static class DependencyInjection
{
    /// <summary>
    /// Registers read-side persistence (Postgres, read-only, no tracking) and the Redis cache.
    /// Reads <c>ConnectionStrings:Postgres</c> (use the SELECT-only database user), <c>ConnectionStrings:Redis</c> and the optional <c>Cache</c> section.
    /// </summary>
    public static IServiceCollection AddReadInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var postgres = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("The connection string 'ConnectionStrings:Postgres' is not configured.");
        var redis = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("The connection string 'ConnectionStrings:Redis' is not configured.");

        // Pooled: this is the hot path, so contexts are reused instead of rebuilt per request.
        services.AddDbContextPool<ReadDbContext>(options => options
            .UseNpgsql(postgres)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        services.AddScoped<IShortUrlReadRepository, ShortUrlReadRepository>();

        services.AddSingleton<IValidateOptions<ReadCacheOptions>, ReadCacheOptionsValidator>();
        services.AddOptions<ReadCacheOptions>().Bind(configuration.GetSection(ReadCacheOptions.SectionName)).ValidateOnStart();

        services.AddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(ReadInfrastructureOptions.RedisOptions(redis, sp.GetRequiredService<IOptions<ReadCacheOptions>>().Value)));
        services.AddSingleton<IShortUrlCache, RedisShortUrlCache>();
        services.AddSingleton(sp => new RedirectCachePolicy(sp.GetRequiredService<IOptions<ReadCacheOptions>>().Value.DefaultTtl));

        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
