using System.Threading;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Unified contract for fetching historical data from vendors.
/// Consolidates previous V1, V2, and Extended interfaces into a single contract
/// with optional capabilities indicated by properties.
/// </summary>
/// <remarks>
/// This interface is the core contract for ADR-001 (Provider Abstraction Pattern).
/// All historical data providers must implement this interface for backfill operations.
///
/// Capabilities are indicated via properties (SupportsXxx) rather than interface
/// inheritance, allowing consumers to check capabilities without type casting.
///
/// Implements <see cref="IProviderMetadata"/> for unified provider discovery
/// and capability reporting across all provider types.
/// </remarks>
[ImplementsAdr("ADR-001", "Core historical data provider contract")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
public interface IHistoricalDataProvider : IProviderMetadata, IDisposable
{
    #region Core Identity

    /// <summary>
    /// Unique identifier for the provider (e.g., "alpaca", "tiingo").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Human-readable display name.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Description of the provider's capabilities and data coverage.
    /// </summary>
    string Description { get; }

    #endregion

    #region Priority and Rate Limiting

    /// <summary>
    /// Priority for fallback ordering (lower = higher priority, tried first).
    /// Default: 100 for basic providers.
    /// </summary>
    int Priority => 100;

    /// <summary>
    /// Minimum delay between API calls to respect rate limits.
    /// Default: no delay.
    /// </summary>
    TimeSpan RateLimitDelay => TimeSpan.Zero;

    /// <summary>
    /// Maximum number of requests allowed per time window.
    /// Default: unlimited.
    /// </summary>
    int MaxRequestsPerWindow => int.MaxValue;

    /// <summary>
    /// Time window for rate limiting (e.g., 1 minute, 1 hour).
    /// Default: 1 hour.
    /// </summary>
    TimeSpan RateLimitWindow => TimeSpan.FromHours(1);

    #endregion

    #region Data Capabilities

    /// <summary>
    /// Consolidated capability flags for this provider.
    /// Prefer using this property over the individual SupportsXxx properties.
    /// </summary>
    HistoricalDataCapabilities Capabilities => HistoricalDataCapabilities.None;

    /// <summary>
    /// Whether this provider returns split/dividend adjusted prices.
    /// </summary>
    /// <remarks>Delegates to <see cref="Capabilities"/>. Kept for backwards compatibility.</remarks>
    bool SupportsAdjustedPrices => Capabilities.AdjustedPrices;

    /// <summary>
    /// Whether this provider supports intraday bar data.
    /// </summary>
    /// <remarks>Delegates to <see cref="Capabilities"/>. Kept for backwards compatibility.</remarks>
    bool SupportsIntraday => Capabilities.Intraday;

    /// <summary>
    /// Whether this provider includes dividend data.
    /// </summary>
    /// <remarks>Delegates to <see cref="Capabilities"/>. Kept for backwards compatibility.</remarks>
    bool SupportsDividends => Capabilities.Dividends;

    /// <summary>
    /// Whether this provider includes split data.
    /// </summary>
    /// <remarks>Delegates to <see cref="Capabilities"/>. Kept for backwards compatibility.</remarks>
    bool SupportsSplits => Capabilities.Splits;

    /// <summary>
    /// Whether this provider supports historical quote (NBBO) data.
    /// </summary>
    /// <remarks>Delegates to <see cref="Capabilities"/>. Kept for backwards compatibility.</remarks>
    bool SupportsQuotes => Capabilities.Quotes;

    /// <summary>
    /// Whether this provider supports historical trade data.
    /// </summary>
    /// <remarks>Delegates to <see cref="Capabilities"/>. Kept for backwards compatibility.</remarks>
    bool SupportsTrades => Capabilities.Trades;

    /// <summary>
    /// Whether this provider supports historical auction data.
    /// </summary>
    /// <remarks>Delegates to <see cref="Capabilities"/>. Kept for backwards compatibility.</remarks>
    bool SupportsAuctions => Capabilities.Auctions;

    /// <summary>
    /// Market regions/countries supported (e.g., "US", "UK", "DE").
    /// </summary>
    /// <remarks>Delegates to <see cref="Capabilities"/>. Kept for backwards compatibility.</remarks>
    IReadOnlyList<string> SupportedMarkets => Capabilities.SupportedMarkets;

    #endregion

    #region Health Check

    /// <summary>
    /// Check if the provider is currently available and healthy.
    /// Default implementation returns true (assumes always available).
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

    #endregion

    #region Core Data Methods

    /// <summary>
    /// Fetch daily OHLCV bars for a symbol within the specified date range.
    /// </summary>
    Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default);

    /// <summary>
    /// Get extended bar data with adjustment information when supported.
    /// Default implementation converts standard bars to adjusted bars without adjustment data.
    /// </summary>
    async Task<IReadOnlyList<AdjustedHistoricalBar>> GetAdjustedDailyBarsAsync(
        string symbol,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        var bars = await GetDailyBarsAsync(symbol, from, to, ct).ConfigureAwait(false);
        return bars.Select(b => new AdjustedHistoricalBar(
            b.Symbol, b.SessionDate, b.Open, b.High, b.Low, b.Close, b.Volume, b.Source, b.SequenceNumber
        )).ToList();
    }

    #endregion

    #region Extended Data Methods (Optional)

    /// <summary>
    /// Fetch historical NBBO quotes for a symbol.
    /// Only call if SupportsQuotes is true.
    /// </summary>
    Task<HistoricalQuotesResult> GetHistoricalQuotesAsync(
        string symbol,
        DateTimeOffset start,
        DateTimeOffset end,
        int? limit = null,
        CancellationToken ct = default)
        => Task.FromResult(new HistoricalQuotesResult(Array.Empty<HistoricalQuote>()));

    /// <summary>
    /// Fetch historical quotes for multiple symbols.
    /// Only call if SupportsQuotes is true.
    /// </summary>
    Task<HistoricalQuotesResult> GetHistoricalQuotesAsync(
        IEnumerable<string> symbols,
        DateTimeOffset start,
        DateTimeOffset end,
        int? limit = null,
        CancellationToken ct = default)
        => Task.FromResult(new HistoricalQuotesResult(Array.Empty<HistoricalQuote>()));

    /// <summary>
    /// Fetch historical trades for a symbol.
    /// Only call if SupportsTrades is true.
    /// </summary>
    Task<HistoricalTradesResult> GetHistoricalTradesAsync(
        string symbol,
        DateTimeOffset start,
        DateTimeOffset end,
        int? limit = null,
        CancellationToken ct = default)
        => Task.FromResult(new HistoricalTradesResult(Array.Empty<HistoricalTrade>()));

    /// <summary>
    /// Fetch historical trades for multiple symbols.
    /// Only call if SupportsTrades is true.
    /// </summary>
    Task<HistoricalTradesResult> GetHistoricalTradesAsync(
        IEnumerable<string> symbols,
        DateTimeOffset start,
        DateTimeOffset end,
        int? limit = null,
        CancellationToken ct = default)
        => Task.FromResult(new HistoricalTradesResult(Array.Empty<HistoricalTrade>()));

    /// <summary>
    /// Fetch historical auction data for a symbol.
    /// Only call if SupportsAuctions is true.
    /// </summary>
    Task<HistoricalAuctionsResult> GetHistoricalAuctionsAsync(
        string symbol,
        DateOnly start,
        DateOnly end,
        CancellationToken ct = default)
        => Task.FromResult(new HistoricalAuctionsResult(Array.Empty<HistoricalAuction>()));

    /// <summary>
    /// Fetch historical auctions for multiple symbols.
    /// Only call if SupportsAuctions is true.
    /// </summary>
    Task<HistoricalAuctionsResult> GetHistoricalAuctionsAsync(
        IEnumerable<string> symbols,
        DateOnly start,
        DateOnly end,
        CancellationToken ct = default)
        => Task.FromResult(new HistoricalAuctionsResult(Array.Empty<HistoricalAuction>()));

    #endregion

    #region IProviderMetadata Default Implementations

    /// <inheritdoc/>
    string IProviderMetadata.ProviderId => Name;

    /// <inheritdoc/>
    string IProviderMetadata.ProviderDisplayName => DisplayName;

    /// <inheritdoc/>
    string IProviderMetadata.ProviderDescription => Description;

    /// <inheritdoc/>
    int IProviderMetadata.ProviderPriority => Priority;

    /// <inheritdoc/>
    ProviderCapabilities IProviderMetadata.ProviderCapabilities =>
        ProviderCapabilities.FromHistoricalCapabilities(
            Capabilities,
            MaxRequestsPerWindow == int.MaxValue ? null : MaxRequestsPerWindow,
            RateLimitWindow,
            RateLimitDelay == TimeSpan.Zero ? null : RateLimitDelay);

    #endregion

    #region IDisposable Default Implementation

    /// <summary>
    /// Disposes provider resources. Default implementation does nothing.
    /// </summary>
    void IDisposable.Dispose() { }

    #endregion
}

