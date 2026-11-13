using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Aba.Starter.Platform.Observability;

public sealed record AbaObservabilityOptions(bool Enabled, string ServiceName, Uri? OtlpEndpoint)
{
    public static AbaObservabilityOptions FromConfiguration(IConfiguration config, bool isDevelopment)
    {
        var enabled = config.GetValue<bool>("Platform:Observability:Enabled");
        var service = config["Platform:Observability:ServiceName"] ?? "Aba.Starter.Api";
        if (string.IsNullOrWhiteSpace(service) || service.Length > 120)
            throw new InvalidOperationException("A valid Platform:Observability:ServiceName is required.");

        var endpointText = config["Platform:Observability:OtlpEndpoint"];
        if (string.IsNullOrWhiteSpace(endpointText))
            return new(enabled, service, null);

        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttps
                && !(isDevelopment && endpoint.Scheme == Uri.UriSchemeHttp))
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
            throw new InvalidOperationException(
                "OTLP endpoint must be HTTPS (HTTP permitted only in local Development), without URI credentials, query or fragment.");

        return new(enabled, service, endpoint);
    }
}

public static class AbaObservabilityServices
{
    public static void AddAbaObservability(this WebApplicationBuilder builder)
    {
        var options = AbaObservabilityOptions.FromConfiguration(
            builder.Configuration, builder.Environment.IsDevelopment());
        if (!options.Enabled) return;

        builder.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(options.ServiceName))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSource("Aba.Starter.Platform", "Wolverine");
                if (options.OtlpEndpoint is not null)
                    tracing.AddOtlpExporter(x => x.Endpoint = options.OtlpEndpoint);
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
                if (options.OtlpEndpoint is not null)
                    metrics.AddOtlpExporter(x => x.Endpoint = options.OtlpEndpoint);
            });
    }
}
