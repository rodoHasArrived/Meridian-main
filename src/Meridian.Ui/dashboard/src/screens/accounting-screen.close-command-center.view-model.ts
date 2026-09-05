import {
  normalizeLocalWorkstationRoute,
  WORKSTATION_ROUTE_CATALOG,
} from "@/lib/workspace";
import { formatCount } from "./accounting-screen.formatting";
import {
  closeCommandCenterTextMatches,
  humanizeCloseBlockerCode,
  isCloseChecklistDone,
  isOpenAccountingBreakStatus,
} from "./accounting-screen.close-cockpit-presenters";
import type {
  AccountingToolingTone,
  CloseCommandCenterActionViewModel,
  CloseCommandCenterMetricViewModel,
  CloseCommandCenterStatus,
  CloseCommandCenterViewState,
} from "./accounting-screen.view-model";
import type {
  AccountingSystemImportDetail,
  AccountingSystemProvider,
  AccountingSystemReconciliationSummary,
  AccountingWorkspaceResponse,
  DailyValuationScheduleWorkItem,
  FinancialOperationsCommandCenter,
  MultiAssetCoverageSummary,
  OperationsContinuityWorkflow,
  OperationsWorkflowBlocker,
} from "@/types";

interface CloseCommandCenterRawBlocker {
  code: string;
  category: string;
  severity: string;
  message: string;
  gate: OperationsWorkflowBlocker["gate"];
  routeHint: string | null;
}

