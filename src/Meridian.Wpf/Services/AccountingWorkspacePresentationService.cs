using System;
using System.Collections.Generic;
using System.Linq;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

internal static class AccountingWorkspacePresentationService
{
    internal static WorkspaceCommandGroup BuildCommandGroup(bool hasFund) => hasFund
        ? new WorkspaceCommandGroup
        {
            PrimaryCommands =
            [
                new WorkspaceCommandItem { Id = "FundLedger", Label = "Operations", Description = "Open operations lane", ShortcutHint = "Ctrl+1", Glyph = "\uEE94", Tone = WorkspaceTone.Primary },
                new WorkspaceCommandItem { Id = "FundTrialBalance", Label = "Accounting", Description = "Open accounting lane", ShortcutHint = "Ctrl+2", Glyph = "\uE9D9" },
                new WorkspaceCommandItem { Id = "FundReconciliation", Label = "Reconciliation", Description = "Open reconciliation lane", ShortcutHint = "Ctrl+3", Glyph = "\uE895" }
            ],
            SecondaryCommands =
            [
                new WorkspaceCommandItem { Id = "FundAccounts", Label = "Accounts", Description = "Open account surfaces", Glyph = "\uE8D4" },
                new WorkspaceCommandItem { Id = "FundCashFinancing", Label = "Reporting", Description = "Open cash and reporting view", Glyph = "\uE8C7" },
                new WorkspaceCommandItem { Id = "FundReportPack", Label = "Report Pack", Description = "Open governed report-pack preview", Glyph = "\uE8A5" },
                new WorkspaceCommandItem { Id = "FundAuditTrail", Label = "Audit", Description = "Open audit trail", Glyph = "\uE7BA" },
                new WorkspaceCommandItem { Id = "Diagnostics", Label = "Diagnostics", Description = "Open diagnostics", Glyph = "\uE7BA" },
                new WorkspaceCommandItem { Id = "NotificationCenter", Label = "Notifications", Description = "Open notifications", Glyph = "\uE7F4" },
                new WorkspaceCommandItem { Id = "Settings", Label = "Settings", Description = "Open settings", Glyph = "\uE713" }
            ]
        }
        : new WorkspaceCommandGroup
        {
            PrimaryCommands =
            [
                new WorkspaceCommandItem { Id = "SwitchContext", Label = "Switch Context", Description = "Choose an active operating context", ShortcutHint = "Required", Glyph = "\uE777", Tone = WorkspaceTone.Primary }
            ],
            SecondaryCommands =
            [
                new WorkspaceCommandItem { Id = "Diagnostics", Label = "Diagnostics", Description = "Open diagnostics", Glyph = "\uE7BA" },
                new WorkspaceCommandItem { Id = "NotificationCenter", Label = "Notifications", Description = "Open notifications", Glyph = "\uE7F4" },
                new WorkspaceCommandItem { Id = "Settings", Label = "Settings", Description = "Open settings", Glyph = "\uE713" }
            ]
        };

    internal static PaneDropAction ResolveDockAction(string actionId) => actionId switch
    {
        "FundLedger" => PaneDropAction.Replace,
        "FundAccounts" or "FundReconciliation" or "FundTrialBalance" => PaneDropAction.SplitRight,
        "FundCashFinancing" or "NotificationCenter" or "Diagnostics" => PaneDropAction.SplitBelow,
        _ => PaneDropAction.OpenTab
    };

    internal static IReadOnlyList<WorkspaceQueueItem> BuildOperationsQueue(
        FundProfileDetail profile,
        FundOperationsWorkspaceDto workspace)
    {
        var ledger = workspace.Ledger;
        var summary = workspace.Workspace;

        return
        [
            new WorkspaceQueueItem { Title = "Fund operations posture", Detail = $"{summary.TotalAccounts} linked account(s), {ledger.EntityCount} entities, {ledger.SleeveCount} sleeves, and {ledger.VehicleCount} vehicles feed {ledger.JournalEntryCount} journals and {ledger.TrialBalance.Count} trial-balance lines through the shared Accounting workspace.", StatusLabel = ledger.JournalEntryCount > 0 ? "Live review" : "Needs setup", CountLabel = ledger.JournalEntryCount > 0 ? $"{ledger.JournalEntryCount} journals" : "No journals", Tone = ledger.JournalEntryCount > 0 ? WorkspaceTone.Info : WorkspaceTone.Warning, PrimaryActionId = "FundLedger", PrimaryActionLabel = "Open Operations", SecondaryActionId = "FundAccounts", SecondaryActionLabel = "Accounts" },
            new WorkspaceQueueItem { Title = "Accounts and banking coordination", Detail = $"{profile.DisplayName} now reuses the shared fund-operations projection for account, banking, and entity drill-ins from the Accounting shell.", StatusLabel = "Operator review", CountLabel = workspace.BankSnapshots.Count > 0 ? $"{workspace.BankSnapshots.Count} bank views" : profile.BaseCurrency, Tone = WorkspaceTone.Neutral, PrimaryActionId = "FundAccounts", PrimaryActionLabel = "Accounts", SecondaryActionId = "FundLedger", SecondaryActionLabel = "Operations" }
        ];
    }

