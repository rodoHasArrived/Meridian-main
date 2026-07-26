import {
  appendLinkedContextSearchValue,
  buildLinkedContextItem,
  type AppShellLinkedContextItem
} from "@/app-shell.linked-context";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type { DataBackfillRecord, DataWorkspaceResponse } from "@/types";

export function buildDataLinkedContextItem(
  data: DataWorkspaceResponse | null,
  symbol: string
): AppShellLinkedContextItem {
  const route = appendLinkedContextSearchValue(WORKSTATION_ROUTE_CATALOG.dataQuotes, "symbol", symbol);
  if (!data) {
    return buildLinkedContextItem({
      id: "data-quotes",
      label: "Quote evidence",
      detail: `Open quote, tape, depth, and history evidence for ${symbol}.`,
      route,
      workspaceLabel: "Data",
      statusLabel: "Waiting",
      tone: "pending"
    });
  }

  const providerAttention = (data.providers ?? []).find((provider) => provider.status !== "Healthy");
  if (providerAttention) {
    return buildLinkedContextItem({
      id: "data-quotes",
      label: "Quote evidence",
      detail: providerAttention.recommendedAction || providerAttention.note || `${providerAttention.provider} requires review before trusting ${symbol}.`,
      route,
      workspaceLabel: "Data",
      statusLabel: providerAttention.status === "Blocked"
        ? "Provider blocked"
        : providerAttention.status === "Degraded"
          ? "Provider degraded"
          : "Provider review",
      tone: providerAttention.status === "Degraded" || providerAttention.status === "Blocked"
        ? "blocked"
        : "review"
    });
  }

  const backfill = findSymbolBackfill(data.backfills ?? [], symbol);
  if (backfill) {
    return buildLinkedContextItem({
      id: "data-quotes",
      label: "Quote evidence",
      detail: `${backfill.provider} backfill ${backfill.status.toLowerCase()} at ${backfill.progress}.`,
      route,
      workspaceLabel: "Data",
      statusLabel: backfill.status === "Review" ? "Backfill review" : "Backfill active",
      tone: backfill.status === "Review" ? "review" : "pending"
    });
  }

  return buildLinkedContextItem({
    id: "data-quotes",
    label: "Quote evidence",
    detail: `Live quote, alert, and historical evidence routes retain ${symbol}.`,
    route,
    workspaceLabel: "Data",
    statusLabel: "Trusted",
    tone: "ready"
  });
}

function findSymbolBackfill(backfills: DataBackfillRecord[], symbol: string): DataBackfillRecord | null {
  return backfills.find((backfill) => backfill.scope.toUpperCase().includes(symbol)) ?? null;
}
