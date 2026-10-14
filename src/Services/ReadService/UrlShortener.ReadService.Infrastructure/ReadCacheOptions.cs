using Microsoft.Extensions.Options;

namespace UrlShortener.ReadService.Infrastructure;

public sealed class ReadCacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>Upper bound for a cached redirect; a URL that expires sooner is cached only until then.</summary>
    public TimeSpan DefaultTtl { get; init; } = TimeSpan.FromHours(24);

    /// <summary>Per-operation Redis timeout. Short on purpose: a slow Redis must not blow the redirect latency budget (the database is the fallback).</summary>
    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromMilliseconds(100);
}

public sealed class ReadCacheOptionsValidator : IValidateOptions<ReadCacheOptions>
{
    private static readonly TimeSpan MinTtl = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan MinTimeout = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromSeconds(5);

    public ValidateOptionsResult Validate(string? name, ReadCacheOptions options)
    {
        var failures = new List<string>();

        if (options.DefaultTtl < MinTtl || options.DefaultTtl > MaxTtl)
        {
            failures.Add($"Cache:DefaultTtl must be between {MinTtl} and {MaxTtl}.");
        }

        if (options.OperationTimeout < MinTimeout || options.OperationTimeout > MaxTimeout)
        {
            failures.Add($"Cache:OperationTimeout must be between {MinTimeout} and {MaxTimeout}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
