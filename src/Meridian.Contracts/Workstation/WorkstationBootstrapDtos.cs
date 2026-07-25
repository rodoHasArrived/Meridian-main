using Meridian.Contracts.Configuration;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.Workstation;

// ── PR-03: Typed workstation bootstrap payload DTOs ─────────────────────────
//
// These records replace the anonymous-object returns in WorkstationEndpoints.cs,
// giving the bootstrap API surface a stable, testable, and governance-ready shape.
// Follow the positional-record pattern used in StrategyBriefingDtos.cs.

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
/// Shared drill-in links attached to strategy, trading, and accounting run records.
/// </summary>
public sealed record WorkstationRunDrillInLinks(
    string EquityCurve,
    string Fills,
    string Attribution,
    string? Ledger,
    string CashFlows,
    string Continuity,
    string Comparison);

public sealed record WorkstationSecurityCoverageReferencePayload(
    string Source,
    string Symbol,
    string? AccountName,
    string? SecurityId,
    string DisplayName,
    string? AssetClass,
    string? SubType,
    string? Currency,
    string? Status,
    string? PrimaryIdentifier,
    string CoverageStatus,
    string? CoverageReason,
    string? MatchedIdentifierKind,
    string? MatchedIdentifierValue,
    string? MatchedProvider);

public sealed record WorkstationSecurityCoverageGapPayload(
    string Source,
    string Symbol,
    string? AccountName,
    string Reason);

public sealed record WorkstationSecurityCoveragePayload(
    int PortfolioResolved,
    int PortfolioMissing,
    int LedgerResolved,
    int LedgerMissing,
    bool HasIssues,
    string Tone,
    string Summary,
    IReadOnlyList<WorkstationSecurityCoverageReferencePayload> ResolvedReferences,
    IReadOnlyList<WorkstationSecurityCoverageGapPayload> MissingReferences);

public sealed record WorkstationStrategyRunCard(
    string Id,
    string StrategyName,
    string Engine,
    string Mode,
    string Status,
    string Dataset,
    string Window,
    string Pnl,
    string Sharpe,
    string LastUpdated,
    string Notes,
    string? PromotionState,
    string? LedgerReference,
    string? PortfolioId,
    decimal? NetPnl,
    decimal? TotalReturn,
    decimal? FinalEquity,
    WorkstationSecurityCoveragePayload SecurityCoverage,
    WorkstationRunDrillInLinks DrillIn,
    BiasDisclosureDto? BiasDisclosure = null);

public sealed record WorkstationModeComparisonRun(
    string RunId,
    string Mode,
    string Status,
    decimal? NetPnl,
    decimal? TotalReturn,
    WorkstationRunDrillInLinks DrillIn);

/// <summary>
/// Minimal run digest attached to session and strategy payloads.
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
    WorkstationSecurityCoveragePayload SecurityCoverage);

/// <summary>
/// Per-strategy cross-mode comparison group used by strategy and trading surfaces.
/// </summary>
public sealed record WorkstationModeComparisonGroup(
    string StrategyName,
    IReadOnlyList<WorkstationModeComparisonRun> Modes);

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
/// Workspace-level summary embedded inside <see cref="WorkstationStrategyPayload"/>.
/// </summary>
public sealed record WorkstationStrategyWorkspaceSummary(
    int TotalRuns,
    string? LatestRunId,
    string? LatestStrategyName,
    bool HasLedgerCoverage,
    bool HasPortfolioCoverage,
    int PromotionCandidates);

/// <summary>
/// PlotTool tab strip state attached to the strategy payload.
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
/// PlotTool workspace/statistics payload embedded in strategy responses.
/// </summary>
public sealed record WorkstationPlotToolTickPayload(
    int Value,
    string Label);

public sealed record WorkstationPlotToolPointPayload(
    int X,
    int Y,
    bool Emphasis);

public sealed record WorkstationPlotToolSummaryItemPayload(
    string Id,
    string Label,
    string Value);

public sealed record WorkstationPlotToolLegendItemPayload(
    string Id,
    string Label,
    string Detail,
    string Tone);

