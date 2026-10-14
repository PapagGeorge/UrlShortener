using System.Collections.Concurrent;

namespace UrlShortener.Gateway.Tests.Infrastructure;

public sealed record RecordedRequest(string Method, string PathAndQuery, IReadOnlyDictionary<string, string> Headers, long BodyLength);

/// <summary>A real HTTP server on a random localhost port that records what the Gateway forwards to it. No Docker involved.</summary>
public sealed class FakeBackend : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<RecordedRequest> _requests = new();

    private FakeBackend(WebApplication app, string address)
    {
        _app = app;
        Address = address;
    }

    public string Address { get; }

    public IReadOnlyCollection<RecordedRequest> Requests => _requests.ToArray();

    public static async Task<FakeBackend> StartAsync(Func<HttpContext, Task> handler)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();

        FakeBackend? backend = null;
        app.Run(async context =>
        {
            long length = 0;
            var buffer = new byte[8192];
            int read;
            while ((read = await context.Request.Body.ReadAsync(buffer)) > 0)
            {
                length += read;
            }

            backend!._requests.Enqueue(new RecordedRequest(
                context.Request.Method,
                context.Request.Path + context.Request.QueryString,
                context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase),
                length));

            await handler(context);
        });

        await app.StartAsync();
        backend = new FakeBackend(app, app.Urls.First());
        return backend;
    }

    /// <summary>Behaves like the Write Service: 201 with a short URL.</summary>
    public static Task<FakeBackend> StartWriteAsync() => StartAsync(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status201Created;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("""{"shortUrl":"https://short.test/0aZ9xYb"}""");
    });

    /// <summary>Behaves like the Read Service: a 302 that must not be cached.</summary>
    public static Task<FakeBackend> StartReadAsync() => StartAsync(context =>
    {
        context.Response.StatusCode = StatusCodes.Status302Found;
        context.Response.Headers.Location = "https://example.com/target";
        context.Response.Headers.CacheControl = "no-store";
        return Task.CompletedTask;
    });

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
