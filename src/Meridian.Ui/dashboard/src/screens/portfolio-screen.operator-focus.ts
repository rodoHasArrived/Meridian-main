import { buildOperatorFocusCandidate, type OperatorFocusCandidate } from "@/app-shell.operator-focus";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type { PortfolioWorkspaceResponse } from "@/types";

export function buildPortfolioOperatorFocusItems(portfolio: PortfolioWorkspaceResponse | null): OperatorFocusCandidate[] {
  if (!portfolio || portfolio.risk.state === "Healthy") {
    return [];
  }

  return [buildOperatorFocusCandidate({
    id: "portfolio-risk",
    label: `Portfolio risk ${portfolio.risk.state.toLowerCase()}`,
    detail: portfolio.risk.summary,
    route: WORKSTATION_ROUTE_CATALOG.portfolio,
    workspaceLabel: "Portfolio",
    actionLabel: "Open exposure",
    tone: portfolio.risk.state === "Constrained" ? "blocked" : "review",
    sourcePriority: 10,
    sourceIndex: 0
  })];
}
