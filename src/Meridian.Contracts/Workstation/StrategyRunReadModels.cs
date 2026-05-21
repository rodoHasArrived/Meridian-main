using System.Text.Json.Serialization;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.Workstation;

/// <summary>
/// Shared workstation-facing mode for a strategy run.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StrategyRunMode>))]
public enum StrategyRunMode : byte
{
    Backtest,
    Paper,
    Live
}

/// <summary>
/// Shared workstation-facing execution engine for a strategy run.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StrategyRunEngine>))]
public enum StrategyRunEngine : byte
{
    Unknown,
    MeridianNative,
    Lean,
    BrokerPaper,
    BrokerLive
}

/// <summary>
/// Normalized status used by workstation surfaces across backtest, paper, and live runs.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StrategyRunStatus>))]
public enum StrategyRunStatus : byte
{
    Pending,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled,
    Stopped
}

/// <summary>
/// Promotion-oriented readiness signal derived from the current run state.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StrategyRunPromotionState>))]
public enum StrategyRunPromotionState : byte
{
    None,
    RequiresCompletion,
    CandidateForPaper,
    CandidateForLive,
    LiveManaged
}

// ── PR-02: Paper/live-compatible run shapes ──────────────────────────────────

/// <summary>
/// Live-execution status fields attached to a running or recently completed live run.
/// Surfaced on <see cref="StrategyRunSummary"/> and <see cref="StrategyRunDetail"/> when
/// <see cref="StrategyRunMode"/> is <c>Live</c>.
/// </summary>
public sealed record StrategyRunLiveStatus(
    /// <summary>Broker or execution-venue identifier.</summary>
    string BrokerId,
    /// <summary>Brokerage account the run is executing against.</summary>
    string AccountId,
    /// <summary>Whether the live run is currently connected to the broker feed.</summary>
    bool IsConnected,
    /// <summary>Whether the broker has confirmed the session as active.</summary>
    bool IsSessionActive,
    /// <summary>Number of open orders currently tracked by the live execution engine.</summary>
    int OpenOrderCount,
    /// <summary>Number of fills received since the run started.</summary>
    int FillCount,
    /// <summary>Current realized P&amp;L as reported by the broker reconciliation path.</summary>
    decimal? RealizedPnl,
    /// <summary>Current unrealized P&amp;L as reported by the broker reconciliation path.</summary>
    decimal? UnrealizedPnl,
    /// <summary>When the broker feed was last successfully polled.</summary>
    DateTimeOffset? LastBrokerPollAt,
    /// <summary>Non-null when the live engine has a pending or active reconciliation break.</summary>
    string? ReconciliationBreakReason = null);

/// <summary>
/// Paper-execution status fields attached to a running or recently completed paper run.
/// Surfaced on <see cref="StrategyRunSummary"/> and <see cref="StrategyRunDetail"/> when
/// <see cref="StrategyRunMode"/> is <c>Paper</c>.
/// </summary>
public sealed record StrategyRunPaperStatus(
    /// <summary>Data-feed or simulation engine driving fills for this paper run.</summary>
    string SimulationFeedId,
    /// <summary>Whether the paper engine is currently active and consuming market data.</summary>
    bool IsActive,
    /// <summary>Number of simulated fills since the run started.</summary>
    int FillCount,
    /// <summary>Number of open simulated orders at the time of last snapshot.</summary>
    int OpenOrderCount,
    /// <summary>Current simulated realized P&amp;L.</summary>
    decimal? RealizedPnl,
    /// <summary>Current simulated unrealized P&amp;L.</summary>
    decimal? UnrealizedPnl,
    /// <summary>When the paper engine last processed a market-data event.</summary>
    DateTimeOffset? LastMarketEventAt,
    /// <summary>Whether the paper run has a ledger coverage seam active.</summary>
    bool HasLedgerCoverage = false);

