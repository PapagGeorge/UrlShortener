using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using UrlShortener.ServiceDefaults;

namespace UrlShortener.Gateway.Tests.ServiceDefaults;

/// <summary>Covers the telemetry set up by <c>AddServiceDefaults</c> (shared by all three services; it has no test project of its own).</summary>
public sealed class TelemetryTests
{
    private sealed class SpanCapture : BaseProcessor<Activity>
    {
        public List<(string Name, string? Path, string? Service)> Spans { get; } = [];

        public override void OnEnd(Activity data)
        {
            lock (Spans)
            {
                Spans.Add((data.DisplayName, data.GetTagItem("url.path") as string, null));
            }
        }
    }

    private sealed class MetricCapture : BaseExporter<Metric>
    {
        public List<string> Names { get; } = [];

        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
            {
                Names.Add(metric.Name);
            }

            return ExportResult.Success;
        }
    }

    private static async Task<(WebApplication App, SpanCapture Spans, MetricCapture Metrics, MetricReader Reader)> StartApp(
        Dictionary<string, string?>? settings = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        if (settings is not null)
        {
            builder.Configuration.AddInMemoryCollection(settings);
        }

        builder.AddServiceDefaults();

        var spans = new SpanCapture();
        var metrics = new MetricCapture();
        var reader = new BaseExportingMetricReader(metrics);
        builder.Services.ConfigureOpenTelemetryTracerProvider(b => b.AddProcessor(spans));
        builder.Services.ConfigureOpenTelemetryMeterProvider(b => b.AddReader(reader));

        var app = builder.Build();
        app.UseServiceDefaults();
        app.MapDefaultEndpoints();
        app.MapGet("/work", () => "ok");
        await app.StartAsync();
        return (app, spans, metrics, reader);
    }

    /// <summary>The server span ends just after the response is sent, so a test has to wait for it.</summary>
    private static async Task WaitForSpan(SpanCapture spans, string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (spans.Spans)
            {
                if (spans.Spans.Any(s => s.Path == path))
                {
                    return;
                }
            }

            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Request_ProducesAServerSpan()
    {
        var (app, spans, _, _) = await StartApp();
        await using var _a = app;

        using var response = await app.GetTestClient().GetAsync("/work");
        await WaitForSpan(spans, "/work");

        spans.Spans.ShouldContain(s => s.Path == "/work");
    }

    [Fact]
    public async Task HealthChecks_AreNotTraced()
    {
        var (app, spans, _, _) = await StartApp();
        await using var _a = app;

        using var health = await app.GetTestClient().GetAsync("/health");
        using var work = await app.GetTestClient().GetAsync("/work");
        await WaitForSpan(spans, "/work");   // once the later request is traced, an earlier /health span would be there too

        spans.Spans.ShouldNotContain(s => s.Path == "/health");
    }

    [Fact]
    public async Task MetersWhoseNameStartsWithUrlShortener_AreExported()
    {
        var (app, _, metrics, reader) = await StartApp();
        await using var _a = app;
        using var meter = new Meter("UrlShortener.TelemetryTest");
        var counter = meter.CreateCounter<long>("urlshortener.telemetrytest.count");

        counter.Add(1);
        reader.Collect();

        metrics.Names.ShouldContain("urlshortener.telemetrytest.count");
    }

    [Fact]
    public async Task OtherMeters_AreNotExported()
    {
        var (app, _, metrics, reader) = await StartApp();
        await using var _a = app;
        using var meter = new Meter("Somebody.Else");
        var counter = meter.CreateCounter<long>("somebody.else.count");

        counter.Add(1);
        reader.Collect();

        metrics.Names.ShouldNotContain("somebody.else.count");
    }

    [Fact]
    public async Task WithAnUnreachableOtlpEndpoint_RequestsStillSucceed()
    {
        var (app, _, _, _) = await StartApp(new() { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://collector.invalid:4317" });
        await using var _a = app;

        using var response = await app.GetTestClient().GetAsync("/work");

        response.IsSuccessStatusCode.ShouldBeTrue();
    }
}
