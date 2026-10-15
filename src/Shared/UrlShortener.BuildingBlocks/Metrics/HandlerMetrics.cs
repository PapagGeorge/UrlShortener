using System.Diagnostics;
using System.Diagnostics.Metrics;
using UrlShortener.BuildingBlocks.Results;

namespace UrlShortener.BuildingBlocks.Metrics;

/// <summary>
/// The handler-level metrics shared by both services. Tags are low-cardinality on purpose:
/// <c>request</c> is the request type name, <c>outcome</c> is <c>success</c>, the lowercase <see cref="ErrorType"/>, or <c>exception</c>.
/// Never tag with a code, URL or alias.
/// </summary>
public static class HandlerMetrics
{
    public const string MeterName = "UrlShortener.Handlers";
    public const string RequestsName = "urlshortener.handler.requests";
    public const string DurationName = "urlshortener.handler.duration";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>(RequestsName, unit: "{request}", description: "Handled requests by outcome.");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(DurationName, unit: "s", description: "Handler duration by outcome.");

    internal static async Task<Result<TResult>> Measure<TResult>(string requestName, Func<Task<Result<TResult>>> handle)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "exception";
        try
        {
            var result = await handle();
            outcome = result.IsSuccess ? "success" : result.Error.Type.ToString().ToLowerInvariant();
            return result;
        }
        finally
        {
            var tags = new TagList { { "request", requestName }, { "outcome", outcome } };
            Requests.Add(1, tags);
            Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
        }
    }
}
