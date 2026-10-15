using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace UrlShortener.ReadService.Infrastructure.Caching;

/// <summary>
/// Connects to Redis at startup, before the server takes traffic. Created lazily inside the first request, the connection
/// handshake competes for thread-pool threads with every request waiting on the singleton, and under a cold burst it can
/// exceed ConnectTimeout (the Write Service hit exactly this). With AbortOnConnectFail=false an unreachable Redis only delays
/// startup by the connect timeout (at most 2 s here); it never stops it.
/// </summary>
public sealed class RedisConnectionWarmup(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = services.GetRequiredService<IConnectionMultiplexer>();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