public sealed record WorkstationPlotToolFocusPointPayload(
    string Label,
    string XValueText,
    string YValueText,
    string Detail);

public sealed record WorkstationPlotToolSignalCardPayload(
    string Id,
    string Label,
    string Value,
    string Detail,
    string Tone);

public sealed record WorkstationPlotToolWorkspacePayload(
    string Eyebrow,
    string Title,
    string Description,
    string StatusBadgeLabel,
    string StatusBadgeVariant,
    string Expression,
    IReadOnlyList<string> ToolbarPills,
    IReadOnlyList<string> MetaItems,
    string XAxisLabel,
    string YAxisLabel,
    IReadOnlyList<WorkstationPlotToolTickPayload> XTicks,
    IReadOnlyList<WorkstationPlotToolTickPayload> YTicks,
    IReadOnlyList<WorkstationPlotToolPointPayload> Points,
    IReadOnlyList<WorkstationPlotToolSummaryItemPayload> StudySummary,
    IReadOnlyList<WorkstationPlotToolLegendItemPayload> LegendItems,
    WorkstationPlotToolFocusPointPayload FocusPoint,
    IReadOnlyList<WorkstationPlotToolSignalCardPayload> SignalCards,
    string ConsoleTitle,
    string ConsoleBody,
    string OverlayTitle,
    IReadOnlyList<string> OverlayItems);

public sealed record WorkstationPlotToolSummaryTilePayload(
    string Id,
    string Label,
    string Value,
    string Detail,
    string Tone);

public sealed record WorkstationPlotToolMomentPayload(
    string Id,
    string Label,
    string Value,
    string Benchmark);

public sealed record WorkstationPlotToolRegressionPayload(
    string Equation,
    IReadOnlyList<string> DetailItems);

public sealed record WorkstationPlotToolSampleRowPayload(
    string Id,
    string Timestamp,
    string SpreadText,
    string ImpliedVolText,
    string ZScoreText,
    string SignalText,
    string Tone);

public sealed record WorkstationPlotToolStatisticsPayload(
    string Eyebrow,
    string Title,
    string Description,
    IReadOnlyList<WorkstationPlotToolSummaryTilePayload> SummaryTiles,
    IReadOnlyList<int> DistributionBars,
    string DistributionSummary,
    string DistributionFootnote,
    IReadOnlyList<WorkstationPlotToolMomentPayload> Moments,
    WorkstationPlotToolRegressionPayload Regression,
    IReadOnlyList<WorkstationPlotToolSampleRowPayload> SampleRows);

public sealed record WorkstationPlotToolStudyPayload(
    string Id,
    string Title,
    string Subtitle,
    string StatusText,
    string StatusBadgeLabel,
    string StatusBadgeVariant,
    string MetricText,
    string NoteText,
    bool IsActive);

public sealed record WorkstationPlotToolPayload(
    WorkstationPlotToolWorkspacePayload Workspace,
    WorkstationPlotToolStatisticsPayload Statistics,
    IReadOnlyList<WorkstationPlotToolStudyPayload> Studies,
    IReadOnlyList<WorkstationPlotToolTabState> Tabs,
    string ActiveView = "workspace");

/// <summary>
/// Typed payload returned by <c>GET /api/workstation/strategy</c>.
/// </summary>
public sealed record WorkstationStrategyPayload(
    IReadOnlyList<WorkstationMetricCard> Metrics,
    IReadOnlyList<WorkstationStrategyRunCard> Runs,
    IReadOnlyList<WorkstationModeComparisonGroup> Comparisons,
    IReadOnlyList<WorkstationTimelineCard> Timeline,
    WorkstationStrategyWorkspaceSummary Workspace,
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
    TradingOperatorReadinessDto Readiness,
    IReadOnlyList<WorkstationModeComparisonGroup> Comparisons,
    WorkstationRunDrillInLinks? DrillIn);

// ---------------------------------------------------------------------------
// /api/workstation/accounting and /api/workstation/reporting (legacy alias: /api/workstation/governance)
// ---------------------------------------------------------------------------

