using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CodeAgent.Observability;
internal static class OpenTelemetrySetup
{
    public static void Configure(
        WebApplicationBuilder builder,
        string serviceName,
        string sourceName,
        string otlpEndpoint)
    {
        var otlpUri = BuildOtlpUri(otlpEndpoint);
        var serviceVersion = typeof(OpenTelemetrySetup).Assembly.GetName().Version?.ToString() ?? "1.0.0";

        // Ensure standard W3C trace/baggage propagation.
        Sdk.SetDefaultTextMapPropagator(new CompositeTextMapPropagator(
        [
            new TraceContextPropagator(),
            new BaggagePropagator()
        ]));

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName, serviceVersion: serviceVersion, serviceInstanceId: Environment.MachineName);

        // Register custom ActivitySource/Meter in DI for app instrumentation.
        builder.Services.AddSingleton(new ActivitySource(sourceName));
        builder.Services.AddSingleton(new Meter(sourceName));

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName, serviceVersion: serviceVersion, serviceInstanceId: Environment.MachineName))
            .WithTracing(tracing => tracing
                .AddSource(sourceName)
                .AddSource("Microsoft.Agents.AI")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(options => options.Endpoint = otlpUri))
            .WithMetrics(metrics => metrics
                .AddMeter(sourceName)
                .AddMeter("Microsoft.Agents.AI")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(options => options.Endpoint = otlpUri));

        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Logging.AddOpenTelemetry(options =>
        {
            options.SetResourceBuilder(resourceBuilder);
            options.IncludeScopes = true;
            options.IncludeFormattedMessage = true;
            options.ParseStateValues = true;
            options.AddOtlpExporter(otlpOptions => otlpOptions.Endpoint = otlpUri);
        });
    }

    private static Uri BuildOtlpUri(string otlpEndpoint)
        => Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var uri)
            ? uri
            : new Uri("http://localhost:4317");
}