/// <summary>
/// Governance hook attached to a run detail or summary, capturing the relevant
/// approval, audit, and compliance signals for this run at the time of the query.
/// Multiple hooks may be present — one per governance seam that is active.
/// </summary>
public sealed record StrategyRunGovernanceHook(
    /// <summary>Stable identifier for this governance seam (e.g. "approval", "audit", "compliance").</summary>
    string SeamId,
    /// <summary>Human-readable label shown on governance dashboards.</summary>
    string Label,
    /// <summary>Whether this seam is currently satisfied / cleared.</summary>
    bool IsSatisfied,
    /// <summary>Current approval or audit status string, if applicable.</summary>
    string? StatusLabel,
    /// <summary>External reference for this governance record (e.g. ticket ID, audit log ID).</summary>
    string? ExternalReference,
    /// <summary>When this governance hook was last evaluated or updated.</summary>
    DateTimeOffset? LastEvaluatedAt,
    /// <summary>Narrative note or reason for the current governance state.</summary>
    string? Note = null);

/// <summary>
/// Shared execution summary used by workstation drill-ins and governance surfaces.
/// </summary>
public sealed record StrategyRunExecutionSummary(
    int FillCount,
    int TotalTrades,
    int WinningTrades,
    int LosingTrades,
    decimal TotalCommissions,
    decimal TotalMarginInterest,
    decimal TotalShortRebates,
    bool HasPortfolio,
    bool HasLedger,
    bool HasAuditTrail,
    string? AuditReference);

/// <summary>
/// Shared promotion summary used by research, trading, and governance workflows.
/// </summary>
public sealed record StrategyRunPromotionSummary(
    StrategyRunPromotionState State,
    StrategyRunMode? SuggestedNextMode,
    bool RequiresReview,
    string Reason,
    string? SourceRunId = null,
    string? TargetRunId = null,
    string? AuditReference = null,
    StrategyRunIdentity? Identity = null,
    string? ApprovalStatus = null,
    string? ManualOverrideId = null,
    string? ApprovedBy = null,
    IReadOnlyList<string>? ApprovalChecklist = null);

/// <summary>
/// Shared governance summary used by audit and control surfaces.
/// </summary>
public sealed record StrategyRunGovernanceSummary(
    DateTimeOffset LastUpdatedAt,
    bool HasParameters,
    bool HasPortfolio,
    bool HasLedger,
    bool HasAuditTrail,
    string? AuditReference,
    string? DatasetReference,
    string? FeedReference);

/// <summary>
/// Canonical run identity and provenance contract shared by run projections.
/// </summary>

public sealed record StrategyRunIdentity(
    string CanonicalRunKey,
    string? ParentCanonicalRunKey,
    IReadOnlyList<string> DerivedCanonicalRunKeys,
    StrategyRunMode Mode,
    string? PromotionSourceRunId,
    string? PromotionTargetRunId,
    string? PromotionDecision,
    string? ReplayAuditReference,
    DateTimeOffset? ReplayVerifiedAt,
    bool HasReplayAudit);

/// <summary>
/// Summary row shown in a run browser or recent-run list.
/// </summary>
public sealed record StrategyRunSummary(
    string RunId,
    string StrategyId,
    string StrategyName,
    StrategyRunMode Mode,
    StrategyRunEngine Engine,
    StrategyRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? DatasetReference,
    string? FeedReference,
    string? PortfolioId,
    string? LedgerReference,
    decimal? NetPnl,
    decimal? TotalReturn,
    decimal? FinalEquity,
    int FillCount,
    DateTimeOffset LastUpdatedAt,
    string? AuditReference = null,
    StrategyRunIdentity? Identity = null,
    StrategyRunExecutionSummary? Execution = null,
    StrategyRunPromotionSummary? Promotion = null,
    StrategyRunGovernanceSummary? Governance = null,
    string? FundProfileId = null,
    string? FundDisplayName = null,
    string? ParentRunId = null,
    string? SweepId = null,
    string? SweepDefinitionHash = null,
    string? SweepObjective = null,
    // PR-02: paper/live-compatible status shapes
    StrategyRunLiveStatus? LiveStatus = null,
    StrategyRunPaperStatus? PaperStatus = null);

public sealed record StrategySweepObjectiveRanking(
    string RunId,
    string StrategyName,
    decimal? ObjectiveValue,
    decimal? NetPnl,
    decimal? TotalReturn,
    decimal? FinalEquity,
    DateTimeOffset LastUpdatedAt);

public sealed record StrategySweepResultGroup(
    string SweepId,
    string SweepDefinitionHash,
    string? Objective,
    DateTimeOffset StartedAt,
    int RunCount,
    IReadOnlyList<StrategyRunSummary> Runs,
    IReadOnlyList<StrategySweepObjectiveRanking> ObjectiveRankings);