/// <summary>
/// Workspace-level summary embedded inside <see cref="WorkstationAccountingPayload"/>.
/// </summary>
public sealed record WorkstationAccountingWorkspaceSummary(
    int TotalRuns,
    int ReconciledRuns,
    int LedgerReadyRuns,
    int OpenBreaks,
    int SecurityIssues);

/// <summary>
/// A single export/reporting profile shown in the Reporting workspace.
/// Matches the Reporting profile type in browser workstation models.
/// </summary>
public sealed record WorkstationReportingProfilePayload(
    string Id,
    string Name,
    string TargetTool,
    string Format,
    string Description,
    bool LoaderScript,
    bool DataDictionary);

/// <summary>
/// Template metadata exposed to browser and desktop Reporting operator surfaces.
/// </summary>
public sealed record WorkstationReportWriterMetricPayload(
    string Name,
    string SourceField,
    string Function,
    string? Label = null);

public sealed record WorkstationReportWriterFormulaPayload(
    string Name,
    string Expression,
    string? Label = null);

public sealed record WorkstationReportWriterFilterPayload(
    string Field,
    string Operator,
    string? Value = null,
    string? Label = null);

public sealed record WorkstationReportWriterFieldPayload(
    string Name,
    string Label,
    string Role,
    string DataType,
    string Dataset,
    string? Description = null);

public sealed record WorkstationReportWriterDatasetSourcePayload(
    string SourceId,
    string Label,
    string Description,
    int RowCount,
    IReadOnlyList<WorkstationReportWriterFieldPayload> Fields,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Rows,
    IReadOnlyList<string>? Tags = null,
    string? CertificationState = null,
    string? ValidationState = null,
    string? ReconciliationState = null,
    string? RefreshCadence = null,
    string? Owner = null,
    string? Version = null,
    string? ReleaseApproval = null,
    string? LineageManifest = null,
    IReadOnlyList<string>? SourceRunIds = null,
    IReadOnlyList<string>? PermittedConsumers = null,
    string? RowLineageKeyField = null,
    string? EvidenceIndexField = null);

public sealed record WorkstationReportWriterGridPayload(
    string GridId,
    string Title,
    string Kind,
    int DimensionCount,
    int MetricCount,
    int FormulaCount,
    IReadOnlyList<string>? RowFields = null,
    IReadOnlyList<string>? ColumnFields = null,
    IReadOnlyList<WorkstationReportWriterMetricPayload>? Metrics = null,
    IReadOnlyList<WorkstationReportWriterFormulaPayload>? Formulas = null,
    int? TopN = null,
    string? SortBy = null,
    bool SortDescending = true,
    IReadOnlyList<WorkstationReportWriterFilterPayload>? Filters = null,
    IReadOnlyList<WorkstationReportWriterFieldPayload>? SourceFields = null);

public sealed record WorkstationReportingTemplatePayload(
    string TemplateId,
    string Family,
    string Name,
    string Version,
    IReadOnlyList<string> Sections,
    string LifecycleStatus = "Approved",
    bool IsBuiltIn = true,
    bool IsLatestApproved = true,
    string ApprovalSummary = "Built-in approved template",
    string AuthoringRoute = "/api/fund-structure/reporting/templates",
    IReadOnlyList<WorkstationReportWriterGridPayload>? ReportWriterGrids = null,
    string AccessMode = "CompanyWide",
    string AccessSummary = "Company-wide access",
    bool IsAccessible = true,
    string? CreatedBy = null,
    DateTimeOffset? CreatedAt = null,
    string? UpdatedBy = null,
    DateTimeOffset? UpdatedAt = null,
    string? SubmittedBy = null,
    DateTimeOffset? SubmittedAt = null,
    string? ApprovedBy = null,
    DateTimeOffset? ApprovedAt = null,
    string? RejectedBy = null,
    DateTimeOffset? RejectedAt = null,
    string? DecisionRationale = null,
    string? ApprovalReference = null,
    VersionedReportTemplateIdDto? BasedOnTemplateId = null,
    IReadOnlyList<ReportTemplateAuditEventDto>? AuditTrail = null,
    IReadOnlyList<string>? ValidationIssues = null,
    IReadOnlyList<ReportTemplateParameterDefinitionDto>? Parameters = null);