export function buildCloseCommandCenterViewState({
  data,
  commandCenter,
  commandCenterLoading,
  commandCenterError,
  workflow,
  workflowLoading,
  workflowError,
  accountingSystemProviders,
  accountingSystemImport,
  accountingSystemReconciliation,
  multiAssetCoverage,
  currentDailyValuationSchedule
}: {
  data: AccountingWorkspaceResponse;
  commandCenter?: FinancialOperationsCommandCenter | null;
  commandCenterLoading?: boolean;
  commandCenterError?: string | null;
  workflow: OperationsContinuityWorkflow | null;
  workflowLoading: boolean;
  workflowError: string | null;
  accountingSystemProviders: AccountingSystemProvider[];
  accountingSystemImport: AccountingSystemImportDetail | null;
  accountingSystemReconciliation: AccountingSystemReconciliationSummary | null;
  multiAssetCoverage: MultiAssetCoverageSummary | null | undefined;
  currentDailyValuationSchedule?: DailyValuationScheduleWorkItem | null;
}): CloseCommandCenterViewState {
  if (commandCenter) {
    return buildSharedFinancialOperationsCommandCenterViewState(
      commandCenter,
      commandCenterLoading ?? workflowLoading,
      commandCenterError ?? workflowError,
      currentDailyValuationSchedule ?? null
    );
  }

  const openBreakCount = data.breakQueue.filter((item) => isOpenAccountingBreakStatus(item.status)).length;
  const workflowOpenBreakCount = workflow?.breakCases.filter((item) => isOpenAccountingBreakStatus(item.status)).length ?? 0;
  const closeBlockers = workflow ? collectCloseCommandCenterBlockers(workflow) : [];
  const incompleteEvidenceCategories = workflow?.accountingRecordSummary?.evidenceCategories.filter((category) => !category.isComplete) ?? [];
  const missingSourceCount = incompleteEvidenceCategories.filter((category) => closeCommandCenterTextMatches(category.key, category.label, "source")).length
    + (workflow?.closeChecklist.filter((task) => !isCloseChecklistDone(task.status) && !task.evidencePointer).length ?? 0);
  const pendingApprovalCount = workflow?.approvals.filter((approval) => approval.status !== "Approved").length ?? 0;
  const unapprovedChecklistCount = workflow?.closeChecklist.filter((task) => !isCloseChecklistDone(task.status) && task.requiredApprovalCount > 0).length ?? 0;
  const unapprovedAdjustmentCount = pendingApprovalCount + unapprovedChecklistCount;
  const staleValuationCount = countCloseCommandCenterValuationIssues(multiAssetCoverage);
  const providerWarningCount = countCloseCommandCenterProviderWarnings(accountingSystemProviders, accountingSystemImport, accountingSystemReconciliation);
  const reportPackReady = workflow?.reportPackReadiness.isReady ?? false;
  const reportPackLabel = workflow
    ? reportPackReady
      ? workflow.reportPackReadiness.reportPackId ?? "Ready"
      : "Not ready"
    : "Detail pending";
  const signOffStatus = resolveCloseCommandCenterSignOffStatus(workflow);
  // Legacy lane metrics are diagnostic only; readiness requires the shared server projection.
  const status: CloseCommandCenterStatus = workflowLoading && !workflow ? "loading" : "blocked";
  const statusTone = closeCommandCenterStatusTone(status);
  const readinessLabel = workflow?.closeReadiness?.severity
    ?? workflow?.status
    ?? data.controlCenter?.closeReadiness
    ?? "Close detail pending";
  const updatedLabel = workflow?.updatedAtUtc ?? accountingSystemReconciliation?.generatedAtUtc ?? multiAssetCoverage?.asOfUtc ?? "Not refreshed";

  const metricRows: CloseCommandCenterMetricViewModel[] = [
    {
      id: "period",
      label: "Period status",
      value: readinessLabel,
      detail: workflow ? `${workflow.status} workflow ${workflow.workflowId}` : "Using workspace close status until workflow detail loads.",
      tone: statusTone,
      href: workflow ? WORKSTATION_ROUTE_CATALOG.accountingApprovals : null
    },
    {
      id: "breaks",
      label: "Open breaks",
      value: String(openBreakCount + workflowOpenBreakCount),
      detail: `${formatCount(openBreakCount, "queue break")} and ${formatCount(workflowOpenBreakCount, "workflow break")} remain open.`,
      tone: openBreakCount + workflowOpenBreakCount > 0 ? "warning" : "success",
      href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation
    },
    {
      id: "source-files",
      label: "Missing source files",
      value: String(missingSourceCount),
      detail: missingSourceCount > 0 ? "Required source evidence or checklist evidence pointers are incomplete." : "Required source evidence is retained.",
      tone: missingSourceCount > 0 ? "warning" : "success",
      href: WORKSTATION_ROUTE_CATALOG.reportingEvidence
    },
    {
      id: "adjustments",
      label: "Unapproved adjustments",
      value: String(unapprovedAdjustmentCount),
      detail: unapprovedAdjustmentCount > 0 ? "Pending approvals or checklist controls remain before sign-off." : "No pending approval controls are surfaced.",
      tone: unapprovedAdjustmentCount > 0 ? "warning" : "success",
      href: WORKSTATION_ROUTE_CATALOG.accountingApprovals
    },
    {
      id: "valuations",
      label: "Stale valuations",
      value: String(staleValuationCount),
      detail: staleValuationCount > 0 ? "Asset readiness signals include valuation or stale-data review items." : "No stale valuation blockers are surfaced.",
      tone: staleValuationCount > 0 ? "warning" : "success",
      href: multiAssetCoverage?.drillThroughRoutes.coverage ?? null
    },
    {
      id: "providers",
      label: "Provider warnings",
      value: String(providerWarningCount),
      detail: providerWarningCount > 0 ? "Provider, external GL, or import reconciliation warnings need review." : "Provider and external GL evidence is clean.",
      tone: providerWarningCount > 0 ? "warning" : "success",
      href: WORKSTATION_ROUTE_CATALOG.accountingLedger
    },
    {
      id: "report-pack",
      label: "Report-pack readiness",
      value: reportPackLabel,
      detail: workflow?.reportPackReadiness.blockingReason ?? "Report-pack readiness comes from the selected close workflow.",
      tone: reportPackReady ? "success" : workflow ? "warning" : "default",
      href: WORKSTATION_ROUTE_CATALOG.reportingEvidence
    },
    {
      id: "signoff",
      label: "Sign-off status",
      value: signOffStatus.label,
      detail: signOffStatus.detail,
      tone: signOffStatus.tone,
      href: WORKSTATION_ROUTE_CATALOG.accountingApprovals
    }
  ];

  const blockerRows = [
    ...closeBlockers.map((blocker) => ({
      id: blocker.code,
      label: blocker.category?.trim() || humanizeCloseBlockerCode(blocker.code),
      detail: blocker.message,
      tone: closeCommandCenterSeverityTone(blocker.severity),
      href: blocker.routeHint ?? closeCommandCenterGateRoute(blocker.gate),
      statusLabel: blocker.severity,
      ownerLabel: null,
      dueLabel: null,
      evidenceLabel: "Evidence pending",
      actionLabel: "Resolve blocker before sign-off.",
      impactLabel: blocker.category
    })),
    ...incompleteEvidenceCategories.slice(0, 3).map((category) => ({
      id: `evidence-${category.key}`,
      label: category.label,
      detail: category.requiredEvidence?.length
        ? `Missing: ${category.requiredEvidence.join(", ")}`
        : category.status,
      tone: "warning" as AccountingToolingTone,
      href: category.routeHint,
      statusLabel: category.status,
      ownerLabel: null,
      dueLabel: null,
      evidenceLabel: category.requiredEvidence?.length ? formatCount(category.requiredEvidence.length, "required item") : "Evidence pending",
      actionLabel: "Attach retained evidence before sign-off.",
      impactLabel: "Retained evidence"
    })),
    ...((data.controlCenter?.alerts ?? []).slice(0, 2).map((alert, index) => ({
      id: `bootstrap-alert-${index}`,
      label: "Control-center alert",
      detail: alert.message,
      tone: alert.tone === "danger" ? "danger" as AccountingToolingTone : "warning" as AccountingToolingTone,
      href: null,
      statusLabel: alert.tone === "danger" ? "Blocked" : "Review",
      ownerLabel: null,
      dueLabel: null,
      evidenceLabel: "Control-center alert",
      actionLabel: "Review alert before close release.",
      impactLabel: "Control center"
    })))
  ].slice(0, 8);
  return {
    title: "CFO / Controller close command center",
    description: "Controller-facing period readiness, close blockers, evidence gaps, provider warnings, report-pack readiness, and sign-off status from shared Accounting read models.",
    ariaLabel: "CFO and controller close command center",
    status,
    statusLabel: status === "loading" ? "Loading" : "Blocked",
    statusTone,
    periodLabel: workflow?.periodId ?? "Current period",
    fundAccountLabel: workflow?.fundAccountId ?? multiAssetCoverage?.fundAccountId ?? "All accounts",
    summary: "Shared close readiness is unavailable. Select the full close scope and refresh before sign-off.",
    updatedLabel,
    updatedAtUtc: workflow?.updatedAtUtc ?? accountingSystemReconciliation?.generatedAtUtc ?? multiAssetCoverage?.asOfUtc ?? null,
    metricRows,
    blockerRows,
    actionRows: [
      {
        id: "reconciliation",
        label: "Review breaks",
        href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
        ariaLabel: "Open Accounting reconciliation breaks from close command center",
        tone: openBreakCount + workflowOpenBreakCount > 0 ? "warning" : "success"
      },
      {
        id: "approvals",
        label: "Open approvals",
        href: WORKSTATION_ROUTE_CATALOG.accountingApprovals,
        ariaLabel: "Open Accounting approvals from close command center",
        tone: unapprovedAdjustmentCount > 0 || signOffStatus.tone !== "success" ? "warning" : "success"
      },
      {
        id: "reporting",
        label: "Report evidence",
        href: WORKSTATION_ROUTE_CATALOG.reportingEvidence,
        ariaLabel: "Open Reporting evidence from close command center",
        tone: reportPackReady ? "success" : "warning"
      }
    ],
    loadingText: workflowLoading ? "Refreshing close workflow detail." : null,
    errorText: workflowError,
    liveRegionText: `Close command center ${status}. ${readinessLabel}. ${formatCount(blockerRows.length, "visible blocker")}.`
  };
}

