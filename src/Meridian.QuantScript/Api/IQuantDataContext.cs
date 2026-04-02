using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Contracts;

namespace Meridian.QuantScript.Api;

/// <summary>
/// Async data access contract; implemented by QuantDataContext which delegates to
/// HistoricalDataQueryService.
/// </summary>
[ImplementsAdr("ADR-004", "All async data access methods support CancellationToken")]
public interface IQuantDataContext
{
    Task<PriceSeries> PricesAsync(
        string symbol, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<PriceSeries> PricesAsync(
        string symbol, DateOnly from, DateOnly to, string? provider, CancellationToken ct = default);

    Task<IReadOnlyList<ScriptTrade>> TradesAsync(
        string symbol, DateOnly date, CancellationToken ct = default);

    Task<ScriptOrderBook?> OrderBookAsync(
        string symbol, DateTimeOffset timestamp, CancellationToken ct = default);

    /// <summary>
    /// Returns the Security Master detail record for <paramref name="symbol"/> (ticker),
    /// including asset class, economic terms, and all registered identifiers.
    /// Returns <see langword="null"/> when the symbol is not found in the Security Master
    /// or when no Security Master is configured.
    /// </summary>
    Task<SecurityDetailDto?> SecMasterAsync(string symbol, CancellationToken ct = default);

    /// <summary>
    /// Returns the time-ordered list of corporate action events for <paramref name="symbol"/>.
    /// Returns an empty list when no corporate actions are recorded or when no Security Master is configured.
    /// </summary>
    Task<IReadOnlyList<CorporateActionDto>> CorporateActionsAsync(
        string symbol, CancellationToken ct = default);
}
