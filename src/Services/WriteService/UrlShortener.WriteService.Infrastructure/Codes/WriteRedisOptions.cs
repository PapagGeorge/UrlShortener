using StackExchange.Redis;

namespace UrlShortener.WriteService.Infrastructure.Codes;

public static class WriteRedisOptions
{
    /// <summary>
    /// AbortOnConnectFail=false: the service starts and serves aliases even while Redis is down (the connection is retried in the background).
    /// FailFast: while disconnected, a counter reservation fails at once instead of waiting in the client backlog, so a generated-code
    /// request answers 500 immediately instead of after about a second per attempt.
    /// </summary>
    public static ConfigurationOptions Create(string connectionString)
    {
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;
        options.BacklogPolicy = BacklogPolicy.FailFast;
        return options;
    }
}