function buildSharedFinancialOperationsCommandCenterViewState(
  commandCenter: FinancialOperationsCommandCenter,
  loading: boolean,
  errorText: string | null,
  currentDailyValuationSchedule: DailyValuationScheduleWorkItem | null
): CloseCommandCenterViewState {
  const projection = commandCenter.closeReadiness;
  const status = mapCommandCenterStatus(projection?.isComplete && projection.isReadyToClose ? projection.status : "Blocked", loading);
  const statusTone = closeCommandCenterStatusTone(status);
  const closeSupportDecision = commandCenter.closeSupportDecision ?? null;
  const closeSupportMetrics: CloseCommandCenterMetricViewModel[] = closeSupportDecision
    ? [
      {
        id: "close-support-period-state",
        label: "Period state",
        value: closeSupportDecision.status,
        detail: closeSupportDecision.periodState,
        tone: commandCenterMetricTone(closeSupportDecision.status),
        href: WORKSTATION_ROUTE_CATALOG.accountingApprovals
      },
      {
        id: "close-support-exceptions",
        label: "Unresolved exceptions",
        value: String(closeSupportDecision.unresolvedExceptionCount),
        detail: closeSupportDecision.lockReopenPosture,
        tone: closeSupportDecision.unresolvedExceptionCount > 0 ? "warning" : "success",
        href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation
      },
      {
        id: "close-support-approvals",
        label: "Pending approvals",
        value: String(closeSupportDecision.pendingApprovalCount),
        detail: closeSupportDecision.pendingApprovalCount > 0
          ? "Shared close-support approvals must clear before completion."
          : "No pending shared close-support approvals are surfaced.",
        tone: closeSupportDecision.pendingApprovalCount > 0 ? "warning" : "success",
        href: WORKSTATION_ROUTE_CATALOG.accountingApprovals
      },
      {
        id: "close-support-evidence",
        label: "Retained evidence gaps",
        value: String(closeSupportDecision.retainedEvidenceGapCount),
        detail: closeSupportDecision.navReportDependencyPosture,
        tone: closeSupportDecision.retainedEvidenceGapCount > 0 ? "warning" : "success",
        href: WORKSTATION_ROUTE_CATALOG.reportingEvidence
      }
    ]
    : [];
  const metricRows: CloseCommandCenterMetricViewModel[] = [
    ...commandCenter.metrics.map((metric) => ({
      id: metric.metricId,
      label: metric.label,
      value: metric.value,
      detail: metric.detail,
      tone: commandCenterMetricTone(metric.status),
      href: localCommandCenterRoute(metric.routeHint, metric.metricId)
    })),
    ...closeSupportMetrics
  ];
  const decisionBlockerRows = (closeSupportDecision?.decisions ?? [])
    .filter((decision) => decision.isBlocking)
    .map((decision) => ({
      id: decision.decisionId,
      label: `${decision.category} - ${decision.label}`,
      detail: `${decision.detail} ${decision.requiredAction}`.trim(),
      tone: "danger" as AccountingToolingTone,
      href: localCommandCenterRoute(decision.routeHint, decision.category),
      statusLabel: decision.status,
      ownerLabel: null,
      dueLabel: null,
      evidenceLabel: formatCount(decision.evidenceLinks.length, "evidence link"),
      actionLabel: decision.requiredAction,
      impactLabel: decision.category
    }));
  const queueBlockerRows = commandCenter.queueRows.map((row) => {
    const metadata = [
      row.severityLabel ? `Severity: ${row.severityLabel}.` : null,
      row.slaLabel ? `SLA: ${row.slaLabel}.` : null,
      row.blockerType ? `Blocker: ${row.blockerType}.` : null,
      row.closeReportImpact ? `Impact: ${row.closeReportImpact}.` : null
    ].filter(Boolean);

    return {
      id: row.queueId,
      label: `${row.kindLabel} - ${row.title}`,
      detail: [row.detail, row.actionLabel, ...metadata].join(" ").trim(),
      tone: row.isBlocked ? "danger" as AccountingToolingTone : "warning" as AccountingToolingTone,
      href: localCommandCenterRoute(row.routeHint, row.sourceKind),
      statusLabel: row.statusLabel,
      ownerLabel: row.ownerLabel,
      dueLabel: row.slaLabel ?? row.dueLabel,
      evidenceLabel: row.evidenceLabel,
      actionLabel: row.actionLabel,
      impactLabel: row.closeReportImpact ?? row.blockerType ?? row.kindLabel
    };
  });
  const blockerRows = [...decisionBlockerRows, ...queueBlockerRows].slice(0, 10);
  const routedRows = commandCenter.queueRows
    .filter((row) => localCommandCenterRoute(row.routeHint, row.sourceKind))
    .slice(0, 3);
  const routedDecisionRows = (closeSupportDecision?.decisions ?? [])
    .filter((decision) => localCommandCenterRoute(decision.routeHint, decision.category))
    .slice(0, 3);
  const baseActionRows: CloseCommandCenterActionViewModel[] = routedRows.length > 0
    ? routedRows.map((row) => ({
      id: row.queueId,
      label: row.actionLabel || row.title,
      href: localCommandCenterRoute(row.routeHint, row.sourceKind) ?? WORKSTATION_ROUTE_CATALOG.accountingApprovals,
      ariaLabel: `Open ${row.title} from close command center`,
      tone: row.isBlocked ? "warning" : "success"
    }))
    : routedDecisionRows.length > 0
      ? routedDecisionRows.map((decision) => ({
        id: decision.decisionId,
        label: decision.requiredAction || decision.label,
        href: localCommandCenterRoute(decision.routeHint, decision.category) ?? WORKSTATION_ROUTE_CATALOG.accountingApprovals,
        ariaLabel: `Open ${decision.label} close-support decision from close command center`,
        tone: decision.isBlocking ? "warning" : "success"
      }))
    : [
      {
        id: "financial-operations",
        label: commandCenter.isReadyToComplete ? "Review close evidence" : "Open operations",
        href: WORKSTATION_ROUTE_CATALOG.accountingApprovals,
        ariaLabel: "Open Accounting approvals from close command center",
        tone: commandCenter.isReadyToComplete ? "success" : "warning"
      }
    ];
  const dailyValuationStatus = commandCenter.privateCapitalCloseCockpit?.dailyValuationStatus ?? null;
  const hasRetainedValuationBatch = Boolean(dailyValuationStatus?.batchCorrelationId) &&
    (dailyValuationStatus?.journalEntryIds.length ?? 0) > 0;
  const configureScheduleDisabledReason = !currentDailyValuationSchedule
    ? "No server-retained daily valuation schedule is loaded for this close scope."
    : dailyValuationStatus?.state === "Running"
      ? "Wait for the running daily valuation schedule to finish before reconfiguring it."
      : dailyValuationStatus?.state === "DraftReady" ||
          (dailyValuationStatus?.state === "Blocked" && hasRetainedValuationBatch)
        ? "Complete or correct the retained daily valuation batch before reconfiguring its schedule."
        : null;
  const runDueScheduleDisabledReason = !currentDailyValuationSchedule || !dailyValuationStatus?.isConfigured
    ? "Configure a retained daily valuation schedule before running due work."
    : !dailyValuationStatus.isEnabled
      ? "Enable the retained daily valuation schedule before running due work."
      : dailyValuationStatus.state !== "Scheduled"
        ? `Run due is available only from Scheduled state; current state is ${dailyValuationStatus.state}.`
        : null;
  const dailyValuationScheduleActions: CloseCommandCenterActionViewModel[] = dailyValuationStatus || currentDailyValuationSchedule ? [
    {
      id: "daily-valuation-configure",
      label: currentDailyValuationSchedule ? "Configure current valuation schedule" : "Configure valuation schedule",
      href: WORKSTATION_ROUTE_CATALOG.accountingApprovals,
      ariaLabel: "Configure the server-retained daily valuation schedule for the current close scope",
      tone: configureScheduleDisabledReason ? "warning" : "success",
      command: "configure-daily-valuation-schedule",
      busyLabel: "Configuring daily valuation schedule",
      disabledReason: configureScheduleDisabledReason
    },
    {
      id: "daily-valuation-run-due",
      label: "Run due valuation schedules",
      href: WORKSTATION_ROUTE_CATALOG.accountingApprovals,
      ariaLabel: "Run due daily valuation schedules for the current tenant scope",
      tone: runDueScheduleDisabledReason ? "warning" : "success",
      command: "run-due-daily-valuation-schedules",
      busyLabel: "Running due daily valuation schedules",
      disabledReason: runDueScheduleDisabledReason
    }
  ] : [];
  const dailyValuationLifecycleAction: CloseCommandCenterActionViewModel[] =
    dailyValuationStatus?.state === "DraftReady" &&
    Boolean(dailyValuationStatus.scheduleId) &&
    Boolean(dailyValuationStatus.fundProfileId) &&
    dailyValuationStatus.journalEntryIds.length > 0
      ? [{
        id: "daily-valuation-approve-post",
        label: `Approve and post ${formatCount(dailyValuationStatus.journalEntryIds.length, "valuation draft")}`,
        href: WORKSTATION_ROUTE_CATALOG.accountingApprovals,
        ariaLabel: "Approve and post the complete retained daily valuation batch",
        tone: "warning",
        command: "approve-daily-valuation-batch",
        busyLabel: "Approving and posting daily valuation batch",
        disabledReason: null
      }]
      : dailyValuationStatus?.state === "Blocked" &&
          Boolean(dailyValuationStatus.scheduleId) &&
          Boolean(dailyValuationStatus.fundProfileId) &&
          hasRetainedValuationBatch
        ? [{
          id: "daily-valuation-retry-batch",
          label: `Correct and retry ${formatCount(dailyValuationStatus.journalEntryIds.length, "valuation draft")}`,
          href: WORKSTATION_ROUTE_CATALOG.accountingJournalEntries,
          ariaLabel: "Correct and retry the incomplete retained daily valuation batch",
          tone: "warning",
          command: "retry-daily-valuation-batch",
          busyLabel: "Retrying daily valuation batch",
          disabledReason: null
        }]
        : [];
  const actionRows = [...dailyValuationScheduleActions, ...dailyValuationLifecycleAction, ...baseActionRows].slice(0, 4);

  return {
    title: "CFO / Controller close command center",
    description: "Controller-facing period readiness, close blockers, evidence gaps, report-pack readiness, and sign-off status from Meridian Financial Operations review data.",
    ariaLabel: "CFO and controller close command center",
    status,
    statusLabel: status === "ready" ? "Ready" : status === "blocked" ? "Blocked" : status === "loading" ? "Loading" : "At risk",
    statusTone,
    periodLabel: commandCenter.periodId ?? "Current period",
    fundAccountLabel: commandCenter.fundAccountId ?? commandCenter.fundProfileId ?? "All accounts",
    summary: errorText ? `${closeSupportDecision?.summary ?? commandCenter.summary} ${errorText}` : closeSupportDecision?.summary ?? commandCenter.summary,
    updatedLabel: commandCenter.generatedAtUtc,
    updatedAtUtc: commandCenter.generatedAtUtc ?? null,
    metricRows,
    blockerRows,
    actionRows,
    loadingText: loading ? "Refreshing Financial Operations command center." : null,
    errorText,
    liveRegionText: `Close command center ${status}. ${closeSupportDecision?.summary ?? commandCenter.summary} ${formatCount(commandCenter.activeItemCount, "active item")}.`
  };
}

