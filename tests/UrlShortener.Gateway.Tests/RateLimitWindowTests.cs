using System.Net;
using System.Text;
using UrlShortener.Gateway.Tests.Infrastructure;

namespace UrlShortener.Gateway.Tests;

public sealed class RateLimitWindowTests
{
    private static Task<HttpResponseMessage> Post(HttpClient client, string ip)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/urls") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        request.Headers.Add(GatewayFactory.RemoteIpHeader, ip);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task AfterTheWindowPasses_TheClientMayCreateAgain_AndTheLimitFollowsTheConfiguration()
    {
        await using var write = await FakeBackend.StartWriteAsync();
        await using var read = await FakeBackend.StartReadAsync();
        await using var factory = new GatewayFactory(write.Address, read.Address, new Dictionary<string, string?>
        {
            ["RateLimit:PermitLimit"] = "3",
            ["RateLimit:Window"] = "00:00:02",
        });
        using var client = factory.CreateNonRedirectingClient();
        var ip = GatewayFixture.NewClientIp();

        for (var i = 0; i < 3; i++)
        {
            (await Post(client, ip)).StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var limited = await Post(client, ip);
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        int.Parse(limited.Headers.GetValues("Retry-After").Single()).ShouldBeInRange(1, 2);

        await Task.Delay(TimeSpan.FromSeconds(2.5));

        (await Post(client, ip)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}
