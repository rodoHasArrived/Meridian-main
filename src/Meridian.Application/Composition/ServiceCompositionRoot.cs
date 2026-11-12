using Meridian.Application.Composition.Features;
using Meridian.Application.Monitoring;
using Meridian.Application.Pipeline;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Meridian.Contracts.Monitoring;
using Meridian.Platform.Monitoring;
using Meridian.Platform.Tracing;

namespace Meridian.Application.Composition;

/// <summary>
/// Centralizes all shared service registration for the application.
/// This composition root is consumed by <see cref="HostStartup"/>, <see cref="HostBuilder"/>,
/// and the extracted startup orchestration layer so every host mode builds from the same graph.
/// </summary>
/// <remarks>
/// <para><b>Design Philosophy:</b></para>
/// <list type="bullet">
/// <item><description>Single source of truth for service registration</description></item>
/// <item><description>Host-agnostic core services shared across console, web, desktop, and MCP hosts</description></item>
/// <item><description>Canonical <see cref="CompositionOptions"/> presets select optional capabilities</description></item>
/// <item><description>Feature registration delegated to <see cref="IServiceFeatureRegistration"/> modules</description></item>
/// </list>
/// </remarks>
[ImplementsAdr("ADR-001", "Centralized composition root for service configuration")]
public static class ServiceCompositionRoot
{
    // One ordered plan owns feature membership and activation. Keep configuration/storage before
    // their consumers, providers before symbol/backfill services, and canonicalization after pipeline.
    // Modules are stateless registration helpers; instances are reused across independent hosts.
    private static readonly (IServiceFeatureRegistration Module, Func<CompositionOptions, bool> IsEnabled)[] FeatureModules =
    [
        (new ConfigurationFeatureRegistration(), static _ => true),
        (new CoordinationFeatureRegistration(), static _ => true),
        (new StorageFeatureRegistration(), static _ => true),
        (new LedgerFeatureRegistration(), static _ => true),
        (new CredentialFeatureRegistration(), static options => options.EnableCredentialServices),
        (new ProviderFeatureRegistration(), static options => options.EnableProviderServices),
        (new ProviderRoutingFeatureRegistration(), static options => options.EnableProviderServices),
        (new SymbolManagementFeatureRegistration(), static options => options.EnableSymbolManagement),
        (new BackfillFeatureRegistration(), static options => options.EnableBackfillServices),
        (new EtlFeatureRegistration(), static options => options.EnableEtlServices),
        (new MaintenanceFeatureRegistration(), static options => options.EnableMaintenanceServices),
        (new DiagnosticsFeatureRegistration(), static options => options.EnableDiagnosticServices),
        (new PipelineFeatureRegistration(), static options => options.EnablePipelineServices),
        (new CollectorFeatureRegistration(), static options => options.EnableCollectorServices),
        (new CanonicalizationFeatureRegistration(), static options => options.EnableCanonicalizationServices),
        (new HttpClientFeatureRegistration(), static options => options.EnableHttpClientFactory),
    ];

    /// <summary>
    /// Registers all core services required by any host type.
    /// This is the minimum set of services needed for the application to function.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="options">Composition options controlling which services to register.</param>
    /// <returns>The configured service collection for chaining.</returns>
    /// <remarks>
    /// <para><b>Service Registration Order:</b></para>
    /// <list type="number">
    /// <item><description>Core configuration services (always required)</description></item>
    /// <item><description>Storage services (always required)</description></item>
    /// <item><description>Credential services (before providers for credential resolution)</description></item>
    /// <item><description>Provider services (ProviderRegistry, ProviderFactory - before dependent services)</description></item>
    /// <item><description>Symbol management services (depends on ProviderFactory/Registry)</description></item>
    /// <item><description>Backfill services (depends on ProviderRegistry/Factory)</description></item>
    /// <item><description>Other services (maintenance, diagnostic, pipeline, collector)</description></item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddMarketDataServices(
        this IServiceCollection services,
        CompositionOptions? options = null)
    {
        options ??= CompositionOptions.Default;
        services.TryAddSingleton(new CompositionConfiguration(options.Configuration));

        services.AddFundScopeTenantServices();

        foreach (var (module, isEnabled) in FeatureModules)
        {
            if (isEnabled(options))
                module.Register(services, options);
        }

        ProductionServiceRegistrationPolicy.Validate(services);

        // ADR-019: composition-time validation alone cannot see registrations added after this
        // call, so the guard re-validates the final graph as the first hosted service at startup.
        services.AddProductionRegistrationGuard();

        // Backtesting services are registered by the host project (Meridian.Backtesting references
        // Meridian.Application, so registration here would create a circular dependency).

        return services;
    }
}

/// <summary>
/// Simple publisher that wraps EventPipeline for IMarketEventPublisher interface.
/// Registered as singleton in the composition root, but also usable directly.
/// </summary>
public sealed class PipelinePublisher : IMarketEventPublisher
{
    private readonly IMarketEventPublisher _publisher;
    private readonly IEventMetrics _metrics;