/// <summary>
/// Expanded detail for a single run, including derived portfolio and ledger views.
/// </summary>
public sealed record StrategyRunDetail(
    StrategyRunSummary Summary,
    IReadOnlyDictionary<string, string> Parameters,
    PortfolioSummary? Portfolio,
    LedgerSummary? Ledger,
    StrategyRunExecutionSummary? Execution = null,
    StrategyRunPromotionSummary? Promotion = null,
    StrategyRunGovernanceSummary? Governance = null,
    // PR-02: governance hooks for approval/audit/compliance seams
    IReadOnlyList<StrategyRunGovernanceHook>? GovernanceHooks = null);

/// <summary>
/// Security Master coverage state associated with a workstation security reference.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkstationSecurityCoverageStatus>))]
public enum WorkstationSecurityCoverageStatus : byte
{
    Resolved,
    Partial,
    Missing,
    Unavailable
}

/// <summary>
/// Lightweight Security Master reference used by workstation portfolio and ledger surfaces.
/// <para><see cref="SubType"/> is the most specific classification available at query time
/// (e.g. "CommonShare", "Bond", "OptionContract"). It is derived from the security's asset
/// class and is null when the asset class does not map to a unique sub-type.</para>
/// <para><see cref="RiskCountry"/> is extracted from the CommonTerms JSON blob when available.
/// <see cref="IssuerType"/> is only populated by full workbench queries; the lightweight
/// symbol-lookup path leaves it null to avoid an extra round-trip.</para>
/// </summary>
public sealed record WorkstationSecurityReference(
    Guid SecurityId,
    string DisplayName,
    string AssetClass,
    string Currency,
    SecurityStatusDto Status,
    string? PrimaryIdentifier,
    string? SubType = null,
    WorkstationSecurityCoverageStatus CoverageStatus = WorkstationSecurityCoverageStatus.Resolved,
    string? MatchedIdentifierKind = null,
    string? MatchedIdentifierValue = null,
    string? MatchedProvider = null,
    string? ResolutionReason = null,
    string? LookupPath = null,
    string? LookupSource = null,
    bool IsInferredMatch = false,
    /// <summary>ISO 3166-1 alpha-2 country of risk extracted from CommonTerms, if available.</summary>
    string? RiskCountry = null,
    /// <summary>Issuer type classification populated by full workbench queries only.</summary>
    string? IssuerType = null);

/// <summary>
/// Shared portfolio rollup for workstation research and trading surfaces.
/// </summary>
public sealed record PortfolioSummary(
    string PortfolioId,
    string RunId,
    DateTimeOffset AsOf,
    decimal Cash,
    decimal LongMarketValue,
    decimal ShortMarketValue,
    decimal GrossExposure,
    decimal NetExposure,
    decimal TotalEquity,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    decimal Commissions,
    decimal Financing,
    IReadOnlyList<PortfolioPositionSummary> Positions,
    int SecurityResolvedCount = 0,
    int SecurityMissingCount = 0,
    [property: JsonPropertyName("accountScopeId")] string? AccountScopeId = null,
    [property: JsonPropertyName("accountScopeDisplayName")] string? AccountScopeDisplayName = null,
    [property: JsonPropertyName("entityScopeId")] string? EntityScopeId = null,
    [property: JsonPropertyName("entityScopeDisplayName")] string? EntityScopeDisplayName = null,
    [property: JsonPropertyName("sleeveScopeId")] string? SleeveScopeId = null,
    [property: JsonPropertyName("sleeveScopeDisplayName")] string? SleeveScopeDisplayName = null,
    [property: JsonPropertyName("vehicleScopeId")] string? VehicleScopeId = null,
    [property: JsonPropertyName("vehicleScopeDisplayName")] string? VehicleScopeDisplayName = null,
    AccountingBasisKindDto AccountingBasis = AccountingBasisKindDto.Primary,
    string AccountingPolicyId = "legacy-v1",
    string AccountingPolicyVersion = "legacy-v1",
    string? RuleId = null,
    string? RuleVersion = null,
    string? SourceEventId = null,
    Guid? SourceJournalEntryId = null);

