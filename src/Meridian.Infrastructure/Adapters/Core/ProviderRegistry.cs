using System.Collections.Concurrent;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Application.Monitoring;
using Meridian.Application.Monitoring.Core;
using Meridian.Contracts.Api;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Provider capability metadata for discovery and routing.
/// </summary>
/// <param name="Name">Unique provider identifier.</param>
/// <param name="DisplayName">Human-readable provider name.</param>
/// <param name="ProviderType">Type of provider (streaming, backfill, search).</param>
/// <param name="Priority">Priority for routing (lower = higher priority).</param>
/// <param name="IsEnabled">Whether the provider is currently enabled.</param>
/// <param name="Capabilities">Provider-specific capabilities.</param>
public sealed record ProviderInfo(
    string Name,
    string DisplayName,
    ProviderType ProviderType,
    int Priority,
    bool IsEnabled,
    IReadOnlyDictionary<string, object>? Capabilities = null);

/// <summary>
/// Centralized registry for all market data providers enabling plugin-style
/// provider management with discovery, routing, and health monitoring.
/// </summary>
/// <remarks>
/// The provider registry provides:
/// - Centralized provider registration and discovery
/// - Priority-based provider routing
/// - Provider health monitoring and automatic failover
/// - Capability-based provider selection
/// - Unified metadata access via <see cref="IProviderMetadata"/>
/// </remarks>
[ImplementsAdr("ADR-001", "Centralized provider registry for plugin-style management")]
public sealed class ProviderRegistry : IDisposable
{
    /// <summary>
    /// Single unified registry of all providers. Type-specific queries filter by ProviderCapabilities.
    /// </summary>
    private readonly ConcurrentDictionary<string, RegisteredProvider> _allProviders = new();

    /// <summary>
    /// Dictionary of streaming client factory functions keyed by <see cref="DataSourceKind"/>.
    /// Populated during DI setup via <see cref="RegisterStreamingFactory"/> to replace
    /// the switch-statement-based creation in the old MarketDataClientFactory.
    /// </summary>
    private readonly ConcurrentDictionary<DataSourceKind, Func<IMarketDataClient>> _streamingFactories = new();

    private readonly ILogger _log;
    private readonly IAlertDispatcher? _alertDispatcher;
    private bool _disposed;

    public ProviderRegistry(IAlertDispatcher? alertDispatcher = null, ILogger? log = null)
    {
        _alertDispatcher = alertDispatcher;
        _log = log ?? LoggingSetup.ForContext<ProviderRegistry>();
    }

    #region Unified Provider Registration

    /// <summary>
    /// Registers any provider implementing <see cref="IProviderMetadata"/>.
    /// All providers are stored in a single unified registry.
    /// </summary>
    /// <typeparam name="T">The provider type.</typeparam>
    /// <param name="provider">The provider instance.</param>
    /// <param name="priorityOverride">Optional priority override.</param>
    public void Register<T>(T provider, int? priorityOverride = null) where T : IProviderMetadata
    {
        ArgumentNullException.ThrowIfNull(provider);

        var id = provider.ProviderId;
        var priority = priorityOverride ?? provider.ProviderPriority;
        ValidateName(id);

        var registered = new RegisteredProvider(id, provider, priority, true);
        if (_allProviders.TryAdd(id, registered))
        {
            MigrationDiagnostics.IncProviderRegistered();
            _log.Information("Registered provider: {Name} (type: {Type}, priority: {Priority})",
                id, provider.ProviderCapabilities.PrimaryType, priority);
        }
        else
        {
            _log.Warning("Provider already registered: {Name}", id);
        }
    }

    /// <summary>
    /// Registers a factory function for creating a streaming client for the specified data source kind.
    /// This replaces the switch-statement approach previously used in MarketDataClientFactory.
    /// </summary>
    /// <param name="kind">The data source kind to register.</param>
    /// <param name="factory">Factory function that creates the streaming client.</param>
    public void RegisterStreamingFactory(DataSourceKind kind, Func<IMarketDataClient> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (_streamingFactories.TryAdd(kind, factory))
        {
            MigrationDiagnostics.IncStreamingFactoryRegistered();
            _log.Information("Registered streaming factory for {DataSource}", kind);
        }
        else
        {
            _streamingFactories[kind] = factory;
            _log.Information("Replaced streaming factory for {DataSource}", kind);
        }
    }

