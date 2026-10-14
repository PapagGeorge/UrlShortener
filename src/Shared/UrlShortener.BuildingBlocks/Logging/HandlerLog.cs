using System.Diagnostics;
using Microsoft.Extensions.Logging;
using UrlShortener.BuildingBlocks.Results;

namespace UrlShortener.BuildingBlocks.Logging;

/// <summary>
/// Shared logging for the command and query decorators. Logs the request type, outcome and duration,
/// never the request payload (URLs and aliases may be sensitive).
/// </summary>
internal static class HandlerLog
{
    public static async Task<Result<TResult>> Run<TResult>(
        ILogger logger, string requestName, Func<Task<Result<TResult>>> handle)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await handle();
            var elapsed = Stopwatch.GetElapsedTime(started);

            if (result.IsSuccess)
            {
                logger.LogInformation("Handled {Request} successfully in {ElapsedMs} ms", requestName, elapsed.TotalMilliseconds);
            }
            else if (result.Error.Type == ErrorType.Failure)
            {
                logger.LogError("Handling {Request} failed with {ErrorCode} in {ElapsedMs} ms", requestName, result.Error.Code, elapsed.TotalMilliseconds);
            }
            else
            {
                logger.LogInformation("Handling {Request} ended with {ErrorCode} in {ElapsedMs} ms", requestName, result.Error.Code, elapsed.TotalMilliseconds);
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Handling {Request} threw after {ElapsedMs} ms", requestName, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }
}
