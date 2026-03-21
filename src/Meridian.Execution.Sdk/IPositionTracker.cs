namespace Meridian.Execution.Sdk;

/// <summary>
/// Tracks real-time position state across all symbols.
/// Updated by execution reports from the gateway.
/// </summary>
public interface IPositionTracker
{
    /// <summary>Gets the current position for a symbol.</summary>
    PositionState GetPosition(string symbol);

    /// <summary>Gets all current positions.</summary>
    IReadOnlyDictionary<string, PositionState> GetAllPositions();

    /// <summary>Gets the total portfolio value (cash + positions at mark).</summary>
    decimal GetPortfolioValue();

    /// <summary>Gets available cash.</summary>
    decimal GetCash();

    /// <summary>Gets total unrealized P&amp;L across all positions.</summary>
    decimal GetUnrealizedPnl();

    /// <summary>Gets total realized P&amp;L for the session.</summary>
    decimal GetRealizedPnl();
}
