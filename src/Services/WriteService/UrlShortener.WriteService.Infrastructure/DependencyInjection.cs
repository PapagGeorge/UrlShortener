using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using UrlShortener.WriteService.Application.Ports;
using UrlShortener.WriteService.Infrastructure.Cleanup;
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
