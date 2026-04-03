using Meridian.Execution.Sdk;

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

    /// <summary>
    /// Open positions keyed by symbol, typed against the cross-pillar <see cref="IPosition"/> interface.
    /// Replaces the former <c>IReadOnlyDictionary&lt;string, ExecutionPosition&gt;</c> surface.
    /// Callers that require the concrete <see cref="ExecutionPosition"/> type (e.g. serialisation
    /// boundaries) should cast individual values: <c>portfolio.Positions.Values.Cast&lt;ExecutionPosition&gt;()</c>.
    /// </summary>
    IReadOnlyDictionary<string, IPosition> Positions { get; }
}
