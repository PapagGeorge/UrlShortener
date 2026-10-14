using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using UrlShortener.WriteService.Infrastructure.Persistence;

namespace UrlShortener.WriteService.IntegrationTests.FullStack;

/// <summary>A fresh Postgres and Redis, and the real Write Service started against them (it applies its own migrations on startup).</summary>
public sealed class FullStackFixture : IAsyncLifetime
{
    public const string PublicBaseUrl = "https://short.test";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();
    private readonly List<StackApiFactory> _factories = [];

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public string RedisConnectionString => _redis.GetConnectionString();

    public IConnectionMultiplexer Redis { get; private set; } = null!;

    /// <summary>Starts another instance of the service, as a second replica would.</summary>
    public StackApiFactory StartInstance(string? redisConnectionString = null)
    {
        var factory = new StackApiFactory(PostgresConnectionString, redisConnectionString ?? RedisConnectionString);
        _factories.Add(factory);
        return factory;
    }

    public WriteDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<WriteDbContext>().UseNpgsql(PostgresConnectionString).Options);

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());
        Redis = await ConnectionMultiplexer.ConnectAsync(RedisConnectionString);
    }

    public async Task ResetAsync()
    {
        await using var context = CreateContext();
        if (await context.Database.CanConnectAsync() && (await context.Database.GetAppliedMigrationsAsync()).Any())
        {
            await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE short_urls");
        }

        await Redis.GetDatabase().KeyDeleteAsync("urlshortener:counter");
    }

    public async Task DisposeAsync()
    {
        foreach (var factory in _factories)
        {
            await factory.DisposeAsync();
        }

        await Redis.DisposeAsync();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }
}

public sealed class StackApiFactory(string postgres, string redis) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", postgres);
        builder.UseSetting("ConnectionStrings:Redis", redis);
        builder.UseSetting("Obfuscation:Key", Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)(i * 5)).ToArray()));
        builder.UseSetting("ShortUrl:PublicBaseUrl", FullStackFixture.PublicBaseUrl);
        builder.UseSetting("Database:MigrateOnStartup", "true");
    }
}

[CollectionDefinition(Name)]
public sealed class FullStackCollection : ICollectionFixture<FullStackFixture>
{
    public const string Name = "fullstack";
}
