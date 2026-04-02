using Meridian.Application.Config;
using Meridian.Application.Monitoring.Core;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Extension methods for registering provider services with the DI container.
/// </summary>
/// <remarks>
/// This provides a unified entry point for all provider-related DI registration,
/// replacing scattered provider creation logic throughout the codebase.
/// </remarks>
[ImplementsAdr("ADR-001", "Unified DI registration for all provider types")]
public static class ProviderServiceExtensions
{
    /// <summary>
    /// Adds the provider registry and factory to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="config">The application configuration.</param>
    /// <param name="credentialResolver">The credential resolver.</param>
    /// <param name="log">Optional logger.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddProviderServices(
        this IServiceCollection services,
        AppConfig config,
        IProviderCredentialResolver credentialResolver,
        ILogger? log = null)
    {
        // Register credential resolver
        services.AddSingleton(credentialResolver);

        // Register provider registry as singleton
        services.AddSingleton(sp =>
        {
            var alertDispatcher = sp.GetService<IAlertDispatcher>();
            return new ProviderRegistry(alertDispatcher, log);
        });

        // Register data source registry and module-driven provider registration
        services.AddSingleton(sp =>
        {
            var registry = new DataSourceRegistry();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            registry.DiscoverFromAssemblies(assemblies);
            registry.RegisterModules(services, assemblies);
            registry.RegisterServices(services);
            return registry;
        });

        // Register provider factory
        services.AddSingleton(sp => new ProviderFactory(
            config,
            sp.GetRequiredService<IProviderCredentialResolver>(),
            log));

        return services;
    }

    /// <summary>
    /// Initializes all providers using the factory and registers them with the registry.
    /// Call this after the service provider is built.
    /// </summary>
    /// <param name="serviceProvider">The built service provider.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The provider creation result.</returns>
    public static async Task<ProviderCreationResult> InitializeProvidersAsync(
        this IServiceProvider serviceProvider,
        CancellationToken ct = default)
    {
        var factory = serviceProvider.GetRequiredService<ProviderFactory>();
        var registry = serviceProvider.GetRequiredService<ProviderRegistry>();

        return await factory.CreateAndRegisterAllAsync(registry, ct);
    }

    /// <summary>
    /// Adds all backfill providers to the registry based on configuration.
    /// </summary>
    /// <param name="registry">The provider registry.</param>
    /// <param name="config">The application configuration.</param>
    /// <param name="credentialResolver">The credential resolver.</param>
    /// <param name="log">Optional logger.</param>
    /// <returns>List of registered backfill providers.</returns>
    public static IReadOnlyList<IHistoricalDataProvider> RegisterBackfillProviders(
        this ProviderRegistry registry,
        AppConfig config,
        IProviderCredentialResolver credentialResolver,
        ILogger? log = null)
    {
        var factory = new ProviderFactory(config, credentialResolver, log);
        var providers = factory.CreateBackfillProviders();

        foreach (var provider in providers)
        {
            registry.Register(provider);
        }

        return providers;
    }

    /// <summary>
    /// Adds all symbol search providers to the registry based on configuration.
    /// </summary>
    /// <param name="registry">The provider registry.</param>
    /// <param name="config">The application configuration.</param>
    /// <param name="credentialResolver">The credential resolver.</param>
    /// <param name="log">Optional logger.</param>
    /// <returns>List of registered symbol search providers.</returns>
    public static IReadOnlyList<ISymbolSearchProvider> RegisterSymbolSearchProviders(
        this ProviderRegistry registry,
        AppConfig config,
        IProviderCredentialResolver credentialResolver,
        ILogger? log = null)
    {
        var factory = new ProviderFactory(config, credentialResolver, log);
        var providers = factory.CreateSymbolSearchProviders();

        foreach (var provider in providers)
        {
            registry.Register(provider);
        }

        return providers;
    }

    /// <summary>
    /// Gets the composite backfill provider with automatic failover.
    /// Creates one if not already registered.
    /// </summary>
    /// <param name="registry">The provider registry.</param>
    /// <param name="config">The application configuration.</param>
    /// <param name="credentialResolver">The credential resolver.</param>
    /// <param name="log">Optional logger.</param>
    /// <returns>The composite backfill provider.</returns>
    public static CompositeHistoricalDataProvider GetOrCreateCompositeBackfillProvider(
        this ProviderRegistry registry,
        AppConfig config,
        IProviderCredentialResolver credentialResolver,
        ILogger? log = null)
    {
        // Check if already registered
        var existing = registry.GetProvider<CompositeHistoricalDataProvider>("composite-backfill");
        if (existing != null)
            return existing;

        // Create and register
        var providers = registry.GetProviders<IHistoricalDataProvider>();
        if (providers.Count == 0)
        {
            providers = registry.RegisterBackfillProviders(config, credentialResolver, log);
        }

        var factory = new ProviderFactory(config, credentialResolver, log);
        var composite = factory.CreateCompositeBackfillProvider(providers);

        registry.Register(composite);

        return composite;
    }
}

/// <summary>
/// Provider availability summary for monitoring and diagnostics.
/// </summary>
public sealed record ProviderAvailabilitySummary
{
    public required int TotalProviders { get; init; }
    public required int AvailableProviders { get; init; }
    public required int StreamingAvailable { get; init; }
    public required int BackfillAvailable { get; init; }
    public required int SymbolSearchAvailable { get; init; }
    public required IReadOnlyDictionary<string, bool> ProviderStatus { get; init; }

    public bool HasStreamingCapability => StreamingAvailable > 0;
    public bool HasBackfillCapability => BackfillAvailable > 0;
    public bool HasSymbolSearchCapability => SymbolSearchAvailable > 0;
    public double AvailabilityRate => TotalProviders > 0 ? (double)AvailableProviders / TotalProviders : 0;
}

/// <summary>
/// Extension methods for provider availability checking.
/// </summary>
public static class ProviderAvailabilityExtensions
{
    /// <summary>
    /// Gets a comprehensive availability summary for all registered providers.
    /// </summary>
    /// <param name="registry">The provider registry.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Availability summary.</returns>
    public static async Task<ProviderAvailabilitySummary> GetAvailabilitySummaryAsync(
        this ProviderRegistry registry,
        CancellationToken ct = default)
    {
        var streamingStatus = await registry.GetProviderAvailabilityAsync<IMarketDataClient>(ct);
        var backfillStatus = await registry.GetProviderAvailabilityAsync<IHistoricalDataProvider>(ct);
        var searchStatus = await registry.GetProviderAvailabilityAsync<ISymbolSearchProvider>(ct);

        var allStatus = streamingStatus
            .Concat(backfillStatus)
            .Concat(searchStatus)
            .GroupBy(x => x.Key)
            .ToDictionary(g => g.Key, g => g.First().Value);

        return new ProviderAvailabilitySummary
        {
            TotalProviders = allStatus.Count,
            AvailableProviders = allStatus.Count(x => x.Value),
            StreamingAvailable = streamingStatus.Count(x => x.Value),
            BackfillAvailable = backfillStatus.Count(x => x.Value),
            SymbolSearchAvailable = searchStatus.Count(x => x.Value),
            ProviderStatus = allStatus
        };
    }
}
