using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using UrlShortener.ReadService.Application;
using UrlShortener.ReadService.Application.Ports;
using UrlShortener.ReadService.Infrastructure;
using UrlShortener.ReadService.Infrastructure.Caching;
using UrlShortener.ReadService.Infrastructure.Persistence;

namespace UrlShortener.ReadService.UnitTests.Persistence;

public sealed class ReadInfrastructureRegistrationTests
{
    private const string Postgres = "ConnectionStrings:Postgres";
    private const string Redis = "ConnectionStrings:Redis";
    private const string AnyPostgres = "Host=read-registration-test.invalid;Database=x";
    private const string AnyRedis = "redis-registration-test.invalid:6379";

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

    private static ServiceProvider Build(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReadInfrastructure(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Theory]
    [InlineData(Postgres)]
    [InlineData(Redis)]
    public void MissingConnectionString_FailsFast(string missing)
    {
        var values = new[] { (Postgres, (string?)AnyPostgres), (Redis, AnyRedis) }.Where(v => v.Item1 != missing).ToArray();

        Should.Throw<InvalidOperationException>(() => Build(Config(values))).Message.ShouldContain(missing);
    }

    [Fact]
    public void Repository_IsRegisteredPerScope()
    {
        using var provider = Build(Config((Postgres, AnyPostgres), (Redis, AnyRedis)));
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IShortUrlReadRepository>().ShouldBeOfType<ShortUrlReadRepository>();
    }

    [Fact]
    public void ReadDbContext_NeverTracksEntities()
    {
        using var provider = Build(Config((Postgres, AnyPostgres), (Redis, AnyRedis)));
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ReadDbContext>().ChangeTracker.QueryTrackingBehavior
            .ShouldBe(QueryTrackingBehavior.NoTracking);
    }

    [Fact]
    public void Cache_IsASingletonRedisImplementation()
    {
        using var provider = Build(Config((Postgres, AnyPostgres), (Redis, AnyRedis)));

        var cache = provider.GetRequiredService<IShortUrlCache>();

        cache.ShouldBeOfType<RedisShortUrlCache>();
        provider.GetRequiredService<IShortUrlCache>().ShouldBeSameAs(cache);
    }

    [Fact]
    public void CachePolicy_UsesTheConfiguredDefaultTtl()
    {
        using var provider = Build(Config((Postgres, AnyPostgres), (Redis, AnyRedis), ("Cache:DefaultTtl", "02:00:00")));

        provider.GetRequiredService<RedirectCachePolicy>().DefaultTtl.ShouldBe(TimeSpan.FromHours(2));
    }

    [Fact]
    public void CacheOptions_DefaultsAreSensible()
    {
        using var provider = Build(Config((Postgres, AnyPostgres), (Redis, AnyRedis)));

        var options = provider.GetRequiredService<IOptions<ReadCacheOptions>>().Value;

        options.DefaultTtl.ShouldBe(TimeSpan.FromHours(24));
        options.OperationTimeout.ShouldBe(TimeSpan.FromMilliseconds(100));
    }

    [Theory]
    [InlineData("Cache:DefaultTtl", "00:00:00")]
    [InlineData("Cache:DefaultTtl", "8.00:00:00")]
    [InlineData("Cache:OperationTimeout", "00:00:00.001")]
    [InlineData("Cache:OperationTimeout", "00:01:00")]
    public void CacheOptions_OutOfRange_AreRejected(string key, string value)
    {
        using var provider = Build(Config((Postgres, AnyPostgres), (Redis, AnyRedis), (key, value)));

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ReadCacheOptions>>().Value);
    }

    [Fact]
    public void RedisConnection_IsConfiguredToFailFastAndNeverAbortStartup()
    {
        using var provider = Build(Config((Postgres, AnyPostgres), (Redis, AnyRedis), ("Cache:OperationTimeout", "00:00:00.250")));

        var options = ReadInfrastructureOptions.RedisOptions(AnyRedis, new ReadCacheOptions { OperationTimeout = TimeSpan.FromMilliseconds(250) });

        options.AbortOnConnectFail.ShouldBeFalse();
        options.AsyncTimeout.ShouldBe(250);
        options.SyncTimeout.ShouldBe(250);
        options.ConnectTimeout.ShouldBeLessThanOrEqualTo(2000);
        provider.GetRequiredService<ILoggerFactory>().ShouldNotBeNull();
    }
}
