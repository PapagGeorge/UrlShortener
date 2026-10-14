using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.BuildingBlocks.Messaging;
using UrlShortener.BuildingBlocks.Results;
using UrlShortener.WriteService.Application.CreateShortUrl;

namespace UrlShortener.WriteService.IntegrationTests.Api;

/// <summary>How a handler outcome becomes an HTTP response, independent of the real handler (a stub returns the outcome).</summary>
public sealed class ErrorMappingTests
{
    private sealed class StubHandler(Func<CreateShortUrlCommand, Result<CreateShortUrlResult>> behaviour)
        : ICommandHandler<CreateShortUrlCommand, CreateShortUrlResult>
    {
        public CreateShortUrlCommand? Received { get; private set; }

        public Task<Result<CreateShortUrlResult>> Handle(CreateShortUrlCommand command, CancellationToken ct)
        {
            Received = command;
            return Task.FromResult(behaviour(command));
        }
    }

    private sealed class StubbedFactory(StubHandler handler) : WriteApiFactory
    {
        protected override void ConfigureServices(IServiceCollection services) =>
            services.AddScoped<ICommandHandler<CreateShortUrlCommand, CreateShortUrlResult>>(_ => handler);
    }

    private static async Task<(HttpResponseMessage Response, JsonElement Body)> PostWith(Func<CreateShortUrlCommand, Result<CreateShortUrlResult>> behaviour)
    {
        using var factory = new StubbedFactory(new StubHandler(behaviour));
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/urls", new { originalUrl = "https://example.com/a" });
        var text = await response.Content.ReadAsStringAsync();
        return (response, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement);
    }

    [Theory]
    [InlineData(ErrorType.NotFound, HttpStatusCode.NotFound)]
    [InlineData(ErrorType.Conflict, HttpStatusCode.Conflict)]
    [InlineData(ErrorType.Gone, HttpStatusCode.Gone)]
    [InlineData(ErrorType.Validation, HttpStatusCode.BadRequest)]
    [InlineData(ErrorType.Failure, HttpStatusCode.InternalServerError)]
    public async Task ErrorType_MapsToTheExpectedStatusCodeWithTheErrorCode(ErrorType type, HttpStatusCode expected)
    {
        var (response, body) = await PostWith(_ => new Error("Some.Code", "Something specific happened", type));

        response.StatusCode.ShouldBe(expected);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        body.GetProperty("code").GetString().ShouldBe("Some.Code");
        body.GetProperty("detail").GetString().ShouldBe("Something specific happened");
        body.GetProperty("status").GetInt32().ShouldBe((int)expected);
    }

    [Fact]
    public async Task ValidationErrorWithDetails_BecomesAValidationProblemWithAnErrorsMap()
    {
        var details = new Dictionary<string, string[]> { ["OriginalUrl"] = ["bad url"], ["Alias"] = ["bad alias", "reserved"] };

        var (response, body) = await PostWith(_ => new Error("Validation.Failed", "One or more validation errors occurred.", ErrorType.Validation, details));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        body.GetProperty("errors").GetProperty("Alias").EnumerateArray().Select(e => e.GetString()).ShouldBe(["bad alias", "reserved"]);
        body.GetProperty("code").GetString().ShouldBe("Validation.Failed");
    }

    [Fact]
    public async Task Success_Returns201WithTheShortUrlBuiltFromTheConfiguredBase()
    {
        var code = UrlShortener.Domain.ShortCode.Create("0aZ9xYb").Value;

        var (response, body) = await PostWith(_ => new CreateShortUrlResult(code));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        body.GetProperty("shortUrl").GetString().ShouldBe("https://short.test/0aZ9xYb");
    }

    [Fact]
    public async Task RequestFields_AreMappedToTheCommand()
    {
        var handler = new StubHandler(_ => new Error("X", "x", ErrorType.Failure));
        using var factory = new StubbedFactory(handler);
        using var client = factory.CreateClient();
        var expiresAt = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

        await client.PostAsJsonAsync("/urls", new { originalUrl = "https://example.com/a", alias = "mapped-alias", expiresAt });

        handler.Received.ShouldBe(new CreateShortUrlCommand("https://example.com/a", "mapped-alias", expiresAt));
    }

    [Fact]
    public async Task UnhandledException_Returns500ProblemWithoutLeakingTheMessage()
    {
        var (response, body) = await PostWith(_ => throw new InvalidOperationException("secret-connection-detail"));

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        body.GetRawText().ShouldNotContain("secret-connection-detail");
        body.GetRawText().ShouldNotContain("InvalidOperationException");
    }
}