/// <summary>
/// Lightweight reporting run status with lineage and approval posture for operator surfaces.
/// </summary>
public sealed record WorkstationReportingRunLinkPayload(
    string Id,
    string Kind,
    string Label,
    string Href,
    string Method,
    bool IsBrowserNavigable,
    string Source);

public sealed record WorkstationReportingRunNextActionPayload(
    string Id,
    string Kind,
    string Label,
    string Href,
    string Method,
    bool IsEnabled,
    string? DisabledReason,
    bool IsBrowserNavigable);

public sealed record WorkstationGeneratedReportWriterGridPayload(
    string GridId,
    string Title,
    string Kind,
    string Artifact,
    int DimensionCount,
    int MetricCount,
    int FormulaCount,
    string? ValidationSummary = null,
    int? ValidationPassedCount = null,
    int? ValidationWarningCount = null,
    int? ValidationFailedCount = null);

public sealed record WorkstationReportingRunPayload(
    string RunId,
    string TemplateId,
    string Family,
    string Status,
    string Trigger,
    string AsOfDate,
    int AttemptCount,
    int SectionCount,
    int LineageLinkedSections,
    IReadOnlyList<string> Artifacts,
    IReadOnlyList<string> AuditActions,
    string? FailureReason,
    IReadOnlyList<WorkstationReportingRunLinkPayload>? DrilldownLinks = null,
    IReadOnlyList<WorkstationReportingRunNextActionPayload>? NextActions = null,
    IReadOnlyList<WorkstationGeneratedReportWriterGridPayload>? GeneratedReportWriterGrids = null,
    string? ReportWriterDatasetSourceId = null,
    string? ReportWriterDatasetSourceLabel = null,
    int? ReportWriterDatasetRowCount = null,
    string? RunSeriesId = null,
    int? RunAttemptOrdinal = null,
    string? PriorRunId = null,
    string? RetryReason = null,
    string? LatestGeneratedRunId = null,
    string? LatestApprovedRunId = null,
    bool? IsLatestGenerated = null,
    bool? IsLatestApproved = null,
    string? ComparisonSummary = null,
    int? ChangedLineCount = null,
    int? AddedLineCount = null,
    int? RemovedLineCount = null,
    VersionedReportTemplateIdDto? ResolvedTemplate = null,
    ReportingRunParametersDto? ResolvedParameters = null,
    ReportingRunReadinessDto? Readiness = null);

/// <summary>
/// Daily reporting-work item surfaced in the workstation cockpit for operator triage.
/// </summary>
public sealed record WorkstationReportingDailyWorkItemDto(
    string WorkItemId,
    string Kind,
    string Title,
    string StatusLabel,
    string Detail,
    string Tone,
    string Owner,
    DateTimeOffset? DueAtUtc,
    string PrimaryActionLabel,
    string? PrimaryActionHref,
    IReadOnlyList<string>? EvidenceGaps = null,
    IReadOnlyList<string>? Context = null,
    string? SecondaryActionLabel = null,
    string? SecondaryActionHref = null);

/// <summary>
/// Recipient-level distribution posture for governed report-pack output.
/// </summary>
public sealed record WorkstationReportPackDistributionPayload(
    string DistributionId,
    string Recipient,
    string RecipientRole,
    string Channel,
    string State,
    int PendingItems,
    string PendingSummary,
    string Owner,
    DateTimeOffset? DueAtUtc,
    DateTimeOffset? LastSentAtUtc,
    string Route);

