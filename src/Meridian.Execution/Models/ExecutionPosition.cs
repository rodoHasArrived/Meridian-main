using System.Text.Json.Serialization;
using Meridian.Execution.Sdk;

namespace Meridian.Execution.Models;

/// <summary>
/// A position held in the live portfolio as tracked by the execution layer.
/// This is the Execution-pillar equivalent of <c>Meridian.Backtesting.Sdk.Position</c>
/// and exists so that the Execution pillar does not depend on Backtesting infrastructure
/// (per ADR-016 pillar isolation rules).
/// </summary>
/// <param name="Symbol">Ticker symbol (e.g., "AAPL").</param>
/// <param name="Quantity">Shares held; negative means short.</param>
/// <param name="AverageCostBasis">FIFO-weighted average entry price.</param>
/// <param name="UnrealisedPnl">Mark-to-market unrealised P&amp;L.</param>
/// <param name="RealisedPnl">Cumulative realised P&amp;L since session start.</param>
public sealed record ExecutionPosition(
    string Symbol,
    long Quantity,
    decimal AverageCostBasis,
    decimal UnrealisedPnl,
    decimal RealisedPnl) : IPosition
{
    /// <summary>
    /// Signed quantity attributed to each owning fund account. Carried through from the
    /// fills so a shared execution book can still be read per fund.
    /// </summary>
    /// <remarks>
    /// Never serialized. The general execution reads — <c>/api/execution/positions</c>,
    /// <c>/api/execution/portfolio</c>, and the account-position routes — return this record
    /// directly with no fund-account scope check, so emitting the map would hand every
    /// authenticated execution reader the fund ids and exact signed holdings of funds they
    /// are not authorized to see. Attribution is consumed in-process by
    /// <c>AggregatePortfolioService</c>; the scoped aggregate routes do their own
    /// authorization before projecting per-fund figures.
    /// </remarks>
    [JsonIgnore]
    public IReadOnlyDictionary<string, decimal> OwnerQuantities { get; init; } =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Contract multiplier; 1 for outright instruments, 100 for equity options.</summary>
    public decimal ContractMultiplier { get; init; } = 1m;

    private readonly decimal? _exactQuantity;

    /// <summary>
    /// Signed quantity before the whole-share rounding <see cref="Quantity"/> applies.
    /// Falls back to <see cref="Quantity"/> when a producer did not carry a fractional
    /// size, so positions that only ever hold whole shares are unaffected.
    /// </summary>
    /// <remarks>
    /// Not serialized: <see cref="Quantity"/> remains the wire contract, and emitting a
    /// second quantity would give clients two fields to disagree about. Consumed in-process
    /// by fund-ownership attribution, which must not round.
    /// </remarks>
    [JsonIgnore]
    public decimal ExactQuantity
    {
        get => _exactQuantity ?? Quantity;
        init => _exactQuantity = value;
    }

    /// <summary>True when this is a short (negative) position.</summary>
    public bool IsShort => Quantity < 0;

    /// <summary>Absolute number of shares without sign.</summary>
    public long AbsoluteQuantity => Math.Abs(Quantity);

    /// <summary>Signed notional market value at <paramref name="lastPrice"/>.</summary>
    public decimal NotionalValue(decimal lastPrice) => Quantity * lastPrice;

    // ── IPosition explicit implementations ──────────────────────────────────
    // ExecutionPosition uses the British spelling (UnrealisedPnl / RealisedPnl) while
    // IPosition standardises on the American spelling (UnrealizedPnl / RealizedPnl).
    // Explicit implementations bridge the naming gap without renaming the record parameters,
    // which would be a breaking wire-format change for JSON serialisation.

    /// <inheritdoc cref="IPosition.UnrealizedPnl"/>
    decimal IPosition.UnrealizedPnl => UnrealisedPnl;

    /// <inheritdoc cref="IPosition.RealizedPnl"/>
    decimal IPosition.RealizedPnl => RealisedPnl;
}
