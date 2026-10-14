using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using UrlShortener.Domain;
using UrlShortener.WriteService.Application.Ports;
using UrlShortener.WriteService.Infrastructure.Cleanup;

namespace UrlShortener.WriteService.IntegrationTests.Api;

/// <summary>In-memory repository: same contract as the real one (a duplicate code returns false), no database.</summary>
public sealed class InMemoryShortUrlRepository : IShortUrlWriteRepository
{
    public ConcurrentDictionary<string, ShortUrl> Items { get; } = new(StringComparer.Ordinal);

    public Task<bool> TryAddAsync(ShortUrl shortUrl, CancellationToken ct) =>
        Task.FromResult(Items.TryAdd(shortUrl.ShortCode.Value, shortUrl));

    public Task<int> DeleteExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken ct) => Task.FromResult(0);
}

public sealed class SequentialIdAllocator : IIdAllocator
{
    private long _last;

    public ValueTask<long> NextAsync(CancellationToken ct) => ValueTask.FromResult(Interlocked.Increment(ref _last));
}

/// <summary>
/// Hosts the real Write Service pipeline (routing, validation, handlers, code generation) with only the ports that need
/// Postgres and Redis swapped for in-memory fakes, so HTTP behaviour is testable without Docker.
/// </summary>
public class WriteApiFactory : WebApplicationFactory<Program>
{
    public const string PublicBaseUrl = "https://short.test";

    public InMemoryShortUrlRepository Repository { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", "Host=postgres-never-used.invalid;Database=x");
        builder.UseSetting("ConnectionStrings:Redis", "redis-never-used.invalid:6379");
        builder.UseSetting("Obfuscation:Key", Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)(i * 3)).ToArray()));
        builder.UseSetting("ShortUrl:PublicBaseUrl", PublicBaseUrl);
        builder.UseSetting("Database:MigrateOnStartup", "false");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IShortUrlWriteRepository>();
            services.AddSingleton<IShortUrlWriteRepository>(Repository);
            services.RemoveAll<IIdAllocator>();
            services.AddSingleton<IIdAllocator>(new SequentialIdAllocator());
            services.RemoveAll<IHostedService>();   // no cleanup timer in HTTP tests
            ConfigureServices(services);
        });
    }

    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }
}
