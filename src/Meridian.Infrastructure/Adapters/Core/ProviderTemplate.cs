using Meridian.Contracts.Api;
using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Standardized provider metadata template for registry and UI surfaces.
/// </summary>
public sealed record ProviderTemplate(
    string Name,
    string DisplayName,
    ProviderType ProviderType,
    int Priority,
    bool IsEnabled,
    IReadOnlyDictionary<string, object> Capabilities,
    ProviderRateLimitProfile? RateLimit = null)
{
    public ProviderInfo ToInfo()
    {
        var capabilities = new Dictionary<string, object>(Capabilities);

        if (RateLimit is not null)
        {
            capabilities["MaxRequestsPerWindow"] = RateLimit.MaxRequestsPerWindow;
            capabilities["RateLimitWindowSeconds"] = RateLimit.Window.TotalSeconds;
            if (RateLimit.MinDelay.HasValue)
            {
                capabilities["RateLimitMinDelayMs"] = RateLimit.MinDelay.Value.TotalMilliseconds;
            }
        }

        return new ProviderInfo(Name, DisplayName, ProviderType, Priority, IsEnabled, capabilities);
    }
}

/// <summary>
/// Standardized rate limit profile for provider metadata.
/// </summary>
public sealed record ProviderRateLimitProfile(
    int MaxRequestsPerWindow,
    TimeSpan Window,
    TimeSpan? MinDelay = null);

/// <summary>
/// Factory for consistent provider templates across streaming, backfill, and search providers.
/// </summary>
/// <remarks>
/// The <see cref="FromMetadata"/> method is the preferred unified approach for creating
/// templates from any provider implementing <see cref="IProviderMetadata"/>.
/// The type-specific methods are kept for backwards compatibility.
/// </remarks>
public static class ProviderTemplateFactory
{
    /// <summary>
    /// Creates a provider template from any provider implementing <see cref="IProviderMetadata"/>.
    /// This is the preferred unified approach that eliminates special-case logic.
    /// </summary>
    /// <param name="provider">The provider implementing IProviderMetadata.</param>
    /// <param name="isEnabled">Whether the provider is currently enabled.</param>
    /// <param name="priorityOverride">Optional priority override (uses provider's priority if null).</param>
    /// <returns>A normalized ProviderTemplate for UI/monitoring consumption.</returns>
    public static ProviderTemplate FromMetadata(IProviderMetadata provider, bool isEnabled, int? priorityOverride = null)
    {
        var caps = provider.ProviderCapabilities;
        var priority = priorityOverride ?? provider.ProviderPriority;

        ProviderRateLimitProfile? rateLimit = null;
        if (caps.MaxRequestsPerWindow.HasValue && caps.RateLimitWindow.HasValue)
        {
            rateLimit = new ProviderRateLimitProfile(
                caps.MaxRequestsPerWindow.Value,
                caps.RateLimitWindow.Value,
                caps.MinRequestDelay);
        }

        return new ProviderTemplate(
            Name: provider.ProviderId,
            DisplayName: provider.ProviderDisplayName,
            ProviderType: caps.PrimaryType,
            Priority: priority,
            IsEnabled: isEnabled,
            Capabilities: caps.ToDictionary(),
            RateLimit: rateLimit);
    }

    /// <summary>
    /// Creates a provider template for a streaming provider.
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="FromMetadata"/> for unified handling.
    /// Kept for backwards compatibility.
    /// </remarks>
    public static ProviderTemplate ForStreaming(string name, IMarketDataClient provider, int priority, bool isEnabled)
    {
        // Use unified metadata path
        return FromMetadata(provider, isEnabled, priority);
    }

    /// <summary>
    /// Creates a provider template for a backfill provider.
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="FromMetadata"/> for unified handling.
    /// Kept for backwards compatibility.
    /// </remarks>
    public static ProviderTemplate ForBackfill(string name, IHistoricalDataProvider provider, int priority, bool isEnabled)
    {
        // Use unified metadata path
        return FromMetadata(provider, isEnabled, priority);
    }

