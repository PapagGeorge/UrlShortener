using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UrlShortener.WriteService.Application.Ports;
using UrlShortener.WriteService.Infrastructure;
using UrlShortener.WriteService.Infrastructure.Codes;

namespace UrlShortener.WriteService.UnitTests.Codes;

public sealed class CodeGenerationRegistrationTests
{
    private static readonly string ValidKey = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

    private static (ServiceCollection Services, ServiceProvider Provider) Build(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddWriteCodeGeneration(configuration);
        return (services, services.BuildServiceProvider());
    }

    private static readonly (string, string?) Redis = ("ConnectionStrings:Redis", "redis-registration-test.invalid:6379");
    private static readonly (string, string?) Key = ("Obfuscation:Key", ValidKey);

    [Fact]
    public void AddWriteCodeGeneration_WithoutARedisConnectionString_FailsFast()
    {
        var ex = Should.Throw<InvalidOperationException>(() => Build(Config(Key)));

        ex.Message.ShouldContain("ConnectionStrings:Redis");
    }

    [Fact]
    public void ICodeObfuscator_WithAValidKey_ProducesCodes()
    {
        var (_, provider) = Build(Config(Redis, Key));

        var code = provider.GetRequiredService<ICodeObfuscator>().ToCode(1);

        code.Value.Length.ShouldBe(7);
        provider.GetRequiredService<ICodeObfuscator>().ShouldBeSameAs(provider.GetRequiredService<ICodeObfuscator>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("c2hvcnQ=")]   // "short"
    public void ICodeObfuscator_WithAMissingOrWeakKey_FailsOnFirstUse(string? key)
    {
        var (_, provider) = Build(Config(Redis, ("Obfuscation:Key", key)));

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<ICodeObfuscator>());
    }

    [Fact]
    public void Allocator_IsASingleton_SoEveryRequestSharesTheBatch()
    {
        var (services, _) = Build(Config(Redis, Key));

        var descriptor = services.Single(d => d.ServiceType == typeof(IIdAllocator));

        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    [Fact]
    public void CounterOptions_AreBoundAndValidated()
    {
        var (_, provider) = Build(Config(Redis, Key, ("Counter:BatchSize", "250")));
        provider.GetRequiredService<IOptions<CounterOptions>>().Value.BatchSize.ShouldBe(250);

        var (_, invalid) = Build(Config(Redis, Key, ("Counter:BatchSize", "0")));
        Should.Throw<OptionsValidationException>(() => invalid.GetRequiredService<IOptions<CounterOptions>>().Value);
    }

    [Fact]
    public void Registration_DoesNotConnectToRedisUntilTheCounterIsNeeded()
    {
        // Resolving the obfuscator must not touch Redis: aliases keep working while Redis is down.
        var (_, provider) = Build(Config(Redis, Key));

        Should.NotThrow(() => provider.GetRequiredService<ICodeObfuscator>());
    }
}
