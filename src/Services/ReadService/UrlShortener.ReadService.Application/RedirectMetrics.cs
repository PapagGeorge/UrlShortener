using System.Diagnostics.Metrics;

namespace UrlShortener.ReadService.Application;

/// <summary>
/// Read-side metrics. Owns its <see cref="Meter"/> so tests can observe one instance in isolation.
/// <c>urlshortener.cache.lookups{result=hit|miss}</c>: a miss is any lookup the cache could not answer, including a Redis outage.
/// </summary>
public sealed class RedirectMetrics : IDisposable
{
    public const string MeterName = "UrlShortener.Read";
    public const string CacheLookupsName = "urlshortener.cache.lookups";

    private readonly Counter<long> _cacheLookups;

    public RedirectMetrics()
    {
        Meter = new Meter(MeterName);
        _cacheLookups = Meter.CreateCounter<long>(CacheLookupsName, unit: "{lookup}", description: "Redirect cache lookups by result.");
    }

    public Meter Meter { get; }

    public void CacheHit() => _cacheLookups.Add(1, new KeyValuePair<string, object?>("result", "hit"));

    public void CacheMiss() => _cacheLookups.Add(1, new KeyValuePair<string, object?>("result", "miss"));

    public void Dispose() => Meter.Dispose();
}