/// <summary>
/// Aggregate access-policy evidence for the reporting payload visible to the current caller.
/// </summary>
public sealed record WorkstationReportAccessAuditSummaryDto(
    string EvaluationScope,
    string Summary,
    IReadOnlyList<string> PrincipalScopes,
    int VisibleTemplateCount,
    int HiddenTemplateCount,
    int VisibleReportPackCount,
    int HiddenReportPackCount,
    int VisibleScheduleCount,
    int HiddenScheduleCount,
    int VisibleDeliveryAttemptCount,
    int HiddenDeliveryAttemptCount,
    int VisibleStructuredExportCount,
    int HiddenStructuredExportCount,
    IReadOnlyList<string> DenialReasons);

/// <summary>
/// Typed reporting summary embedded inside <see cref="WorkstationAccountingPayload"/>.
/// </summary>
public sealed record WorkstationReportingPayload(
    int ProfileCount,
    IReadOnlyList<string> RecommendedProfiles,
    IReadOnlyList<WorkstationReportingProfilePayload> Profiles,
    IReadOnlyList<WorkstationReportPackDistributionPayload> ReportPackDistributions,
    string Summary,
    IReadOnlyList<WorkstationReportingTemplatePayload> Templates,
    IReadOnlyList<WorkstationReportingRunPayload> RecentRuns,
    IReadOnlyList<ReportingScheduleRecordDto>? Schedules = null,
    IReadOnlyList<ReportPackDeliveryAttemptDto>? DeliveryAttempts = null,
    string? SelectedFundProfileId = null,
    IReadOnlyList<ReportingScheduleDeliveryPlanDto>? ScheduleDeliveryPlans = null,
    FinancialRecordExplorerDto? ReportLineProvenanceExplorer = null,
    IReadOnlyList<PortfolioReportingCutDto>? PortfolioCuts = null,
    IReadOnlyList<PortfolioReportingLiveViewDto>? LivePortfolioViews = null,
    IReadOnlyList<CrossFundReportingConsolidationDto>? CrossFundConsolidations = null,
    IReadOnlyList<PortfolioReportingPnlSliceDto>? PnlSlices = null,
    IReadOnlyList<PortfolioReportingAnalyticsRowDto>? AnalyticsRows = null,
    IReadOnlyList<StructuredReportingExportDto>? StructuredExports = null,
    IReadOnlyList<ReportBrandingThemeDto>? BrandingThemes = null,
    IReadOnlyList<WorkstationReportWriterDatasetSourcePayload>? ReportWriterDatasetSources = null,
    WorkstationReportAccessAuditSummaryDto? AccessAudit = null,
    IReadOnlyList<WorkstationReportingDailyWorkItemDto>? DailyWork = null,
    IReadOnlyList<ReportingStarterKitDto>? StarterKits = null,
    ReportingStarterKitStateDto? StarterKitState = null);

/// <summary>
/// Accounting run-card governance details linked to strategy evidence.
/// </summary>
public sealed record WorkstationAccountingRunGovernancePayload(
    bool HasAuditTrail,
    bool HasPortfolio,
    bool HasLedger,
    string? DatasetReference,
    string? FeedReference);

public sealed record WorkstationAccountingRunReconciliationPayload(
    string? ReconciliationRunId,
    int BreakCount,
    int OpenBreakCount,
    int MatchCount,
    bool HasTimingDrift,
    int SecurityIssueCount,
    bool HasSecurityCoverageIssues,
    string? LastUpdated,
    string Tone);

public sealed record WorkstationAccountingRunCashFlowPayload(
    decimal CashBalance,
    decimal LedgerCashBalance,
    decimal CashVariance,
    decimal Financing,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    int JournalEntryCount,
    string Tone,
    string Summary);

public sealed record WorkstationAccountingCashFlowSummaryPayload(
    decimal TotalCash,
    decimal TotalLedgerCash,
    decimal NetVariance,
    decimal TotalFinancing,
    int RunsWithCashSignals,
    int RunsWithCashVariance,
    string Tone,
    string Summary);

public sealed record WorkstationAccountingRunRecord(
    string RunId,
    string StrategyName,
    string Mode,
    string Status,
    string LastUpdated,
    string? AuditReference,
    string? LedgerReference,
    string? PortfolioId,
    int BreakCount,
    int OpenBreakCount,
    string ReconciliationStatus,
    WorkstationAccountingRunGovernancePayload Governance,
    WorkstationSecurityCoveragePayload SecurityCoverage,
    WorkstationAccountingRunCashFlowPayload CashFlow,
    WorkstationAccountingRunReconciliationPayload? LatestReconciliation,
    WorkstationKernelObservabilityPayload? KernelObservability = null);

