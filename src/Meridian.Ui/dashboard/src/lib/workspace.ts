import type { WorkspaceKey, WorkspaceSummary } from "@/types";

export const WORKSPACES: WorkspaceSummary[] = [
  {
    key: "research",
    label: "Research",
    description: "Backtests, run comparisons, experiment tracking, and dataset validation.",
    status: "Live"
  },
  {
    key: "trading",
    label: "Trading",
    description: "Paper operations cockpit, positions, blotter, and execution controls.",
    status: "Planned"
  },
  {
    key: "data-operations",
    label: "Data Operations",
    description: "Providers, backfills, storage, export, and symbol workflows.",
    status: "Planned"
  },
  {
    key: "governance",
    label: "Governance",
    description: "Ledger, risk, diagnostics, audit history, and operational settings.",
    status: "Planned"
  }
];

export function workspacePath(key: WorkspaceKey) {
  return key === "research" ? "/" : `/${key}`;
}
