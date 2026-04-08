using Meridian.Execution.Models;
using Meridian.Execution.Sdk;

namespace Meridian.Execution.Interfaces;

/// <summary>
/// A read-only, per-account view of live portfolio state.
/// Covers both brokerage (equity/margin) and bank (cash/money-market) accounts.
/// </summary>
public interface IAccountPortfolio
{
    /// <summary>Unique account identifier (broker-assigned or user-defined).</summary>
    string AccountId { get; }

    /// <summary>Human-readable account display name.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Whether this is a brokerage (equity/margin) or bank (cash/money-market) account.
    /// </summary>
    AccountKind Kind { get; }

    /// <summary>Available cash balance not tied up in open orders.</summary>
    decimal Cash { get; }

    /// <summary>Margin balance (positive = margin used; 0 for cash-only accounts).</summary>
    decimal MarginBalance { get; }

    /// <summary>
    /// Open positions keyed by ticker symbol (upper-case), typed against the cross-pillar
    /// <see cref="IPosition"/> interface.
    /// Callers that require the concrete <see cref="ExecutionPosition"/> type (e.g. serialisation
    /// boundaries) should cast individual values: <c>account.Positions.Values.Cast&lt;ExecutionPosition&gt;()</c>.
    /// </summary>
    IReadOnlyDictionary<string, IPosition> Positions { get; }

    /// <summary>Aggregate unrealised P&amp;L across all open positions.</summary>
    decimal UnrealisedPnl { get; }

    /// <summary>Cumulative realised P&amp;L since the session started.</summary>
    decimal RealisedPnl { get; }

    /// <summary>Total market value of all long positions.</summary>
    decimal LongMarketValue { get; }

    /// <summary>Total (absolute) market value of all short positions.</summary>
    decimal ShortMarketValue { get; }

    /// <summary>
    /// Captures the current state as an immutable snapshot suitable for
    /// persistence or API serialisation.
    /// </summary>
    ExecutionAccountDetailSnapshot TakeSnapshot();
}

/// <summary>
/// Detailed, per-account snapshot including full position list.
/// Returned by <see cref="IAccountPortfolio.TakeSnapshot"/> and the per-account REST endpoint.
/// </summary>
/// <param name="AccountId">Broker-assigned or user-defined account identifier.</param>
/// <param name="DisplayName">Human-readable account name.</param>
/// <param name="Kind">Brokerage or Bank.</param>
/// <param name="Cash">Available cash.</param>
/// <param name="MarginBalance">Margin used (0 for cash accounts).</param>
/// <param name="LongMarketValue">Sum of long position market values.</param>
/// <param name="ShortMarketValue">Sum of short position market values (absolute).</param>
/// <param name="GrossExposure">LongMarketValue + ShortMarketValue.</param>
/// <param name="NetExposure">LongMarketValue − ShortMarketValue.</param>
/// <param name="UnrealisedPnl">Aggregate unrealised P&amp;L.</param>
/// <param name="RealisedPnl">Cumulative realised P&amp;L.</param>
/// <param name="Positions">All open positions.</param>
/// <param name="AsOf">UTC timestamp of the snapshot.</param>
public sealed record ExecutionAccountDetailSnapshot(
    string AccountId,
    string DisplayName,
    AccountKind Kind,
    decimal Cash,
    decimal MarginBalance,
    decimal LongMarketValue,
    decimal ShortMarketValue,
    decimal GrossExposure,
    decimal NetExposure,
    decimal UnrealisedPnl,
    decimal RealisedPnl,
    IReadOnlyList<ExecutionPosition> Positions,
    DateTimeOffset AsOf);
