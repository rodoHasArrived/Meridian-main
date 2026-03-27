using Meridian.Application.Subscriptions.Models;
using Meridian.Contracts.Domain;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Interface for symbol search and autocomplete providers.
/// </summary>
/// <remarks>
/// Implements <see cref="IProviderMetadata"/> for unified provider discovery
/// and capability reporting across all provider types.
/// </remarks>
public interface ISymbolSearchProvider : IProviderMetadata
{
    /// <summary>
    /// Provider identifier.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Human-readable provider name.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Priority for this provider (lower = higher priority).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Check if the provider is available/configured.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>
    /// Search for symbols matching the query.
    /// </summary>
    /// <param name="query">Search query (partial symbol or company name).</param>
    /// <param name="limit">Maximum number of results.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of matching symbols.</returns>
    Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(
        string query,
        int limit = 10,
        CancellationToken ct = default);

    /// <summary>
    /// Get detailed information about a specific symbol.
    /// </summary>
    /// <param name="symbol">Symbol ticker as a typed <see cref="SymbolId"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Symbol details or null if not found.</returns>
    Task<SymbolDetails?> GetDetailsAsync(SymbolId symbol, CancellationToken ct = default);

    #region IProviderMetadata Default Implementations

    /// <inheritdoc/>
    string IProviderMetadata.ProviderId => Name;

    /// <inheritdoc/>
    string IProviderMetadata.ProviderDisplayName => DisplayName;

    /// <inheritdoc/>
    string IProviderMetadata.ProviderDescription => "Symbol search and lookup provider";

    /// <inheritdoc/>
    int IProviderMetadata.ProviderPriority => Priority;

    /// <inheritdoc/>
    ProviderCapabilities IProviderMetadata.ProviderCapabilities => ProviderCapabilities.SymbolSearch;

    #endregion
}

/// <summary>
/// Interface for providers that support filtering in symbol search.
/// </summary>
/// <remarks>
/// Extends <see cref="ISymbolSearchProvider"/> with asset type and exchange filtering.
/// The <see cref="IProviderMetadata.ProviderCapabilities"/> property is overridden to
/// include filter capabilities.
/// </remarks>
[ImplementsAdr("ADR-001", "ISymbolSearchProvider contract with filtering capabilities")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
public interface IFilterableSymbolSearchProvider : ISymbolSearchProvider
{
    /// <summary>
    /// Supported asset types for filtering.
    /// </summary>
    IReadOnlyList<string> SupportedAssetTypes { get; }

    /// <summary>
    /// Supported exchanges for filtering.
    /// </summary>
    IReadOnlyList<string> SupportedExchanges { get; }

    /// <summary>
    /// Search for symbols with filtering options.
    /// </summary>
    Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(
        string query,
        int limit = 10,
        string? assetType = null,
        string? exchange = null,
        CancellationToken ct = default);

    #region IProviderMetadata Override

    /// <inheritdoc/>
    ProviderCapabilities IProviderMetadata.ProviderCapabilities =>
        ProviderCapabilities.SymbolSearchFilterable(SupportedAssetTypes, SupportedExchanges);

    #endregion
}
