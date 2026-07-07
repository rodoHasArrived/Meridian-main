import { buildEvidenceTimelineCandidate, type EvidenceTimelineCandidate } from "@/app-shell.evidence-timeline";
import { pluralizeCount } from "@/lib/format";
import {
  normalizeLocalWorkstationRoute,
  WORKSTATION_ROUTE_CATALOG
} from "@/lib/workspace";
import type { AccountingWorkspaceResponse } from "@/types";

export function buildAccountingEvidenceTimelineItems(accounting: AccountingWorkspaceResponse | null): EvidenceTimelineCandidate[] {
  if (!accounting) {
    return [];
  }

  return [
    ...(accounting.breakQueue ?? []).map((item, index) => buildEvidenceTimelineCandidate({
      id: `accounting-break:${item.breakId}`,
      label: `${item.category} break ${item.status.toLowerCase()}`,
      detail: item.recommendedAction ?? item.explainabilitySummary ?? item.reason,
      route: normalizeLocalWorkstationRoute(item.routingTarget) ?? WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
      workspaceLabel: "Accounting",
      timestamp: item.lastUpdatedAt,
      tone: item.status === "Open" ? "blocked" : item.status === "InReview" ? "review" : "ready",
      sourcePriority: 10,
      sourceIndex: index
    })),
    ...(accounting.reconciliationQueue ?? []).map((row, index) => buildEvidenceTimelineCandidate({
      id: `accounting-reconciliation:${row.runId}`,
      label: `${row.strategyName} reconciliation ${row.reconciliationStatus}`,
      detail: `${formatAccountingEvidenceCount(row.openBreakCount, "open break")} across ${formatAccountingEvidenceCount(row.breakCount, "total break")}. Status: ${row.status}.`,
      route: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
      workspaceLabel: "Accounting",
      timestamp: row.lastUpdated,
      tone: row.openBreakCount > 0 ? row.reconciliationStatus === "SecurityCoverageOpen" ? "blocked" : "review" : "ready",
      sourcePriority: 11,
      sourceIndex: index
    }))
  ].filter((item): item is EvidenceTimelineCandidate => Boolean(item));
}

function formatAccountingEvidenceCount(count: number, singular: string, plural = `${singular}s`): string {
  return pluralizeCount(count, singular, { plural });
}
