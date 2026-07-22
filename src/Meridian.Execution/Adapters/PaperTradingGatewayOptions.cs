namespace Meridian.Execution.Adapters;

/// <summary>
/// Configuration options for <see cref="PaperTradingGateway"/>.
/// Bind from <c>appsettings.json</c> using the <see cref="SectionKey"/> section.
/// </summary>
public sealed class PaperTradingGatewayOptions
{
    /// <summary>Configuration section key for <c>IOptions</c> binding.</summary>
    public const string SectionKey = "PaperTrading:Gateway";

    /// <summary>
    /// Whether market-style orders may fill at <see cref="ScaffoldMarketFillPrice"/> when no
    /// live feed price is available. Off by default: a fabricated fill price produces
    /// plausible-looking but meaningless paper P&amp;L, so the gateways fail closed and reject
    /// priceless market orders unless this is explicitly enabled.
    /// </summary>
    public bool AllowScaffoldMarketFills { get; set; }

    /// <summary>
    /// Notional fill price used for market-style orders when no live feed price is available
    /// and <see cref="AllowScaffoldMarketFills"/> is enabled. Every fill priced from this
    /// value produces meaningless paper P&amp;L, and the gateway logs a warning the first
    /// time it is used.
    /// </summary>
    public decimal ScaffoldMarketFillPrice { get; set; } = 1m;
}
