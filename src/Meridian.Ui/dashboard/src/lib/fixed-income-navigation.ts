import { workstationRouteWithQuery } from "@/lib/workspace";

export type BondWorkflowAction = "analytics" | "migration";

export function isBondAssetClass(assetClass: string): boolean {
  return ["bond", "fixedincome"].includes(assetClass.toLowerCase().replace(/[\s_-]/g, ""));
}

export function resolveBondWorkflowAction(value: string | null): BondWorkflowAction | null {
  return value === "analytics" || value === "migration" ? value : null;
}

export function bondWorkflowRoute(securityId: string, action: BondWorkflowAction): string {
  return workstationRouteWithQuery("accountingSecurityMaster", { securityId, tab: "evidence", bondAction: action });
}

/** Symbol-only holdings resolve to an authoritative identity before opening bond calculations. */
export function holdingBondWorkflowRoute(symbol: string, action: BondWorkflowAction): string {
  return workstationRouteWithQuery("portfolioAssetDetail", { symbol, source: "portfolio", bondAction: action });
}
