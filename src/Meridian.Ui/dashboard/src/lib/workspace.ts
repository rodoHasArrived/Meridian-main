import type { LegacyWorkspaceKey, WorkspaceKey, WorkspaceSummary } from "@/types";
import { WORKSPACE_DEFINITIONS } from "./workspace-catalog.generated";

export const WORKSPACES: WorkspaceSummary[] = WORKSPACE_DEFINITIONS.map((workspace) => ({
  key: workspace.key as WorkspaceKey,
  label: workspace.label,
  description: workspace.description,
  status: workspace.status
}));

export const LEGACY_WORKSPACE_ALIASES: Record<LegacyWorkspaceKey, WorkspaceKey> = {
  overview: "trading",
  research: "strategy",
  "data-operations": "data",
  governance: "accounting"
};

export const WORKSTATION_ROUTE_CATALOG = {
  trading: "/trading",
  tradingOrders: "/trading/orders",
  tradingPositions: "/trading/positions",
  tradingRisk: "/trading/risk",
  tradingReadiness: "/trading/readiness",
  portfolio: "/portfolio",
  portfolioAttribution: "/portfolio/attribution",
  portfolioAssetDetail: "/portfolio/asset-detail",
  portfolioBrokerageSync: "/portfolio/brokerage-sync",
  portfolioCashLadder: "/portfolio/cash-ladder",
  portfolioFamilyOffice: "/portfolio/family-office",
  accounting: "/accounting",
  accountingConfigure: "/accounting/configure",
  accountingOperationsContinuity: "/accounting/operations-continuity",
  accountingEntitySetup: "/accounting/entity-setup",
  accountingLedger: "/accounting/ledger",
  accountingTrialBalance: "/accounting/trial-balance",
  accountingAccountDetail: "/accounting/accounts/detail",
  accountingJournalEntries: "/accounting/journal-entries",
  accountingJournalEntryDetail: "/accounting/journal-entries/detail",
  accountingCapitalAccounts: "/accounting/capital-accounts",
  accountingReconciliation: "/accounting/reconciliation",
  accountingReconciliationMatch: "/accounting/reconciliation/match",
  accountingStatementImport: "/accounting/statement-import",
  accountingCloseCalendar: "/accounting/close-calendar",
  accountingExceptions: "/accounting/exceptions",
  accountingSecurityMaster: "/accounting/security-master",
  accountingAssetDetail: "/accounting/security-master/detail",
  accountingApprovals: "/accounting/approvals",
  accountingApprovalInbox: "/accounting/approvals/inbox",
  accountingEvidence: "/accounting/evidence",
  accountingEvidenceDetail: "/accounting/evidence/detail",
  reporting: "/reporting",
  reportingReportBuilder: "/reporting/report-builder",
  reportingLibrary: "/reporting/library",
  reportingRunParameters: "/reporting/run",
  reportingPreviewValidation: "/reporting/preview",
  reportingRunDetail: "/reporting/runs/detail",
  reportingScheduled: "/reporting/scheduled",
  reportingRunStatus: "/reporting/run-status",
  reportingOperationsRecord: "/reporting/operations-record",
  reportingReportPacks: "/reporting/report-packs",
  reportingEvidence: "/reporting/evidence",
  reportingExports: "/reporting/exports",
  reportingGovernance: "/reporting/governance",
  strategy: "/strategy",
  strategyDesigner: "/strategy/designer",
  strategyFormulaWorkbench: "/strategy/formula-workbench",
  strategyCoveredCall: "/strategy/covered-call",
  strategyPromotions: "/strategy/promotions",
  strategyLab: "/strategy/lab",
  strategyQuantLab: "/strategy/quant-lab",
  data: "/data",
  dataProviders: "/data/providers",
  dataWatchlist: "/data/watchlist",
  dataQuotes: "/data/quotes",
  dataAlerts: "/data/alerts",
  dataEvidence: "/data/evidence",
  dataBackfills: "/data/backfills",
  dataSecurityMasterLegacy: "/data/security-master",
  settings: "/settings",
  settingsPreferences: "/settings/preferences",
  settingsIntegrations: "/settings/integrations",
  settingsAlpacaProviderSetup: "/settings#alpaca-provider-setup",
  settingsBackendCapabilityCoverage: "/settings#backend-capability-coverage",
  settingsDiagnosticEndpoints: "/settings#diagnostic-endpoints"
} as const;

export type WorkstationRouteKey = keyof typeof WORKSTATION_ROUTE_CATALOG;
export type WorkstationRoutePath = (typeof WORKSTATION_ROUTE_CATALOG)[WorkstationRouteKey];
export type WorkstationRouteQueryValue = string | number | boolean | null | undefined;

