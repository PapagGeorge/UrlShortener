using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using UrlShortener.WriteService.Infrastructure.Codes;

namespace UrlShortener.WriteService.UnitTests.Codes;

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
            return NSubstitute.Substitute.For<IConnectionMultiplexer>();
        });
        await using var provider = services.BuildServiceProvider();
        var sut = new RedisConnectionWarmup(provider);

        await sut.StartAsync(CancellationToken.None);

        created.ShouldBe(1);
    }
}
