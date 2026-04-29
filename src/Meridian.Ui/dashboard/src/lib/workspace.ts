import type { WorkspaceKey, WorkspaceSummary } from "@/types";

export const WORKSPACES: WorkspaceSummary[] = [
  {
    key: "overview",
    label: "Overview",
    description: "System health, recent activity, and workspace handoffs for the browser workstation.",
    status: "Live"
  },
  {
    key: "research",
    label: "Research",
    description: "Backtest runs, comparisons, run diffing, and paper-promotion review.",
    status: "Paper"
  },
  {
    key: "trading",
    label: "Trading",
    description: "Paper cockpit readiness, sessions, orders, positions, replay, and promotion evidence.",
    status: "Review"
  },
  {
    key: "data-operations",
    label: "Data Operations",
    description: "Provider posture, backfill queues, symbol readiness, and data-quality handoffs.",
    status: "Live"
  },
  {
    key: "governance",
    label: "Governance",
    description: "Security Master, reconciliation, ledger, cash-flow, and reporting review surfaces.",
    status: "Review"
  }
];

export function workspacePath(key: WorkspaceKey) {
  if (key === "research") {
    return "/";
  }

  return `/${key}`;
}
