import type { LegacyWorkspaceKey, WorkspaceKey, WorkspaceSummary } from "@/types";

export const WORKSPACES: WorkspaceSummary[] = [
  {
    key: "trading",
    label: "Trading",
    description: "Paper cockpit readiness, sessions, orders, positions, replay, and promotion evidence.",
    status: "Review"
  },
  {
    key: "portfolio",
    label: "Portfolio",
    description: "Portfolio exposure, positions, attribution, fills, and run-level equity continuity.",
    status: "Preview"
  },
  {
    key: "accounting",
    label: "Accounting",
    description: "Ledger, cash-flow, reconciliation, Security Master coverage, and fund-account evidence.",
    status: "Review"
  },
  {
    key: "reporting",
    label: "Reporting",
    description: "Report packs, governed exports, loader scripts, data dictionaries, and approval posture.",
    status: "Review"
  },
  {
    key: "strategy",
    label: "Strategy",
    description: "Backtest runs, comparisons, run diffing, and paper-promotion review.",
    status: "Paper"
  },
  {
    key: "data",
    label: "Data",
    description: "Provider posture, backfill queues, symbol readiness, and data-quality handoffs.",
    status: "Live"
  },
  {
    key: "settings",
    label: "Settings",
    description: "Operator session context, shell preferences, integrations, and workstation setup checks.",
    status: "Setup"
  }
];

export const LEGACY_WORKSPACE_ALIASES: Record<LegacyWorkspaceKey, WorkspaceKey> = {
  overview: "trading",
  research: "strategy",
  "data-operations": "data",
  governance: "accounting"
};

export function workspacePath(key: WorkspaceKey) {
  return `/${key}`;
}

export function workspaceForKey(key: WorkspaceKey): WorkspaceSummary {
  return WORKSPACES.find((workspace) => workspace.key === key) ?? WORKSPACES[0];
}

export function workspaceForPath(pathname: string): WorkspaceSummary {
  return workspaceForKey(normalizeWorkspacePath(pathname));
}

export function normalizeWorkspacePath(pathname: string): WorkspaceKey {
  const firstSegment = firstPathSegment(pathname);
  if (!firstSegment) {
    return "trading";
  }

  if (isWorkspaceKey(firstSegment)) {
    return firstSegment;
  }

  if (isLegacyWorkspaceKey(firstSegment)) {
    return LEGACY_WORKSPACE_ALIASES[firstSegment];
  }

  return "trading";
}

export function isWorkspacePathActive(pathname: string, key: WorkspaceKey): boolean {
  return normalizeWorkspacePath(pathname) === key;
}

export function legacyWorkspaceRedirect(pathname: string, search = "", hash = ""): string | null {
  const firstSegment = firstPathSegment(pathname);
  if (!firstSegment || !isLegacyWorkspaceKey(firstSegment)) {
    return null;
  }

  const suffix = pathname.slice(`/${firstSegment}`.length);
  return `${workspacePath(LEGACY_WORKSPACE_ALIASES[firstSegment])}${suffix}${search}${hash}`;
}

function firstPathSegment(pathname: string): string | null {
  return pathname.split(/[/?#]/).filter(Boolean)[0] ?? null;
}

function isWorkspaceKey(value: string): value is WorkspaceKey {
  return WORKSPACES.some((workspace) => workspace.key === value);
}

function isLegacyWorkspaceKey(value: string): value is LegacyWorkspaceKey {
  return Object.prototype.hasOwnProperty.call(LEGACY_WORKSPACE_ALIASES, value);
}
