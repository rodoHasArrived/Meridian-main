namespace Meridian.Contracts.Workstation;

// ── PR-03: Typed workstation bootstrap payload DTOs ─────────────────────────
//
// These records replace the anonymous-object returns in WorkstationEndpoints.cs,
// giving the bootstrap API surface a stable, testable, and governance-ready shape.
// Follow the positional-record pattern used in ResearchBriefingDtos.cs.
// Sub-objects that are still complex/in-flight use object? as a migration placeholder.

// ---------------------------------------------------------------------------
// Shared building blocks
// ---------------------------------------------------------------------------

/// <summary>
/// A single KPI card shown in a workstation dashboard header strip.
/// </summary>
public sealed record WorkstationMetricCard(
    string Id,
    string Label,
    string Value,
    string Delta = "0%",
    string Tone = "default");

/// <summary>
/// Minimal run digest attached to session and research payloads.
/// Fields match <c>BuildRunDigest</c> in WorkstationEndpoints.
/// </summary>
public sealed record WorkstationRunDigest(
    string RunId,
    string StrategyName,
    string Mode,
    string Status,
    string LastUpdated,
    bool HasLedger,
    bool HasPortfolio,
    object? SecurityCoverage = null);

/// <summary>
/// Per-strategy cross-mode comparison group used by research and trading surfaces.
/// </summary>
public sealed record WorkstationModeComparisonGroup(
    string StrategyName,
    IReadOnlyList<object> Modes);

/// <summary>
/// Single entry in a run timeline strip.
/// Fields match <c>BuildTimelineCard</c> in WorkstationEndpoints.
/// </summary>
public sealed record WorkstationTimelineCard(
    string RunId,
    string StrategyName,
    string Mode,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset LastUpdatedAt,
    decimal? TotalReturn);

// ---------------------------------------------------------------------------
// /api/workstation/session
// ---------------------------------------------------------------------------

/// <summary>
/// Workspace-level summary embedded inside <see cref="WorkstationSessionPayload"/>.
/// </summary>
public sealed record WorkstationSessionWorkspaceSummary(
    int TotalRuns,
    int ActiveRuns,
    int ReviewRuns,
    int LedgerCoverage,
    int PortfolioCoverage);

/// <summary>
/// Typed payload returned by <c>GET /api/workstation/session</c>.
/// </summary>
public sealed record WorkstationSessionPayload(
    string DisplayName,
    string Role,
    string Environment,
    string ActiveWorkspace,
    int CommandCount,
    WorkstationRunDigest? LatestRun,
    WorkstationSessionWorkspaceSummary WorkspaceSummary);

// ---------------------------------------------------------------------------
// /api/workstation/research
// ---------------------------------------------------------------------------

/// <summary>
/// Workspace-level summary embedded inside <see cref="WorkstationResearchPayload"/>.
/// </summary>
public sealed record WorkstationResearchWorkspaceSummary(
    int TotalRuns,
    string? LatestRunId,
    string? LatestStrategyName,
    bool HasLedgerCoverage,
    bool HasPortfolioCoverage,
    int PromotionCandidates);

/// <summary>
/// Typed payload returned by <c>GET /api/workstation/research</c>.
/// Run cards and comparisons retain <c>object</c> pending their own DTO evolution.
/// </summary>
public sealed record WorkstationResearchPayload(
    IReadOnlyList<WorkstationMetricCard> Metrics,
    IReadOnlyList<object> Runs,
    IReadOnlyList<WorkstationModeComparisonGroup> Comparisons,
    IReadOnlyList<WorkstationTimelineCard> Timeline,
    WorkstationResearchWorkspaceSummary Workspace);

// ---------------------------------------------------------------------------
// /api/workstation/trading
// ---------------------------------------------------------------------------

/// <summary>
/// A single position row shown in the trading workstation positions table.
/// </summary>
public sealed record WorkstationTradingPositionRow(
    string Symbol,
    string Side,
    string Quantity,
    string AveragePrice,
    string MarkPrice,
    string DayPnl,
    string UnrealizedPnl,
    string Exposure);

/// <summary>
/// A single open-order row shown in the trading workstation orders table.
/// </summary>
public sealed record WorkstationTradingOrderRow(
    string OrderId,
    string Symbol,
    string Side,
    string Type,
    string Quantity,
    string LimitPrice,
    string Status,
    string SubmittedAt);

/// <summary>
/// A single fill row shown in the trading workstation fills table.
/// </summary>
public sealed record WorkstationTradingFillRow(
    string FillId,
    string OrderId,
    string Symbol,
    string Side,
    string Quantity,
    string Price,
    string Venue,
    string Timestamp);

/// <summary>
/// Risk state block embedded inside the trading payload.
/// </summary>
public sealed record WorkstationTradingRiskState(
    string State,
    string Summary,
    string NetExposure,
    string GrossExposure,
    string Var95,
    string MaxDrawdown,
    string BuyingPowerUsed,
    IReadOnlyList<string> ActiveGuardrails);

/// <summary>
/// Brokerage connection summary embedded inside the trading payload.
/// </summary>
public sealed record WorkstationTradingBrokerageState(
    string Provider,
    string Account,
    string Environment,
    string Connection,
    string LastHeartbeat,
    string OrderIngress,
    string FillFeed,
    IReadOnlyList<string> Notes);

/// <summary>
/// Typed payload returned by <c>GET /api/workstation/trading</c>.
/// <c>Readiness</c> is <see cref="TradingOperatorReadinessDto"/>; typed at the endpoint layer
/// to avoid a circular project reference.
/// </summary>
public sealed record WorkstationTradingPayload(
    IReadOnlyList<WorkstationMetricCard> Metrics,
    IReadOnlyList<WorkstationTradingPositionRow> Positions,
    IReadOnlyList<WorkstationTradingOrderRow> OpenOrders,
    IReadOnlyList<WorkstationTradingFillRow> Fills,
    WorkstationTradingRiskState Risk,
    WorkstationTradingBrokerageState Brokerage,
    object Readiness,
    IReadOnlyList<WorkstationModeComparisonGroup> Comparisons,
    object? DrillIn);

// ---------------------------------------------------------------------------
// /api/workstation/governance
// ---------------------------------------------------------------------------

/// <summary>
/// Workspace-level summary embedded inside <see cref="WorkstationGovernancePayload"/>.
/// </summary>
public sealed record WorkstationGovernanceWorkspaceSummary(
    int TotalRuns,
    int ReconciledRuns,
    int LedgerReadyRuns,
    int OpenBreaks,
    int SecurityIssues);

/// <summary>
/// Typed payload returned by <c>GET /api/workstation/governance</c>.
/// <c>ReconciliationQueue</c>, <c>BreakQueue</c>, <c>CashFlow</c>, <c>Reporting</c>, and
/// <c>KernelObservability</c> are kept as <c>object</c> / <c>IReadOnlyList&lt;object&gt;</c>
/// pending their own DTO evolution in a follow-on PR.
/// </summary>
public sealed record WorkstationGovernancePayload(
    IReadOnlyList<WorkstationMetricCard> Metrics,
    IReadOnlyList<object> ReconciliationQueue,
    IReadOnlyList<object> BreakQueue,
    WorkstationGovernanceWorkspaceSummary Workspace,
    object CashFlow,
    object Reporting,
    object KernelObservability);