    public PipelinePublisher(IMarketEventPublisher publisher, IEventMetrics? metrics = null)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _metrics = metrics ?? new DefaultEventMetrics();
    }

    public bool TryPublish(in MarketEvent evt)
    {
        var ok = _publisher.TryPublish(evt);

        // Integrity tracking lives here because EventPipeline is type-agnostic.
        if (evt.Type == MarketEventType.Integrity)
            _metrics.IncIntegrity();
        return ok;
    }
}

/// <summary>
/// Options controlling which services are registered by the composition root.
/// </summary>
public sealed record CompositionOptions
{
    /// <summary>
    /// Default options enabling all commonly used services.
    /// </summary>
    public static CompositionOptions Default => new()
    {
        EnableSymbolManagement = true,
        EnableBackfillServices = true,
        EnableEtlServices = true,
        EnableMaintenanceServices = true,
        EnableDiagnosticServices = true,
        EnableCredentialServices = true,
        EnableProviderServices = true,
        EnablePipelineServices = true,
        EnableCollectorServices = true,
        EnableHttpClientFactory = true,
        EnableCanonicalizationServices = true
    };

    /// <summary>
    /// Minimal options for console-only operation (utility commands, validation, etc.).
    /// </summary>
    public static CompositionOptions Minimal => new()
    {
        EnableSymbolManagement = false,
        EnableBackfillServices = false,
        EnableEtlServices = false,
        EnableMaintenanceServices = false,
        EnableDiagnosticServices = false,
        EnableCredentialServices = false,
        EnableProviderServices = false,
        EnablePipelineServices = false,
        EnableCollectorServices = false,
        EnableHttpClientFactory = false
    };

    /// <summary>
    /// Options optimized for web dashboard hosting.
    /// </summary>
    public static CompositionOptions WebDashboard => new()
    {
        EnableSymbolManagement = true,
        EnableBackfillServices = true,
        EnableEtlServices = true,
        EnableMaintenanceServices = true,
        EnableDiagnosticServices = true,
        EnableCredentialServices = true,
        EnableProviderServices = true,
        EnablePipelineServices = true,
        EnableCollectorServices = true,
        EnableHttpClientFactory = true,
        EnableCanonicalizationServices = true
    };

    /// <summary>
    /// Options for streaming data collection (CLI headless mode).
    /// </summary>
    public static CompositionOptions Streaming => new()
    {
        EnableSymbolManagement = true,
        EnableBackfillServices = true,
        EnableEtlServices = true,
        EnableMaintenanceServices = false,
        EnableDiagnosticServices = true,
        EnableCredentialServices = true,
        EnableProviderServices = true,
        EnablePipelineServices = true,
        EnableCollectorServices = true,
        EnableHttpClientFactory = true,
        EnableCanonicalizationServices = true
    };

    /// <summary>
    /// Options for backfill-only operation.
    /// </summary>
    public static CompositionOptions BackfillOnly => new()
    {
        // Backfill is a provider-scoped identity workflow. Keep the canonical symbol spine
        // enabled so the worker/coordinator/factory paths cannot silently fall back to raw
        // input symbols when provider aliases are required.
        EnableSymbolManagement = true,
        EnableBackfillServices = true,
        EnableEtlServices = true,
        EnableMaintenanceServices = false,
        EnableDiagnosticServices = false,
        EnableCredentialServices = true,
        EnableProviderServices = true,
        EnablePipelineServices = true,
        EnableCollectorServices = false,
        EnableHttpClientFactory = true
    };

    /// <summary>
    /// Path to the configuration file. If null, ConfigStore will use default resolution.
    /// </summary>
    public string? ConfigPath { get; init; }

    /// <summary>
    /// Host-owned runtime settings. When supplied, missing keys never fall back to the process
    /// environment. Omit this for the existing environment-driven production startup contract.
    /// </summary>
    public IConfiguration? Configuration { get; init; }

    /// <summary>
    /// Data root directory override. If null, uses value from configuration.
    /// </summary>
    public string? DataRoot { get; init; }

    public bool EnableSymbolManagement { get; init; }
    public bool EnableBackfillServices { get; init; }
    public bool EnableEtlServices { get; init; }
    public bool EnableMaintenanceServices { get; init; }
    public bool EnableDiagnosticServices { get; init; }
    public bool EnableCredentialServices { get; init; }
    public bool EnableProviderServices { get; init; }
    public bool EnablePipelineServices { get; init; }
    public bool EnableCollectorServices { get; init; }
    public bool EnableHttpClientFactory { get; init; }
    public bool EnableCanonicalizationServices { get; init; }

    /// <summary>
    /// Whether this graph owns process-wide hosted loops and workers. Desktop child graphs disable
    /// this while retaining guards, migrations, and child-local storage/symbol initialization.
    /// </summary>
    public bool EnableProcessWideHostedServices { get; init; } = true;

    /// <summary>
    /// Explicit programmatic opt-in to tracing and the existing metrics decorator.
    /// Operators normally opt in through AppConfig.Tracing.Enabled.
    /// </summary>
    public bool EnableOpenTelemetry { get; init; }

    /// <summary>
    /// Whether this graph owns a tracing provider. Desktop child graphs reuse their parent's
    /// listener; independent utility and ETL hosts still own telemetry even without workers.
    /// </summary>
    public bool OwnsTracingProvider { get; init; } = true;
}