    internal static IReadOnlyList<WorkspaceQueueItem> BuildAccountingQueue(
        FundProfileDetail profile,
        FundOperationsWorkspaceDto workspace)
    {
        var ledger = workspace.Ledger;
        var cash = workspace.CashFinancing;

        return
        [
            new WorkspaceQueueItem { Title = "Trial balance and journals", Detail = $"{ledger.TrialBalance.Count} trial-balance line(s) and {ledger.JournalEntryCount} journal(s) are ready for accounting review from the shared fund-operations query path.", StatusLabel = ledger.TrialBalance.Count > 0 ? "Accounting ready" : "Awaiting snapshot", CountLabel = ledger.TrialBalance.Count > 0 ? $"{ledger.TrialBalance.Count} lines" : "No lines", Tone = ledger.TrialBalance.Count > 0 ? WorkspaceTone.Info : WorkspaceTone.Warning, PrimaryActionId = "FundTrialBalance", PrimaryActionLabel = "Open Accounting", SecondaryActionId = "FundLedger", SecondaryActionLabel = "Ledger" },
            new WorkspaceQueueItem { Title = "Cash and financing posture", Detail = $"Total cash {cash.TotalCash:C0}, financing cost {cash.FinancingCost:C0}, and pending settlement {cash.PendingSettlement:C0} are synchronized for reporting and sign-off.", StatusLabel = "Ready", CountLabel = profile.BaseCurrency, Tone = WorkspaceTone.Success, PrimaryActionId = "FundCashFinancing", PrimaryActionLabel = "Open Reporting", SecondaryActionId = "FundTrialBalance", SecondaryActionLabel = "Trial Balance" }
        ];
    }

    internal static IReadOnlyList<WorkspaceQueueItem> BuildReconciliationQueue(ReconciliationSummary reconciliation, FundLedgerSummary? ledger) =>
    [
        new WorkspaceQueueItem { Title = "Reconciliation review queue", Detail = reconciliation.OpenBreakCount > 0 ? $"{reconciliation.OpenBreakCount} open break(s) across {reconciliation.RunCount} recent run(s) with {reconciliation.BreakAmountTotal:C0} at risk." : $"{reconciliation.RunCount} reconciliation run(s) are currently matched and ready for sign-off.", StatusLabel = reconciliation.OpenBreakCount > 0 ? "Approval hold" : "Matched", CountLabel = reconciliation.OpenBreakCount > 0 ? $"{reconciliation.OpenBreakCount} open" : $"{reconciliation.RunCount} reviewed", Tone = reconciliation.OpenBreakCount > 0 ? WorkspaceTone.Warning : WorkspaceTone.Success, PrimaryActionId = "FundReconciliation", PrimaryActionLabel = "Review Breaks", SecondaryActionId = "FundTrialBalance", SecondaryActionLabel = "Trial Balance" },
        new WorkspaceQueueItem { Title = "Security coverage posture", Detail = reconciliation.SecurityCoverageIssueCount > 0 ? $"{reconciliation.SecurityCoverageIssueCount} coverage issue(s) need review before approvals are released." : $"Security coverage is aligned for the current reconciliation scope{(ledger is null ? string.Empty : $" with {ledger.TrialBalance.Count} ledger lines available for validation")}.", StatusLabel = reconciliation.SecurityCoverageIssueCount > 0 ? "Coverage open" : "Aligned", CountLabel = reconciliation.SecurityCoverageIssueCount > 0 ? $"{reconciliation.SecurityCoverageIssueCount} issue(s)" : "0 issues", Tone = reconciliation.SecurityCoverageIssueCount > 0 ? WorkspaceTone.Warning : WorkspaceTone.Success, PrimaryActionId = "FundReconciliation", PrimaryActionLabel = "Open Review", SecondaryActionId = "FundAuditTrail", SecondaryActionLabel = "Audit Trail" }
    ];

