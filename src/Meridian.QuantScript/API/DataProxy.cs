namespace Meridian.QuantScript.API;

/// <summary>
/// Synchronous façade over <see cref="IQuantDataContext"/> for use inside scripts.
/// </summary>
/// <remarks>
/// IMPORTANT: All methods call <c>.GetAwaiter().GetResult()</c> and MUST only be called
/// from a <c>Task.Run</c> thread pool context.  Never call from the UI thread.
/// </remarks>
public sealed class DataProxy
{
    private readonly IQuantDataContext _ctx;

    internal DataProxy(IQuantDataContext ctx)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
    }

    /// <summary>
    /// Synchronously fetches OHLCV bars for the given symbol and date range.
    /// </summary>
    /// <remarks>
    /// CA2012: intentional — ScriptRunner always calls via Task.Run.
    /// </remarks>
    public PriceSeries Prices(string symbol, DateTime from, DateTime to)
        // CA2012: intentional — ScriptRunner always calls via Task.Run
        => _ctx.GetPricesAsync(symbol, from, to).GetAwaiter().GetResult();

    /// <summary>
    /// Synchronously returns recent order book snapshots for the given symbol.
    /// </summary>
    public IReadOnlyList<ScriptOrderBook> OrderBook(string symbol)
        // CA2012: intentional — ScriptRunner always calls via Task.Run
        => _ctx.GetOrderBookAsync(symbol).GetAwaiter().GetResult();
}