    /// <summary>
    /// Creates a streaming client for the specified data source kind using the registered factory.
    /// Falls back to <see cref="DataSourceKind.IB"/> if the requested kind has no registered factory.
    /// </summary>
    /// <param name="kind">The data source kind to create a client for.</param>
    /// <returns>A new streaming client instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no factory is registered for the kind and no fallback is available.</exception>
    public IMarketDataClient CreateStreamingClient(DataSourceKind kind)
    {
        if (_streamingFactories.TryGetValue(kind, out var factory))
        {
            MigrationDiagnostics.IncStreamingFactoryHit(kind.ToString());
            _log.Information("Creating streaming client for {DataSource}", kind);
            return factory();
        }

        // Fallback to IB (default provider)
        if (kind != DataSourceKind.IB && _streamingFactories.TryGetValue(DataSourceKind.IB, out var ibFactory))
        {
            _log.Warning("No factory registered for {DataSource}, falling back to IB", kind);
            return ibFactory();
        }

        throw new InvalidOperationException(
            $"No streaming factory registered for {kind} and no IB fallback available. " +
            "Register factories via RegisterStreamingFactory() during startup.");
    }

    /// <summary>
    /// Gets all data source kinds that have a registered streaming factory.
    /// </summary>
    public IReadOnlyList<DataSourceKind> SupportedStreamingSources =>
        _streamingFactories.Keys.OrderBy(k => k).ToList();

    #endregion

    #region Provider Metadata Queries

    /// <summary>
    /// Gets all registered providers as unified metadata.
    /// </summary>
    public IReadOnlyList<IProviderMetadata> GetAllProviderMetadata()
    {
        return _allProviders.Values
            .Where(r => r.IsEnabled)
            .OrderBy(r => r.Priority)
            .Select(r => r.Provider)
            .ToList();
    }

    /// <summary>
    /// Gets a provider by ID from the unified registry.
    /// </summary>
    public IProviderMetadata? GetProvider(string id)
    {
        return _allProviders.TryGetValue(id, out var registered) && registered.IsEnabled
            ? registered.Provider
            : null;
    }

    /// <summary>
    /// Gets providers filtered by capability.
    /// </summary>
    /// <param name="predicate">Capability filter predicate.</param>
    public IReadOnlyList<IProviderMetadata> GetProvidersByCapability(Func<ProviderCapabilities, bool> predicate)
    {
        return _allProviders.Values
            .Where(r => r.IsEnabled && predicate(r.Provider.ProviderCapabilities))
            .OrderBy(r => r.Priority)
            .Select(r => r.Provider)
            .ToList();
    }

    #endregion

    #region Generic Provider Retrieval

    /// <summary>
    /// Gets all providers of a specific type, ordered by priority.
    /// This is the unified generic approach that replaces type-specific methods.
    /// </summary>
    /// <typeparam name="T">The provider interface type (e.g., IMarketDataClient, IHistoricalDataProvider).</typeparam>
    /// <returns>All enabled providers of the specified type, ordered by priority.</returns>
    public IReadOnlyList<T> GetProviders<T>() where T : class, IProviderMetadata
    {
        return _allProviders.Values
            .Where(r => r.IsEnabled && r.Provider is T)
            .OrderBy(r => r.Priority)
            .Select(r => (T)r.Provider)
            .ToList();
    }

    /// <summary>
    /// Gets a provider of a specific type by ID.
    /// </summary>
    /// <typeparam name="T">The provider interface type.</typeparam>
    /// <param name="id">The provider ID.</param>
    /// <returns>The provider if found and of the correct type, null otherwise.</returns>
    public T? GetProvider<T>(string id) where T : class, IProviderMetadata
    {
        return _allProviders.TryGetValue(id, out var registered) &&
               registered.IsEnabled &&
               registered.Provider is T provider
            ? provider
            : null;
    }