function mapCommandCenterStatus(status: string, loading: boolean): CloseCommandCenterStatus {
  if (loading) {
    return "loading";
  }

  const normalized = status.trim().toLowerCase();
  if (normalized === "ready") {
    return "ready";
  }

  if (normalized === "blocked") {
    return "blocked";
  }

  return "at-risk";
}

function commandCenterMetricTone(status: string): AccountingToolingTone {
  const normalized = status.trim().toLowerCase();
  if (normalized === "ready") {
    return "success";
  }

  if (normalized === "blocked" || normalized === "missing") {
    return "danger";
  }

  if (normalized === "review" || normalized === "reviewrequired" || normalized === "atrisk") {
    return "warning";
  }

  return "default";
}

function localCommandCenterRoute(route: string | null | undefined, sourceKind: string): string | null {
  return normalizeLocalWorkstationRoute(route)
    ?? commandCenterFallbackRoute(sourceKind);
}

function commandCenterFallbackRoute(sourceKind: string): string | null {
  const normalized = sourceKind.trim().toLowerCase();
  if (normalized.includes("break") || normalized.includes("reconciliation")) {
    return WORKSTATION_ROUTE_CATALOG.accountingReconciliation;
  }

  if (normalized.includes("approval") || normalized.includes("checklist") || normalized.includes("calendar") || normalized.includes("lock") || normalized.includes("reopen")) {
    return WORKSTATION_ROUTE_CATALOG.accountingApprovals;
  }

  if (normalized.includes("nav") || normalized.includes("private-capital")) {
    return WORKSTATION_ROUTE_CATALOG.accountingCapitalAccounts;
  }

  if (normalized.includes("evidence") || normalized.includes("report")) {
    return WORKSTATION_ROUTE_CATALOG.reportingEvidence;
  }

  return null;
}

