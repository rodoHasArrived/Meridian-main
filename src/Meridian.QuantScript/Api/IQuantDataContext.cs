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
}
