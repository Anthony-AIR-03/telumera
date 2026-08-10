using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Telumera.ServiceDefaults;

/// <summary>
/// Cross-cutting concerns shared by every Telumera service.
/// <see cref="AddServiceDefaults{TBuilder}"/> is host-agnostic (OpenTelemetry logging/metrics/tracing)
/// and safe to call from an API or a worker. <see cref="AddApiServiceDefaults"/>,
/// <see cref="MapDefaultEndpoints"/> add the web-only pieces (health checks, ProblemDetails,
/// ASP.NET Core request instrumentation) and only apply to services with an HTTP pipeline.
/// </summary>
public static class ServiceDefaultsExtensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddRuntimeInstrumentation()
                .AddHttpClientInstrumentation())
            .WithTracing(tracing => tracing
                .AddHttpClientInstrumentation());

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    // Emits via OTLP only when OTEL_EXPORTER_OTLP_ENDPOINT is configured (e.g. by the local Compose
    // collector or an Azure Monitor OTLP ingestion endpoint) — services stay silent and unaffected
    // otherwise, so this is safe to call even before infrastructure/observability exists (M00.5).
    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            builder.Services.Configure<OpenTelemetryLoggerOptions>(logging => logging.AddOtlpExporter());
            builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddOtlpExporter());
            builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddOtlpExporter());
        }

        return builder;
    }

    /// <summary>
    /// Adds the web-only defaults on top of <see cref="AddServiceDefaults{TBuilder}"/>: ASP.NET Core
    /// request instrumentation, ProblemDetails error responses, and the health check registry backing
    /// <see cref="MapDefaultEndpoints"/>.
    /// </summary>
    public static WebApplicationBuilder AddApiServiceDefaults(this WebApplicationBuilder builder)
    {
        builder.AddServiceDefaults();

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation())
            .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation());

        builder.Services.AddProblemDetails();

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

        return builder;
    }

    /// <summary>
    /// Maps the standard health and readiness endpoints and enables ProblemDetails responses for
    /// unhandled exceptions. Call after <see cref="AddApiServiceDefaults"/>.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.UseExceptionHandler();

        // Liveness: is the process up at all. Readiness: is it ready to take traffic (all checks pass).
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live"),
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = _ => true,
        });

        return app;
    }
}
