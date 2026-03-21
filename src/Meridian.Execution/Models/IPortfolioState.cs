namespace Meridian.Execution.Models;

/// <summary>
/// A read-only view of the current portfolio state as seen by a live strategy.
/// Updated after each fill acknowledgement.
/// </summary>
public interface IPortfolioState
{
    /// <summary>Available cash (not including unrealised margin).</summary>
    decimal Cash { get; }

    /// <summary>Gross portfolio value: cash + long market value + short market value.</summary>
    decimal PortfolioValue { get; }

    /// <summary>Unrealised P&amp;L across all open positions.</summary>
    decimal UnrealisedPnl { get; }

    /// <summary>Realised P&amp;L since the session began.</summary>
    decimal RealisedPnl { get; }

    /// <summary>Open positions keyed by symbol.</summary>
    IReadOnlyDictionary<string, ExecutionPosition> Positions { get; }
}
