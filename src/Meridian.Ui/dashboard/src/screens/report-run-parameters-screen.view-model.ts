import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type { AccountingWorkspaceResponse, ManualJournalEntryDraft } from "@/types";

export type ReadinessGateTone = "success" | "warning";

export interface ReadinessGateItem {
  id: string;
  label: string;
  count: number;
  tone: ReadinessGateTone;
  href: string | null;
  linkLabel: string | null;
}

export interface ReadinessGateViewState {
  items: ReadinessGateItem[];
  isClear: boolean;
  disclaimer: string;
}

/**
 * Advisory-only readiness check. There is no backend endpoint that aggregates "open reconciliation
 * breaks + unposted journals for this report's exact fund/period" — this sums whatever
 * reconciliation-queue and manual-journal-draft data is already loaded client-side. It is a
 * heads-up for the operator, not a compliance-grade blocking gate; see the plan's cross-cutting
 * risk notes before treating a "clear" result as authoritative.
 */
export function buildReportRunReadinessGateViewState({
  reconciliationQueue,
  manualDrafts
}: {
  reconciliationQueue: AccountingWorkspaceResponse["reconciliationQueue"];
  manualDrafts: ManualJournalEntryDraft[];
}): ReadinessGateViewState {
  const openBreakCount = reconciliationQueue.reduce((sum, item) => sum + item.openBreakCount, 0);
  const unpostedJournalCount = manualDrafts.filter(
    (draft) => draft.status !== "Posted" && draft.status !== "Rejected" && draft.status !== "Reversed"
  ).length;

  const items: ReadinessGateItem[] = [
    {
      id: "open-breaks",
      label: "Open reconciliation breaks",
      count: openBreakCount,
      tone: openBreakCount > 0 ? "warning" : "success",
      href: openBreakCount > 0 ? WORKSTATION_ROUTE_CATALOG.accountingReconciliation : null,
      linkLabel: openBreakCount > 0 ? "Review breaks" : null
    },
    {
      id: "unposted-journals",
      label: "Unposted journal entries",
      count: unpostedJournalCount,
      tone: unpostedJournalCount > 0 ? "warning" : "success",
      href: unpostedJournalCount > 0 ? WORKSTATION_ROUTE_CATALOG.accountingJournalEntries : null,
      linkLabel: unpostedJournalCount > 0 ? "Review journal entries" : null
    }
  ];

  return {
    items,
    isClear: items.every((item) => item.count === 0),
    disclaimer: "Advisory only: this reflects accounting data already loaded in this workspace, not a fund/period-scoped compliance check. It does not block the run."
  };
}
