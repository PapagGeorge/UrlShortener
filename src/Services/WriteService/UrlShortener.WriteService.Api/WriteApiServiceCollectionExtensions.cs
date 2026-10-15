using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using UrlShortener.BuildingBlocks.Logging;
using UrlShortener.BuildingBlocks.Metrics;
using UrlShortener.BuildingBlocks.Messaging;
using UrlShortener.WriteService.Api.Options;
using UrlShortener.WriteService.Application.CreateShortUrl;
using UrlShortener.WriteService.Application.DeleteExpiredShortUrls;
using UrlShortener.WriteService.Application.Ports;
using UrlShortener.WriteService.Application.Validation;
using UrlShortener.ServiceDefaults;
using UrlShortener.WriteService.Infrastructure;
using UrlShortener.WriteService.Infrastructure.Persistence;

namespace UrlShortener.WriteService.Api;

/// <summary>The composition root of the Write Service: wires handlers and their decorators to the Infrastructure implementations.</summary>
internal static class WriteApiServiceCollectionExtensions
{
    public static IServiceCollection AddWriteApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<ShortUrlOptions>, ShortUrlOptionsValidator>();
        services.AddOptions<ShortUrlOptions>().Bind(configuration.GetSection(ShortUrlOptions.SectionName)).ValidateOnStart();
        services.AddSingleton(sp => new PublicHost(sp.GetRequiredService<IOptions<ShortUrlOptions>>().Value.Host));

        services.AddWritePersistence(configuration);
        services.AddWriteCodeGeneration(configuration);

        services.AddSingleton<IValidator<CreateShortUrlCommand>>(sp =>
            new CreateShortUrlCommandValidator(sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<PublicHost>()));

        // Metrics and logging are the outermost decorators, so validation failures are counted and logged too.
        services.AddScoped<ICommandHandler<CreateShortUrlCommand, CreateShortUrlResult>>(sp =>
        {
            var handler = new CreateShortUrlCommandHandler(
                sp.GetRequiredService<IShortUrlWriteRepository>(),
                sp.GetRequiredService<IIdAllocator>(),
                sp.GetRequiredService<ICodeObfuscator>(),
                sp.GetRequiredService<TimeProvider>());

            var validated = new ValidationCommandDecorator<CreateShortUrlCommand, CreateShortUrlResult>(
                handler, sp.GetServices<IValidator<CreateShortUrlCommand>>());

            return new MetricsCommandDecorator<CreateShortUrlCommand, CreateShortUrlResult>(
                new LoggingCommandDecorator<CreateShortUrlCommand, CreateShortUrlResult>(
                    validated, sp.GetRequiredService<ILogger<LoggingCommandDecorator<CreateShortUrlCommand, CreateShortUrlResult>>>()));
        });

        services.AddScoped<ICommandHandler<DeleteExpiredShortUrlsCommand, int>>(sp =>
            new MetricsCommandDecorator<DeleteExpiredShortUrlsCommand, int>(
                new LoggingCommandDecorator<DeleteExpiredShortUrlsCommand, int>(
                    new DeleteExpiredShortUrlsCommandHandler(sp.GetRequiredService<IShortUrlWriteRepository>(), sp.GetRequiredService<TimeProvider>()),
                    sp.GetRequiredService<ILogger<LoggingCommandDecorator<DeleteExpiredShortUrlsCommand, int>>>())));

        // Ready = can issue and store a code: Postgres, and Redis for the counter (aliases would still work, but generated codes would not).
        services.AddHealthChecks()
            .AddReadinessProbe("postgres", async (sp, ct) =>
            {
                if (!await sp.GetRequiredService<WriteDbContext>().Database.CanConnectAsync(ct))
                {
                    throw new InvalidOperationException("Postgres is not reachable.");
                }
            })
            .AddReadinessProbe("redis", async (sp, _) => await sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase().PingAsync());

        return services;
    }
}
