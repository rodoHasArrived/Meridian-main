namespace Meridian.Execution.Sdk;

/// <summary>
/// Configuration for the brokerage execution layer.
/// Determines which gateway is used for live order routing.
/// </summary>
public sealed class BrokerageConfiguration
{
    /// <summary>
    /// The brokerage gateway to use. Must match a registered gateway ID
    /// (e.g., "alpaca", "ib", "paper").
    /// Default is "paper" for safety.
    /// </summary>
    public string Gateway { get; set; } = "paper";

    /// <summary>
    /// Whether live execution is explicitly enabled. Must be set to true
    /// along with a valid gateway to route orders to a real broker.
    /// This is a safety switch to prevent accidental live trading.
    /// </summary>
    public bool LiveExecutionEnabled { get; set; }

    /// <summary>
    /// Maximum position size (in shares) across all symbols. Zero means unlimited.
    /// Applied as a pre-trade risk check.
    /// </summary>
    public decimal MaxPositionSize { get; set; }

    /// <summary>
    /// Maximum notional order value (price * quantity). Zero means unlimited.
    /// Applied as a pre-trade risk check.
    /// </summary>
    public decimal MaxOrderNotional { get; set; }

    /// <summary>
    /// Maximum number of open orders allowed at any time. Zero means unlimited.
    /// </summary>
    public int MaxOpenOrders { get; set; }
}