    internal static IReadOnlyList<WorkspaceQueueItem> BuildReportingQueue(
        FundProfileDetail profile,
        FundOperationsWorkspaceDto workspace)
    {
        var cash = workspace.CashFinancing;
        var reporting = workspace.Reporting;

        return
        [
            new WorkspaceQueueItem { Title = "Portfolio and cash reporting", Detail = $"Cash, financing, NAV, and portfolio-linked reporting can be reviewed without leaving Accounting. {reporting.ProfileCount} reporting/export profile(s) are already available through the shared workspace summary.", StatusLabel = "Ready", CountLabel = $"{cash.TotalCash:C0}", Tone = WorkspaceTone.Info, PrimaryActionId = "FundCashFinancing", PrimaryActionLabel = "Open Reporting", SecondaryActionId = "FundPortfolio", SecondaryActionLabel = "Portfolio" },
            new WorkspaceQueueItem { Title = "Board and operator handoff", Detail = $"Keep reporting, trial-balance, audit references, and {string.Join(", ", reporting.ReportPackTargets)} pack targets together before approvals or exports leave the workstation.", StatusLabel = "Review", CountLabel = profile.BaseCurrency, Tone = WorkspaceTone.Neutral, PrimaryActionId = "FundReportPack", PrimaryActionLabel = "Open Report Pack", SecondaryActionId = "FundAuditTrail", SecondaryActionLabel = "Audit" }
        ];
    }

    internal static IReadOnlyList<WorkspaceQueueItem> BuildAuditQueue(ReconciliationSummary reconciliation, IReadOnlyList<NotificationHistoryItem> notifications, int unreadAlerts) =>
    [
        new WorkspaceQueueItem { Title = "Audit trail and approvals", Detail = notifications.FirstOrDefault() is { } latest ? $"Latest notification: {latest.Title} at {latest.Timestamp:t}. Open grouped alerts and acknowledgement history from the workspace." : "No recent notifications. Keep the audit trail docked when approval gates change.", StatusLabel = unreadAlerts > 0 ? "Unread alerts" : "Quiet", CountLabel = unreadAlerts > 0 ? $"{unreadAlerts} unread" : $"{notifications.Count} recent", Tone = unreadAlerts > 0 ? WorkspaceTone.Warning : WorkspaceTone.Info, PrimaryActionId = "FundAuditTrail", PrimaryActionLabel = "Open Audit", SecondaryActionId = "NotificationCenter", SecondaryActionLabel = "Alerts" },
        new WorkspaceQueueItem { Title = "Diagnostics and system readiness checks", Detail = reconciliation.OpenBreakCount > 0 ? "Use diagnostics and system health before releasing approvals with open reconciliation pressure." : "Diagnostics and system health remain available as quick trust checks before operator handoff.", StatusLabel = unreadAlerts > 0 ? "Escalated" : "Available", CountLabel = unreadAlerts > 0 ? $"{unreadAlerts} alert-linked" : "Diagnostics ready", Tone = unreadAlerts > 0 ? WorkspaceTone.Warning : WorkspaceTone.Info, PrimaryActionId = "Diagnostics", PrimaryActionLabel = "Diagnostics", SecondaryActionId = "SystemHealth", SecondaryActionLabel = "System Health" }
    ];

