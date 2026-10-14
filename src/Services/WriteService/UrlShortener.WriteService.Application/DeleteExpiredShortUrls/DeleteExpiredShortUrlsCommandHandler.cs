using UrlShortener.BuildingBlocks.Messaging;
using UrlShortener.BuildingBlocks.Results;
using UrlShortener.WriteService.Application.Ports;

namespace UrlShortener.WriteService.Application.DeleteExpiredShortUrls;

/// <summary>Deletes expired rows in batches until a short batch shows nothing is left. Returns the total deleted.</summary>
public sealed class DeleteExpiredShortUrlsCommandHandler(IShortUrlWriteRepository repository, TimeProvider time)
    : ICommandHandler<DeleteExpiredShortUrlsCommand, int>
{
    public async Task<Result<int>> Handle(DeleteExpiredShortUrlsCommand command, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(command.BatchSize);

        var now = time.GetUtcNow();
        var total = 0;
        int deleted;
        do
        {
            ct.ThrowIfCancellationRequested();
            deleted = await repository.DeleteExpiredAsync(now, command.BatchSize, ct);
            total += deleted;
        }
        while (deleted == command.BatchSize);

        return total;
    }
}
