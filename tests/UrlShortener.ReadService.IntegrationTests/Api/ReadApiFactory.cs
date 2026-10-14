using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using UrlShortener.Domain;
using UrlShortener.ReadService.Application;
using UrlShortener.ReadService.Application.Ports;

namespace UrlShortener.ReadService.IntegrationTests.Api;

public sealed class InMemoryReadRepository : IShortUrlReadRepository
{
    private int _calls;

    public ConcurrentDictionary<string, RedirectTarget> Items { get; } = new(StringComparer.Ordinal);

    public int Calls => Volatile.Read(ref _calls);

    public Exception? FailWith { get; set; }

    public Task<RedirectTarget?> FindAsync(ShortCode code, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        return FailWith is not null
            ? throw FailWith
            : Task.FromResult(Items.TryGetValue(code.Value, out var target) ? target : null);
    }
}

public sealed class InMemoryCache : IShortUrlCache
{
    private int _reads;

    public ConcurrentDictionary<string, (RedirectTarget Target, TimeSpan Ttl)> Items { get; } = new(StringComparer.Ordinal);

    public int Reads => Volatile.Read(ref _reads);

    public Task<RedirectTarget?> GetAsync(ShortCode code, CancellationToken ct)
    {
        Interlocked.Increment(ref _reads);
        return Task.FromResult(Items.TryGetValue(code.Value, out var entry) ? entry.Target : null);
    }

    public Task SetAsync(ShortCode code, RedirectTarget target, TimeSpan ttl, CancellationToken ct)
    {
        Items[code.Value] = (target, ttl);
        return Task.CompletedTask;
    }
}

/// <summary>The real Read Service pipeline (routing, handler, decorators) with only the database and Redis ports replaced by in-memory fakes, so it runs without Docker.</summary>
public sealed class ReadApiFactory : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    public InMemoryReadRepository Repository { get; } = new();

    public InMemoryCache Cache { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", "Host=postgres-never-used.invalid;Database=x");
        builder.UseSetting("ConnectionStrings:Redis", "redis-never-used.invalid:6379");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IShortUrlReadRepository>();
            services.AddSingleton<IShortUrlReadRepository>(Repository);
            services.RemoveAll<IShortUrlCache>();
            services.AddSingleton<IShortUrlCache>(Cache);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        });
    }

    /// <summary>A client that does not follow redirects, so the 302 itself can be inspected.</summary>
    public HttpClient CreateNonRedirectingClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}