    internal static IReadOnlyList<FinancialOperationsWorkflowStep> BuildFinancialOperationsWorkflowSteps(
        FundProfileDetail? profile,
        FundOperationsWorkspaceDto? workspace,
        WorkspaceWorkflowSummary? workflow,
        IReadOnlyList<NotificationHistoryItem> notifications,
        int unreadAlerts)
    {
        if (profile is null || workspace is null)
        {
            return
            [
                CreateFinancialOperationsWorkflowStep(
                    "receive-activity",
                    "Receive Activity",
                    "Choose a fund-linked operating context before accounting activity can be loaded.",
                    WorkflowStepStatus.Current,
                    workflow?.StatusLabel ?? "Context required",
                    WorkspaceTone.Warning,
                    "SwitchContext"),
                CreateFinancialOperationsWorkflowStep("match-records", "Match Records", "Record matching waits for the active fund scope.", WorkflowStepStatus.Pending, "Pending", WorkspaceTone.Neutral, "FundLedger"),
                CreateFinancialOperationsWorkflowStep("resolve-exceptions", "Resolve Exceptions", "Exception review opens after source activity is matched.", WorkflowStepStatus.Pending, "Pending", WorkspaceTone.Neutral, "FundReconciliation"),
                CreateFinancialOperationsWorkflowStep("approve-results", "Approve Results", "Approval release waits for reconciled accounting evidence.", WorkflowStepStatus.Pending, "Pending", WorkspaceTone.Neutral, "AccountingApprovals"),
                CreateFinancialOperationsWorkflowStep("produce-evidence", "Produce Evidence", "Evidence package production waits for approved results.", WorkflowStepStatus.Pending, "Pending", WorkspaceTone.Neutral, "FundAuditTrail")
            ];
        }

        var ledger = workspace.Ledger;
        var reconciliation = workspace.Reconciliation;
        var hasActivity = workspace.Workspace.TotalAccounts > 0 || ledger.JournalEntryCount > 0 || workspace.RecordedRunCount > 0;
        var hasMatches = reconciliation.RunCount > 0;
        var hasOpenExceptions = reconciliation.OpenBreakCount > 0 || reconciliation.SecurityCoverageIssueCount > 0;
        var hasApprovalEvidence = !string.IsNullOrWhiteSpace(workspace.Governance?.DecisionPosture)
            || !string.IsNullOrWhiteSpace(workspace.Governance?.SignoffPosture)
            || workflow?.Evidence.Count > 0;
        var hasProducedEvidence = !string.IsNullOrWhiteSpace(workspace.Governance?.AuditTraceability)
            || notifications.Count > 0
            || unreadAlerts > 0
            || hasApprovalEvidence;

        return
        [
            CreateFinancialOperationsWorkflowStep(
                "receive-activity",
                "Receive Activity",
                $"{workspace.Workspace.TotalAccounts} linked account(s), {ledger.JournalEntryCount} journal(s), and {workspace.RecordedRunCount} run reference(s) feed the Accounting workspace.",
                hasActivity ? WorkflowStepStatus.Complete : WorkflowStepStatus.Current,
                hasActivity ? "Loaded" : "Awaiting activity",
                hasActivity ? WorkspaceTone.Success : WorkspaceTone.Warning,
                "FundLedger"),
            CreateFinancialOperationsWorkflowStep(
                "match-records",
                "Match Records",
                hasMatches
                    ? $"{reconciliation.RunCount} reconciliation run(s) are available for matching across ledger, cash, security, and position records."
                    : "Record matching waits for reconciliation runs in the current Accounting scope.",
                hasMatches ? (hasOpenExceptions ? WorkflowStepStatus.Current : WorkflowStepStatus.Complete) : WorkflowStepStatus.Pending,
                hasMatches ? (hasOpenExceptions ? "Review matches" : "Matched") : "Pending",
                hasMatches ? (hasOpenExceptions ? WorkspaceTone.Info : WorkspaceTone.Success) : WorkspaceTone.Neutral,
                "FundReconciliation"),
            CreateFinancialOperationsWorkflowStep(
                "resolve-exceptions",
                "Resolve Exceptions",
                hasOpenExceptions
                    ? $"{reconciliation.OpenBreakCount} break(s) and {reconciliation.SecurityCoverageIssueCount} security coverage issue(s) require exception review."
                    : "No open reconciliation exceptions are blocking the Accounting lane.",
                hasOpenExceptions ? WorkflowStepStatus.Current : hasMatches ? WorkflowStepStatus.Complete : WorkflowStepStatus.Pending,
                hasOpenExceptions ? "In review" : hasMatches ? "Resolved" : "Pending",
                hasOpenExceptions ? WorkspaceTone.Warning : hasMatches ? WorkspaceTone.Success : WorkspaceTone.Neutral,
                "FundReconciliation"),
            CreateFinancialOperationsWorkflowStep(
                "approve-results",
                "Approve Results",
                hasOpenExceptions
                    ? "Approval release remains held until reconciliation exceptions and security coverage issues are resolved."
                    : workspace.Governance?.SignoffPosture ?? "Results are ready for accounting approval review.",
                hasOpenExceptions ? WorkflowStepStatus.Pending : hasApprovalEvidence ? WorkflowStepStatus.Complete : WorkflowStepStatus.Current,
                hasOpenExceptions ? "Blocked" : hasApprovalEvidence ? "Approved" : "Ready",
                hasOpenExceptions ? WorkspaceTone.Warning : hasApprovalEvidence ? WorkspaceTone.Success : WorkspaceTone.Info,
                "AccountingApprovals"),
            CreateFinancialOperationsWorkflowStep(
                "produce-evidence",
                "Produce Evidence",
                hasProducedEvidence
                    ? workspace.Governance?.AuditTraceability ?? workflow?.Evidence.FirstOrDefault()?.Value ?? "Audit trail and notification evidence are available for reporting handoff."
                    : "Evidence package production follows approval release.",
                hasProducedEvidence ? WorkflowStepStatus.Complete : hasOpenExceptions ? WorkflowStepStatus.Pending : WorkflowStepStatus.Current,
                hasProducedEvidence ? "Evidence ready" : hasOpenExceptions ? "Pending" : "Ready",
                hasProducedEvidence ? WorkspaceTone.Success : hasOpenExceptions ? WorkspaceTone.Neutral : WorkspaceTone.Info,
                "FundAuditTrail")
        ];
    }