/// <summary>
/// Optional interface for providers that can report their rate limit status.
/// </summary>
public interface IRateLimitAwareProvider
{
    /// <summary>
    /// Get current rate limit usage information.
    /// </summary>
    RateLimitInfo GetRateLimitInfo();

    /// <summary>
    /// Event raised when the provider hits a rate limit.
    /// </summary>
    event Action<RateLimitInfo>? OnRateLimitHit;
}

/// <summary>
/// Information about a provider's current rate limit status.
/// </summary>
public sealed record RateLimitInfo(
    string ProviderName,
    int RequestsMade,
    int MaxRequests,
    TimeSpan Window,
    DateTimeOffset? ResetsAt = null,
    bool IsLimited = false,
    TimeSpan? RetryAfter = null
)
{
    public int RemainingRequests => Math.Max(0, MaxRequests - RequestsMade);
    public double UsageRatio => MaxRequests > 0 ? (double)RequestsMade / MaxRequests : 0;
    public TimeSpan? TimeUntilReset => ResetsAt.HasValue ? ResetsAt.Value - DateTimeOffset.UtcNow : null;
}

/// <summary>
/// Result of a provider health check.
/// </summary>
public sealed record ProviderHealthStatus(
    string ProviderName,
    bool IsAvailable,
    string? Message = null,
    DateTimeOffset? CheckedAt = null,
    TimeSpan? ResponseTime = null
)
{
    public ProviderHealthStatus() : this("unknown", false) { }
}

