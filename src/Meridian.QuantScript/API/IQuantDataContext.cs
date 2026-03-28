using Meridian.Infrastructure.Contracts;

namespace Meridian.QuantScript.API;

/// <summary>
/// Provides market data access to scripts.
/// </summary>
[ImplementsAdr("ADR-004", "All async methods accept CancellationToken for cooperative cancellation")]
public interface IQuantDataContext
{
    /// <summary>Fetches OHLCV bars for the given symbol and date range.</summary>
    Task<PriceSeries> GetPricesAsync(
        string symbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);

    /// <summary>Returns recent order book snapshots for the given symbol.</summary>
    Task<IReadOnlyList<ScriptOrderBook>> GetOrderBookAsync(
        string symbol,
        CancellationToken ct = default);
}