    internal static AccountingLaneSummaries BuildLaneSummaries(
        FundProfileDetail? profile,
        FundOperationsWorkspaceDto? workspace,
        WorkspaceWorkflowSummary? workflow,
        IReadOnlyList<NotificationHistoryItem> notifications,
        int unreadAlerts)
    {
        if (profile is null || workspace is null)
        {
            return new AccountingLaneSummaries(
                Accounting: new AccountingLaneSummary("Locked", "Select a fund-linked context to unlock accounting review."),
                Reconciliation: new AccountingLaneSummary(workflow?.StatusLabel ?? "Locked", workflow?.StatusDetail ?? "Select a fund-linked context to unlock reconciliation review."),
                Reporting: new AccountingLaneSummary("Locked", "Select a fund-linked context to unlock reporting review."),
                Audit: new AccountingLaneSummary("Locked", "Select a fund-linked context to unlock audit review."));
        }

        var ledger = workspace.Ledger;
        var reconciliation = workspace.Reconciliation;
        var reporting = workspace.Reporting;
        var accountingLifecycle = workspace.Governance;
        var accountingSummary = ledger is null || ledger.TrialBalance.Count == 0
            ? "Awaiting ledger snapshot"
            : $"{ledger.TrialBalance.Count} trial-balance lines ready";
        var accountingDetail = ledger is null
            ? "Accounting review will become specific once the shared ledger snapshot is available."
            : $"{ledger.JournalEntryCount} journal(s) are available for continuity, accrual, and sign-off review.";
        var reconciliationSummary = !string.IsNullOrWhiteSpace(accountingLifecycle?.DecisionPosture)
            ? accountingLifecycle.DecisionPosture
            : reconciliation.OpenBreakCount > 0
                ? $"{reconciliation.OpenBreakCount} break(s) open"
                : workflow?.PrimaryBlocker.Code == "as-of-drift" || workflow?.PrimaryBlocker.Code == "missing-ledger" || workflow?.PrimaryBlocker.Code == "missing-reconciliation"
                    ? workflow.StatusLabel
                    : "Matched and review-ready";
        var reconciliationDetail = !string.IsNullOrWhiteSpace(accountingLifecycle?.SignoffPosture)
            ? $"{accountingLifecycle.SignoffPosture} {accountingLifecycle.CloseReadiness}".Trim()
            : workflow?.PrimaryBlocker.Code == "as-of-drift" || workflow?.PrimaryBlocker.Code == "missing-ledger" || workflow?.PrimaryBlocker.Code == "missing-reconciliation"
                ? workflow.PrimaryBlocker.Detail
                : reconciliation.OpenBreakCount > 0
                    ? workflow?.PrimaryBlocker.Detail ?? $"{reconciliation.OpenBreakCount} break(s) block close sign-off until the queue is reviewed."
                    : $"{reconciliation.RunCount} reconciliation run(s) are linked for the current context.";
        var reportingSummary = reporting.ProfileCount > 0
            ? $"{reporting.ProfileCount} report profile(s) ready"
            : "Reporting shell ready";
        var reportingDetail = !string.IsNullOrWhiteSpace(accountingLifecycle?.CloseReadiness)
            ? accountingLifecycle.CloseReadiness
            : $"Cash {workspace.CashFinancing.TotalCash:C0}, financing {workspace.CashFinancing.FinancingCost:C0}, and report-pack exports stay in the Accounting lane.";
        var latestNotification = notifications.FirstOrDefault();
        var auditSummary = unreadAlerts > 0
            ? $"{unreadAlerts} unread alert(s)"
            : accountingLifecycle?.AuditTraceability
                ?? workflow?.Evidence.FirstOrDefault(static evidence => string.Equals(evidence.Label, "Audit", StringComparison.OrdinalIgnoreCase))?.Value
                ?? "Audit trail ready";
        var auditDetail = !string.IsNullOrWhiteSpace(accountingLifecycle?.AuditTraceability) && latestNotification is null
            ? accountingLifecycle.AuditTraceability
            : latestNotification is null
            ? "Audit evidence and sign-off history remain available from the shared Accounting shell."
            : $"Latest approval signal: {latestNotification.Title} at {latestNotification.Timestamp:t}.";

        return new AccountingLaneSummaries(
            Accounting: new AccountingLaneSummary(accountingSummary, accountingDetail),
            Reconciliation: new AccountingLaneSummary(reconciliationSummary, reconciliationDetail),
            Reporting: new AccountingLaneSummary(reportingSummary, reportingDetail),
            Audit: new AccountingLaneSummary(auditSummary, auditDetail));
    }