const WORKSPACE_ROOT_ROUTES: Record<WorkspaceKey, WorkstationRoutePath> = {
  trading: WORKSTATION_ROUTE_CATALOG.trading,
  portfolio: WORKSTATION_ROUTE_CATALOG.portfolio,
  accounting: WORKSTATION_ROUTE_CATALOG.accounting,
  reporting: WORKSTATION_ROUTE_CATALOG.reporting,
  strategy: WORKSTATION_ROUTE_CATALOG.strategy,
  data: WORKSTATION_ROUTE_CATALOG.data,
  settings: WORKSTATION_ROUTE_CATALOG.settings
};

export const WORKSTATION_PAGE_TAG_ROUTES: Record<string, WorkstationRoutePath> = {
  AccountPortfolio: WORKSTATION_ROUTE_CATALOG.portfolioBrokerageSync,
  AccountingShell: WORKSTATION_ROUTE_CATALOG.accounting,
  FundAccountingConfigure: WORKSTATION_ROUTE_CATALOG.accountingConfigure,
  OperationsContinuity: WORKSTATION_ROUTE_CATALOG.accountingOperationsContinuity,
  FundStructureSetup: WORKSTATION_ROUTE_CATALOG.accountingEntitySetup,
  OperationsClose: WORKSTATION_ROUTE_CATALOG.accountingOperationsContinuity,
  Backtest: WORKSTATION_ROUTE_CATALOG.strategy,
  Backfill: WORKSTATION_ROUTE_CATALOG.dataBackfills,
  BrokerageSync: WORKSTATION_ROUTE_CATALOG.portfolioBrokerageSync,
  DataShell: WORKSTATION_ROUTE_CATALOG.data,
  DataOperationsShell: WORKSTATION_ROUTE_CATALOG.data,
  EvidenceWorkbench: WORKSTATION_ROUTE_CATALOG.accountingEvidence,
  FundExceptionWorkbench: WORKSTATION_ROUTE_CATALOG.accountingExceptions,
  FundAuditTrail: WORKSTATION_ROUTE_CATALOG.accounting,
  FundReconciliation: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
  FundReportPack: WORKSTATION_ROUTE_CATALOG.reportingReportPacks,
  FundTrialBalance: WORKSTATION_ROUTE_CATALOG.accountingLedger,
  FundJournalEntryWorkbench: WORKSTATION_ROUTE_CATALOG.accountingJournalEntries,
  CapitalAccountWorkbench: WORKSTATION_ROUTE_CATALOG.accountingCapitalAccounts,
  GovernanceShell: WORKSTATION_ROUTE_CATALOG.accounting,
  PortfolioFamilyOffice: WORKSTATION_ROUTE_CATALOG.portfolioFamilyOffice,
  PortfolioShell: WORKSTATION_ROUTE_CATALOG.portfolio,
  ProviderHealth: WORKSTATION_ROUTE_CATALOG.dataProviders,
  ProviderTrust: WORKSTATION_ROUTE_CATALOG.dataProviders,
  ResearchShell: WORKSTATION_ROUTE_CATALOG.strategy,
  ReportLineProvenanceExplorer: WORKSTATION_ROUTE_CATALOG.reportingEvidence,
  ReportingShell: WORKSTATION_ROUTE_CATALOG.reporting,
  ReportPackApproval: WORKSTATION_ROUTE_CATALOG.reportingReportPacks,
  RunRisk: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
  SecurityMaster: WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster,
  SettingsShell: WORKSTATION_ROUTE_CATALOG.settings,
  StrategyRuns: WORKSTATION_ROUTE_CATALOG.strategy,
  StrategyShell: WORKSTATION_ROUTE_CATALOG.strategy,
  TradingReadiness: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
  TradingReadinessConsole: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
  TradingShell: WORKSTATION_ROUTE_CATALOG.trading
};

export function workspacePath(key: WorkspaceKey) {
  return WORKSPACE_ROOT_ROUTES[key];
}

export function workstationRoute(key: WorkstationRouteKey): WorkstationRoutePath {
  return WORKSTATION_ROUTE_CATALOG[key];
}

export function workstationRouteWithQuery(
  key: WorkstationRouteKey,
  query: Record<string, WorkstationRouteQueryValue>
): string {
  return appendRouteQuery(WORKSTATION_ROUTE_CATALOG[key], query);
}