    /// <summary>
    /// Creates a provider template for a symbol search provider.
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="FromMetadata"/> for unified handling.
    /// Kept for backwards compatibility.
    /// </remarks>
    public static ProviderTemplate ForSymbolSearch(ISymbolSearchProvider provider, int priority, bool isEnabled)
    {
        // Use unified metadata path
        return FromMetadata(provider, isEnabled, priority);
    }

    /// <summary>
    /// Generates a <see cref="ProviderCatalogEntry"/> from any provider implementing <see cref="IProviderMetadata"/>.
    /// This is the unified approach for deriving catalog entries from registered providers,
    /// eliminating the need for hardcoded static catalog data.
    /// </summary>
    /// <param name="provider">The provider implementing IProviderMetadata.</param>
    /// <returns>A normalized ProviderCatalogEntry for UI consumption.</returns>
    public static ProviderCatalogEntry ToCatalogEntry(IProviderMetadata provider)
    {
        var caps = provider.ProviderCapabilities;

        // Determine provider type kind
        var typeKind = (caps.SupportsStreaming, caps.SupportsBackfill) switch
        {
            (true, true) => ProviderTypeKind.Hybrid,
            (true, false) => ProviderTypeKind.Streaming,
            (false, true) => ProviderTypeKind.Backfill,
            _ => caps.SupportsSymbolSearch ? ProviderTypeKind.SymbolSearch : ProviderTypeKind.Streaming
        };

        // Convert credential fields
        var credentialFields = provider.ProviderCredentialFields
            .Select(f => new CredentialFieldInfo(
                f.Name,
                f.EnvironmentVariable,
                f.DisplayName,
                f.Required,
                f.DefaultValue))
            .ToArray();

        // Build rate limit info if available
        Meridian.Contracts.Api.RateLimitInfo? rateLimit = null;
        if (caps.MaxRequestsPerWindow.HasValue || caps.RateLimitWindow.HasValue)
        {
            rateLimit = new Meridian.Contracts.Api.RateLimitInfo
            {
                MaxRequestsPerWindow = caps.MaxRequestsPerWindow ?? 0,
                WindowSeconds = (int)(caps.RateLimitWindow?.TotalSeconds ?? 0),
                MinDelayMs = (int)(caps.MinRequestDelay?.TotalMilliseconds ?? 0),
                Description = caps.MaxRequestsPerWindow.HasValue
                    ? $"{caps.MaxRequestsPerWindow} requests/{(caps.RateLimitWindow?.TotalMinutes ?? 60):0} minutes"
                    : ""
            };
        }

        // Build capability info
        var capabilityInfo = new CapabilityInfo
        {
            SupportsStreaming = caps.SupportsStreaming,
            SupportsMarketDepth = caps.SupportsMarketDepth,
            MaxDepthLevels = caps.MaxDepthLevels,
            SupportsAdjustedPrices = caps.SupportsAdjustedPrices,
            SupportsDividends = caps.SupportsDividends,
            SupportsSplits = caps.SupportsSplits,
            SupportsIntraday = caps.SupportsIntraday,
            SupportsTrades = caps.SupportsRealtimeTrades || caps.SupportsHistoricalTrades,
            SupportsQuotes = caps.SupportsRealtimeQuotes || caps.SupportsHistoricalQuotes,
            SupportsAuctions = caps.SupportsHistoricalAuctions
        };

        return new ProviderCatalogEntry
        {
            ProviderId = provider.ProviderId,
            DisplayName = provider.ProviderDisplayName,
            Description = provider.ProviderDescription,
            ProviderType = typeKind,
            RequiresCredentials = provider.RequiresCredentials,
            CredentialFields = credentialFields,
            RateLimit = rateLimit,
            Notes = provider.ProviderNotes,
            Warnings = provider.ProviderWarnings,
            SupportedMarkets = caps.SupportedMarkets.ToArray(),
            DataTypes = provider.SupportedDataTypes,
            Capabilities = capabilityInfo
        };
    }
}