    internal static AccountingLaneHeroState BuildLaneHeroState(
        AccountingSubarea subarea,
        WorkstationOperatingContext? operatingContext,
        FundProfileDetail? profile,
        FundOperationsWorkspaceDto? workspace,
        WorkspaceWorkflowSummary? workflow,
        IReadOnlyList<NotificationHistoryItem> notifications,
        int unreadAlerts)
    {
        var laneLabel = GetLaneLabel(subarea);

        if (profile is null || workspace is null)
        {
            return new AccountingLaneHeroState(
                LaneLabel: laneLabel,
                Summary: $"{laneLabel} review is waiting for a fund-linked context.",
                Detail: GetLockedLaneDetail(subarea, operatingContext),
                HandoffTitle: workflow?.StatusLabel ?? "Context required",
                HandoffDetail: workflow?.StatusDetail ?? "Switch context to unlock accounting queues for the selected lane.",
                PrimaryActionId: "SwitchContext",
                PrimaryActionLabel: "Switch Context",
                SecondaryActionId: "Diagnostics",
                SecondaryActionLabel: "Open Diagnostics",
                TargetLabel: "Target page: Context selector");
        }

        var ledger = workspace.Ledger;
        var reconciliation = workspace.Reconciliation;
        var cash = workspace.CashFinancing;
        var reporting = workspace.Reporting;
        var accountingLifecycle = workspace.Governance;
        var latestNotification = notifications.FirstOrDefault();
        var workflowBlockerCode = workflow?.PrimaryBlocker.Code;
        var workflowCarriesReconciliationBlocker =
            string.Equals(workflowBlockerCode, "as-of-drift", StringComparison.OrdinalIgnoreCase)
            || string.Equals(workflowBlockerCode, "missing-ledger", StringComparison.OrdinalIgnoreCase)
            || string.Equals(workflowBlockerCode, "missing-reconciliation", StringComparison.OrdinalIgnoreCase);

        return subarea switch
        {
            AccountingSubarea.Operations => new AccountingLaneHeroState(
                LaneLabel: laneLabel,
                Summary: ledger.JournalEntryCount > 0
                    ? $"{ledger.JournalEntryCount} journals ready for operations review"
                    : "Operations snapshot pending",
                Detail: $"{workspace.Workspace.TotalAccounts} linked account(s), {ledger.EntityCount} entities, and {ledger.VehicleCount} vehicles stay aligned inside the Accounting shell.",
                HandoffTitle: ledger.JournalEntryCount > 0 ? "Open operations lane" : "Restore operations baseline",
                HandoffDetail: ledger.JournalEntryCount > 0
                    ? "Keep ledger, account, and bank posture docked before moving into accounting or reconciliation."
                    : "Open operations first and confirm the shared ledger snapshot before downstream review.",
                PrimaryActionId: "FundLedger",
                PrimaryActionLabel: "Open Operations",
                SecondaryActionId: "FundAccounts",
                SecondaryActionLabel: "Accounts",
                TargetLabel: "Target page: FundLedger"),
            AccountingSubarea.Accounting => new AccountingLaneHeroState(
                LaneLabel: laneLabel,
                Summary: ledger.TrialBalance.Count > 0
                    ? $"{ledger.TrialBalance.Count} trial-balance lines ready"
                    : "Accounting snapshot pending",
                Detail: ledger.TrialBalance.Count > 0
                    ? $"{ledger.JournalEntryCount} journal(s) are available for continuity, accrual, and sign-off review."
                    : "The accounting lane becomes actionable once the shared ledger snapshot is available.",
                HandoffTitle: ledger.TrialBalance.Count > 0 ? "Open accounting lane" : "Wait for shared ledger data",
                HandoffDetail: ledger.TrialBalance.Count > 0
                    ? "Review journals, trial balance, and financing posture together before sign-off."
                    : "Use operations first to restore the ledger baseline before accounting review.",
                PrimaryActionId: "FundTrialBalance",
                PrimaryActionLabel: "Open Accounting",
                SecondaryActionId: ledger.TrialBalance.Count > 0 ? "FundCashFinancing" : "FundLedger",
                SecondaryActionLabel: ledger.TrialBalance.Count > 0 ? "Reporting" : "Operations",
                TargetLabel: "Target page: FundTrialBalance"),
            AccountingSubarea.Reconciliation => new AccountingLaneHeroState(
                LaneLabel: laneLabel,
                Summary: !string.IsNullOrWhiteSpace(accountingLifecycle?.DecisionPosture)
                    ? accountingLifecycle.DecisionPosture
                    : reconciliation.OpenBreakCount > 0
                        ? $"{reconciliation.OpenBreakCount} break(s) open"
                        : workflowCarriesReconciliationBlocker
                            ? workflow?.StatusLabel ?? "Reconciliation review pending"
                            : "Matched and review-ready",
                Detail: !string.IsNullOrWhiteSpace(accountingLifecycle?.SignoffPosture)
                    ? $"{accountingLifecycle.SignoffPosture} {accountingLifecycle.CloseReadiness}".Trim()
                    : workflowCarriesReconciliationBlocker
                        ? workflow?.PrimaryBlocker.Detail ?? "Reconciliation review is waiting on the current approval blocker."
                        : reconciliation.OpenBreakCount > 0
                            ? workflow?.PrimaryBlocker.Detail ?? $"{reconciliation.OpenBreakCount} break(s) block close sign-off until the queue is reviewed."
                            : $"{reconciliation.RunCount} reconciliation run(s) are linked for the current scope with {reconciliation.SecurityCoverageIssueCount} coverage issue(s).",
                HandoffTitle: reconciliation.OpenBreakCount > 0
                    ? "Review breaks before approval release"
                    : workflowCarriesReconciliationBlocker
                        ? workflow?.PrimaryBlocker.Label ?? "Resolve reconciliation blocker"
                        : "Open reconciliation lane",
                HandoffDetail: reconciliation.OpenBreakCount > 0
                    ? "Inspect breaks, security coverage, and related audit evidence before releasing approvals."
                    : workflowCarriesReconciliationBlocker
                        ? workflow?.StatusDetail ?? "Reconciliation review should stay paused until the blocker clears."
                        : "Matched runs, security coverage, and trial-balance continuity stay one action away from the same shell.",
                PrimaryActionId: "FundReconciliation",
                PrimaryActionLabel: reconciliation.OpenBreakCount > 0 ? "Review Breaks" : "Open Review",
                SecondaryActionId: reconciliation.OpenBreakCount > 0 || reconciliation.SecurityCoverageIssueCount > 0 ? "FundAuditTrail" : "FundTrialBalance",
                SecondaryActionLabel: reconciliation.OpenBreakCount > 0 || reconciliation.SecurityCoverageIssueCount > 0 ? "Audit Trail" : "Trial Balance",
                TargetLabel: "Target page: FundReconciliation"),
            AccountingSubarea.Reporting => new AccountingLaneHeroState(
                LaneLabel: laneLabel,
                Summary: reporting.ProfileCount > 0
                    ? $"{reporting.ProfileCount} report profile(s) ready"
                    : "Reporting handoff ready",
                Detail: $"Cash {cash.TotalCash:C0}, financing {cash.FinancingCost:C0}, and {BuildReportPackTargetLabel(reporting)} stay aligned for operator handoff.",
                HandoffTitle: reporting.ProfileCount > 0 ? "Prepare report pack" : "Open reporting lane",
                HandoffDetail: reporting.ProfileCount > 0
                    ? "Review cash posture first, then package board and operator outputs without leaving Accounting."
                    : "Cash and financing posture are available even before dedicated report profiles are configured.",
                PrimaryActionId: "FundCashFinancing",
                PrimaryActionLabel: "Open Reporting",
                SecondaryActionId: "FundReportPack",
                SecondaryActionLabel: "Open Report Pack",
                TargetLabel: "Target page: FundCashFinancing"),
            AccountingSubarea.Audit => new AccountingLaneHeroState(
                LaneLabel: laneLabel,
                Summary: unreadAlerts > 0
                    ? $"{unreadAlerts} unread alert(s)"
                    : accountingLifecycle?.AuditTraceability
                        ?? workflow?.Evidence.FirstOrDefault(static evidence => string.Equals(evidence.Label, "Audit", StringComparison.OrdinalIgnoreCase))?.Value
                        ?? "Audit trail ready",
                Detail: !string.IsNullOrWhiteSpace(accountingLifecycle?.AuditTraceability) && latestNotification is null
                    ? accountingLifecycle.AuditTraceability
                    : latestNotification is null
                    ? "Audit evidence, alerts, and operator sign-off history remain attached to the current Accounting scope."
                    : $"Latest approval signal: {latestNotification.Title} at {latestNotification.Timestamp:t}.",
                HandoffTitle: unreadAlerts > 0 ? "Review unread alerts" : "Open audit trail",
                HandoffDetail: unreadAlerts > 0
                    ? "Keep alerts, diagnostics, and sign-off evidence together before releasing approvals."
                    : "Use the audit trail to validate recent accounting activity before the handoff leaves the shell.",
                PrimaryActionId: "FundAuditTrail",
                PrimaryActionLabel: "Open Audit",
                SecondaryActionId: "NotificationCenter",
                SecondaryActionLabel: "Open Alerts",
                TargetLabel: "Target page: FundAuditTrail"),
            _ => new AccountingLaneHeroState(
                LaneLabel: "Operations",
                Summary: "Operations snapshot pending",
                Detail: "Select an accounting lane to continue.",
                HandoffTitle: "Open operations lane",
                HandoffDetail: "The Accounting shell defaults to the operations lane.",
                PrimaryActionId: "FundLedger",
                PrimaryActionLabel: "Open Operations",
                SecondaryActionId: "FundAccounts",
                SecondaryActionLabel: "Accounts",
                TargetLabel: "Target page: FundLedger")
        };
    }

