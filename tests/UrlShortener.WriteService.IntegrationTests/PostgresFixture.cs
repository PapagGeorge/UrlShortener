using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using UrlShortener.WriteService.Infrastructure.Persistence;

namespace UrlShortener.WriteService.IntegrationTests;

/// <summary>One real Postgres container, with the Write Service migrations applied, shared by the tests of the "postgres" collection.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public WriteDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<WriteDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task ResetAsync()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE short_urls");
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
