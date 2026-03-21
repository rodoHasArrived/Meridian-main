namespace Meridian.Strategies.Models;

/// <summary>Classifies a strategy run by the execution environment it used.</summary>
public enum RunType
{
    /// <summary>Run executed against historical data using the backtest engine.</summary>
    Backtest,

    /// <summary>Run executed against a live feed using <c>PaperTradingGateway</c>.</summary>
    Paper,

    /// <summary>Run executed against a live feed using a real broker.</summary>
    Live
}
