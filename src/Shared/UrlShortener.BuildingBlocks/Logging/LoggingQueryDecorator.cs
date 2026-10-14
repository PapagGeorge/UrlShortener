using Microsoft.Extensions.Logging;
using UrlShortener.BuildingBlocks.Messaging;
using UrlShortener.BuildingBlocks.Results;

namespace UrlShortener.BuildingBlocks.Logging;

public sealed class LoggingQueryDecorator<TQuery, TResult>(
    IQueryHandler<TQuery, TResult> inner,
    ILogger<LoggingQueryDecorator<TQuery, TResult>> logger) : IQueryHandler<TQuery, TResult>
{
    public Task<Result<TResult>> Handle(TQuery query, CancellationToken ct) =>
        HandlerLog.Run(logger, typeof(TQuery).Name, () => inner.Handle(query, ct));
}
