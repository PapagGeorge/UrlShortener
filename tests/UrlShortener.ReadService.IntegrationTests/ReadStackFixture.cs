using Microsoft.EntityFrameworkCore;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using UrlShortener.Domain;
using UrlShortener.ReadService.Infrastructure.Persistence;
using UrlShortener.WriteService.Infrastructure.Persistence;

namespace UrlShortener.ReadService.IntegrationTests;

/// <summary>
/// Real Postgres and Redis. The database is prepared exactly like production: the roles script first, then the Write Service
/// migrations run as the write role, and the Read Service connects as the SELECT-only role.
/// </summary>
public sealed class ReadStackFixture : IAsyncLifetime
{
    private const string WritePassword = "WriteTestPw1";
    private const string ReadPassword = "ReadTestPw1";
    private const string Database = "urlshortener";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").WithDatabase(Database).Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    public string AdminConnectionString => _postgres.GetConnectionString();

    public string WriteConnectionString => WithUser("urlshortener_write", WritePassword);

    public string ReadConnectionString => WithUser("urlshortener_read", ReadPassword);

    public string RedisConnectionString => _redis.GetConnectionString();

    public IConnectionMultiplexer Redis { get; private set; } = null!;

    public ReadDbContext CreateReadContext() =>
        new(new DbContextOptionsBuilder<ReadDbContext>().UseNpgsql(ReadConnectionString).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);

    public WriteDbContext CreateWriteContext() =>
        new(new DbContextOptionsBuilder<WriteDbContext>().UseNpgsql(WriteConnectionString).Options);

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());
        Redis = await ConnectionMultiplexer.ConnectAsync(RedisConnectionString);

        await using (var admin = new NpgsqlDataSourceBuilder(AdminConnectionString).Build())
        {
            await using var command = admin.CreateCommand(RolesScript());
            await command.ExecuteNonQueryAsync();
        }

        await using var write = CreateWriteContext();
        await write.Database.MigrateAsync();   // the Write Service owns the schema
    }

    public async Task ResetAsync()
    {
        await using var write = CreateWriteContext();
        await write.Database.ExecuteSqlRawAsync("TRUNCATE TABLE short_urls");
        await Redis.GetServer(Redis.GetEndPoints()[0]).FlushDatabaseAsync();
    }

    /// <summary>Inserts through the real Write Service repository, so the Read Service reads what the Write Service really produces.</summary>
    public async Task SeedAsync(string code, string url, DateTimeOffset? expiresAt = null, DateTimeOffset? createdAt = null)
    {
        await using var write = CreateWriteContext();
        var shortUrl = ShortUrl.Create(ShortCode.Create(code).Value, OriginalUrl.Create(url).Value, createdAt ?? DateTimeOffset.UtcNow, expiresAt).Value;
        (await new ShortUrlWriteRepository(write).TryAddAsync(shortUrl, CancellationToken.None)).ShouldBeTrue();
    }

    public async Task DeleteRowAsync(string code)
    {
        await using var write = CreateWriteContext();
        await write.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM short_urls WHERE short_code = {code}");
    }

    public async Task DisposeAsync()
    {
        await Redis.DisposeAsync();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }

    private string WithUser(string user, string password) =>
        new NpgsqlConnectionStringBuilder(AdminConnectionString) { Username = user, Password = password }.ConnectionString;

    private static string RolesScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        var path = Path.Combine(directory!.FullName, "deploy", "postgres", "roles.sql");
        return File.ReadAllText(path)
            .Replace("{{DATABASE}}", Database)
            .Replace("{{WRITE_PASSWORD}}", WritePassword)
            .Replace("{{READ_PASSWORD}}", ReadPassword);
    }
}

[CollectionDefinition(Name)]
public sealed class ReadStackCollection : ICollectionFixture<ReadStackFixture>
{
    public const string Name = "read-stack";
}
