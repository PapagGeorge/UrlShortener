using UrlShortener.BuildingBlocks.Messaging;
using UrlShortener.BuildingBlocks.Results;

namespace UrlShortener.BuildingBlocks.Metrics;

public sealed class MetricsCommandDecorator<TCommand, TResult>(ICommandHandler<TCommand, TResult> inner)
    : ICommandHandler<TCommand, TResult>
{
    public Task<Result<TResult>> Handle(TCommand command, CancellationToken ct) =>
        HandlerMetrics.Measure(typeof(TCommand).Name, () => inner.Handle(command, ct));
}