/// <summary>
/// Shared position row for workstation portfolio views.
/// </summary>
public sealed record PortfolioPositionSummary(
    string Symbol,
    long Quantity,
    decimal AverageCostBasis,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    bool IsShort,
    WorkstationSecurityReference? Security = null,
    [property: JsonPropertyName("accountScopeId")] string? AccountScopeId = null,
    [property: JsonPropertyName("accountScopeDisplayName")] string? AccountScopeDisplayName = null,
    [property: JsonPropertyName("entityScopeId")] string? EntityScopeId = null,
    [property: JsonPropertyName("entityScopeDisplayName")] string? EntityScopeDisplayName = null,
    [property: JsonPropertyName("sleeveScopeId")] string? SleeveScopeId = null,
    [property: JsonPropertyName("sleeveScopeDisplayName")] string? SleeveScopeDisplayName = null,
    [property: JsonPropertyName("vehicleScopeId")] string? VehicleScopeId = null,
    [property: JsonPropertyName("vehicleScopeDisplayName")] string? VehicleScopeDisplayName = null);

/// <summary>
/// Shared ledger rollup for workstation governance and audit surfaces.
/// </summary>
public sealed record LedgerSummary(
    string LedgerReference,
    string RunId,
    DateTimeOffset AsOf,
    int JournalEntryCount,
    int LedgerEntryCount,
    decimal AssetBalance,
    decimal LiabilityBalance,
    decimal EquityBalance,
    decimal RevenueBalance,
    decimal ExpenseBalance,
    IReadOnlyList<LedgerTrialBalanceLine> TrialBalance,
    IReadOnlyList<LedgerJournalLine> Journal,
    int SecurityResolvedCount = 0,
    int SecurityMissingCount = 0,
    [property: JsonPropertyName("accountScopeId")] string? AccountScopeId = null,
    [property: JsonPropertyName("accountScopeDisplayName")] string? AccountScopeDisplayName = null,
    [property: JsonPropertyName("entityScopeId")] string? EntityScopeId = null,
    [property: JsonPropertyName("entityScopeDisplayName")] string? EntityScopeDisplayName = null,
    [property: JsonPropertyName("sleeveScopeId")] string? SleeveScopeId = null,
    [property: JsonPropertyName("sleeveScopeDisplayName")] string? SleeveScopeDisplayName = null,
    [property: JsonPropertyName("vehicleScopeId")] string? VehicleScopeId = null,
    [property: JsonPropertyName("vehicleScopeDisplayName")] string? VehicleScopeDisplayName = null);

/// <summary>
/// Shared trial-balance row for workstation ledger views.
/// </summary>
public sealed record LedgerTrialBalanceLine(
    string AccountName,
    string AccountType,
    string? Symbol,
    string? FinancialAccountId,
    decimal Balance,
    int EntryCount,
    WorkstationSecurityReference? Security = null,
    [property: JsonPropertyName("accountScopeId")] string? AccountScopeId = null,
    [property: JsonPropertyName("accountScopeDisplayName")] string? AccountScopeDisplayName = null,
    [property: JsonPropertyName("entityScopeId")] string? EntityScopeId = null,
    [property: JsonPropertyName("entityScopeDisplayName")] string? EntityScopeDisplayName = null,
    [property: JsonPropertyName("sleeveScopeId")] string? SleeveScopeId = null,
    [property: JsonPropertyName("sleeveScopeDisplayName")] string? SleeveScopeDisplayName = null,
    [property: JsonPropertyName("vehicleScopeId")] string? VehicleScopeId = null,
    [property: JsonPropertyName("vehicleScopeDisplayName")] string? VehicleScopeDisplayName = null);

/// <summary>
/// Shared journal row for workstation audit surfaces.
/// </summary>
public sealed record LedgerJournalLine(
    Guid JournalEntryId,
    DateTimeOffset Timestamp,
    string Description,
    decimal TotalDebits,
    decimal TotalCredits,
    int LineCount,
    [property: JsonPropertyName("accountScopeId")] string? AccountScopeId = null,
    [property: JsonPropertyName("accountScopeDisplayName")] string? AccountScopeDisplayName = null,
    [property: JsonPropertyName("entityScopeId")] string? EntityScopeId = null,
    [property: JsonPropertyName("entityScopeDisplayName")] string? EntityScopeDisplayName = null,
    [property: JsonPropertyName("sleeveScopeId")] string? SleeveScopeId = null,
    [property: JsonPropertyName("sleeveScopeDisplayName")] string? SleeveScopeDisplayName = null,
    [property: JsonPropertyName("vehicleScopeId")] string? VehicleScopeId = null,
    [property: JsonPropertyName("vehicleScopeDisplayName")] string? VehicleScopeDisplayName = null);

