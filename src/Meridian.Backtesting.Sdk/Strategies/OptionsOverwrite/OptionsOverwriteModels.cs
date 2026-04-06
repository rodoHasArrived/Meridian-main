using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;

namespace Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite;

// ------------------------------------------------------------------ //
//  Option chain provider contract                                      //
// ------------------------------------------------------------------ //

/// <summary>
/// Provides option chain snapshots to the covered-call strategy.
/// Implement this interface to plug in real or simulated option data.
/// </summary>
public interface IOptionChainProvider
{
    /// <summary>
    /// Returns the candidate call options available for the given underlying on the specified date.
    /// Returns an empty collection when no chain is available.
    /// </summary>
    IReadOnlyList<OptionCandidateInfo> GetCalls(
        string underlyingSymbol,
        DateOnly asOf,
        decimal underlyingPrice);
}

// ------------------------------------------------------------------ //
//  Option candidate (chain entry)                                      //
// ------------------------------------------------------------------ //

/// <summary>
/// A single call option that is a candidate for the overwrite trade.
/// Aggregates all data needed by filters, scoring, and BSM mark-to-market.
/// </summary>
public sealed record OptionCandidateInfo(
    /// <summary>The underlying symbol (e.g. "SPY").</summary>
    string UnderlyingSymbol,
    /// <summary>Option strike price.</summary>
    decimal Strike,
    /// <summary>Option expiration date.</summary>
    DateOnly Expiration,
    /// <summary>Exercise style (American or European).</summary>
    OptionStyle Style,
    /// <summary>Contract multiplier (typically 100).</summary>
    int Multiplier,
    /// <summary>Best bid (executable short price).</summary>
    decimal Bid,
    /// <summary>Best ask.</summary>
    decimal Ask,
    /// <summary>Days to expiration from the scan date.</summary>
    int DaysToExpiration,
    /// <summary>Current open interest.</summary>
    long OpenInterest,
    /// <summary>Current day volume.</summary>
    long Volume,
    /// <summary>Black-Scholes delta (0..1 for calls).</summary>
    double Delta,
    /// <summary>Implied volatility as a decimal (e.g. 0.25 = 25 %).</summary>
    double? ImpliedVolatility = null,
    /// <summary>Vega (per 1 pp move in IV). Used for relative scoring.</summary>
    double? Vega = null,
    /// <summary>IV percentile (0–100) against recent history.</summary>
    double? IvPercentile = null,
    /// <summary>Days to the next ex-dividend date. Null when unknown.</summary>
    int? DaysToNextExDiv = null,
    /// <summary>Expected dividend amount. Null when unknown.</summary>
    decimal? NextDividendAmount = null)
{
    /// <summary>Mid-point price.</summary>
    public decimal Mid => Bid > 0 && Ask > 0 ? (Bid + Ask) / 2m : Bid;

    /// <summary>Fractional spread: (ask - bid) / mid.</summary>
    public double SpreadPct => Mid > 0 ? (double)((Ask - Bid) / Mid) : double.PositiveInfinity;
}

// ------------------------------------------------------------------ //
//  Short call position (open or closed)                               //
// ------------------------------------------------------------------ //

/// <summary>
/// Tracks a single open simulated short call position.
/// P&amp;L is computed by comparing the current BSM mark-to-close against the
/// original entry credit.
/// </summary>
public sealed class ShortCallPosition
{
    /// <summary>Unique position identifier.</summary>
    public Guid PositionId { get; } = Guid.NewGuid();

    /// <summary>Underlying symbol (e.g. "SPY").</summary>
    public required string UnderlyingSymbol { get; init; }

    /// <summary>Strike price of the short call.</summary>
    public required decimal Strike { get; init; }

    /// <summary>Expiration date.</summary>
    public required DateOnly Expiration { get; init; }

    /// <summary>Number of contracts (positive integer; the position is short).</summary>
    public required int Contracts { get; init; }

    /// <summary>Contract multiplier (typically 100).</summary>
    public required int Multiplier { get; init; }

    /// <summary>Exercise style.</summary>
    public required OptionStyle Style { get; init; }

    /// <summary>Date the position was opened.</summary>
    public required DateOnly EntryDate { get; init; }

    /// <summary>Premium collected per share at entry (bid price used conservatively).</summary>
    public required decimal EntryCredit { get; init; }

    /// <summary>
    /// Entry implied volatility (for relative scoring and BSM mark-to-market).
    /// Settable so tests and position managers can override the IV used for daily re-marking.
    /// </summary>
    public double? EntryImpliedVolatility { get; set; }

    /// <summary>Current mark-to-close (BSM value of the short call). Updated daily.</summary>
    public decimal MarkToClose { get; set; }

    /// <summary>Current delta (absolute value). Updated daily.</summary>
    public double CurrentDelta { get; set; }