public sealed record WorkstationAccountingSeverityCountPayload(
    string Severity,
    int Count);

public sealed record WorkstationAccountingAgingBucketPayload(
    string Bucket,
    int Count);

public sealed record WorkstationAccountingOwnerWorkloadPayload(
    string Owner,
    int OpenCount);

public sealed record WorkstationAccountingTrendSnapshotPayload(
    string Metric,
    int Value,
    string Trend);

public sealed record WorkstationAccountingDrillLinkPayload(
    string Label,
    string Href);

public sealed record WorkstationAccountingAlertPayload(
    string Tone,
    string Message);

public sealed record WorkstationAccountingControlCenterPayload(
    string CloseReadiness,
    IReadOnlyList<string> PortfolioFilterOptions,
    IReadOnlyList<string> AccountFilterOptions,
    IReadOnlyList<WorkstationAccountingSeverityCountPayload> BlockerSeverityDistribution,
    IReadOnlyList<WorkstationAccountingAgingBucketPayload> AgingCurves,
    IReadOnlyList<WorkstationAccountingOwnerWorkloadPayload> OwnerWorkload,
    int SlaBreachCount,
    IReadOnlyList<WorkstationAccountingTrendSnapshotPayload> TrendSnapshots,
    IReadOnlyList<WorkstationAccountingDrillLinkPayload> DrillLinks,
    IReadOnlyList<WorkstationAccountingAlertPayload> Alerts);

public sealed record WorkstationKernelLatencyPayload(
    double P50,
    double P95,
    double P99);

public sealed record WorkstationKernelDriftPayload(
    double Score,
    double Severity,
    string Methodology);

public sealed record WorkstationKernelAlertThresholdsPayload(
    int MinimumSampleCount,
    double MinimumShortRate,
    double ZeroBaselineShortRate,
    double RelativeMultiplier,
    double AbsoluteIncrease);

public sealed record WorkstationKernelCriticalSeverityRatePayload(
    double ShortWindow,
    double LongWindow,
    int ShortWindowSamples,
    int LongWindowSamples,
    bool JumpAlertActive,
    int JumpAlertCount,
    WorkstationKernelAlertThresholdsPayload AlertThresholds);

public sealed record WorkstationKernelDomainPayload(
    string Domain,
    long Evaluations,
    double ThroughputPerMinute,
    WorkstationKernelLatencyPayload LatencyMs,
    double ReasonCoveragePercent,
    WorkstationKernelDriftPayload Drift,
    WorkstationKernelCriticalSeverityRatePayload CriticalSeverityRate,
    long DeterminismMismatches,
    DateTimeOffset LastUpdatedUtc);

public sealed record WorkstationKernelObservabilityPayload(
    DateTimeOffset? UpdatedAtUtc,
    bool DeterminismChecksEnabled,
    int ActiveAlerts,
    int TotalAlerts,
    int Alerts,
    IReadOnlyList<WorkstationKernelDomainPayload> Domains);

/// <summary>
/// Typed payload returned by <c>GET /api/workstation/accounting</c> and
/// <c>GET /api/workstation/reporting</c>.
/// </summary>
public sealed record WorkstationAccountingPayload(
    IReadOnlyList<WorkstationMetricCard> Metrics,
    IReadOnlyList<WorkstationAccountingRunRecord> ReconciliationQueue,
    IReadOnlyList<ReconciliationBreakQueueItem> BreakQueue,
    WorkstationAccountingWorkspaceSummary Workspace,
    WorkstationAccountingCashFlowSummaryPayload CashFlow,
    WorkstationReportingPayload Reporting,
    WorkstationAccountingControlCenterPayload ControlCenter,
    WorkstationKernelObservabilityPayload KernelObservability,
    ManualJournalEntryWorkbenchDto? ManualJournalWorkbench = null);

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
    WorkstationAccountingCashFlowSummaryPayload? CashFlow);



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

