import { buildOperatorFocusCandidate, type OperatorFocusCandidate } from "@/app-shell.operator-focus";
import {
  normalizeLocalWorkstationRoute,
  WORKSTATION_ROUTE_CATALOG,
  workflowTargetPath
} from "@/lib/workspace";
import type {
  AccountingWorkspaceResponse,
  OperatorWorkflowHomeSummary,
  ReconciliationBreakQueueItem,
  WorkspaceWorkflowSummary
} from "@/types";

export interface AccountingOperatorFocusInput {
  accounting: AccountingWorkspaceResponse | null;
  workflowSummary: OperatorWorkflowHomeSummary | null;
}

export function buildAccountingOperatorFocusItems({
  accounting,
  workflowSummary
}: AccountingOperatorFocusInput): OperatorFocusCandidate[] {
  const workflowSummaryItem = buildAccountingWorkflowSummaryFocusItem(workflowSummary);
  const reviewedAutomationItem = buildReviewedAutomationFocusItem(workflowSummary);

  return [
    workflowSummaryItem,
    reviewedAutomationItem,
    ...((accounting?.breakQueue ?? [])
      .map((item, index) => buildOperatorFocusCandidateFromBreak(item, index))
      .filter((item): item is OperatorFocusCandidate => Boolean(item))),
    ...((accounting?.reconciliationQueue ?? [])
      .filter((row) => row.openBreakCount > 0)
      .map((row, index) => buildOperatorFocusCandidate({
        id: `reconciliation-run:${row.runId}`,
        label: `${row.strategyName} reconciliation open`,
        detail: `${formatAccountingFocusCount(row.openBreakCount, "open break")} across ${formatAccountingFocusCount(row.breakCount, "total break")}. Status: ${row.reconciliationStatus}.`,
        route: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
        workspaceLabel: "Accounting",
        actionLabel: "Open reconciliation",
        tone: row.reconciliationStatus === "SecurityCoverageOpen" ? "blocked" : "review",
        sourcePriority: 13,
        sourceIndex: index
      }))),
    accounting?.cashFlow?.tone === "danger" || accounting?.cashFlow?.tone === "warning"
      ? buildOperatorFocusCandidate({
          id: "cash-flow-variance",
          label: "Cash-flow variance open",
          detail: accounting.cashFlow.summary,
          route: WORKSTATION_ROUTE_CATALOG.accounting,
          workspaceLabel: "Accounting",
          actionLabel: "Open ledger",
          tone: accounting.cashFlow.tone === "danger" ? "blocked" : "review",
          sourcePriority: 14,
          sourceIndex: 0
        })
      : null
  ].filter((item): item is OperatorFocusCandidate => Boolean(item));
}

function buildAccountingWorkflowSummaryFocusItem(workflowSummary: OperatorWorkflowHomeSummary | null): OperatorFocusCandidate | null {
  const summary = getAccountingWorkflowSummary(workflowSummary);
  if (!summary || !isFinancialOperationsWorkflowSummary(summary) || !summary.primaryBlocker?.isBlocking) {
    return null;
  }

  return buildOperatorFocusCandidate({
    id: `financial-operations:${summary.primaryBlocker.code}`,
    label: summary.primaryBlocker.label,
    detail: summary.primaryBlocker.detail || summary.statusDetail,
    route: workflowTargetPath(summary.nextAction?.targetPageTag, summary.workspaceId) ?? WORKSTATION_ROUTE_CATALOG.accounting,
    workspaceLabel: summary.workspaceTitle || "Accounting",
    actionLabel: summary.nextAction?.label || "Open Accounting",
    tone: workflowSummaryToneToOperatorFocusTone(summary.statusTone, summary.primaryBlocker),
    sourcePriority: 11,
    sourceIndex: 0
  });
}

function buildReviewedAutomationFocusItem(workflowSummary: OperatorWorkflowHomeSummary | null): OperatorFocusCandidate | null {
  const summary = getAccountingWorkflowSummary(workflowSummary);
  if (!summary || !isFinancialOperationsWorkflowSummary(summary) || summary.primaryBlocker?.isBlocking) {
    return null;
  }

  const automation = summary.evidence?.find((badge) => normalizeWorkflowText(badge.label) === "reviewed automation");
  if (!automation || !isReviewTone(automation.tone)) {
    return null;
  }

  return buildOperatorFocusCandidate({
    id: "financial-operations:reviewed-automation",
    label: "Reviewed automation requires operator review",
    detail: `${automation.value}; automation can suggest, classify, extract, match, summarize, draft, and flag, but cannot approve, post, publish, release payments, or erase evidence.`,
    route: workflowTargetPath(summary.nextAction?.targetPageTag, summary.workspaceId) ?? WORKSTATION_ROUTE_CATALOG.accounting,
    workspaceLabel: summary.workspaceTitle || "Accounting",
    actionLabel: summary.nextAction?.label || "Review automation",
    tone: "review",
    sourcePriority: 12,
    sourceIndex: 0
  });
}

function buildOperatorFocusCandidateFromBreak(
  item: ReconciliationBreakQueueItem,
  index: number
): OperatorFocusCandidate | null {
  if (item.status !== "Open" && item.status !== "InReview") {
    return null;
  }

  return buildOperatorFocusCandidate({
    id: `break:${item.breakId}`,
    label: `${item.category} break ${item.status === "Open" ? "open" : "in review"}`,
    detail: item.recommendedAction ?? item.explainabilitySummary ?? item.reason,
    route: normalizeLocalWorkstationRoute(item.routingTarget) ?? WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
    workspaceLabel: "Accounting",
    actionLabel: "Open break queue",
    tone: item.status === "Open" ? "blocked" : "review",
    sourcePriority: 12,
    sourceIndex: index
  });
}

function getAccountingWorkflowSummary(summary: OperatorWorkflowHomeSummary | null | undefined): WorkspaceWorkflowSummary | null {
  return summary?.workspaces?.find((workspace) => workspace.workspaceId.toLowerCase() === "accounting") ?? null;
}

function isFinancialOperationsWorkflowSummary(summary: WorkspaceWorkflowSummary): boolean {
  const sourceText = [
    summary.statusLabel,
    summary.statusDetail,
    summary.nextAction?.label,
    summary.primaryBlocker?.code,
    summary.primaryBlocker?.label,
    ...((summary.evidence ?? []).map((badge) => `${badge.label} ${badge.value}`))
  ].join(" ").toLowerCase();

  return sourceText.includes("financial operations") || sourceText.includes("core flow");
}

function workflowSummaryToneToOperatorFocusTone(
  statusTone: string | null | undefined,
  blocker: WorkspaceWorkflowSummary["primaryBlocker"]
): OperatorFocusCandidate["tone"] {
  const normalizedTone = normalizeWorkflowText(statusTone);
  if (normalizedTone.includes("success")) return "ready";
  if (normalizedTone.includes("danger") || normalizedTone.includes("error")) return "blocked";
  if (blocker?.isBlocking && normalizedTone.includes("warning")) return "review";
  if (normalizedTone.includes("warning") || normalizedTone.includes("info")) return "review";
  return blocker?.isBlocking ? "review" : "pending";
}

function isReviewTone(tone: string | null | undefined): boolean {
  const normalized = normalizeWorkflowText(tone);
  return normalized === "warning" || normalized === "review" || normalized === "reviewrequired";
}

function normalizeWorkflowText(value: string | null | undefined): string {
  return value?.trim().toLowerCase() ?? "";
}

function formatAccountingFocusCount(count: number, singular: string, plural = `${singular}s`): string {
  return `${count} ${count === 1 ? singular : plural}`;
}
