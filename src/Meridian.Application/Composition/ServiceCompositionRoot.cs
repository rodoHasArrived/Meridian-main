using Meridian.Application.Composition.Features;
using Meridian.Application.Monitoring;
using Meridian.Application.Pipeline;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

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
    // Feature registration modules — instantiated once and reused across calls.
    // Registration order matters: later modules may depend on services registered by earlier ones.
    private static readonly IServiceFeatureRegistration[] FeatureModules =
    [
        new ConfigurationFeatureRegistration(),
        new CoordinationFeatureRegistration(),
        new StorageFeatureRegistration(),
        new CredentialFeatureRegistration(),
        new ProviderFeatureRegistration(),
        new SymbolManagementFeatureRegistration(),
        new BackfillFeatureRegistration(),
        new EtlFeatureRegistration(),
        new MaintenanceFeatureRegistration(),
        new DiagnosticsFeatureRegistration(),
        new PipelineFeatureRegistration(),
        new CollectorFeatureRegistration(),
        new CanonicalizationFeatureRegistration(),
        new HttpClientFeatureRegistration(),
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

        // Core configuration and storage — always required
        services.RegisterFeature<ConfigurationFeatureRegistration>(options);
        services.RegisterFeature<CoordinationFeatureRegistration>(options);
        services.RegisterFeature<StorageFeatureRegistration>(options);

        // Credential services — must come before provider services
        if (options.EnableCredentialServices)
            services.RegisterFeature<CredentialFeatureRegistration>(options);

        // Provider services — must come before dependent services (Symbol, Backfill)
        if (options.EnableProviderServices)
            services.RegisterFeature<ProviderFeatureRegistration>(options);

        // Symbol management — depends on ProviderFactory/ProviderRegistry
        if (options.EnableSymbolManagement)
            services.RegisterFeature<SymbolManagementFeatureRegistration>(options);

        // Backfill services — depends on ProviderRegistry/ProviderFactory
        if (options.EnableBackfillServices)
            services.RegisterFeature<BackfillFeatureRegistration>(options);

        if (options.EnableEtlServices)
            services.RegisterFeature<EtlFeatureRegistration>(options);

        // Remaining optional services
        if (options.EnableMaintenanceServices)
            services.RegisterFeature<MaintenanceFeatureRegistration>(options);

        if (options.EnableDiagnosticServices)
            services.RegisterFeature<DiagnosticsFeatureRegistration>(options);

        if (options.EnablePipelineServices)
            services.RegisterFeature<PipelineFeatureRegistration>(options);

        if (options.EnableCollectorServices)
            services.RegisterFeature<CollectorFeatureRegistration>(options);

        // Canonicalization — must come after pipeline (decorates IMarketEventPublisher)
        if (options.EnableCanonicalizationServices)
            services.RegisterFeature<CanonicalizationFeatureRegistration>(options);

        if (options.EnableHttpClientFactory)
            services.RegisterFeature<HttpClientFeatureRegistration>(options);

        TryRegisterCppTraderIntegration(services, options.ConfigPath);

        return services;
    }

    /// <summary>
    /// Initializes the circuit breaker callback router with the built service provider.
    /// Call this once after the DI container is built (i.e., after <c>WebApplication.Build()</c>
    /// or <c>ServiceCollection.BuildServiceProvider()</c>) to connect Polly state-change
    /// callbacks to <see cref="CircuitBreakerStatusService"/>.
    /// </summary>
    public static void InitializeCircuitBreakerCallbackRouter(IServiceProvider sp)
    {
        var cbService = sp.GetService<CircuitBreakerStatusService>();
        if (cbService != null)
            CircuitBreakerCallbackRouter.Initialize(cbService);
    }

    /// <summary>
    /// Locates the singleton feature module instance of type <typeparamref name="T"/>
    /// from the static module array and invokes its registration.
    /// </summary>
    private static IServiceCollection RegisterFeature<T>(
        this IServiceCollection services,
        CompositionOptions options) where T : IServiceFeatureRegistration
    {
        foreach (var module in FeatureModules)
        {
            if (module is T feature)
                return feature.Register(services, options);
        }

        // Fallback: create a new instance (should not happen with the static array)
        return Activator.CreateInstance<T>().Register(services, options);
    }

    private static void TryRegisterCppTraderIntegration(IServiceCollection services, string? configPath)
    {
        try
        {
            var extensionType = Type.GetType(
                "Meridian.Infrastructure.CppTrader.CppTraderServiceCollectionExtensions, Meridian.Infrastructure.CppTrader",
                throwOnError: false);
            var method = extensionType?.GetMethod(
                "AddCppTraderIntegration",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(IServiceCollection), typeof(string)],
                modifiers: null);

            method?.Invoke(null, [services, configPath]);
        }
        catch
        {
            // Optional integration: ignore when the extension assembly is not present
            // or when the CppTrader feature is not included in the current host.
        }
    }
}

/// <summary>
/// Simple publisher that wraps EventPipeline for IMarketEventPublisher interface.
/// Registered as singleton in the composition root, but also usable directly.
/// </summary>
public sealed class PipelinePublisher : IMarketEventPublisher
{
    private readonly EventPipeline _pipeline;
    private readonly IEventMetrics _metrics;

    public PipelinePublisher(EventPipeline pipeline, IEventMetrics? metrics = null)
    {
        _pipeline = pipeline;
        _metrics = metrics ?? new DefaultEventMetrics();
    }

    public bool TryPublish(in MarketEvent evt)
    {
        var ok = _pipeline.TryPublish(evt);

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
        EnableSymbolManagement = false,
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
    /// Options for the MCP server host. Enables provider discovery and backfill services
    /// without the streaming pipeline or collector, since the MCP server is query-oriented.
    /// </summary>
    public static CompositionOptions McpServer => new()
    {
        EnableSymbolManagement = false,
        EnableBackfillServices = true,
        EnableEtlServices = true,
        EnableMaintenanceServices = false,
        EnableDiagnosticServices = false,
        EnableCredentialServices = true,
        EnableProviderServices = true,
        EnablePipelineServices = false,
        EnableCollectorServices = false,
        EnableHttpClientFactory = true,
        EnableCanonicalizationServices = false
    };

    /// <summary>
    /// Path to the configuration file. If null, ConfigStore will use default resolution.
    /// </summary>
    public string? ConfigPath { get; init; }

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
    /// Whether to enable OpenTelemetry tracing and metrics instrumentation.
    /// </summary>
    public bool EnableOpenTelemetry { get; init; }
}