/// <summary>
/// Progress information for provider-level backfill operations.
/// </summary>
public sealed record ProviderBackfillProgress(
    string Symbol,
    string Provider,
    int BarsDownloaded,
    int TotalSymbols,
    int CurrentSymbolIndex,
    DateTimeOffset StartedAt,
    string? CurrentStatus = null,
    string? Error = null
)
{
    public double PercentComplete => TotalSymbols > 0 ? (CurrentSymbolIndex * 100.0) / TotalSymbols : 0;
    public TimeSpan Elapsed => DateTimeOffset.UtcNow - StartedAt;
}

/// <summary>
/// Result container for historical quotes with pagination support.
/// </summary>
public sealed record HistoricalQuotesResult(
    IReadOnlyList<HistoricalQuote> Quotes,
    string? NextPageToken = null,
    int TotalCount = 0
)
{
    public bool HasMore => !string.IsNullOrEmpty(NextPageToken);
}

/// <summary>
/// Result container for historical trades with pagination support.
/// </summary>
public sealed record HistoricalTradesResult(
    IReadOnlyList<HistoricalTrade> Trades,
    string? NextPageToken = null,
    int TotalCount = 0
)
{
    public bool HasMore => !string.IsNullOrEmpty(NextPageToken);
}

/// <summary>
/// Result container for historical auctions with pagination support.
/// </summary>
public sealed record HistoricalAuctionsResult(
    IReadOnlyList<HistoricalAuction> Auctions,
    string? NextPageToken = null,
    int TotalCount = 0
)
{
    public bool HasMore => !string.IsNullOrEmpty(NextPageToken);
}
