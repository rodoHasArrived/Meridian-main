using Meridian.Contracts.Configuration;

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
/// <c>ActiveWorkspace</c> uses the canonical browser shell roots:
/// trading, portfolio, accounting, reporting, strategy, data, or settings.
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
// /api/workstation/strategy (legacy alias: /api/workstation/research)
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
/// PlotTool tab strip state attached to the strategy/research payload.
/// </summary>
public sealed record WorkstationPlotToolTabState(
    string Id,
    string Label,
    string TabId,
    string PanelId,
    bool Selected,
    string ButtonVariant,
    int TabIndex,
    string AriaLabel);

/// <summary>
/// PlotTool workspace/statistics payload embedded in research responses.
/// </summary>
public sealed record WorkstationPlotToolPayload(
    object Workspace,
    object Statistics,
    IReadOnlyList<object> Studies,
    IReadOnlyList<WorkstationPlotToolTabState> Tabs,
    string ActiveView = "workspace");

/// <summary>
/// Typed payload returned by <c>GET /api/workstation/strategy</c>.
/// Run cards and comparisons retain <c>object</c> pending their own DTO evolution.
/// </summary>
public sealed record WorkstationResearchPayload(
    IReadOnlyList<WorkstationMetricCard> Metrics,
    IReadOnlyList<object> Runs,
    IReadOnlyList<WorkstationModeComparisonGroup> Comparisons,
    IReadOnlyList<WorkstationTimelineCard> Timeline,
    WorkstationResearchWorkspaceSummary Workspace,
    WorkstationPlotToolPayload? PlotTool = null);

// ---------------------------------------------------------------------------
// /api/workstation/trading
// ---------------------------------------------------------------------------

