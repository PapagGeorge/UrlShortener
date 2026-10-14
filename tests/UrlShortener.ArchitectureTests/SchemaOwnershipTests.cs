using Microsoft.EntityFrameworkCore.Migrations;
using NetArchTest.Rules;
using UrlShortener.ReadService.Infrastructure.Persistence;
using UrlShortener.WriteService.Infrastructure.Persistence;

namespace UrlShortener.ArchitectureTests;

/// <summary>The Write Service owns the schema. The Read Service maps the same table but must never migrate it.</summary>
public sealed class SchemaOwnershipTests
{
    [Fact]
    public void ReadService_HasNoMigrations()
    {
        var migrations = Types.InAssembly(typeof(ReadDbContext).Assembly)
            .That().Inherit(typeof(Migration))
            .GetTypes();

        migrations.ShouldBeEmpty("only the Write Service owns the schema");
    }

    [Fact]
    public void WriteService_OwnsTheMigrations()
    {
        var migrations = Types.InAssembly(typeof(WriteDbContext).Assembly)
            .That().Inherit(typeof(Migration))
            .GetTypes();

        migrations.ShouldNotBeEmpty();
    }

    [Fact]
    public void ReadDbContext_IsNotAWriteContext()
    {
        // Guards against someone "sharing" the Write context into the Read Service.
        Types.InAssembly(typeof(ReadDbContext).Assembly)
            .ShouldNot().HaveDependencyOn("UrlShortener.WriteService")
            .GetResult().IsSuccessful.ShouldBeTrue();
    }
}
