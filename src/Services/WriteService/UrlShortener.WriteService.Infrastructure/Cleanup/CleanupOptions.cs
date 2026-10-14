using Microsoft.Extensions.Options;

namespace UrlShortener.WriteService.Infrastructure.Cleanup;

public sealed class CleanupOptions
{
    public const string SectionName = "Cleanup";

    /// <summary>How often expired rows are deleted.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Rows deleted per round trip.</summary>
    public int BatchSize { get; init; } = 1000;
}

public sealed class CleanupOptionsValidator : IValidateOptions<CleanupOptions>
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(1);
    private const int MaxBatchSize = 10_000;

    public ValidateOptionsResult Validate(string? name, CleanupOptions options)
    {
        var failures = new List<string>();

        if (options.Interval < MinimumInterval)
        {
            failures.Add($"{nameof(CleanupOptions.Interval)} must be at least {MinimumInterval.TotalSeconds} second.");
        }

        if (options.BatchSize is < 1 or > MaxBatchSize)
        {
            failures.Add($"{nameof(CleanupOptions.BatchSize)} must be between 1 and {MaxBatchSize}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