/// <summary>
/// A single position row shown in the trading workstation positions table.
/// </summary>
public sealed record WorkstationTradingPositionRow(
    string PositionKey,
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
// /api/workstation/accounting and /api/workstation/reporting (legacy alias: /api/workstation/governance)
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
/// A single export/reporting profile shown in the Reporting workspace.
/// Matches <c>GovernanceReportingProfile</c> in the frontend types.
/// </summary>
public sealed record WorkstationGovernanceReportingProfilePayload(
    string Id,
    string Name,
    string TargetTool,
    string Format,
    string Description,
    bool LoaderScript,
    bool DataDictionary);

/// <summary>
/// Typed reporting summary embedded inside <see cref="WorkstationGovernancePayload"/>.
/// Replaces the anonymous-object placeholder introduced in the initial governance
/// payload commit (PR-03 follow-on).
/// </summary>
public sealed record WorkstationGovernanceReportingPayload(
    int ProfileCount,
    IReadOnlyList<string> RecommendedProfiles,
    IReadOnlyList<WorkstationGovernanceReportingProfilePayload> Profiles,
    IReadOnlyList<string> ReportPackTargets,
    string Summary);

/// <summary>
/// Typed payload returned by <c>GET /api/workstation/accounting</c> and
/// <c>GET /api/workstation/reporting</c>.
/// <c>ReconciliationQueue</c>, <c>BreakQueue</c>, <c>CashFlow</c>, and
/// <c>KernelObservability</c> are kept as <c>object</c> / <c>IReadOnlyList&lt;object&gt;</c>
/// pending their own DTO evolution in a follow-on PR.
/// </summary>
public sealed record WorkstationGovernancePayload(
    IReadOnlyList<WorkstationMetricCard> Metrics,
    IReadOnlyList<object> ReconciliationQueue,
    IReadOnlyList<object> BreakQueue,
    WorkstationGovernanceWorkspaceSummary Workspace,
    object CashFlow,
    WorkstationGovernanceReportingPayload Reporting,
    object ControlCenter,
    object KernelObservability);

// ---------------------------------------------------------------------------
// /api/workstation/portfolio
// ---------------------------------------------------------------------------

/// <summary>
/// A single run linked to the portfolio view — lightweight digest for
/// the portfolio run-linked equity panel.
/// </summary>
public sealed record WorkstationPortfolioRunRow(
    string RunId,
    string StrategyName,
    string Engine,
    string Mode,
    string Status,
    string Pnl,
    string Sharpe,
    string Dataset,
    string Window,
    string LastUpdated,
    string Notes,
    string? PromotionState);

/// <summary>
/// Unified payload returned by <c>GET /api/workstation/portfolio</c>.
/// Aggregates paper positions, brokerage wiring state, run-linked equity,
/// and cash-flow summary so the Portfolio workspace needs a single request.
/// </summary>
public sealed record WorkstationPortfolioPayload(
    IReadOnlyList<WorkstationMetricCard> Metrics,
    IReadOnlyList<WorkstationTradingPositionRow> Positions,
    WorkstationTradingRiskState Risk,
    WorkstationTradingBrokerageState Brokerage,
    IReadOnlyList<WorkstationPortfolioRunRow> Runs,
    object? CashFlow);



public sealed record WorkstationPortfolioSummaryTelemetry(
    long RefreshLatencyMs,
    int PayloadSizeBytes,
    bool IsStale,
    string? StaleReason,
    string AsOfUtc);

public sealed record WorkstationPortfolioSummaryPayload(
    string FundAccountId,
    string StrategyId,
    string Entity,
    IReadOnlyList<WorkstationMetricCard> ConsolidatedCards,
    IReadOnlyList<WorkstationTradingPositionRow> Positions,
    WorkstationTradingRiskState Risk,
    WorkstationPortfolioSummaryTelemetry Telemetry,
    IReadOnlyDictionary<string, string> DrillThroughRoutes);

// ---------------------------------------------------------------------------
// /api/workstation/data and /api/workstation/data-operations
// ---------------------------------------------------------------------------

/// <summary>
/// Compact provider diagnostic row embedded in the Data workspace provider center.
/// </summary>
public sealed record WorkstationDataProviderDiagnostic(
    string Id,
    string Label,
    string Status,
    string StatusLabel,
    string Detail);

/// <summary>
/// Compact routing summary embedded in a Data workspace provider row.
/// </summary>
public sealed record WorkstationDataProviderRoutingSummary(
    string? ConnectionId,
    string? ProviderFamilyId,
    bool? ProductionReady,
    bool? CertificationFresh,
    int BindingCount,
    int FallbackRouteCount,
    string? HealthStatus);

/// <summary>
/// Provider-center row returned by the Data workspace bootstrap payload.
/// </summary>
public sealed record WorkstationDataProviderRecord(
    string ProviderId,
    string DisplayName,
    string Status,
    string Capability,
    string Latency,
    string Note,
    string TrustScore,
    string SignalSource,
    string ReasonCode,
    string RecommendedAction,
    string GateImpact,
    ProviderConnectionRowDto? ConnectionSummary,
    WorkstationDataProviderRoutingSummary? RoutingSummary,
    IReadOnlyList<WorkstationDataProviderDiagnostic> Diagnostics)
{
    public string Provider => ProviderId;
}

/// <summary>
/// Backfill row returned by the Data workspace bootstrap payload.
/// </summary>
public sealed record WorkstationDataBackfillRecord(
    string JobId,
    string Scope,
    string Provider,
    string Status,
    string Progress,
    string UpdatedAt);

/// <summary>
/// Export row returned by the Data workspace bootstrap payload.
/// </summary>
public sealed record WorkstationDataExportRecord(
    string ExportId,
    string Profile,
    string Target,
    string Status,
    string Rows,
    string UpdatedAt);

/// <summary>
/// Typed payload returned by <c>GET /api/workstation/data</c> and
/// <c>GET /api/workstation/data-operations</c>.
/// </summary>
public sealed record WorkstationDataOperationsPayload(
    IReadOnlyList<WorkstationMetricCard> Metrics,
    IReadOnlyList<WorkstationDataProviderRecord> Providers,
    IReadOnlyList<WorkstationDataBackfillRecord> Backfills,
    IReadOnlyList<WorkstationDataExportRecord> Exports,
    object KernelObservability);
