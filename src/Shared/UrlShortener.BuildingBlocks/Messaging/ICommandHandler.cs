using UrlShortener.BuildingBlocks.Results;

namespace UrlShortener.BuildingBlocks.Messaging;

/// <summary>Handles a command (a state change). Own abstraction: no MediatR.</summary>
public interface ICommandHandler<in TCommand, TResult>
{
    Task<Result<TResult>> Handle(TCommand command, CancellationToken ct);
}
