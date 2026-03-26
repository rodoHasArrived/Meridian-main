import type { WorkspaceKey, WorkspaceSummary } from "@/types";

export interface WorkspaceCommand {
  id: string;
  label: string;
  description: string;
  href: string;
  workspace: WorkspaceKey;
}

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
    status: "Live"
  },
  {
    key: "data-operations",
    label: "Data Operations",
    description: "Providers, backfills, storage, export, and symbol workflows.",
    status: "Live"
  },
  {
    key: "governance",
    label: "Governance",
    description: "Ledger, risk, diagnostics, audit history, and operational settings.",
    status: "Live"
  }
];

export function workspacePath(key: WorkspaceKey) {
  return key === "research" ? "/" : `/${key}`;
}

export const WORKSPACE_COMMANDS: WorkspaceCommand[] = [
  {
    id: "research-run-queue",
    label: "Open active research run queue",
    description: "Review running and queued research runs.",
    href: "/",
    workspace: "research"
  },
  {
    id: "research-comparisons",
    label: "Review run comparison metrics",
    description: "Jump into current comparison-ready research runs.",
    href: "/",
    workspace: "research"
  },
  {
    id: "trading-orders",
    label: "Open orders blotter",
    description: "Inspect working and partially filled trading orders.",
    href: "/trading/orders",
    workspace: "trading"
  },
  {
    id: "trading-positions",
    label: "Review live positions",
    description: "Check exposure, marks, and unrealized P&L.",
    href: "/trading/positions",
    workspace: "trading"
  },
  {
    id: "trading-risk",
    label: "Inspect risk guardrails",
    description: "Open the trading risk cockpit and guardrail state.",
    href: "/trading/risk",
    workspace: "trading"
  },
  {
    id: "data-operations-providers",
    label: "Review provider health",
    description: "See feed status, latency, and operational notes.",
    href: "/data-operations/providers",
    workspace: "data-operations"
  },
  {
    id: "data-operations-backfills",
    label: "Open backfill queue",
    description: "Inspect active backfill progress and review items.",
    href: "/data-operations/backfills",
    workspace: "data-operations"
  },
  {
    id: "data-operations-exports",
    label: "Review storage exports",
    description: "Check export profiles and recent delivery targets.",
    href: "/data-operations/exports",
    workspace: "data-operations"
  },
  {
    id: "governance-ledger",
    label: "Open ledger overview",
    description: "Review cash flow and audit-facing ledger summaries.",
    href: "/governance/ledger",
    workspace: "governance"
  },
  {
    id: "governance-reconciliation",
    label: "Review reconciliation history",
    description: "Inspect open breaks and balanced runs.",
    href: "/governance/reconciliation",
    workspace: "governance"
  },
  {
    id: "governance-security-master",
    label: "Open security master coverage",
    description: "Check unresolved references and coverage risk.",
    href: "/governance/security-master",
    workspace: "governance"
  }
];