    /// <summary>
    /// Gets the best available provider of a specific type based on priority and availability.
    /// This is the unified availability check that works for all provider types.
    /// </summary>
    /// <typeparam name="T">The provider interface type.</typeparam>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The best available provider, or null if none available.</returns>
    public async Task<T?> GetBestAvailableProviderAsync<T>(CancellationToken ct = default)
        where T : class, IProviderMetadata
    {
        var candidates = _allProviders.Values
            .Where(r => r.IsEnabled && r.Provider is T)
            .OrderBy(r => r.Priority);

        foreach (var registered in candidates)
        {
            var provider = (T)registered.Provider;
            try
            {
                var isAvailable = await CheckProviderAvailabilityAsync(provider, ct);
                if (isAvailable)
                {
                    return provider;
                }
            }
            catch (Exception ex)
            {
                _log.Debug(ex, "Provider {Name} availability check failed", registered.Name);
            }
        }
        return null;
    }

    /// <summary>
    /// Checks availability for any provider type using the appropriate method.
    /// </summary>
    private static async Task<bool> CheckProviderAvailabilityAsync(IProviderMetadata provider, CancellationToken ct)
    {
        return provider switch
        {
            IHistoricalDataProvider backfill => await backfill.IsAvailableAsync(ct),
            ISymbolSearchProvider search => await search.IsAvailableAsync(ct),
            IMarketDataClient streaming => streaming.IsEnabled,
            _ => true // Default to available for unknown types
        };
    }

    /// <summary>
    /// Checks if any provider of the specified type is available.
    /// </summary>
    /// <typeparam name="T">The provider interface type.</typeparam>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if at least one provider is available.</returns>
    public async Task<bool> IsAnyProviderAvailableAsync<T>(CancellationToken ct = default)
        where T : class, IProviderMetadata
    {
        return await GetBestAvailableProviderAsync<T>(ct) != null;
    }

    /// <summary>
    /// Gets availability status for all providers of a specific type.
    /// </summary>
    /// <typeparam name="T">The provider interface type.</typeparam>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Dictionary mapping provider IDs to availability status.</returns>
    public async Task<IReadOnlyDictionary<string, bool>> GetProviderAvailabilityAsync<T>(CancellationToken ct = default)
        where T : class, IProviderMetadata
    {
        var results = new Dictionary<string, bool>();
        var candidates = _allProviders.Values
            .Where(r => r.Provider is T);

        foreach (var registered in candidates)
        {
            try
            {
                var isAvailable = registered.IsEnabled &&
                    await CheckProviderAvailabilityAsync(registered.Provider, ct);
                results[registered.Name] = isAvailable;
            }
            catch
            {
                results[registered.Name] = false;
            }
        }
        return results;
    }

    #endregion

    #region Type-Specific Methods (Convenience wrappers - delegate to generic methods)

