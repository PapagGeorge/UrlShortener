using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UrlShortener.WriteService.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> at design time (e.g. <c>migrations add</c>). It never connects on purpose: the host is an
/// unresolvable placeholder (<c>.invalid</c>), so no secret is needed and no real server can be touched by accident.
/// </summary>
internal sealed class WriteDbContextFactory : IDesignTimeDbContextFactory<WriteDbContext>
{
    public WriteDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<WriteDbContext>()
            .UseNpgsql("Host=design-time-only.invalid;Database=design_time_only")
            .Options);
}