function collectCloseCommandCenterBlockers(workflow: OperationsContinuityWorkflow): CloseCommandCenterRawBlocker[] {
  const workflowBlockers = workflow.blockers.map((blocker) => ({
    code: blocker.code,
    category: blocker.gate ?? "Workflow",
    severity: blocker.severity,
    message: blocker.message,
    gate: blocker.gate,
    routeHint: null
  }));
  const gateBlockers = workflow.gates.flatMap((gate) => gate.blockers.map((blocker) => ({
    code: blocker.code,
    category: gate.displayName,
    severity: blocker.severity,
    message: blocker.message,
    gate: blocker.gate,
    routeHint: closeCommandCenterGateRoute(gate.gateKey)
  })));
  const readinessBlockers = workflow.closeReadiness?.blockers.map((blocker) => ({
    code: blocker.code,
    category: blocker.category,
    severity: blocker.severity,
    message: blocker.message,
    gate: blocker.gate,
    routeHint: blocker.routeHint
  })) ?? [];

  const byKey = new Map<string, CloseCommandCenterRawBlocker>();
  for (const blocker of [...workflowBlockers, ...gateBlockers, ...readinessBlockers]) {
    byKey.set(`${blocker.code}:${blocker.message}`, blocker);
  }

  return [...byKey.values()];
}

