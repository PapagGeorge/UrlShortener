using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using UrlShortener.WriteService.Application.Ports;
using UrlShortener.WriteService.Infrastructure;
using UrlShortener.WriteService.Infrastructure.Cleanup;
using UrlShortener.WriteService.Infrastructure.Persistence;

namespace UrlShortener.WriteService.UnitTests.Persistence;

public sealed class PersistenceRegistrationTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

    private static ServiceProvider Build(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWritePersistence(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private const string Postgres = "ConnectionStrings:Postgres";
    private const string AnyConnection = "Host=registration-test.invalid;Database=x";

    [Fact]
    public void AddWritePersistence_WithoutAConnectionString_FailsFast()
    {
        var ex = Should.Throw<InvalidOperationException>(() => Build(Config()));

        ex.Message.ShouldContain("ConnectionStrings:Postgres");
    }

    [Fact]
    public void AddWritePersistence_RegistersTheRepositoryAndDbContextPerScope()
    {
        using var provider = Build(Config((Postgres, AnyConnection)));
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IShortUrlWriteRepository>().ShouldBeOfType<ShortUrlWriteRepository>();
        scope.ServiceProvider.GetRequiredService<WriteDbContext>().ShouldNotBeNull();
    }

    [Fact]
    public void AddWritePersistence_RegistersTheCleanupJobAsAHostedService()
    {
        using var provider = Build(Config((Postgres, AnyConnection)));

        provider.GetServices<IHostedService>().ShouldContain(s => s is ExpiredUrlCleanupService);
    }

    [Fact]
    public void CleanupOptions_AreBoundFromConfiguration()
    {
        using var provider = Build(Config((Postgres, AnyConnection), ("Cleanup:Interval", "00:05:00"), ("Cleanup:BatchSize", "250")));

        var options = provider.GetRequiredService<IOptions<CleanupOptions>>().Value;

        options.Interval.ShouldBe(TimeSpan.FromMinutes(5));
        options.BatchSize.ShouldBe(250);
    }

    [Fact]
    public void CleanupOptions_InvalidValues_AreRejected()
    {
        using var provider = Build(Config((Postgres, AnyConnection), ("Cleanup:BatchSize", "0")));

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<CleanupOptions>>().Value)
            .Message.ShouldContain(nameof(CleanupOptions.BatchSize));
    }

    [Fact]
    public void AddWritePersistence_UsesTheSystemTimeProviderUnlessOneIsRegistered()
    {
        using var provider = Build(Config((Postgres, AnyConnection)));

        provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(TimeProvider.System);
    }
}
