namespace Meridian.QuantScript.Api;

/// <summary>
/// Synchronous façade over <see cref="IQuantDataContext"/> for ergonomic script use.
/// Internally calls GetAwaiter().GetResult() on the background script thread.
/// <para>
/// <b>Important:</b> This class must only be called from a background thread (never the UI thread)
/// to avoid deadlocks. <see cref="Compilation.ScriptRunner"/> ensures scripts run via Task.Run.
/// </para>
/// </summary>
public sealed class DataProxy(IQuantDataContext context, Func<CancellationToken> ctProvider)
{
    // ── DateOnly overloads (preferred) ────────────────────────────────────────

    public PriceSeries Prices(string symbol, DateOnly from, DateOnly to)
        => context.PricesAsync(symbol, from, to, ctProvider()).GetAwaiter().GetResult();

    public PriceSeries Prices(string symbol, DateOnly from, DateOnly to, string? provider)
        => context.PricesAsync(symbol, from, to, provider, ctProvider()).GetAwaiter().GetResult();

    // ── DateTime convenience overloads ────────────────────────────────────────

    /// <inheritdoc cref="Prices(string,DateOnly,DateOnly)"/>
    public PriceSeries Prices(string symbol, DateTime from, DateTime to)
        => Prices(symbol, DateOnly.FromDateTime(from), DateOnly.FromDateTime(to));

    /// <inheritdoc cref="Prices(string,DateOnly,DateOnly,string?)"/>
    public PriceSeries Prices(string symbol, DateTime from, DateTime to, string? provider)
        => Prices(symbol, DateOnly.FromDateTime(from), DateOnly.FromDateTime(to), provider);

    // ── Trades & order book ───────────────────────────────────────────────────

    public IReadOnlyList<ScriptTrade> Trades(string symbol, DateOnly date)
        => context.TradesAsync(symbol, date, ctProvider()).GetAwaiter().GetResult();

    public ScriptOrderBook? OrderBook(string symbol, DateTimeOffset timestamp)
        => context.OrderBookAsync(symbol, timestamp, ctProvider()).GetAwaiter().GetResult();
}
