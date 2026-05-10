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

const PAGE_TAG_ROUTES: Record<string, string> = {
  AccountPortfolio: "/portfolio/brokerage-sync",
  AccountingShell: "/accounting",
  Backfill: "/data/backfills",
  BrokerageSync: "/portfolio/brokerage-sync",
  DataShell: "/data",
  EvidenceWorkbench: "/reporting/evidence",
  FundAuditTrail: "/accounting",
  FundReconciliation: "/accounting/reconciliation",
  FundReportPack: "/reporting/report-packs",
  FundTrialBalance: "/accounting",
  PortfolioShell: "/portfolio",
  ProviderHealth: "/data/providers",
  ProviderTrust: "/data/providers",
  ReportingShell: "/reporting",
  ReportPackApproval: "/reporting/report-packs",
  RunRisk: "/trading/readiness",
  SecurityMaster: "/data/security-master",
  SettingsShell: "/settings",
  StrategyRuns: "/strategy",
  StrategyShell: "/strategy",
  TradingReadiness: "/trading/readiness",
  TradingReadinessConsole: "/trading/readiness",
  TradingShell: "/trading"
};

export function workspacePath(key: WorkspaceKey) {
  return `/${key}`;
}

export function evidenceWorkbenchPath(subjectKind: string, subjectId: string) {
  return `/reporting/evidence?subjectKind=${encodeURIComponent(subjectKind)}&subjectId=${encodeURIComponent(subjectId)}`;
}

export function workflowTargetPath(
  targetPageTag: string | null | undefined,
  workspaceId: string | null | undefined
) {
  const tagRoute = targetPageTag ? PAGE_TAG_ROUTES[targetPageTag] : undefined;
  if (tagRoute) {
    return tagRoute;
  }

  const workspaceKey = workspaceKeyFromId(workspaceId);
  return workspaceKey ? workspacePath(workspaceKey) : "/trading";
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

function workspaceKeyFromId(workspaceId: string | null | undefined): WorkspaceKey | null {
  const normalized = workspaceId?.trim().toLowerCase();
  if (!normalized) {
    return null;
  }

  return isWorkspaceKey(normalized) ? normalized : null;
}
