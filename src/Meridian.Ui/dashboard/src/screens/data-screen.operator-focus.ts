import { buildOperatorFocusCandidate, type OperatorFocusCandidate } from "@/app-shell.operator-focus";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type { DataBackfillRecord, DataProviderRecord, DataWorkspaceResponse } from "@/types";

export function buildDataOperatorFocusItems(data: DataWorkspaceResponse | null): OperatorFocusCandidate[] {
  if (!data) {
    return [];
  }

  return [
    ...(data.providers ?? [])
      .map((provider, index) => buildOperatorFocusCandidateFromProvider(provider, index))
      .filter((item): item is OperatorFocusCandidate => Boolean(item)),
    ...(data.backfills ?? [])
      .map((backfill, index) => buildOperatorFocusCandidateFromBackfill(backfill, index))
      .filter((item): item is OperatorFocusCandidate => Boolean(item)),
    ...(data.exports ?? [])
      .filter((item) => item.status === "Attention")
      .map((item, index) => buildOperatorFocusCandidate({
        id: `data-export:${item.exportId}`,
        label: `${item.profile} export needs attention`,
        detail: `${item.target} export has ${item.rows} rows as of ${item.updatedAt}.`,
        route: WORKSTATION_ROUTE_CATALOG.data,
        workspaceLabel: "Data",
        actionLabel: "Open data exports",
        tone: "review",
        sourcePriority: 20,
        sourceIndex: index
      }))
  ];
}

function buildOperatorFocusCandidateFromProvider(
  provider: DataProviderRecord,
  index: number
): OperatorFocusCandidate | null {
  if (provider.status === "Healthy") {
    return null;
  }

  return buildOperatorFocusCandidate({
    id: `provider:${provider.provider}`,
    label: `${provider.provider} provider ${provider.status.toLowerCase()}`,
    detail: provider.recommendedAction ?? provider.note,
    route: WORKSTATION_ROUTE_CATALOG.dataProviders,
    workspaceLabel: "Data",
    actionLabel: "Open provider trust",
    tone: provider.status === "Degraded" ? "blocked" : "review",
    sourcePriority: 15,
    sourceIndex: index
  });
}

function buildOperatorFocusCandidateFromBackfill(
  backfill: DataBackfillRecord,
  index: number
): OperatorFocusCandidate | null {
  if (backfill.status !== "Review") {
    return null;
  }

  return buildOperatorFocusCandidate({
    id: `backfill:${backfill.jobId}`,
    label: `${backfill.scope} backfill needs review`,
    detail: `${backfill.provider} backfill is ${backfill.progress}; updated ${backfill.updatedAt}.`,
    route: WORKSTATION_ROUTE_CATALOG.dataOperations,
    workspaceLabel: "Data",
    actionLabel: "Open backfills",
    tone: "review",
    sourcePriority: 16,
    sourceIndex: index
  });
}
