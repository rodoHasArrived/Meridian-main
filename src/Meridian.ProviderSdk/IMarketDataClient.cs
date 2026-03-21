using System.Threading;
using Meridian.Contracts.Configuration;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;

namespace Meridian.Infrastructure;

/// <summary>
/// Market data client abstraction for provider-agnostic market data ingestion.
/// Implementations must be non-blocking on publish paths.
/// </summary>
/// <remarks>
/// This interface is the core contract for ADR-001 (Provider Abstraction Pattern).
/// All streaming data providers must implement this interface.
///
/// Implements <see cref="IProviderMetadata"/> for unified provider discovery
/// and capability reporting across all provider types.
/// </remarks>
[ImplementsAdr("ADR-001", "Core streaming data provider contract")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
public interface IMarketDataClient : IProviderMetadata, IAsyncDisposable
{
    bool IsEnabled { get; }

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>Subscribe to market depth for the symbol described by cfg.</summary>
    int SubscribeMarketDepth(SymbolConfig cfg);

    /// <summary>Unsubscribe a previously returned depth subscription id.</summary>
    void UnsubscribeMarketDepth(int subscriptionId);

    /// <summary>Subscribe to tick-by-tick trade prints for the symbol described by cfg.</summary>
    int SubscribeTrades(SymbolConfig cfg);

    /// <summary>Unsubscribe a previously returned trade subscription id.</summary>
    void UnsubscribeTrades(int subscriptionId);

    #region IProviderMetadata Default Implementations

    /// <inheritdoc/>
    string IProviderMetadata.ProviderId => GetType().Name.Replace("MarketDataClient", "").ToLowerInvariant();

    /// <inheritdoc/>
    string IProviderMetadata.ProviderDisplayName => GetType().Name.Replace("MarketDataClient", " Streaming");

    /// <inheritdoc/>
    string IProviderMetadata.ProviderDescription => "Real-time streaming market data provider";

    /// <inheritdoc/>
    int IProviderMetadata.ProviderPriority => 100;

    /// <inheritdoc/>
    ProviderCapabilities IProviderMetadata.ProviderCapabilities => ProviderCapabilities.Streaming();

    #endregion
}
