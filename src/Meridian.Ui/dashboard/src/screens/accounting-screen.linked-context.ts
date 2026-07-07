import {
  appendLinkedContextSearchValue,
  buildLinkedContextItem,
  type AppShellLinkedContextItem
} from "@/app-shell.linked-context";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { pluralizeCount } from "@/lib/format";
import type { AccountingWorkspaceResponse } from "@/types";

export function buildAccountingLinkedContextItem(
  accounting: AccountingWorkspaceResponse | null,
  symbol: string
): AppShellLinkedContextItem {
  const route = appendLinkedContextSearchValue(WORKSTATION_ROUTE_CATALOG.accountingReconciliation, "symbol", symbol);
  if (!accounting) {
    return buildLinkedContextItem({
      id: "accounting-reconciliation",
      label: "Reconciliation",
      detail: `Open break queue and ledger context with ${symbol} retained.`,
      route,
      workspaceLabel: "Accounting",
      statusLabel: "Waiting",
      tone: "pending"
    });
  }

  const breakCount = (accounting.breakQueue ?? []).filter((item) => item.status === "Open" || item.status === "InReview").length;
  const runBreakCount = (accounting.reconciliationQueue ?? []).reduce((total, row) => total + row.openBreakCount, 0);
  const attentionCount = Math.max(breakCount, runBreakCount);
  return buildLinkedContextItem({
    id: "accounting-reconciliation",
    label: "Reconciliation",
    detail: attentionCount > 0
      ? `${formatAccountingLinkedContextCount(attentionCount, "open break")} can affect ${symbol} ledger confidence.`
      : `No open reconciliation breaks are loaded while reviewing ${symbol}.`,
    route,
    workspaceLabel: "Accounting",
    statusLabel: attentionCount > 0 ? "Breaks open" : "Balanced",
    tone: attentionCount > 0 ? "review" : "ready"
  });
}

function formatAccountingLinkedContextCount(count: number, singular: string, plural = `${singular}s`): string {
  return pluralizeCount(count, singular, { plural });
}