/// <summary>
/// Comparison row used when reviewing multiple runs side by side.
/// </summary>
public sealed record StrategyRunComparison(
    string RunId,
    string StrategyName,
    StrategyRunMode Mode,
    StrategyRunEngine Engine,
    StrategyRunStatus Status,
    decimal? NetPnl,
    decimal? TotalReturn,
    decimal? FinalEquity,
    decimal? MaxDrawdown,
    double? SharpeRatio,
    int FillCount,
    DateTimeOffset LastUpdatedAt,
    StrategyRunPromotionState PromotionState = StrategyRunPromotionState.None,
    bool HasLedger = false,
    bool HasAuditTrail = false);

/// <summary>Filter options used for workstation run history retrieval.</summary>
public sealed record StrategyRunHistoryQuery(
    IReadOnlyList<StrategyRunMode>? Modes = null,
    StrategyRunStatus? Status = null,
    string? StrategyId = null,
    int Limit = 50);

/// <summary>Merged run timeline entry used by research and trading surfaces.</summary>
public sealed record StrategyRunTimelineEntry(
    string RunId,
    string StrategyId,
    string StrategyName,
    StrategyRunMode Mode,
    StrategyRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset LastUpdatedAt,
    decimal? NetPnl,
    decimal? TotalReturn,
    int FillCount);
/// <summary>
/// Normalized cross-mode run comparison DTO that includes the full set of
/// <c>BacktestMetrics</c> fields plus equity curve data and parentage chain info.
/// Returned by <c>GET /api/strategies/runs/compare?ids=a,b</c>.
/// </summary>
public sealed record RunComparisonDto(
    string RunId,
    string? ParentRunId,
    string StrategyName,
    StrategyRunMode Mode,
    StrategyRunEngine Engine,
    StrategyRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    decimal? NetPnl,
    decimal? TotalReturn,
    decimal? AnnualizedReturn,
    decimal? FinalEquity,
    double? SharpeRatio,
    double? SortinoRatio,
    double? CalmarRatio,
    decimal? MaxDrawdown,
    decimal? MaxDrawdownPercent,
    int MaxDrawdownRecoveryDays,
    double? ProfitFactor,
    double? WinRate,
    int TotalTrades,
    int WinningTrades,
    int LosingTrades,
    int FillCount,
    decimal TotalCommissions,
    decimal TotalMarginInterest,
    decimal TotalShortRebates,
    double? Xirr,
    EquityCurveSummary? EquityCurve,
    DateTimeOffset LastUpdatedAt,
    StrategyRunPromotionState PromotionState = StrategyRunPromotionState.None,
    bool HasLedger = false,
    bool HasAuditTrail = false);

// ---------------------------------------------------------------------------
// Track C drill-in models
// ---------------------------------------------------------------------------

/// <summary>A single point on the portfolio equity curve.</summary>
public sealed record EquityCurvePoint(
    DateOnly Date,
    decimal TotalEquity,
    decimal Cash,
    decimal DailyReturn,
    decimal DrawdownFromPeak,
    decimal DrawdownFromPeakPercent);

/// <summary>Full equity curve with summary drawdown statistics for one run.</summary>
public sealed record EquityCurveSummary(
    string RunId,
    decimal InitialEquity,
    decimal FinalEquity,
    decimal MaxDrawdown,
    decimal MaxDrawdownPercent,
    int MaxDrawdownRecoveryDays,
    double SharpeRatio,
    double SortinoRatio,
    IReadOnlyList<EquityCurvePoint> Points);

/// <summary>A single executed fill from a strategy run.</summary>
public sealed record RunFillEntry(
    Guid FillId,
    Guid OrderId,
    string Symbol,
    long FilledQuantity,
    decimal FillPrice,
    decimal Commission,
    DateTimeOffset FilledAt,
    string? AccountId);

