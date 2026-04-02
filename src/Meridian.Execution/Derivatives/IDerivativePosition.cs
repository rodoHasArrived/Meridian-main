namespace Meridian.Execution.Derivatives;

/// <summary>
/// Classifies the type of derivative contract held in a position.
/// </summary>
public enum DerivativeKind
{
    /// <summary>Equity or index option (call or put).</summary>
    Option,

    /// <summary>Exchange-traded futures contract.</summary>
    Future,

    /// <summary>Any other derivative instrument.</summary>
    Other,
}

/// <summary>
/// A polymorphic position interface for derivative instruments.
/// Extends the flat <see cref="Models.ExecutionPosition"/> record to carry
/// contract-specific details, Greeks (for options), and daily MTM state (for futures).
/// </summary>
public interface IDerivativePosition
{
    /// <summary>Ticker symbol of the derivative contract (e.g., "AAPL240119C00150000").</summary>
    string Symbol { get; }

    /// <summary>Number of contracts held (positive = long, negative = short).</summary>
    long Contracts { get; }

    /// <summary>Average cost basis per contract (in premium points or price units).</summary>
    decimal AverageCostBasis { get; }

    /// <summary>Current mark-to-market price of the contract.</summary>
    decimal MarkPrice { get; }

    /// <summary>Unrealized P&amp;L at the current mark price.</summary>
    decimal UnrealizedPnl { get; }

    /// <summary>Cumulative realized P&amp;L from partial or full closes on this position.</summary>
    decimal RealizedPnl { get; }

    /// <summary>Classification of the derivative contract type.</summary>
    DerivativeKind Kind { get; }
}
