using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UrlShortener.BuildingBlocks.Messaging;
using UrlShortener.WriteService.Application.DeleteExpiredShortUrls;

namespace UrlShortener.WriteService.Infrastructure.Cleanup;

/// <summary>
/// Periodically runs <see cref="DeleteExpiredShortUrlsCommand"/>. Safe with several replicas: deletes are idempotent.
/// A failed tick is logged and the loop carries on, so a transient database error never kills the job.
/// </summary>
public sealed partial class ExpiredUrlCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CleanupOptions _options;
    private readonly ILogger<ExpiredUrlCleanupService> _logger;

    // Created in the constructor, not in ExecuteAsync: since .NET 10 the host no longer runs the start of
    // ExecuteAsync synchronously, so creating it there would make the first interval start at an unpredictable moment.
    private readonly PeriodicTimer _timer;

    public ExpiredUrlCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptions<CleanupOptions> options,
        TimeProvider time,
        ILogger<ExpiredUrlCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
        _timer = new PeriodicTimer(_options.Interval, time);
    }

    public override void Dispose()
    {
        _timer.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteExpiredShortUrlsCommand, int>>();

            var result = await handler.Handle(new DeleteExpiredShortUrlsCommand(_options.BatchSize), ct);

            if (result.IsSuccess)
            {
                LogDeleted(result.Value);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Expired URL cleanup deleted {Count} rows")]
    private partial void LogDeleted(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Expired URL cleanup failed; it will retry on the next interval")]
    private partial void LogFailed(Exception exception);
}
