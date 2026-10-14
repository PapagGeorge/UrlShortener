using Microsoft.Extensions.Options;

namespace UrlShortener.WriteService.Infrastructure.Codes;

/// <summary>The secret that keys the code permutation. Comes from configuration (environment or user-secrets), never from the repo.</summary>
public sealed class ObfuscationOptions
{
    public const string SectionName = "Obfuscation";

    /// <summary>Base64, at least 32 bytes. Never rotate it once codes exist: that would re-map the whole code space and collide with stored rows.</summary>
    public string? Key { get; init; }

    public byte[] GetKeyBytes() => Convert.FromBase64String(Key!);
}

public sealed class ObfuscationOptionsValidator : IValidateOptions<ObfuscationOptions>
{
    private const int MinimumKeyBytes = 32;

    public ValidateOptionsResult Validate(string? name, ObfuscationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Key))
        {
            return ValidateOptionsResult.Fail("Obfuscation:Key is required (base64, at least 32 bytes).");
        }

        // The key itself is never echoed in a message.
        Span<byte> buffer = new byte[options.Key.Length];
        if (!Convert.TryFromBase64String(options.Key, buffer, out var written))
        {
            return ValidateOptionsResult.Fail("Obfuscation:Key must be valid base64.");
        }

        return written < MinimumKeyBytes
            ? ValidateOptionsResult.Fail($"Obfuscation:Key must decode to at least {MinimumKeyBytes} bytes.")
            : ValidateOptionsResult.Success;
    }
}

public sealed class CounterOptions
{
    public const string SectionName = "Counter";

    /// <summary>Ids reserved per Redis round trip.</summary>
    public int BatchSize { get; init; } = 1000;

    public string RedisKey { get; init; } = RedisCounterStore.DefaultKey;
}

public sealed class CounterOptionsValidator : IValidateOptions<CounterOptions>
{
    private const int MaxBatchSize = 100_000;

    public ValidateOptionsResult Validate(string? name, CounterOptions options)
    {
        var failures = new List<string>();

        if (options.BatchSize is < 1 or > MaxBatchSize)
        {
            failures.Add($"Counter:BatchSize must be between 1 and {MaxBatchSize}.");
        }

        if (string.IsNullOrWhiteSpace(options.RedisKey))
        {
            failures.Add("Counter:RedisKey must not be blank.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