/// <summary>
/// Asset-class-specific evidence requirement rendered by Portfolio, Accounting, and desktop workstations.
/// </summary>
public sealed record MultiAssetEvidenceRequirementDto(
    string RequirementId,
    string Label,
    string Category,
    string Status,
    string EvidenceRoute,
    bool Required);

/// <summary>
/// Readiness blocker for a single asset class.
/// </summary>
public sealed record MultiAssetReadinessBlockerDto(
    string Code,
    string Severity,
    string Message,
    string Source,
    string? EvidenceRoute);

/// <summary>
/// Shared drill-through target for one asset-class coverage row.
/// </summary>
public sealed record MultiAssetDrillThroughTargetDto(
    string TargetId,
    string TargetType,
    string Label,
    string Route,
    string? EvidenceLink,
    string Status,
    string Source);

/// <summary>
/// Operational coverage row for one asset class across Security Master, provider evidence,
/// ledger, reconciliation, and close readiness.
/// </summary>
public sealed record MultiAssetClassCoverageDto(
    string AssetClass,
    string DisplayName,
    string Status,
    string StatusLabel,
    string Summary,
    IReadOnlyList<MultiAssetEvidenceRequirementDto> EvidenceRequirements,
    IReadOnlyList<MultiAssetReadinessBlockerDto> Blockers,
    IReadOnlyList<MultiAssetDrillThroughTargetDto> DrillThroughTargets,
    IReadOnlyDictionary<string, string> LedgerClassification,
    IReadOnlyDictionary<string, string> ReconciliationSignals);

/// <summary>
/// Shared registry contract for an asset pack exposed by the multi-asset coverage endpoint.
/// </summary>
public sealed record MultiAssetPackCoverageDto(
    string PackId,
    string DisplayName,
    IReadOnlyList<string> AssetClasses,
    AssetPackContractSchema ContractSchema,
    IReadOnlyList<string> LifecycleEvents,
    IReadOnlyList<AssetPackLifecycleCoverage> LifecycleCoverage,
    IReadOnlyList<string> ValuationMethods,
    AssetPackAccountingRules AccountingRules,
    AssetPackValidationRules ValidationRules,
    AssetPackReportingTaxonomy ReportingTaxonomy,
    string AutomationDepth,
    AssetPackAdmissionPolicy AdmissionPolicy,
    string LedgerExtensionPolicy,
    string RegistryValidationStatus = "Unknown",
    IReadOnlyList<AssetPackRegistryValidationIssue>? RegistryValidationIssues = null)
{
    public IReadOnlyList<AssetPackRegistryValidationIssue> RegistryValidationIssues { get; init; } = RegistryValidationIssues ?? [];
}

/// <summary>
/// Shared multi-asset operational coverage payload returned by
/// <c>GET /api/workstation/portfolio/multi-asset-coverage</c>.
/// </summary>
public sealed record MultiAssetCoverageSummaryDto(
    string FundAccountId,
    string Entity,
    string AsOfUtc,
    IReadOnlyList<WorkstationMetricCard> Metrics,
    IReadOnlyList<MultiAssetClassCoverageDto> AssetClasses,
    IReadOnlyDictionary<string, string> DrillThroughRoutes,
    IReadOnlyList<MultiAssetPackCoverageDto>? AssetPacks = null)
{
    public IReadOnlyList<MultiAssetPackCoverageDto> AssetPacks { get; init; } = AssetPacks ?? [];
}

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
public sealed record WorkstationDataPayload(
    IReadOnlyList<WorkstationMetricCard> Metrics,
    IReadOnlyList<WorkstationDataProviderRecord> Providers,
    IReadOnlyList<WorkstationDataBackfillRecord> Backfills,
    IReadOnlyList<WorkstationDataExportRecord> Exports,
    DataUploadTemplateCatalogDto UploadTemplates,
    WorkstationKernelObservabilityPayload KernelObservability);
