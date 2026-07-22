import { buildOperatorFocusCandidate, type OperatorFocusCandidate } from "@/app-shell.operator-focus";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type { StrategyRunRecord, StrategyWorkspaceResponse } from "@/types";

export function buildStrategyOperatorFocusItems(strategy: StrategyWorkspaceResponse | null): OperatorFocusCandidate[] {
  if (!strategy) {
    return [];
  }

  return (strategy.runs ?? [])
    .map((run, index) => buildOperatorFocusCandidateFromStrategyRun(run, index))
    .filter((item): item is OperatorFocusCandidate => Boolean(item));
}

function buildOperatorFocusCandidateFromStrategyRun(
  run: StrategyRunRecord,
  index: number
): OperatorFocusCandidate | null {
  if (run.status !== "Needs Review") {
    return null;
  }

  return buildOperatorFocusCandidate({
    id: `strategy-run:${run.id}`,
    label: `${run.strategyName} run needs review`,
    detail: run.notes || `${run.engine} ${run.mode} run on ${run.dataset}.`,
    route: WORKSTATION_ROUTE_CATALOG.strategy,
    workspaceLabel: "Strategy",
    actionLabel: "Open run library",
    tone: "review",
    sourcePriority: 35,
    sourceIndex: index
  });
}
