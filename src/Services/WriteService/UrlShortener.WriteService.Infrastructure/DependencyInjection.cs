using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using UrlShortener.WriteService.Application.Ports;
using UrlShortener.WriteService.Infrastructure.Cleanup;
using UrlShortener.WriteService.Infrastructure.Codes;
using UrlShortener.WriteService.Infrastructure.Persistence;

namespace UrlShortener.WriteService.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers Postgres persistence and the expired-URL cleanup job. Reads <c>ConnectionStrings:Postgres</c> and the <c>Cleanup</c> section.</summary>
    public static IServiceCollection AddWritePersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("The connection string 'ConnectionStrings:Postgres' is not configured.");

        services.AddDbContext<WriteDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IShortUrlWriteRepository, ShortUrlWriteRepository>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IValidateOptions<CleanupOptions>, CleanupOptionsValidator>();
        services.AddOptions<CleanupOptions>()
            .Bind(configuration.GetSection(CleanupOptions.SectionName))
            .ValidateOnStart();
        services.AddHostedService<ExpiredUrlCleanupService>();

        return services;
    }

    /// <summary>
    /// Registers code generation: the Redis-backed id allocator and the keyed obfuscator.
    /// Reads <c>ConnectionStrings:Redis</c>, <c>Obfuscation:Key</c> (secret) and the optional <c>Counter</c> section.
    /// </summary>
    public static IServiceCollection AddWriteCodeGeneration(this IServiceCollection services, IConfiguration configuration)
    {
        var redisConnectionString = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("The connection string 'ConnectionStrings:Redis' is not configured.");

        services.AddSingleton<IValidateOptions<ObfuscationOptions>, ObfuscationOptionsValidator>();
        services.AddSingleton<IValidateOptions<CounterOptions>, CounterOptionsValidator>();
        services.AddOptions<ObfuscationOptions>().Bind(configuration.GetSection(ObfuscationOptions.SectionName)).ValidateOnStart();
        services.AddOptions<CounterOptions>().Bind(configuration.GetSection(CounterOptions.SectionName)).ValidateOnStart();

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(WriteRedisOptions.Create(redisConnectionString)));

        services.AddHostedService<RedisConnectionWarmup>();

        services.AddSingleton<ICounterStore>(sp => new RedisCounterStore(
            sp.GetRequiredService<IConnectionMultiplexer>(),
            sp.GetRequiredService<IOptions<CounterOptions>>().Value.RedisKey));

        // Singleton: every request shares one batch of ids.
        services.AddSingleton<IIdAllocator>(sp => new BatchingIdAllocator(
            sp.GetRequiredService<ICounterStore>(),
            sp.GetRequiredService<IOptions<CounterOptions>>().Value.BatchSize));

        services.AddSingleton<ICodeObfuscator>(sp =>
            new FeistelCodeObfuscator(sp.GetRequiredService<IOptions<ObfuscationOptions>>().Value.GetKeyBytes()));

        return services;
    }

    /// <summary>
    /// Applies pending migrations. This service owns the schema. EF Core takes a database lock while migrating,
    /// so several replicas starting at once do not race.
    /// </summary>
    public static async Task MigrateWriteDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WriteDbContext>();
        await context.Database.MigrateAsync(ct);
    }
}