/// <summary>Trade-level fill list for one run.</summary>
public sealed record RunFillSummary(
    string RunId,
    StrategyRunMode Mode,
    int TotalFills,
    decimal TotalCommissions,
    IReadOnlyList<RunFillEntry> Fills);

/// <summary>Per-symbol P&amp;L attribution for one run.</summary>
public sealed record SymbolAttributionEntry(
    string Symbol,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    decimal TotalPnl,
    int TradeCount,
    decimal Commissions,
    decimal MarginInterestAllocated);

/// <summary>Complete attribution breakdown for one run.</summary>
public sealed record RunAttributionSummary(
    string RunId,
    StrategyRunMode Mode,
    decimal TotalRealizedPnl,
    decimal TotalUnrealizedPnl,
    decimal TotalCommissions,
    IReadOnlyList<SymbolAttributionEntry> BySymbol);

// ---------------------------------------------------------------------------
// Cash-flow projection models
// ---------------------------------------------------------------------------

/// <summary>A single historical cash-flow entry from a strategy run.</summary>
public sealed record CashFlowEntryDto(
    DateTimeOffset Timestamp,
    decimal Amount,
    string EventKind,
    string? Symbol,
    string Currency,
    string? AccountId,
    string? Description);

/// <summary>A single time-bucket within a projected cash ladder.</summary>
public sealed record CashLadderBucketDto(
    DateTimeOffset BucketStart,
    DateTimeOffset BucketEnd,
    decimal ProjectedInflows,
    decimal ProjectedOutflows,
    decimal NetFlow,
    string Currency,
    int EventCount);

/// <summary>Time-bucketed forward view of projected cash flows for one run.</summary>
public sealed record RunCashLadder(
    DateTimeOffset AsOf,
    string Currency,
    int BucketDays,
    decimal TotalProjectedInflows,
    decimal TotalProjectedOutflows,
    decimal NetPosition,
    IReadOnlyList<CashLadderBucketDto> Buckets);

/// <summary>
/// Cash-flow projection summary for a strategy run.
/// <para>Includes both the raw historical entries and a time-bucketed cash ladder
/// computed by the F# <c>CashLadder.build</c> module.</para>
/// </summary>
public sealed record RunCashFlowSummary(
    string RunId,
    DateTimeOffset AsOf,
    string Currency,
    int TotalEntries,
    decimal TotalInflows,
    decimal TotalOutflows,
    decimal NetCashFlow,
    IReadOnlyList<CashFlowEntryDto> Entries,
    RunCashLadder Ladder);

/// <summary>
/// Composite portfolio drill-in payload that keeps attribution, drawdown, cash-flow, and trade slices
/// aligned under one additive, versioned contract.
/// </summary>
public sealed record RunPortfolioDrillInSummary(
    string SchemaVersion,
    string RunId,
    DateTimeOffset AsOf,
    string Currency,
    StrategyRunMode Mode,
    RunAttributionSummary? Attribution,
    EquityCurveSummary? DrawdownProfile,
    RunCashFlowSummary? CashFlow,
    RunFillSummary? Trades);

/// <summary>
/// Lightweight run identity used to connect research, trading, and governance flows.
/// </summary>
public sealed record StrategyRunContinuityLink(
    string RunId,
    string StrategyId,
    string StrategyName,
    StrategyRunMode Mode,
    StrategyRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    StrategyRunPromotionState PromotionState,
    string? FundProfileId = null,
    string? FundDisplayName = null);

/// <summary>
/// Parent/child run linkage used by shared continuity drill-ins.
/// </summary>
public sealed record StrategyRunContinuityLineage(
    string? ParentRunId,
    StrategyRunContinuityLink? ParentRun,
    IReadOnlyList<StrategyRunContinuityLink> ChildRuns);

/// <summary>
/// Compact cash-flow digest attached to a continuity drill-in.
/// </summary>
public sealed record StrategyRunCashFlowDigest(
    DateTimeOffset AsOf,
    string Currency,
    int TotalEntries,
    decimal TotalInflows,
    decimal TotalOutflows,
    decimal NetCashFlow,
    decimal ProjectedNetPosition,
    int BucketCount,
    DateTimeOffset? NextBucketStart = null,
    DateTimeOffset? NextBucketEnd = null,
    decimal? NextBucketNetFlow = null);

