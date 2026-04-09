using Meridian.Contracts.SecurityMaster;

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

    public PriceSeries Prices(string symbol)
        => Prices(
            symbol,
            DateOnly.FromDateTime(DateTime.Today.AddYears(-10)),
            DateOnly.FromDateTime(DateTime.Today));

    public PriceSeries Prices(string symbol, string? provider)
        => Prices(
            symbol,
            DateOnly.FromDateTime(DateTime.Today.AddYears(-10)),
            DateOnly.FromDateTime(DateTime.Today),
            provider);

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

    // ── Security Master ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the Security Master detail record for <paramref name="symbol"/> (ticker),
    /// including asset class, economic terms, and all registered identifiers.
    /// Returns <see langword="null"/> when the symbol is unknown or no Security Master is configured.
    /// </summary>
    public SecurityDetailDto? SecMaster(string symbol)
        => context.SecMasterAsync(symbol, ctProvider()).GetAwaiter().GetResult();

    /// <summary>
    /// Returns the time-ordered list of corporate action events for <paramref name="symbol"/>.
    /// Returns an empty list when no corporate actions are recorded or no Security Master is configured.
    /// </summary>
    public IReadOnlyList<CorporateActionDto> CorporateActions(string symbol)
        => context.CorporateActionsAsync(symbol, ctProvider()).GetAwaiter().GetResult();
}