    /// <summary>Days to expiration as of last mark. Updated daily.</summary>
    public int CurrentDte { get; set; }

    // ---- helpers ----

    /// <summary>Total premium received (entry credit × contracts × multiplier).</summary>
    public decimal TotalPremiumReceived => EntryCredit * Contracts * Multiplier;

    /// <summary>Current unrealised P&amp;L on the short option (positive = profit).</summary>
    public decimal UnrealisedPnl => (EntryCredit - MarkToClose) * Contracts * Multiplier;

    /// <summary>
    /// Fraction of the original premium that has been "captured".
    /// A value of 1.0 means the option has gone to zero (full profit).
    /// </summary>
    public double PremiumCaptured =>
        EntryCredit > 0 ? Math.Clamp((double)((EntryCredit - MarkToClose) / EntryCredit), 0.0, 1.0) : 0.0;
}

// ------------------------------------------------------------------ //
//  Completed trade record                                              //
// ------------------------------------------------------------------ //

/// <summary>Outcome of a completed short-call cycle (entry → exit).</summary>
public enum ShortCallExitReason
{
    /// <summary>Option expired worthless.</summary>
    ExpiredWorthless,
    /// <summary>Option was exercised (assignment).</summary>
    Assigned,
    /// <summary>Take-profit trigger reached; position was closed / rolled.</summary>
    TakeProfitRoll,
    /// <summary>Delta-risk trigger reached; position was rolled.</summary>
    RiskRoll,
    /// <summary>Dividend-assignment risk trigger; position was closed / rolled.</summary>
    DividendRiskRoll,
    /// <summary>Strategy ended while position was still open (backtest ran out of data).</summary>
    ForcedClose
}

/// <summary>Records the complete lifecycle of a single covered-call trade.</summary>
public sealed record OptionsOverwriteTradeRecord(
    /// <summary>Underlying symbol.</summary>
    string UnderlyingSymbol,
    /// <summary>Strike price.</summary>
    decimal Strike,
    /// <summary>Option expiration date.</summary>
    DateOnly Expiration,
    /// <summary>Number of contracts.</summary>
    int Contracts,
    /// <summary>Contract multiplier.</summary>
    int Multiplier,
    /// <summary>Date the short was entered.</summary>
    DateOnly EntryDate,
    /// <summary>Premium collected per share at entry.</summary>
    decimal EntryCredit,
    /// <summary>Date the short was closed / expired.</summary>
    DateOnly ExitDate,
    /// <summary>Cost to close per share (0 if expired worthless; intrinsic if assigned).</summary>
    decimal ExitDebit,
    /// <summary>How the position ended.</summary>
    ShortCallExitReason ExitReason,
    /// <summary>Entry implied volatility.</summary>
    double? EntryImpliedVolatility = null)
{
    /// <summary>Net P&amp;L per contract × multiplier (positive = profit for the short seller).</summary>
    public decimal NetPnlPerContract => (EntryCredit - ExitDebit) * Multiplier;

    /// <summary>Total net P&amp;L for all contracts.</summary>
    public decimal TotalNetPnl => NetPnlPerContract * Contracts;

    /// <summary>True when the short call finished with positive P&amp;L.</summary>
    public bool IsWin => TotalNetPnl > 0;

    /// <summary>Calendar days the position was held.</summary>
    public int HoldingDays => ExitDate.DayNumber - EntryDate.DayNumber;

    /// <summary>True when the option was assigned (exercised against the writer).</summary>
    public bool WasAssigned => ExitReason == ShortCallExitReason.Assigned;
}

// ------------------------------------------------------------------ //
//  Performance metrics                                                 //
// ------------------------------------------------------------------ //

/// <summary>
/// Extended performance metrics specific to the options overwrite strategy,
/// supplementing the standard <see cref="BacktestMetrics"/> from the engine.
/// </summary>
public sealed record OptionsOverwriteMetrics(
    // ---- Return metrics ----
    double Cagr,
    double AnnualizedVolatility,
    double SharpeRatio,
    double SortinoRatio,
    double CalmarRatio,
    double MaxDrawdownPct,

    // ---- Options-specific ----
    double WinRate,
    double AssignmentRate,
    double AverageHoldingDays,
    int TotalOptionTrades,
    int AssignedTrades,
    decimal TotalPremiumCollected,
    decimal TotalOptionPnl,

    // ---- Underlying vs strategy ----
    double UpCapture,
    double DownCapture,

    // ---- Tail risk ----
    double MonthlyVar1Pct,
    double MonthlyVar5Pct,
    double MonthlyCVar5Pct,
    double ReturnSkewness,
    double ReturnKurtosis,

    // ---- Turnover ----
    double AnnualizedTurnover,

    // ---- Portfolio equity curve ----
    IReadOnlyList<(DateOnly Date, decimal StrategyEquity, decimal UnderlyingEquity)> EquityCurve
);
