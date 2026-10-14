using System.Net;
using Microsoft.Extensions.Options;

namespace UrlShortener.Gateway.Options;

/// <summary>Limits on <c>POST /urls</c>, per client IP. Redirects are never limited.</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    public int PermitLimit { get; init; } = 30;

    public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(1);
}

public sealed class RateLimitOptionsValidator : IValidateOptions<RateLimitOptions>
{
    private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(1);

    public ValidateOptionsResult Validate(string? name, RateLimitOptions options)
    {
        var failures = new List<string>();

        if (options.PermitLimit is < 1 or > 10_000)
        {
            failures.Add("RateLimit:PermitLimit must be between 1 and 10000.");
        }

        if (options.Window <= TimeSpan.Zero || options.Window > MaxWindow)
        {
            failures.Add($"RateLimit:Window must be greater than zero and at most {MaxWindow}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>
/// The proxies whose <c>X-Forwarded-For</c> the Gateway believes. <b>Empty by default</b>: then every forwarded header is ignored and the
/// socket address is the client, so nobody can spoof their IP to dodge the rate limit. Set these only to the proxy in front of the Gateway.
/// </summary>
public sealed class TrustedProxyOptions
{
    public const string SectionName = "ForwardedHeaders";

    public string[] KnownProxies { get; init; } = [];

    /// <summary>CIDR notation, e.g. <c>172.16.0.0/12</c>.</summary>
    public string[] KnownNetworks { get; init; } = [];

    public IReadOnlyList<IPAddress> ParseProxies() => KnownProxies.Select(IPAddress.Parse).ToArray();

    public IReadOnlyList<IPNetwork> ParseNetworks() => KnownNetworks.Select(IPNetwork.Parse).ToArray();
}

public sealed class TrustedProxyOptionsValidator : IValidateOptions<TrustedProxyOptions>
{
    public ValidateOptionsResult Validate(string? name, TrustedProxyOptions options)
    {
        var failures = new List<string>();

        foreach (var proxy in options.KnownProxies.Where(p => !IPAddress.TryParse(p, out _)))
        {
            failures.Add($"ForwardedHeaders:KnownProxies contains '{proxy}', which is not an IP address.");
        }

        foreach (var network in options.KnownNetworks)
        {
            if (!IPNetwork.TryParse(network, out var parsed))
            {
                failures.Add($"ForwardedHeaders:KnownNetworks contains '{network}', which is not a network in CIDR notation (e.g. 172.16.0.0/12).");
            }
            else if (parsed.PrefixLength == 0)
            {
                failures.Add($"ForwardedHeaders:KnownNetworks contains '{network}', which would trust every address and let anyone spoof their IP.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