function countCloseCommandCenterValuationIssues(coverage: MultiAssetCoverageSummary | null | undefined): number {
  if (!coverage) {
    return 0;
  }

  return coverage.assetClasses.filter((assetClass) => {
    const blockerMatch = assetClass.blockers.some((blocker) => (
      closeCommandCenterTextMatches(blocker.code, blocker.message, "valuation")
      || closeCommandCenterTextMatches(blocker.source, blocker.message, "stale")
    ));
    const requirementMatch = assetClass.evidenceRequirements.some((requirement) => (
      requirement.status !== "Ready"
      && (closeCommandCenterTextMatches(requirement.label, requirement.category, "valuation")
        || closeCommandCenterTextMatches(requirement.label, requirement.category, "stale"))
    ));

    return blockerMatch || requirementMatch;
  }).length;
}

function countCloseCommandCenterProviderWarnings(
  providers: AccountingSystemProvider[],
  importDetail: AccountingSystemImportDetail | null,
  reconciliation: AccountingSystemReconciliationSummary | null
): number {
  const unavailableProviders = providers.filter((provider) => provider.state !== "Available").length;
  const importWarnings = importDetail?.summary.warnings.length ?? 0;
  const reconciliationRows = reconciliation?.rows.filter((row) => row.status !== "Matched").length ?? 0;

  return unavailableProviders + importWarnings + reconciliationRows;
}

