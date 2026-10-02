using Meridian.Core.Config;
using Meridian.Application.ProviderRouting;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Platform.Tracing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Application.Composition.Features;

/// <summary>
/// Registers core configuration services that all hosts need.
/// </summary>
internal sealed class ConfigurationFeatureRegistration : IServiceFeatureRegistration
{
    public IServiceCollection Register(IServiceCollection services, CompositionOptions options)
    {
        // ConfigStore - unified configuration access
        var configStore = new ConfigStore(options.ConfigPath);
        services.AddSingleton(configStore);

        // Register telemetry before workers so it starts first and is disposed after them.
        // Desktop child graphs share the parent host's process-wide ActivitySource listener.
        if (options.OwnsTracingProvider)
        {
            var tracing = configStore.Load().Tracing ?? new TracingConfig();
            if (tracing.Enabled || options.EnableOpenTelemetry)
                services.AddOpenTelemetryTracing(new OpenTelemetryConfiguration
                {
                    ServiceName = tracing.ServiceName,
                    ServiceVersion = tracing.ServiceVersion,
                    Environment = tracing.Environment,
                    EnableConsoleExporter = tracing.EnableConsoleExporter,
                    EnableOtlpExporter = tracing.EnableOtlpExporter,
                    OtlpEndpoint = tracing.OtlpEndpoint,
                    OtlpHeaders = tracing.OtlpHeaders,
                    SamplingRatio = tracing.SamplingRatio,
                    FlushTimeoutMilliseconds = tracing.FlushTimeoutMilliseconds
                });
        }

        // ConfigurationService - consolidated configuration operations
        services.AddSingleton(sp => new ConfigurationService(
            providerSelectorAccessor: () => sp.GetService<IBestOfBreedProviderSelector>()));

        // Configuration utilities
        services.AddSingleton<ConfigTemplateGenerator>();
        services.AddSingleton<ConfigEnvironmentOverride>();
        services.AddSingleton<DryRunService>();

        return services;
    }
}
