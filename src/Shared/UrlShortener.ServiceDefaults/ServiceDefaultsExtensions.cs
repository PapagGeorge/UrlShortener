using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace UrlShortener.ServiceDefaults;

/// <summary>Cross-cutting setup shared by every Api: RFC 9457 problem details, safe error handling, liveness, telemetry. (OpenAPI arrives in a later phase.)</summary>
public static class ServiceDefaultsExtensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
                context.ProblemDetails.Extensions.TryAdd("traceId", Activity.Current?.Id ?? context.HttpContext.TraceIdentifier));

        builder.Services.AddHealthChecks();
        builder.AddTelemetry();
        return builder;
    }

    /// <summary>
    /// Traces and metrics through OpenTelemetry. Our own meters are picked up by name (<c>UrlShortener.*</c>), so a new meter needs no registration.
    /// Health checks are not traced (they would drown the real traces). Data leaves the process only when a standard OTLP endpoint
    /// (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, or the per-signal <c>_TRACES_</c> / <c>_METRICS_</c> variants) is configured; without it nothing is exported and nothing can fail.
    /// </summary>
    // The shared endpoint, or one per signal (Jaeger takes the traces, Prometheus the metrics).
    private static readonly string[] OtlpEndpointVariables =
    [
        "OTEL_EXPORTER_OTLP_ENDPOINT",
        "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT",
        "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT",
    ];

    private static void AddTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(builder.Environment.ApplicationName))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter("UrlShortener.*"))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation());

        if (OtlpEndpointVariables.Any(name => !string.IsNullOrWhiteSpace(builder.Configuration[name])))
        {
            telemetry.UseOtlpExporter();
        }
    }

    /// <summary>Unhandled exceptions become a generic 500 problem (the message is never sent to the client); empty 4xx responses get a problem body.</summary>
    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }

    /// <summary>Opt-in (the Gateway has no API of its own to document). Pair with <see cref="MapApiDocumentation"/>.</summary>
    public static TBuilder AddApiDocumentation<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOpenApi();
        return builder;
    }

    /// <summary>
    /// The OpenAPI document (<c>/openapi/v1.json</c>) and the Scalar UI (<c>/scalar</c>), <b>Development only</b>:
    /// the repository is public and production must not advertise its API surface.
    /// </summary>
    public static WebApplication MapApiDocumentation(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        return app;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Liveness runs no checks: a dependency outage must never get the container restarted. Readiness runs the registered probes.
        app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadinessProbeExtensions.ReadyTag) });
        return app;
    }
}