    internal static string ResolveLanePrimaryActionId(AccountingSubarea subarea) => subarea switch
    {
        AccountingSubarea.Operations => "FundLedger",
        AccountingSubarea.Accounting => "FundTrialBalance",
        AccountingSubarea.Reconciliation => "FundReconciliation",
        AccountingSubarea.Reporting => "FundCashFinancing",
        AccountingSubarea.Audit => "FundAuditTrail",
        _ => "FundLedger"
    };

    private static string GetLaneLabel(AccountingSubarea subarea) => subarea switch
    {
        AccountingSubarea.Operations => "Operations",
        AccountingSubarea.Accounting => "Accounting",
        AccountingSubarea.Reconciliation => "Reconciliation",
        AccountingSubarea.Reporting => "Reporting",
        AccountingSubarea.Audit => "Audit",
        _ => "Operations"
    };

    private static string GetLockedLaneDetail(AccountingSubarea subarea, WorkstationOperatingContext? operatingContext)
    {
        var scopePrefix = operatingContext is null
            ? "Select a fund-linked context first."
            : $"Link {operatingContext.DisplayName} to a fund profile first.";

        return subarea switch
        {
            AccountingSubarea.Operations => $"{scopePrefix} Operations, accounts, and banking review stay locked until then.",
            AccountingSubarea.Accounting => $"{scopePrefix} Trial-balance and journal review stay locked until then.",
            AccountingSubarea.Reconciliation => $"{scopePrefix} Break triage and security coverage review stay locked until then.",
            AccountingSubarea.Reporting => $"{scopePrefix} Cash, financing, and report-pack handoff stay locked until then.",
            AccountingSubarea.Audit => $"{scopePrefix} Audit evidence and approval history stay locked until then.",
            _ => $"{scopePrefix} Accounting queues stay locked until then."
        };
    }