function resolveCloseCommandCenterSignOffStatus(workflow: OperationsContinuityWorkflow | null): {
  label: string;
  detail: string;
  tone: AccountingToolingTone;
} {
  if (!workflow) {
    return {
      label: "Detail pending",
      detail: "Load the close workflow before sign-off status can be confirmed.",
      tone: "default"
    };
  }

  if (workflow.closePackage) {
    return {
      label: `Signed by ${workflow.closePackage.publishedBy}`,
      detail: workflow.closePackage.signOffRationale,
      tone: "success"
    };
  }

  const approvedCount = workflow.approvals.filter((approval) => approval.status === "Approved").length;
  const totalApprovalCount = workflow.approvals.length;
  if (totalApprovalCount > 0) {
    return {
      label: `${approvedCount}/${totalApprovalCount} approved`,
      detail: approvedCount === totalApprovalCount
        ? "Approval rows are complete; publish the close package when report evidence is ready."
        : "Approval rows still need reviewer decisions before close sign-off.",
      tone: approvedCount === totalApprovalCount ? "success" : "warning"
    };
  }

  return {
    label: workflow.approvalState,
    detail: "No approval rows are attached to the selected close workflow.",
    tone: workflow.approvalState === "Approved" ? "success" : "warning"
  };
}

function closeCommandCenterSeverityTone(severity: string): AccountingToolingTone {
  const normalized = severity.trim().toLowerCase();
  if (normalized === "critical" || normalized === "blocker" || normalized === "danger") {
    return "danger";
  }

  if (normalized === "warning" || normalized === "warn" || normalized === "review") {
    return "warning";
  }

  return "default";
}

function closeCommandCenterStatusTone(status: CloseCommandCenterStatus): AccountingToolingTone {
  if (status === "ready") return "success";
  if (status === "blocked") return "danger";
  if (status === "at-risk") return "warning";
  return "default";
}

function closeCommandCenterGateRoute(gate: OperationsWorkflowBlocker["gate"]): string | null {
  if (gate === "Reconciliation") return WORKSTATION_ROUTE_CATALOG.accountingReconciliation;
  if (gate === "Approval") return WORKSTATION_ROUTE_CATALOG.accountingApprovals;
  if (gate === "LedgerPosting") return WORKSTATION_ROUTE_CATALOG.accountingLedger;
  if (gate === "SecurityMaster") return WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster;
  if (gate === "BrokerIngest") return WORKSTATION_ROUTE_CATALOG.accountingLedger;
  return null;
}

