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
                        "Primary")
                ],
                EvidenceTags: ["run history", "promotion state", "portfolio coverage", "ledger coverage"],
                MarketPatternTags: ["research to backtest", "backtest to paper handoff", "review queue"]),

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
                        routePrefixes: [UiApiRoutes.ExecutionControls])
                ],
                EvidenceTags: ["readiness gates", "replay verification", "control evidence", "operator work items"],
                MarketPatternTags: ["paper trading", "live readiness gate", "execution controls"]),

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
                        "Primary")
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
                Title: "Portfolio Reporting Output",
                Summary: "Review portfolio context, report packs, exports, and downstream approvals.",
                WorkspaceId: "reporting",
                WorkspaceTitle: "Reporting",
                EntryPageTag: "ReportingShell",
                Tone: "Primary",
                Actions:
                [
                    Action(
                        WorkflowActionIds.PortfolioOpen,
                        "Open Portfolio",
                        "Open portfolio review, accounts, and fund exposure workflows.",
                        "PortfolioShell",
                        "Primary"),
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
                        workItemKind: OperatorWorkItemKindDto.ReportPackApproval)
                ],
                EvidenceTags: ["operating context", "account sync", "report pack", "export presets"],
                MarketPatternTags: ["dashboard to export", "saved output preset", "approval queue"]),

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
