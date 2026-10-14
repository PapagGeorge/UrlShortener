using UrlShortener.BuildingBlocks.Messaging;
using UrlShortener.BuildingBlocks.Results;
using UrlShortener.Domain;
using UrlShortener.WriteService.Application.Ports;

namespace UrlShortener.WriteService.Application.CreateShortUrl;

public sealed class CreateShortUrlCommandHandler(
    IShortUrlWriteRepository repository,
    IIdAllocator allocator,
    ICodeObfuscator obfuscator,
    TimeProvider time) : ICommandHandler<CreateShortUrlCommand, CreateShortUrlResult>
{
    /// <summary>Generated codes can collide only if the counter was reset (e.g. Redis lost its data); a fresh id fixes that.</summary>
    private const int MaxGenerationAttempts = 3;

    public async Task<Result<CreateShortUrlResult>> Handle(CreateShortUrlCommand command, CancellationToken ct)
    {
        var originalUrl = OriginalUrl.Create(command.OriginalUrl);
        if (originalUrl.IsFailure)
        {
            return originalUrl.Error;
        }

        var now = time.GetUtcNow();

        return command.Alias is null
            ? await CreateWithGeneratedCode(originalUrl.Value, command.ExpiresAt, now, ct)
            : await CreateWithAlias(command.Alias, originalUrl.Value, command.ExpiresAt, now, ct);
    }

    private async Task<Result<CreateShortUrlResult>> CreateWithAlias(
        string aliasInput, OriginalUrl originalUrl, DateTimeOffset? expiresAt, DateTimeOffset now, CancellationToken ct)
    {
        var alias = Alias.Create(aliasInput);
        if (alias.IsFailure)
        {
            return alias.Error;
        }

        var shortUrl = ShortUrl.Create(alias.Value.Code, originalUrl, now, expiresAt);
        if (shortUrl.IsFailure)
        {
            return shortUrl.Error;
        }

        return await repository.TryAddAsync(shortUrl.Value, ct)
            ? new CreateShortUrlResult(alias.Value.Code)
            : WriteErrors.AliasTaken;
    }

    private async Task<Result<CreateShortUrlResult>> CreateWithGeneratedCode(
        OriginalUrl originalUrl, DateTimeOffset? expiresAt, DateTimeOffset now, CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxGenerationAttempts; attempt++)
        {
            var id = await allocator.NextAsync(ct);
            var code = obfuscator.ToCode(id);

            if (code.IsReserved)
            {
                continue; // would shadow a route such as /swagger: take another id
            }

            var shortUrl = ShortUrl.Create(code, originalUrl, now, expiresAt);
            if (shortUrl.IsFailure)
            {
                return shortUrl.Error;
            }

            if (await repository.TryAddAsync(shortUrl.Value, ct))
            {
                return new CreateShortUrlResult(code);
            }
        }

        return WriteErrors.CodeGenerationFailed;
    }
}
