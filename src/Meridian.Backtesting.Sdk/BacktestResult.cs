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

/// <summary>Metadata about the engine that produced a backtest result.</summary>
public sealed record BacktestEngineMetadata(string EngineId);

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
    IReadOnlyList<ClosedLot>? AllClosedLots = null,
    BacktestEngineMetadata? EngineMetadata = null);