    /// <summary>
    /// Registers a streaming market data provider.
    /// </summary>
    /// <remarks>Convenience wrapper that delegates to <see cref="Register{T}"/>.</remarks>
    public void RegisterStreaming(string name, IMarketDataClient provider, int priority = 100)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Register(provider, priority);
    }

    /// <summary>
    /// Gets a streaming provider by name.
    /// </summary>
    /// <remarks>Convenience wrapper that delegates to <see cref="GetProvider{T}(string)"/>.</remarks>
    public IMarketDataClient? GetStreamingProvider(string name) => GetProvider<IMarketDataClient>(name);

    /// <summary>
    /// Gets all registered streaming providers ordered by priority.
    /// </summary>
    /// <remarks>Convenience wrapper that delegates to <see cref="GetProviders{T}"/>.</remarks>
    public IReadOnlyList<IMarketDataClient> GetStreamingProviders() => GetProviders<IMarketDataClient>();

    /// <summary>
    /// Registers a historical data provider.
    /// </summary>
    /// <remarks>Convenience wrapper that delegates to <see cref="Register{T}"/>.</remarks>
    public void RegisterBackfill(string name, IHistoricalDataProvider provider, int priority = 100)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Register(provider, priority);
    }

    /// <summary>
    /// Gets a backfill provider by name.
    /// </summary>
    /// <remarks>Convenience wrapper that delegates to <see cref="GetProvider{T}(string)"/>.</remarks>
    public IHistoricalDataProvider? GetBackfillProvider(string name) => GetProvider<IHistoricalDataProvider>(name);

    /// <summary>
    /// Gets all registered backfill providers ordered by priority.
    /// </summary>
    /// <remarks>Convenience wrapper that delegates to <see cref="GetProviders{T}"/>.</remarks>
    public IReadOnlyList<IHistoricalDataProvider> GetBackfillProviders() => GetProviders<IHistoricalDataProvider>();

    /// <summary>
    /// Gets the best available backfill provider based on priority and health.
    /// </summary>
    /// <remarks>Convenience wrapper that delegates to <see cref="GetBestAvailableProviderAsync{T}"/>.</remarks>
    public Task<IHistoricalDataProvider?> GetBestBackfillProviderAsync(CancellationToken ct = default)
        => GetBestAvailableProviderAsync<IHistoricalDataProvider>(ct);

    /// <summary>
    /// Registers a symbol search provider.
    /// </summary>
    /// <remarks>Convenience wrapper that delegates to <see cref="Register{T}"/>.</remarks>
    public void RegisterSymbolSearch(string name, ISymbolSearchProvider provider, int priority = 100)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Register(provider, priority);
    }

    /// <summary>
    /// Gets a symbol search provider by name.
    /// </summary>
    /// <remarks>Convenience wrapper that delegates to <see cref="GetProvider{T}(string)"/>.</remarks>
    public ISymbolSearchProvider? GetSymbolSearchProvider(string name) => GetProvider<ISymbolSearchProvider>(name);

    /// <summary>
    /// Gets all registered symbol search providers ordered by priority.
    /// </summary>
    /// <remarks>Convenience wrapper that delegates to <see cref="GetProviders{T}"/>.</remarks>
    public IReadOnlyList<ISymbolSearchProvider> GetSymbolSearchProviders() => GetProviders<ISymbolSearchProvider>();

    /// <summary>
    /// Gets the best available symbol search provider based on priority and health.
    /// </summary>
    /// <remarks>Convenience wrapper that delegates to <see cref="GetBestAvailableProviderAsync{T}"/>.</remarks>
    public Task<ISymbolSearchProvider?> GetBestSymbolSearchProviderAsync(CancellationToken ct = default)
        => GetBestAvailableProviderAsync<ISymbolSearchProvider>(ct);

    #endregion

    #region Provider Management

    /// <summary>
    /// Enables a provider.
    /// </summary>
    public void Enable(string name)
    {
        if (_allProviders.TryGetValue(name, out var registered))
        {
            _allProviders[name] = registered with { IsEnabled = true };
            _log.Information("Enabled provider: {Name} (type: {Type})",
                name, registered.Provider.ProviderCapabilities.PrimaryType);
        }
        else
        {
            _log.Warning("Provider not found: {Name}", name);
        }
    }

    /// <summary>
    /// Disables a provider.
    /// </summary>
    public void Disable(string name)
    {
        if (_allProviders.TryGetValue(name, out var registered))
        {
            _allProviders[name] = registered with { IsEnabled = false };
            _log.Information("Disabled provider: {Name} (type: {Type})",
                name, registered.Provider.ProviderCapabilities.PrimaryType);

            if (registered.Provider is IMarketDataClient)
            {
                _alertDispatcher?.Publish(MonitoringAlert.Warning(
                    "ProviderRegistry",
                    AlertCategory.Provider,
                    $"Provider Disabled: {name}",
                    $"Streaming provider {name} has been disabled"));
            }
        }
        else
        {
            _log.Warning("Provider not found: {Name}", name);
        }
    }

    /// <summary>
    /// Gets information about all registered providers using standardized metadata.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="ProviderTemplateFactory.FromMetadata"/> via the unified
    /// <see cref="_allProviders"/> dictionary to ensure consistent template output
    /// across all provider types without type-specific branching.
    /// </remarks>
    public IReadOnlyList<ProviderInfo> GetAllProviders()
    {
        return _allProviders.Values
            .Select(p => ProviderTemplateFactory.FromMetadata(p.Provider, p.IsEnabled, p.Priority).ToInfo())
            .ToList();
    }

    /// <summary>
    /// Generates catalog entries from all registered providers using <see cref="ProviderTemplateFactory.ToCatalogEntry"/>.
    /// This replaces static hardcoded catalog data with runtime-derived metadata.
    /// </summary>
    /// <returns>A list of <see cref="ProviderCatalogEntry"/> objects for UI consumption.</returns>
    public IReadOnlyList<ProviderCatalogEntry> GetProviderCatalog()
    {
        return _allProviders.Values
            .Select(p => ProviderTemplateFactory.ToCatalogEntry(p.Provider))
            .ToList();
    }

    /// <summary>
    /// Generates catalog entries for providers of a specific type.
    /// </summary>
    /// <param name="type">The provider type to filter by.</param>
    /// <returns>A list of <see cref="ProviderCatalogEntry"/> objects for UI consumption.</returns>
    public IReadOnlyList<ProviderCatalogEntry> GetProviderCatalogByType(ProviderType type)
    {
        return _allProviders.Values
            .Where(p => p.Provider.ProviderCapabilities.PrimaryType == type ||
                        (type == ProviderType.Streaming && p.Provider.ProviderCapabilities.SupportsStreaming) ||
                        (type == ProviderType.Backfill && p.Provider.ProviderCapabilities.SupportsBackfill))
            .Select(p => ProviderTemplateFactory.ToCatalogEntry(p.Provider))
            .ToList();
    }

    /// <summary>
    /// Gets a catalog entry for a specific provider by ID.
    /// </summary>
    /// <param name="providerId">The provider ID to look up.</param>
    /// <returns>The catalog entry, or null if not found.</returns>
    public ProviderCatalogEntry? GetProviderCatalogEntry(string providerId)
    {
        return _allProviders.TryGetValue(providerId, out var registered)
            ? ProviderTemplateFactory.ToCatalogEntry(registered.Provider)
            : null;
    }

    /// <summary>
    /// Gets a summary of registered provider counts.
    /// </summary>
    public ProviderRegistrySummary GetSummary()
    {
        var providers = _allProviders.Values.ToList();
        return new ProviderRegistrySummary(
            StreamingCount: providers.Count(p => p.Provider is IMarketDataClient),
            BackfillCount: providers.Count(p => p.Provider is IHistoricalDataProvider),
            SymbolSearchCount: providers.Count(p => p.Provider is ISymbolSearchProvider),
            TotalEnabled: providers.Count(p => p.IsEnabled));
    }

    #endregion

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Provider name is required", nameof(name));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        // Dispose all providers based on their capabilities
        foreach (var registered in _allProviders.Values)
        {
            try
            {
                switch (registered.Provider)
                {
                    case IAsyncDisposable asyncDisposable:
                        asyncDisposable.DisposeAsync().AsTask().ContinueWith(
                            t => _log.Warning(t.Exception!.InnerException ?? t.Exception,
                                "Failed to async-dispose provider {ProviderName}", registered.Name),
                            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                        break;
                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to dispose provider {ProviderName}", registered.Name);
            }
        }

        _allProviders.Clear();
    }

    /// <summary>
    /// Internal record for tracking registered providers in the unified registry.
    /// </summary>
    private sealed record RegisteredProvider(string Name, IProviderMetadata Provider, int Priority, bool IsEnabled);
}

/// <summary>
/// Summary of registered providers.
/// </summary>
/// <param name="StreamingCount">Number of streaming providers.</param>
/// <param name="BackfillCount">Number of backfill providers.</param>
/// <param name="SymbolSearchCount">Number of symbol search providers.</param>
/// <param name="TotalEnabled">Total number of enabled providers.</param>
public sealed record ProviderRegistrySummary(int StreamingCount, int BackfillCount, int SymbolSearchCount, int TotalEnabled);
