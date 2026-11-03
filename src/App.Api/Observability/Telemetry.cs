using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace App.Api.Observability;

public static class Telemetry
{
    public const string ServiceName = "app-api";

    public static readonly System.Diagnostics.ActivitySource Source = new(ServiceName);

    public static void AddAppTelemetry(this WebApplicationBuilder builder)
    {
        var resource = ResourceBuilder.CreateDefault().AddService(ServiceName);

        builder.Logging.AddOpenTelemetry(o =>
        {
            o.SetResourceBuilder(resource);
            o.IncludeFormattedMessage = true;
            o.ParseStateValues = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(ServiceName))
            .WithTracing(t => t
                .AddSource(ServiceName)
                .AddSource("Npgsql")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(ServiceName)
                .AddMeter("Npgsql"));

        builder.Services.AddOpenTelemetry().UseOtlpExporter();
    }
}
