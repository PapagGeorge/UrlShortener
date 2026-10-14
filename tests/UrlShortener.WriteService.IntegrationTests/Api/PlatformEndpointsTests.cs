using System.Net;

namespace UrlShortener.WriteService.IntegrationTests.Api;

public sealed class PlatformEndpointsTests(WriteApiFactory factory) : IClassFixture<WriteApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_Returns200Healthy()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Get_OnTheCreateEndpoint_Returns405()
    {
        (await _client.GetAsync("/urls")).StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        (await _client.GetAsync("/nothing-here")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WriteService_DoesNotServeRedirects()
    {
        // GET /{code} belongs to the Read Service: the Write Service must not answer it.
        (await _client.GetAsync("/0aZ9xYb")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
