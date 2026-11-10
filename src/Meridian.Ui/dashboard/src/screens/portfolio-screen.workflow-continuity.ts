import type { WorkflowContinuityStepStatus } from "@/app-shell.workflow-continuity";
import type { PortfolioWorkspaceResponse } from "@/types";
import { pluralizeCount } from "@/lib/format";

export function buildPortfolioLedgerContinuityStatus(portfolio: PortfolioWorkspaceResponse | null): WorkflowContinuityStepStatus {
  if (!portfolio) {
    return { label: "Waiting", tone: "pending" };
  }

  if (portfolio.risk.state === "Constrained") {
    return { label: "Constrained", tone: "blocked" };
  }

  if (portfolio.risk.state === "Observe") {
    return { label: "Observe", tone: "review" };
  }

  return (portfolio.positions ?? []).length > 0
    ? { label: pluralizeCount(portfolio.positions.length, "position"), tone: "ready" }
    : { label: "Ready", tone: "ready" };
}
