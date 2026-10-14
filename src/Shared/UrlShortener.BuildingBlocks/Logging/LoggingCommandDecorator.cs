using Microsoft.Extensions.Logging;
using UrlShortener.BuildingBlocks.Messaging;
using UrlShortener.BuildingBlocks.Results;

namespace UrlShortener.BuildingBlocks.Logging;

public sealed class LoggingCommandDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    ILogger<LoggingCommandDecorator<TCommand, TResult>> logger) : ICommandHandler<TCommand, TResult>
{
    public Task<Result<TResult>> Handle(TCommand command, CancellationToken ct) =>
        HandlerLog.Run(logger, typeof(TCommand).Name, () => inner.Handle(command, ct));
}
