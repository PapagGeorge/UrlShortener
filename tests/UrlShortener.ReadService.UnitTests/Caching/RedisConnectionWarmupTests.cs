using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using StackExchange.Redis;
using UrlShortener.ReadService.Infrastructure.Caching;

namespace UrlShortener.ReadService.UnitTests.Caching;

public sealed class RedisConnectionWarmupTests
{
    [Fact]
    public async Task StartAsync_CreatesTheRedisConnectionBeforeAnyRequestNeedsIt()
    {
        var created = 0;
        var services = new ServiceCollection();
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            created++;
            return Substitute.For<IConnectionMultiplexer>();
        });
        await using var provider = services.BuildServiceProvider();
        var sut = new RedisConnectionWarmup(provider);

        await sut.StartAsync(CancellationToken.None);

        created.ShouldBe(1);
    }
}
