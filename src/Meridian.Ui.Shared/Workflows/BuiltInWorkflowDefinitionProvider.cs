using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Workflows;

/// <summary>
/// Built-in workflow library entries based on the current workstation surfaces.
/// </summary>
public sealed class BuiltInWorkflowDefinitionProvider : IWorkflowDefinitionProvider
{
    public IReadOnlyList<WorkflowDefinitionDto> GetWorkflowDefinitions()
        =>
        [
            new WorkflowDefinitionDto(
                WorkflowId: "strategy-to-paper-review",
                Title: "Strategy to Paper Review",
                Summary: "Create or review strategy evidence, then hand a candidate to Trading.",
                WorkspaceId: "strategy",
                WorkspaceTitle: "Strategy",
                EntryPageTag: "StrategyShell",
                Tone: "Primary",
                Actions:
                [
                    Action(
                        WorkflowActionIds.StrategyStartBacktest,
                        "Start Backtest",
                        "Launch a simulation from the Strategy workspace.",
                        "Backtest",
                        "Primary"),
                    Action(
                        WorkflowActionIds.StrategyReviewRuns,
                        "Review Runs",
                        "Inspect strategy evidence, metrics, continuity, and promotion state.",
                        "StrategyRuns",
                        "Primary",
                        workItemKind: OperatorWorkItemKindDto.PromotionReview),
                    Action(
                        WorkflowActionIds.StrategySendToTradingReview,
                        "Send to Trading Review",
                        "Open the Trading workspace with the strategy handoff in view.",
                        "TradingShell",
                        "Primary"),
                    Action(
                        WorkflowActionIds.EvidenceOpenPacket,
                        "Open Evidence Packet",
                        "Open the reusable evidence packet for the selected workflow subject.",
                        "EvidenceWorkbench",
                        "Primary",
                        routePrefixes: [UiApiRoutes.WorkstationEvidenceSubjectPacket]),
                    Action(
                        WorkflowActionIds.EvidenceValidate,
                        "Validate Evidence",
                        "Validate evidence completeness without mutating source workflows.",
                        "EvidenceWorkbench",
                        "Warning",
                        routePrefixes: [UiApiRoutes.WorkstationEvidenceSubjectValidate]),
                    Action(
                        WorkflowActionIds.EvidenceExportManifest,
                        "Export Evidence Manifest",
                        "Write a manifest-only evidence export for audit review.",
                        "EvidenceWorkbench",
                        "Primary",
                        routePrefixes: [UiApiRoutes.WorkstationEvidenceSubjectExportManifest])
                ],
                EvidenceTags: ["run history", "promotion state", "portfolio coverage", "ledger coverage"],
                MarketPatternTags: ["strategy to backtest", "backtest to paper handoff", "review queue"]),

            new WorkflowDefinitionDto(
                WorkflowId: "paper-trading-readiness",
                Title: "Paper Trading Readiness",
                Summary: "Review context, replay, controls, and cockpit readiness before live escalation.",
                WorkspaceId: "trading",
                WorkspaceTitle: "Trading",
                EntryPageTag: "TradingShell",
                Tone: "Warning",
                Actions:
                [
                    Action(
                        WorkflowActionIds.TradingChooseContext,
                        "Choose Context",
                        "Select the active fund-linked operating context.",
                        "TradingShell",
                        "Primary"),
                    Action(
                        WorkflowActionIds.TradingReviewPaperCandidate,
                        "Review Candidate for Paper",
                        "Continue the Strategy to Trading handoff.",
                        "TradingShell",
                        "Primary",
                        routePrefixes: [UiApiRoutes.WorkstationTradingReadiness]),
                    Action(
                        WorkflowActionIds.TradingOpenCockpit,
                        "Open Active Cockpit",
                        "Continue the active paper or live execution workflow.",
                        "TradingShell",
                        "Primary",
                        routePrefixes: [UiApiRoutes.ExecutionSessions]),
                    Action(
                        WorkflowActionIds.TradingReviewExecutionControls,
                        "Review Execution Controls",
                        "Inspect control evidence and operator override posture.",
                        "RunRisk",
                        "Warning",
                        workItemKind: OperatorWorkItemKindDto.ExecutionControl,
                        routePrefixes: [UiApiRoutes.ExecutionControls]),
                    Action(
                        WorkflowActionIds.EvidenceOpenPacket,
                        "Open Evidence Packet",
                        "Open the reusable evidence packet for the selected workflow subject.",
                        "EvidenceWorkbench",
                        "Primary",
                        routePrefixes: [UiApiRoutes.WorkstationEvidenceSubjectPacket]),
                    Action(
                        WorkflowActionIds.EvidenceValidate,
                        "Validate Evidence",
                        "Validate evidence completeness without mutating source workflows.",
                        "EvidenceWorkbench",
                        "Warning",
                        routePrefixes: [UiApiRoutes.WorkstationEvidenceSubjectValidate]),
                    Action(
                        WorkflowActionIds.EvidenceExportManifest,
                        "Export Evidence Manifest",
                        "Write a manifest-only evidence export for audit review.",
                        "EvidenceWorkbench",
                        "Primary",
                        routePrefixes: [UiApiRoutes.WorkstationEvidenceSubjectExportManifest])
                ],
                EvidenceTags: ["readiness gates", "replay verification", "control evidence", "operator work items"],
                MarketPatternTags: ["paper trading", "live readiness gate", "execution controls"]),

            new WorkflowDefinitionDto(
                WorkflowId: "portfolio-position-review",
                Title: "Portfolio Position Review",
                Summary: "Review holdings, exposures, account sync, run portfolios, and imported snapshots.",
                WorkspaceId: "portfolio",
                WorkspaceTitle: "Portfolio",
                EntryPageTag: "PortfolioShell",
                Tone: "Primary",
                Actions:
                [
                    Action(
                        WorkflowActionIds.PortfolioOpen,
                        "Open Portfolio",
                        "Open portfolio review, accounts, fund exposure, and import workflows.",
                        "PortfolioShell",
                        "Primary"),
                    Action(
                        WorkflowActionIds.PortfolioReviewAggregate,
                        "Review Aggregate Exposure",
                        "Monitor portfolio exposure across accounts, funds, entities, and active runs.",
                        "AggregatePortfolio",
                        "Primary",
                        routePrefixes:
                        [
                            UiApiRoutes.PortfolioAggregate,
                            UiApiRoutes.PortfolioExposure,
                            "/api/portfolio/symbols"
                        ]),
                    Action(
                        WorkflowActionIds.PortfolioReviewRunPortfolio,
                        "Review Run Portfolio",
                        "Inspect holdings, exposure, attribution, and cash-flow continuity for the selected run.",
                        "RunPortfolio",
                        "Primary",
                        routePrefixes:
                        [
                            UiApiRoutes.ExecutionPortfolio,
                            UiApiRoutes.RunsAttribution,
                            UiApiRoutes.PortfolioCashFlows
                        ]),
                    Action(
                        WorkflowActionIds.PortfolioReviewBrokerageSync,
                        "Review Brokerage Sync",
                        "Open account portfolio sync status and exception detail.",
                        "AccountPortfolio",
                        "Warning",
                        workItemKind: OperatorWorkItemKindDto.BrokerageSync,
                        routePrefixes: [UiApiRoutes.FundAccountBrokerageSyncAccounts],
                        routeContains: ["/brokerage-sync"]),
                    Action(
                        WorkflowActionIds.PortfolioImportSnapshots,
                        "Import Portfolio Snapshots",
                        "Import external portfolio snapshots for reconciliation and downstream reporting.",
                        "PortfolioImport",
                        "Warning")
                ],
                EvidenceTags: ["positions", "exposure", "account sync", "run attribution", "snapshot imports"],
                MarketPatternTags: ["portfolio review", "account sync", "exposure monitoring"]),

            new WorkflowDefinitionDto(
                WorkflowId: "accounting-reconciliation-review",
                Title: "Accounting Reconciliation Review",
                Summary: "Work reconciliation breaks, continuity checks, and audit-trail review.",
                WorkspaceId: "accounting",
                WorkspaceTitle: "Accounting",
                EntryPageTag: "AccountingShell",
                Tone: "Warning",
                Actions:
                [
                    Action(
                        WorkflowActionIds.AccountingChooseContext,
                        "Choose Context",
                        "Select a fund-linked context before reviewing accounting queues.",
                        "AccountingShell",
                        "Primary"),
                    Action(
                        WorkflowActionIds.AccountingReviewReconciliation,
                        "Review Reconciliation Breaks",
                        "Open the reconciliation lane and work the break queue.",
                        "FundReconciliation",
                        "Warning",
                        workItemKind: OperatorWorkItemKindDto.ReconciliationBreak,
                        routePrefixes: [UiApiRoutes.ReconciliationBreakQueue]),
                    Action(
                        WorkflowActionIds.AccountingReviewLedgerContinuity,
                        "Review Ledger Continuity",
                        "Open trial-balance and continuity surfaces for the selected context.",
                        "FundTrialBalance",
                        "Primary"),
                    Action(
                        WorkflowActionIds.AccountingReviewOperationsContinuity,
                        "Review Close Workflow",
                        "Open the governed close workflow with gates, blockers, checklist, timeline, and evidence.",
                        "OperationsContinuity",
                        "Warning",
                        routePrefixes: [UiApiRoutes.OperationsContinuity]),
                    Action(
                        WorkflowActionIds.AccountingReviewCloseReadiness,
                        "Review Close Readiness",
                        "Inspect close readiness score, blockers, checklist controls, and next recovery actions.",
                        "OperationsClose",
                        "Warning",
                        routePrefixes: [UiApiRoutes.OperationsContinuityCloseReadiness],
                        routeContains: ["/close-readiness"]),
                    Action(
                        WorkflowActionIds.AccountingReviewAuditTrail,
                        "Review Audit Trail",
                        "Inspect approvals, replay evidence, and trust-gate audit history.",
                        "FundAuditTrail",
                        "Primary",
                        workItemKind: OperatorWorkItemKindDto.PaperReplay,
                        aliases: ["workflow.trading.review-paper-replay"]),
                    Action(
                        WorkflowActionIds.AccountingReviewLiveHandoff,
                        "Open Accounting Review",
                        "Move the handoff forward into Accounting.",
                        "AccountingShell",
                        "Primary"),
                    Action(
                        WorkflowActionIds.AccountingOpen,
                        "Open Accounting Shell",
                        "Continue ledger, reconciliation, cash, banking, and audit review.",
                        "AccountingShell",
                        "Primary"),
                    Action(
                        WorkflowActionIds.EvidenceOpenPacket,
                        "Open Evidence Packet",
                        "Open the reusable evidence packet for the selected workflow subject.",
                        "EvidenceWorkbench",
                        "Primary",
                        routePrefixes: [UiApiRoutes.WorkstationEvidenceSubjectPacket]),
                    Action(
                        WorkflowActionIds.EvidenceValidate,
                        "Validate Evidence",
                        "Validate evidence completeness without mutating source workflows.",
                        "EvidenceWorkbench",
                        "Warning",
                        routePrefixes: [UiApiRoutes.WorkstationEvidenceSubjectValidate]),
                    Action(
                        WorkflowActionIds.EvidenceExportManifest,
                        "Export Evidence Manifest",
                        "Write a manifest-only evidence export for audit review.",
                        "EvidenceWorkbench",
                        "Primary",
                        routePrefixes: [UiApiRoutes.WorkstationEvidenceSubjectExportManifest])
                ],
                EvidenceTags: ["break queue", "trial balance", "ledger continuity", "audit references"],
                MarketPatternTags: ["exception queue", "audit trail", "approval handoff"]),

            new WorkflowDefinitionDto(
                WorkflowId: "data-provider-recovery",
                Title: "Data Provider Recovery",
                Summary: "Review provider health, failed backfills, security coverage, and data quality.",
                WorkspaceId: "data",
                WorkspaceTitle: "Data",
                EntryPageTag: "DataShell",
                Tone: "Warning",
                Actions:
                [
                    Action(
                        WorkflowActionIds.DataOpenProviderHealth,
                        "Open Provider Health",
                        "Inspect provider posture and reconnect degraded feeds.",
                        "ProviderHealth",
                        "Warning"),
                    Action(
                        WorkflowActionIds.DataOpenBackfillQueue,
                        "Open Backfill Queue",
                        "Inspect failed or incomplete queue work.",
                        "Backfill",
                        "Warning"),
                    Action(
                        WorkflowActionIds.DataReviewSecurityMaster,
                        "Review Security Master",
                        "Review reference-data coverage and symbol lifecycle issues.",
                        "SecurityMaster",
                        "Warning",
                        workItemKind: OperatorWorkItemKindDto.SecurityMasterCoverage,
                        routePrefixes: [UiApiRoutes.WorkstationSecurityMasterSearch]),
                    Action(
                        WorkflowActionIds.DataOpenQueueOverview,
                        "Open Queue Overview",
                        "Inspect providers, storage, and backfill posture from the workspace home.",
                        "DataShell",
                        "Primary")
                ],
                EvidenceTags: ["provider metrics", "backfill status", "security coverage", "data quality"],
                MarketPatternTags: ["provider dashboard", "data quality queue", "coverage workbench"]),

            new WorkflowDefinitionDto(
                WorkflowId: "portfolio-reporting-output",
                Title: "Governed Reporting Output",
                Summary: "Review report packs, exports, retained evidence, and downstream approvals.",
                WorkspaceId: "reporting",
                WorkspaceTitle: "Reporting",
                EntryPageTag: "ReportingShell",
                Tone: "Primary",
                Actions:
                [
                    Action(
                        WorkflowActionIds.ReportingOpen,
                        "Open Reporting",
                        "Open report packs, dashboards, export, and preset workflows.",
                        "ReportingShell",
                        "Primary"),
                    Action(
                        WorkflowActionIds.ReportingApproveReportPack,
                        "Approve Report Pack",
                        "Open report-pack review and approval output.",
                        "FundReportPack",
                        "Primary",
                        workItemKind: OperatorWorkItemKindDto.ReportPackApproval),
                    Action(
                        WorkflowActionIds.EvidenceOpenPacket,
                        "Open Evidence Packet",
                        "Open the reusable evidence packet for the selected workflow subject.",
                        "EvidenceWorkbench",
                        "Primary",
                        routePrefixes: [UiApiRoutes.WorkstationEvidenceSubjectPacket]),
                    Action(
                        WorkflowActionIds.EvidenceValidate,
                        "Validate Evidence",
                        "Validate evidence completeness without mutating source workflows.",
                        "EvidenceWorkbench",
                        "Warning",
                        routePrefixes: [UiApiRoutes.WorkstationEvidenceSubjectValidate]),
                    Action(
                        WorkflowActionIds.EvidenceExportManifest,
                        "Export Evidence Manifest",
                        "Write a manifest-only evidence export for audit review.",
                        "EvidenceWorkbench",
                        "Primary",
                        routePrefixes: [UiApiRoutes.WorkstationEvidenceSubjectExportManifest])
                ],
                EvidenceTags: ["report pack", "export presets", "retained evidence", "approval queue"],
                MarketPatternTags: ["governed output", "saved output preset", "approval queue"]),

            new WorkflowDefinitionDto(
                WorkflowId: "workstation-settings-support",
                Title: "Workstation Settings and Support",
                Summary: "Open settings, diagnostics, credentials, and support controls.",
                WorkspaceId: "settings",
                WorkspaceTitle: "Settings",
                EntryPageTag: "SettingsShell",
                Tone: "Neutral",
                Actions:
                [
                    Action(
                        WorkflowActionIds.SettingsOpen,
                        "Open Settings",
                        "Open workstation configuration and support surfaces.",
                        "SettingsShell",
                        "Primary")
                ],
                EvidenceTags: ["preferences", "credentials", "diagnostics", "notifications"],
                MarketPatternTags: ["support workspace", "configuration surface", "health dashboard"])
        ];

    private static WorkflowActionDto Action(
        string actionId,
        string label,
        string detail,
        string targetPageTag,
        string tone,
        OperatorWorkItemKindDto? workItemKind = null,
        IReadOnlyList<string>? routePrefixes = null,
        IReadOnlyList<string>? routeContains = null,
        IReadOnlyList<string>? aliases = null)
        => new(
            ActionId: actionId,
            Label: label,
            Detail: detail,
            TargetPageTag: targetPageTag,
            Tone: tone,
            WorkItemKind: workItemKind,
            RoutePrefixes: routePrefixes ?? Array.Empty<string>(),
            RouteContains: routeContains ?? Array.Empty<string>(),
            Aliases: aliases ?? Array.Empty<string>());
}
