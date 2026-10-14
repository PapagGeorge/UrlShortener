using UrlShortener.BuildingBlocks.Results;

namespace UrlShortener.BuildingBlocks.Messaging;

/// <summary>Handles a query (a read with no side effects). Own abstraction: no MediatR.</summary>
public interface IQueryHandler<in TQuery, TResult>
{
    Task<Result<TResult>> Handle(TQuery query, CancellationToken ct);
}