/// <summary>
/// Machine-readable continuity warning for shared run-centered workflows.
/// Known warning codes include:
/// missing-portfolio, missing-ledger, missing-cash-flow, missing-reconciliation, as-of-drift,
/// open-reconciliation-breaks, security-coverage, lineage-parent-source-mismatch,
/// lineage-missing-parent-with-source, promotion-target-run-missing, and
/// promotion-lineage-shape-inconsistent.
/// </summary>
public sealed record StrategyRunContinuityWarning(
    string Code,
    StrategyRunContinuityWarningSeverity Severity,
    string Message,
    string SourceSeam);

[JsonConverter(typeof(JsonStringEnumConverter<StrategyRunContinuityWarningSeverity>))]
public enum StrategyRunContinuityWarningSeverity : byte
{
    Info,
    Warning,
    Critical
}

[JsonConverter(typeof(JsonStringEnumConverter<StrategyRunContinuitySeamHealthStatus>))]
public enum StrategyRunContinuitySeamHealthStatus : byte
{
    Healthy,
    Missing,
    Stale
}

/// <summary>
/// Continuity posture across run, portfolio, ledger, cash-flow, and reconciliation seams.
/// </summary>
public sealed record StrategyRunContinuityStatus(
    bool HasRun,
    StrategyRunContinuitySeamHealthStatus RunHealth,
    bool HasFills,
    StrategyRunContinuitySeamHealthStatus FillsHealth,
    bool HasPortfolio,
    StrategyRunContinuitySeamHealthStatus PortfolioHealth,
    bool HasLedger,
    StrategyRunContinuitySeamHealthStatus LedgerHealth,
    bool HasCashFlow,
    StrategyRunContinuitySeamHealthStatus CashFlowHealth,
    bool HasReconciliation,
    StrategyRunContinuitySeamHealthStatus ReconciliationHealth,
    int AsOfDriftMinutes,
    int OpenReconciliationBreaks,
    int SecurityCoverageIssueCount,
    bool HasWarnings,
    IReadOnlyList<StrategyRunContinuityWarning> Warnings);

/// <summary>
/// Canonical shared continuity drill-in contract used by Research, Trading, and Governance run drill-ins.
/// </summary>
public record StrategyRunContinuityDto(
    StrategyRunDetail Run,
    StrategyRunContinuityLineage Lineage,
    StrategyRunCashFlowDigest? CashFlow,
    ReconciliationRunSummary? Reconciliation,
    StrategyRunContinuityStatus ContinuityStatus);

/// <summary>
/// Backward-compatible alias for <see cref="StrategyRunContinuityDto"/>.
/// </summary>
public sealed record StrategyRunContinuityDetail(
    StrategyRunDetail Run,
    StrategyRunContinuityLineage Lineage,
    StrategyRunCashFlowDigest? CashFlow,
    ReconciliationRunSummary? Reconciliation,
    StrategyRunContinuityStatus ContinuityStatus)
    : StrategyRunContinuityDto(Run, Lineage, CashFlow, Reconciliation, ContinuityStatus);

// ---------------------------------------------------------------------------
// Lot-level tracking read models
// ---------------------------------------------------------------------------

/// <summary>
/// Workstation-facing summary of a single open lot for a strategy run.
/// </summary>
public sealed record OpenLotSummary(
    Guid LotId,
    string Symbol,
    long Quantity,
    decimal EntryPrice,
    DateTimeOffset OpenedAt,
    decimal CurrentUnrealizedPnl,
    bool IsLongTerm,
    string? AccountId = null);

/// <summary>
/// Workstation-facing summary of a closed lot for a strategy run.
/// </summary>
public sealed record ClosedLotSummary(
    Guid LotId,
    string Symbol,
    long Quantity,
    decimal EntryPrice,
    decimal ClosePrice,
    DateTimeOffset OpenedAt,
    DateTimeOffset ClosedAt,
    decimal RealizedPnl,
    bool IsLongTerm,
    string? AccountId = null);

/// <summary>
/// Lot history for a single strategy run, optionally filtered to one symbol.
/// </summary>
public sealed record RunLotSummary(
    string RunId,
    int TotalOpenLots,
    int TotalClosedLots,
    decimal TotalRealizedPnl,
    IReadOnlyList<OpenLotSummary> OpenLots,
    IReadOnlyList<ClosedLotSummary> ClosedLots);
