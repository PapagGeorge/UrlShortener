using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
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
    /// Short timeouts and FailFast keep a sick or absent Redis from slowing redirects.
    /// </summary>
    public static ConfigurationOptions RedisOptions(string connectionString, ReadCacheOptions cache)
    {
        var options = ConfigurationOptions.Parse(connectionString);
        var timeout = (int)cache.OperationTimeout.TotalMilliseconds;
        options.AbortOnConnectFail = false;
        // Without this, commands issued while Redis is down wait in a backlog and fail about a second later whatever the timeout is,
        // which would add ~2 s (get + set) to every redirect. FailFast fails them immediately, so the redirect falls back to the database.
        options.BacklogPolicy = BacklogPolicy.FailFast;
        options.AsyncTimeout = timeout;
        options.SyncTimeout = timeout;
        options.ConnectTimeout = Math.Min(timeout * 10, 2000);
        return options;
    }
}

public static class DependencyInjection
{
    /// <summary>
    /// Npgsql's default pool (100) can exceed what Postgres accepts (max_connections is 100 including reserved ones), so a burst would fail with
    /// "too many clients" instead of waiting. A smaller pool makes the excess queue. An explicit size in the connection string wins.
    /// </summary>
    private static string WithBoundedPool(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (!builder.ShouldSerialize("Maximum Pool Size"))   // true only when the connection string set it explicitly
        {
            builder.MaxPoolSize = DefaultMaxPoolSize;
        }

        return builder.ConnectionString;
    }

    private const int DefaultMaxPoolSize = 32;

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
            .UseNpgsql(WithBoundedPool(postgres))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        // Concurrent misses for the same code share one query (cache stampede protection). The shared query runs in its own scope.
        services.AddScoped<ShortUrlReadRepository>();
        services.AddSingleton<IShortUrlReadRepository>(sp =>
        {
            var scopes = sp.GetRequiredService<IServiceScopeFactory>();
            return new SingleFlightShortUrlReadRepository(async (code, ct) =>
            {
                await using var scope = scopes.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<ShortUrlReadRepository>().FindAsync(code, ct);
            });
        });

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