export function appendRouteQuery(
  route: string,
  query: Record<string, WorkstationRouteQueryValue>
): string {
  const hashIndex = route.indexOf("#");
  const routeWithoutHash = hashIndex >= 0 ? route.slice(0, hashIndex) : route;
  const hash = hashIndex >= 0 ? route.slice(hashIndex) : "";
  const searchIndex = routeWithoutHash.indexOf("?");
  const pathname = searchIndex >= 0 ? routeWithoutHash.slice(0, searchIndex) : routeWithoutHash;
  const params = new URLSearchParams(searchIndex >= 0 ? routeWithoutHash.slice(searchIndex) : "");

  Object.entries(query).forEach(([key, value]) => {
    if (value === null || value === undefined) {
      return;
    }

    params.set(key, String(value));
  });

  const search = params.toString().replace(/\+/g, "%20");
  return `${pathname}${search ? `?${search}` : ""}${hash}`;
}

export function workstationRouteWithHash(key: WorkstationRouteKey, hashTarget: string): string {
  const target = hashTarget.trim().replace(/^#/, "");
  return target ? `${WORKSTATION_ROUTE_CATALOG[key]}#${target}` : WORKSTATION_ROUTE_CATALOG[key];
}

export function settingsProviderConnectionRoute(providerId: string): string {
  return workstationRouteWithHash("settings", `provider-${providerId}-connection`);
}

export function evidenceWorkbenchPath(subjectKind: string, subjectId: string) {
  return workstationRouteWithQuery("reportingEvidence", {
    subjectKind,
    subjectId
  });
}

export function workflowTargetPath(
  targetPageTag: string | null | undefined,
  workspaceId: string | null | undefined
) {
  const evidenceRoute = evidenceWorkbenchPathFromPageTag(targetPageTag);
  if (evidenceRoute) {
    return evidenceRoute;
  }

  const normalizedPageTag = normalizeParameterizedPageTag(targetPageTag);
  const tagRoute = normalizedPageTag ? WORKSTATION_PAGE_TAG_ROUTES[normalizedPageTag] : undefined;
  if (tagRoute) {
    return tagRoute;
  }

  const workspaceKey = workspaceKeyFromId(workspaceId);
  return workspaceKey ? workspacePath(workspaceKey) : "/trading";
}

function evidenceWorkbenchPathFromPageTag(targetPageTag: string | null | undefined): string | null {
  const trimmed = targetPageTag?.trim();
  if (!trimmed) {
    return null;
  }

  const separatorIndex = trimmed.indexOf(":");
  if (separatorIndex < 0) {
    return null;
  }

  const prefix = trimmed.slice(0, separatorIndex).trim();
  if (prefix.toLowerCase() !== "evidenceworkbench") {
    return null;
  }

  const subject = trimmed.slice(separatorIndex + 1).trim().replace(/^\/+/, "");
  const subjectSeparatorIndex = subject.indexOf("/");
  if (subjectSeparatorIndex < 0) {
    return WORKSTATION_ROUTE_CATALOG.reportingEvidence;
  }

  const subjectKind = subject.slice(0, subjectSeparatorIndex).trim();
  const subjectId = subject.slice(subjectSeparatorIndex + 1).trim();
  return subjectKind && subjectId
    ? evidenceWorkbenchPath(subjectKind, subjectId)
    : WORKSTATION_ROUTE_CATALOG.reportingEvidence;
}

function normalizeParameterizedPageTag(targetPageTag: string | null | undefined): string | null {
  const trimmed = targetPageTag?.trim();
  if (!trimmed) {
    return null;
  }

  const separatorIndex = trimmed.indexOf(":");
  if (separatorIndex < 0) {
    return trimmed;
  }

  const prefix = trimmed.slice(0, separatorIndex).trim();
  return prefix.toLowerCase() === "evidenceworkbench" ? "EvidenceWorkbench" : trimmed;
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

export function canonicalizeWorkspaceSummaries(workspaces: WorkspaceSummary[]): WorkspaceSummary[] {
  const inputByKey = new Map(workspaces.map((workspace) => [workspace.key, workspace]));

  return WORKSPACES
    .filter((workspace) => inputByKey.has(workspace.key))
    .map((workspace) => ({
      ...workspace,
      status: inputByKey.get(workspace.key)?.status ?? workspace.status
    }));
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
    const suffix = pathname.slice(WORKSTATION_ROUTE_CATALOG.dataSecurityMasterLegacy.length);
    return `${WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster}${suffix}${search}${hash}`;
  }

  if (firstSegment === "overview") {
    const suffix = pathname.slice("/overview".length);
    return suffix.length === 0 || suffix === "/"
      ? `/${search}${hash}`
      : `${workspacePath(LEGACY_WORKSPACE_ALIASES[firstSegment])}${suffix}${search}${hash}`;
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
