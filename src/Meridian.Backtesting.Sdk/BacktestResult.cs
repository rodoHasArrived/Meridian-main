using Meridian.Ledger;

namespace Meridian.Backtesting.Sdk;

/// <summary>
/// Completeness state for a canonical backtest artifact when comparing results across engines.
/// </summary>
public enum BacktestArtifactStatus : byte
{
    Missing,
    Partial,
    Complete
}

/// <summary>
/// Coverage summary for the main artifacts that make up a canonical backtest result.
/// </summary>
public sealed record BacktestArtifactCoverage(
    BacktestArtifactStatus Snapshots,
    BacktestArtifactStatus CashFlows,
    BacktestArtifactStatus Fills,
    BacktestArtifactStatus TradeTickets,
    BacktestArtifactStatus Ledger,
    BacktestArtifactStatus TcaReport)
{
    public static BacktestArtifactCoverage Unknown { get; } = new(
        BacktestArtifactStatus.Missing,
        BacktestArtifactStatus.Missing,
        BacktestArtifactStatus.Missing,
        BacktestArtifactStatus.Missing,
        BacktestArtifactStatus.Missing,
        BacktestArtifactStatus.Missing);
}

/// <summary>
/// Engine identity and diagnostics captured alongside a canonical backtest result.
/// </summary>
public sealed record BacktestEngineMetadata(
    string EngineId,
    string EngineVersion,
    string SourceFormat,
    IReadOnlyDictionary<string, string> Diagnostics)
{
    public static BacktestEngineMetadata Unknown { get; } = new(
        EngineId: "Unknown",
        EngineVersion: "unknown",
        SourceFormat: "unknown",
        Diagnostics: new Dictionary<string, string>());
}

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
    IReadOnlyList<ClosedLot>? AllClosedLots = null)
{
    /// <summary>
    /// Coverage status for the main research artifacts contained in this result.
    /// Defaults to <see cref="BacktestArtifactCoverage.Unknown"/> for older construction paths.
    /// </summary>
    public BacktestArtifactCoverage Coverage { get; init; } = BacktestArtifactCoverage.Unknown;

    /// <summary>
    /// Source engine identity and diagnostics for this result.
    /// Defaults to <see cref="BacktestEngineMetadata.Unknown"/> for older construction paths.
    /// </summary>
    public BacktestEngineMetadata EngineMetadata { get; init; } = BacktestEngineMetadata.Unknown;
}
