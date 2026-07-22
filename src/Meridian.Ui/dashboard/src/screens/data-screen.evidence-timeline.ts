import { buildEvidenceTimelineCandidate, type EvidenceTimelineCandidate } from "@/app-shell.evidence-timeline";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type { DataWorkspaceResponse } from "@/types";

export function buildDataEvidenceTimelineItems(data: DataWorkspaceResponse | null): EvidenceTimelineCandidate[] {
  if (!data) {
    return [];
  }

  return [
    ...(data.backfills ?? []).map((backfill, index) => buildEvidenceTimelineCandidate({
      id: `data-backfill:${backfill.jobId}`,
      label: `${backfill.scope} backfill ${backfill.status.toLowerCase()}`,
      detail: `${backfill.provider} backfill progress: ${backfill.progress}.`,
      route: WORKSTATION_ROUTE_CATALOG.dataOperations,
      workspaceLabel: "Data",
      timestamp: backfill.updatedAt,
      tone: backfill.status === "Review" ? "review" : "pending",
      sourcePriority: 20,
      sourceIndex: index
    })),
    ...(data.exports ?? []).map((item, index) => buildEvidenceTimelineCandidate({
      id: `data-export:${item.exportId}`,
      label: `${item.profile} export ${item.status.toLowerCase()}`,
      detail: `${item.target} export has ${item.rows} rows.`,
      route: WORKSTATION_ROUTE_CATALOG.data,
      workspaceLabel: "Data",
      timestamp: item.updatedAt,
      tone: item.status === "Attention" ? "review" : item.status === "Running" ? "pending" : "ready",
      sourcePriority: 21,
      sourceIndex: index
    }))
  ].filter((item): item is EvidenceTimelineCandidate => Boolean(item));
}
