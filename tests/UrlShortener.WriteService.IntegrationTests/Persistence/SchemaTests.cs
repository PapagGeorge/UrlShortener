using Microsoft.EntityFrameworkCore;

namespace UrlShortener.WriteService.IntegrationTests.Persistence;

/// <summary>Checks the schema that the migrations really produce on Postgres (the Read Service depends on it).</summary>
[Trait("Category", "Integration")]
[Collection(PostgresCollection.Name)]
public sealed class SchemaTests(PostgresFixture postgres)
{
    private async Task<List<string>> Query(string sql)
    {
        await using var context = postgres.CreateContext();
        return await context.Database.SqlQueryRaw<string>(sql).ToListAsync();
    }

    [Fact]
    public async Task ShortCodeColumn_IsVarchar30WithTheCCollation()
    {
        var rows = await Query(
            "SELECT character_maximum_length::text || '|' || COALESCE(collation_name, '') AS \"Value\" " +
            "FROM information_schema.columns WHERE table_name = 'short_urls' AND column_name = 'short_code'");

        rows.ShouldBe(["30|C"]);
    }

    [Fact]
    public async Task Table_HasExactlyTheExpectedColumns()
    {
        var rows = await Query(
            "SELECT column_name || ':' || data_type || ':' || is_nullable AS \"Value\" " +
            "FROM information_schema.columns WHERE table_name = 'short_urls' ORDER BY column_name");

        rows.ShouldBe(
        [
            "created_at:timestamp with time zone:NO",
            "created_by:text:YES",
            "expires_at:timestamp with time zone:YES",
            "original_url:text:NO",
            "short_code:character varying:NO",
        ]);
    }

    [Fact]
    public async Task ShortCode_IsThePrimaryKey()
    {
        var rows = await Query(
            "SELECT kcu.column_name AS \"Value\" FROM information_schema.table_constraints tc " +
            "JOIN information_schema.key_column_usage kcu ON kcu.constraint_name = tc.constraint_name " +
            "WHERE tc.table_name = 'short_urls' AND tc.constraint_type = 'PRIMARY KEY'");

        rows.ShouldBe(["short_code"]);
    }

    [Fact]
    public async Task OnlyThePrimaryKeyAndThePartialExpiresAtIndexExist()
    {
        var rows = await Query("SELECT indexname AS \"Value\" FROM pg_indexes WHERE tablename = 'short_urls' ORDER BY indexname");

        rows.ShouldBe(["ix_short_urls_expires_at", "pk_short_urls"]);
    }

    [Fact]
    public async Task ExpiresAtIndex_IsPartial()
    {
        var definition = (await Query("SELECT indexdef AS \"Value\" FROM pg_indexes WHERE indexname = 'ix_short_urls_expires_at'"))
            .ShouldHaveSingleItem();

        definition.ShouldContain("WHERE (expires_at IS NOT NULL)");
    }

    [Fact]
    public async Task Migrate_CalledAgain_IsANoOp()
    {
        await using var context = postgres.CreateContext();

        await Should.NotThrowAsync(() => context.Database.MigrateAsync());
        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }
}
