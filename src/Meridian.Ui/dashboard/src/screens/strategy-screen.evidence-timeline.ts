import { buildEvidenceTimelineCandidate, type EvidenceTimelineCandidate } from "@/app-shell.evidence-timeline";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type { StrategyWorkspaceResponse } from "@/types";

export function buildStrategyEvidenceTimelineItems(strategy: StrategyWorkspaceResponse | null): EvidenceTimelineCandidate[] {
  return (strategy?.runs ?? [])
    .map((run, index) => buildEvidenceTimelineCandidate({
      id: `strategy-run:${run.id}`,
      label: `${run.strategyName} ${run.status.toLowerCase()}`,
      detail: `${run.engine} ${run.mode} run on ${run.dataset}. ${run.notes}`,
      route: WORKSTATION_ROUTE_CATALOG.strategy,
      workspaceLabel: "Strategy",
      timestamp: run.lastUpdated,
      tone: run.status === "Needs Review" ? "review" : run.status === "Running" || run.status === "Queued" ? "pending" : "ready",
      sourcePriority: 30,
      sourceIndex: index
    }))
    .filter((item): item is EvidenceTimelineCandidate => Boolean(item));
}