    private static string BuildReportPackTargetLabel(FundReportingSummaryDto reporting)
    {
        if (reporting.ReportPackTargets.Count == 0)
        {
            return "board and operator packs";
        }

        return string.Join(", ", reporting.ReportPackTargets);
    }

    private static FinancialOperationsWorkflowStep CreateFinancialOperationsWorkflowStep(
        string id,
        string label,
        string description,
        string status,
        string statusLabel,
        string tone,
        string targetPageTag)
        => new(
            Id: id,
            Label: label,
            Description: description,
            Status: status,
            StatusLabel: statusLabel,
            Tone: tone,
            TargetPageTag: targetPageTag);

    internal sealed record AccountingLaneSummary(string Summary, string Detail);

    internal sealed record AccountingLaneSummaries(
        AccountingLaneSummary Accounting,
        AccountingLaneSummary Reconciliation,
        AccountingLaneSummary Reporting,
        AccountingLaneSummary Audit);

    internal sealed record AccountingLaneHeroState(
        string LaneLabel,
        string Summary,
        string Detail,
        string HandoffTitle,
        string HandoffDetail,
        string PrimaryActionId,
        string PrimaryActionLabel,
        string SecondaryActionId,
        string SecondaryActionLabel,
        string TargetLabel);

    internal sealed record FinancialOperationsWorkflowStep(
        string Id,
        string Label,
        string Description,
        string Status,
        string StatusLabel,
        string Tone,
        string TargetPageTag)
    {
        public string AutomationName => $"{Label}: {StatusLabel}";
    }
}
