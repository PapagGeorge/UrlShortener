using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace UrlShortener.WriteService.IntegrationTests.Api;

/// <summary>The OpenAPI document and the Scalar UI exist in Development only: the repo is public and production must not advertise its API surface.</summary>
public sealed class ApiDocumentationTests(WriteApiFactory factory) : IClassFixture<WriteApiFactory>
{
    private HttpClient Client(string environment) =>
        factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment)).CreateClient();

    private async Task<JsonElement> Document()
    {
        using var response = await Client("Development").GetAsync("/openapi/v1.json");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Fact]
    public async Task Development_ServesTheOpenApiDocumentWithTheCreateOperation()
    {
        var document = await Document();

        var post = document.GetProperty("paths").GetProperty("/urls").GetProperty("post");
        post.GetProperty("summary").GetString().ShouldNotBeNullOrWhiteSpace();
        var responses = post.GetProperty("responses");
        foreach (var status in new[] { "201", "400", "409", "500" })
        {
            responses.TryGetProperty(status, out _).ShouldBeTrue($"response {status} is documented");
        }
    }

    [Fact]
    public async Task Development_DocumentDoesNotExposeInternalEndpoints()
    {
        var paths = (await Document()).GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();

        paths.ShouldBe(["/urls"]);   // no /health, no /health/ready
    }

    [Fact]
    public async Task Development_ServesTheScalarUi()
    {
        using var client = Client("Development");

        var response = await client.GetAsync("/scalar/v1");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
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
