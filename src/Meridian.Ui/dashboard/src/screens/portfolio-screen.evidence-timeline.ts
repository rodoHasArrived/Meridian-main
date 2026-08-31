import { buildEvidenceTimelineCandidate, type EvidenceTimelineCandidate } from "@/app-shell.evidence-timeline";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type { PortfolioWorkspaceResponse } from "@/types";

export function buildPortfolioEvidenceTimelineItems(portfolio: PortfolioWorkspaceResponse | null): EvidenceTimelineCandidate[] {
  return (portfolio?.runs ?? [])
    .map((run, index) => buildEvidenceTimelineCandidate({
      id: `portfolio-run:${run.runId}`,
      label: `${run.strategyName} portfolio ${run.status.toLowerCase()}`,
      detail: `${run.engine} ${run.mode} run, PnL ${run.pnl}, promotion ${run.promotionState ?? "none"}.`,
      // Run evidence (run table, drill-ins, run detail) lives on the
      // Attribution view; the overview no longer mounts it.
      route: WORKSTATION_ROUTE_CATALOG.portfolioAttribution,
      workspaceLabel: "Portfolio",
      timestamp: run.lastUpdated,
      tone: run.status.toLowerCase().includes("review") ? "review" : "ready",
      sourcePriority: 31,
      sourceIndex: index
    }))
    .filter((item): item is EvidenceTimelineCandidate => Boolean(item));
}
