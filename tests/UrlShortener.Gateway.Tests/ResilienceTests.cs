using System.Net;
using System.Text;
using UrlShortener.Gateway.Tests.Infrastructure;

namespace UrlShortener.Gateway.Tests;

public sealed class ResilienceTests
{
    private static HttpRequestMessage Post(string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/urls") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add(GatewayFactory.RemoteIpHeader, GatewayFixture.NewClientIp());
        return request;
    }

    private static HttpRequestMessage Get(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(GatewayFactory.RemoteIpHeader, GatewayFixture.NewClientIp());
        return request;
    }

    [Fact]
    public async Task WhenABackendIsDown_TheGatewayAnswersWithAGatewayErrorAndLeaksNothingInternal()
    {
        var down = await FakeBackend.StartWriteAsync();
        var downAddress = down.Address;
        await down.DisposeAsync();   // nothing listens on that port any more
        await using var read = await FakeBackend.StartReadAsync();
        await using var factory = new GatewayFactory(downAddress, read.Address);
        using var client = factory.CreateNonRedirectingClient();

        var response = await client.SendAsync(Post("{}"));

        ((int)response.StatusCode).ShouldBeInRange(502, 504);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("127.0.0.1");
        (await client.SendAsync(Get("/0aZ9xYb"))).StatusCode.ShouldBe(HttpStatusCode.Found);   // the other backend is unaffected
    }

    [Fact]
    public async Task WhenABackendIsTooSlow_TheGatewayGivesUpWithA504()
    {
        await using var slow = await FakeBackend.StartAsync(async context =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), context.RequestAborted);
            context.Response.StatusCode = 201;
        });
        await using var read = await FakeBackend.StartReadAsync();
        await using var factory = new GatewayFactory(slow.Address, read.Address, new Dictionary<string, string?>
        {
            ["ReverseProxy:Clusters:write:HttpRequest:ActivityTimeout"] = "00:00:01",
        });
        using var client = factory.CreateNonRedirectingClient();

        var started = DateTime.UtcNow;
        var response = await client.SendAsync(Post("{}"));

        response.StatusCode.ShouldBe(HttpStatusCode.GatewayTimeout);
        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task ABackendError_IsPassedThroughWithItsBody()
    {
        await using var write = await FakeBackend.StartAsync(async context =>
        {
            context.Response.StatusCode = 409;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync("""{"code":"ShortUrl.AliasTaken"}""");
        });
        await using var read = await FakeBackend.StartReadAsync();
        await using var factory = new GatewayFactory(write.Address, read.Address);
        using var client = factory.CreateNonRedirectingClient();

        var response = await client.SendAsync(Post("{}"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldBe("""{"code":"ShortUrl.AliasTaken"}""");
    }

    [Fact]
    public async Task ARequestBodyWithinTheLimit_IsForwarded()
    {
        await using var write = await FakeBackend.StartWriteAsync();
        await using var read = await FakeBackend.StartReadAsync();
        await using var factory = new GatewayFactory(write.Address, read.Address);
        factory.UseKestrel();
        factory.StartServer();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/urls", new StringContent(new string('a', 8192), Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        write.Requests.Single().BodyLength.ShouldBe(8192);
    }

    [Fact]
    public async Task ARequestBodyOverTheLimit_IsRejectedWith413AndNeverReachesTheWriteService()
    {
        await using var write = await FakeBackend.StartWriteAsync();
        await using var read = await FakeBackend.StartReadAsync();
        await using var factory = new GatewayFactory(write.Address, read.Address);
        // The in-memory TestServer has no request-size feature, so the limit would not be enforced there. Real Kestrel is what runs in production.
        factory.UseKestrel();
        factory.StartServer();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/urls", new StringContent(new string('a', 8193), Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        write.Requests.ShouldBeEmpty();
    }
}
