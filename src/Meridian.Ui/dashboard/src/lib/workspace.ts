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
  Backtest: "/strategy",
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
  SecurityMaster: "/accounting/security-master",
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

export function normalizeLocalWorkstationRoute(route: string | null | undefined): string | null {
  const trimmed = route?.trim();
  if (!trimmed) {
    return null;
  }

  const localRoute = trimmed.startsWith("/workstation/")
    ? trimmed.slice("/workstation".length)
    : trimmed;

  if (!localRoute.startsWith("/") || localRoute.startsWith("//") || localRoute.startsWith("/api/")) {
    return null;
  }

  const { pathname, search, hash } = splitRouteParts(localRoute);
  const canonicalRoute = legacyWorkspaceRedirect(pathname, search, hash) ?? `${pathname}${search}${hash}`;
  const routeWorkspace = firstPathSegment(canonicalRoute);
  if (!routeWorkspace || !isWorkspaceKey(routeWorkspace)) {
    return null;
  }

  return canonicalRoute;
}

export function workspaceForKey(key: WorkspaceKey): WorkspaceSummary {
  return WORKSPACES.find((workspace) => workspace.key === key) ?? WORKSPACES[0];
}

export function workspaceForPath(pathname: string): WorkspaceSummary {
  return workspaceForKey(normalizeWorkspacePath(pathname));
}

export function normalizeWorkspacePath(pathname: string): WorkspaceKey {
  const segments = pathSegments(pathname);
  const firstSegment = segments[0] ?? null;
  if (!firstSegment) {
    return "trading";
  }

  if (firstSegment === "data" && segments[1] === "security-master") {
    return "accounting";
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
  if (!firstSegment) {
    return null;
  }

  if (firstSegment === "data" && pathSegments(pathname)[1] === "security-master") {
    const suffix = pathname.slice("/data/security-master".length);
    return `/accounting/security-master${suffix}${search}${hash}`;
  }

  if (!isLegacyWorkspaceKey(firstSegment)) {
    return null;
  }

  const suffix = pathname.slice(`/${firstSegment}`.length);
  return `${workspacePath(LEGACY_WORKSPACE_ALIASES[firstSegment])}${suffix}${search}${hash}`;
}

function firstPathSegment(pathname: string): string | null {
  return pathSegments(pathname)[0] ?? null;
}

function pathSegments(pathname: string): string[] {
  return pathname.split(/[/?#]/).filter(Boolean);
}

function splitRouteParts(route: string): { pathname: string; search: string; hash: string } {
  const hashIndex = route.indexOf("#");
  const routeWithoutHash = hashIndex >= 0 ? route.slice(0, hashIndex) : route;
  const hash = hashIndex >= 0 ? route.slice(hashIndex) : "";
  const searchIndex = routeWithoutHash.indexOf("?");
  return {
    pathname: searchIndex >= 0 ? routeWithoutHash.slice(0, searchIndex) : routeWithoutHash,
    search: searchIndex >= 0 ? routeWithoutHash.slice(searchIndex) : "",
    hash
  };
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

  if (isWorkspaceKey(normalized)) {
    return normalized;
  }

  return isLegacyWorkspaceKey(normalized) ? LEGACY_WORKSPACE_ALIASES[normalized] : null;
}
