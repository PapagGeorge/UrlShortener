using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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
    /// Health checks are not traced (they would drown the real traces). Data leaves the process only when the standard
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is configured; without it nothing is exported and nothing can fail.
    /// </summary>
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

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
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

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health");
        return app;
    }
}
