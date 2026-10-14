using Npgsql;

namespace UrlShortener.ReadService.IntegrationTests.Persistence;

/// <summary>The database itself, not just the code, keeps the Read Service from writing.</summary>
[Trait("Category", "Integration")]
[Collection(ReadStackCollection.Name)]
public sealed class ReadOnlyEnforcementTests(ReadStackFixture stack) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await stack.ResetAsync();
        await stack.SeedAsync("0aZ9xYb", "https://example.com/a");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<T> Run<T>(string connectionString, string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(sql);
        return (T)(await command.ExecuteScalarAsync() ?? default(T)!)!;
    }

    [Fact]
    public async Task ReadRole_CanSelect()
    {
        (await Run<long>(stack.ReadConnectionString, "SELECT count(*) FROM short_urls")).ShouldBe(1L);
    }

    [Theory]
    [InlineData("INSERT INTO short_urls (short_code, original_url, created_at) VALUES ('1111111', 'https://x.com', now())")]
    [InlineData("UPDATE short_urls SET original_url = 'https://evil.example'")]
    [InlineData("DELETE FROM short_urls")]
    [InlineData("TRUNCATE TABLE short_urls")]
    [InlineData("DROP TABLE short_urls")]
    [InlineData("ALTER TABLE short_urls ADD COLUMN x int")]
    [InlineData("CREATE TABLE sneaky (id int)")]
    public async Task ReadRole_CannotWriteOrChangeTheSchema(string sql)
    {
        var failure = await Should.ThrowAsync<PostgresException>(() => Run<object>(stack.ReadConnectionString, sql));

        failure.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task WriteRole_CanWrite()
    {
        await Run<object>(stack.WriteConnectionString, "UPDATE short_urls SET original_url = 'https://example.com/changed'");

        (await Run<string>(stack.ReadConnectionString, "SELECT original_url FROM short_urls")).ShouldBe("https://example.com/changed");
    }

    [Fact]
    public async Task TablesCreatedLaterByTheWriteRole_AreReadableByTheReadRole()
    {
        // Default privileges: a future migration adds a table and the Read Service can read it without any extra grant.
        await Run<object>(stack.WriteConnectionString, "CREATE TABLE IF NOT EXISTS added_by_a_later_migration (id int)");

        (await Run<long>(stack.ReadConnectionString, "SELECT count(*) FROM added_by_a_later_migration")).ShouldBe(0L);
    }
}
