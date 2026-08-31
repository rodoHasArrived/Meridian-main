using Meridian.Ledger;

namespace Meridian.Backtesting.Sdk;

/// <summary>Per-symbol trade attribution.</summary>
public sealed record SymbolAttribution(
    string Symbol,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    int TradeCount,
    decimal Commissions,
    decimal MarginInterestAllocated);

/// <summary>Aggregate performance statistics computed by <c>BacktestMetricsEngine</c>.</summary>
/// <remarks>
/// <see cref="NetPnl"/> is <c>FinalEquity - InitialCapital</c>: equity already reflects all
/// frictions (commissions, margin interest, short rebates) because the simulated portfolio
/// posts them through cash. <see cref="GrossPnl"/> is <see cref="NetPnl"/> with those frictions
/// added back — the hypothetical friction-free result.
/// </remarks>
/// <param name="TotalReturn">Fraction of initial capital (0.15 = +15%), not percent units.</param>
/// <param name="AnnualizedReturn">Fraction of initial capital (0.15 = +15%), not percent units.</param>
/// <param name="MaxDrawdown">Absolute currency amount of the worst peak-to-trough decline.</param>
/// <param name="MaxDrawdownPercent">
/// Despite the name, this is a FRACTION of peak equity (0.15 = a 15% drawdown), not percent
/// units. Every producer must agree: <c>BacktestMetricsEngine</c>, <c>WalkForwardService</c>,
/// and <c>LiveRunMetricsTracker</c> all emit fractions, and the workstation multiplies by 100
/// at render time. Emitting percent units here reads as a 100x drawdown downstream.
/// </param>
public sealed record BacktestMetrics(
    decimal InitialCapital,
    decimal FinalEquity,
    decimal GrossPnl,
    decimal NetPnl,
    decimal TotalReturn,
    decimal AnnualizedReturn,
    double SharpeRatio,
    double SortinoRatio,
    double CalmarRatio,
    decimal MaxDrawdown,
    decimal MaxDrawdownPercent,
    int MaxDrawdownRecoveryDays,
    double ProfitFactor,            // gross profit / abs(gross loss)
    double WinRate,
    int TotalTrades,
    int WinningTrades,
    int LosingTrades,
    decimal TotalCommissions,
    decimal TotalMarginInterest,
    decimal TotalShortRebates,
    double Xirr,                    // cash-weighted IRR over all cash flows
    IReadOnlyDictionary<string, SymbolAttribution> SymbolAttribution);

/// <summary>Metadata about the engine and normalization path that produced a backtest result.</summary>
public sealed record BacktestEngineMetadata(
    string EngineId,
    string? ExternalRunId = null,
    string ResultKind = BacktestResultKinds.Full,
    IReadOnlyList<string>? CoverageWarnings = null);

/// <summary>Canonical result coverage categories shared by native and external backtest engines.</summary>
public static class BacktestResultKinds
{
    public const string Full = "Full";
    public const string SummaryOnly = "SummaryOnly";
}

/// <summary>Complete result of a finished backtest run.</summary>
public sealed record BacktestResult(
    BacktestRequest Request,
    IReadOnlySet<string> Universe,
    IReadOnlyList<PortfolioSnapshot> Snapshots,
    IReadOnlyList<CashFlowEntry> CashFlows,
    IReadOnlyList<FillEvent> Fills,
    BacktestMetrics Metrics,
    IReadOnlyLedger Ledger,
    TimeSpan ElapsedTime,
    long TotalEventsProcessed,
    IReadOnlyList<TradeTicket>? TradeTickets = null,
    TcaReport? TcaReport = null,
    BacktestEngineMetadata? EngineMetadata = null,
    BiasDisclosureReport? BiasDisclosure = null);
