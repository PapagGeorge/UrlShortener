using UrlShortener.BuildingBlocks.Messaging;
using UrlShortener.BuildingBlocks.Results;

namespace UrlShortener.BuildingBlocks.Metrics;

public sealed class MetricsQueryDecorator<TQuery, TResult>(IQueryHandler<TQuery, TResult> inner)
    : IQueryHandler<TQuery, TResult>
{
    public Task<Result<TResult>> Handle(TQuery query, CancellationToken ct) =>
        HandlerMetrics.Measure(typeof(TQuery).Name, () => inner.Handle(query, ct));
}
