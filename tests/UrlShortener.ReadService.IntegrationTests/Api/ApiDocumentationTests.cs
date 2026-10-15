using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace UrlShortener.ReadService.IntegrationTests.Api;

/// <summary>The OpenAPI document and the Scalar UI exist in Development only.</summary>
public sealed class ApiDocumentationTests(ReadApiFactory factory) : IClassFixture<ReadApiFactory>
{
    private HttpClient Client(string environment) =>
        factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment)).CreateClient();

    [Fact]
    public async Task Development_ServesTheOpenApiDocumentWithTheRedirectOperation()
    {
        using var response = await Client("Development").GetAsync("/openapi/v1.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var paths = document.GetProperty("paths");
        paths.EnumerateObject().Select(p => p.Name).ToList().ShouldBe(["/{shortCode}"]);
        var get = paths.GetProperty("/{shortCode}").GetProperty("get");
        foreach (var status in new[] { "302", "404", "410" })
        {
            get.GetProperty("responses").TryGetProperty(status, out _).ShouldBeTrue($"response {status} is documented");
        }
    }

    [Fact]
    public async Task Development_ServesTheScalarUi()
    {
        (await Client("Development").GetAsync("/scalar/v1")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Testing")]
    public async Task OutsideDevelopment_NeitherTheDocumentNorTheUiExist(string environment)
    {
        using var client = Client(environment);

        (await client.GetAsync("/openapi/v1.json")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync("/scalar/v1")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
