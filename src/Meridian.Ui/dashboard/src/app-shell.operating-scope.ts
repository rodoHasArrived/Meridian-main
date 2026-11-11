import { workspaceForPath } from "@/lib/workspace";
import { splitContinuityRoute } from "@/app-shell.workflow-continuity";
import { normalizeFundAccountGuid } from "@/lib/fund-account-scope";

export interface AppShellOperatingScopeInput {
  symbol?: string | null;
  fundAccountId?: string | null;
  runId?: string | null;
  provider?: string | null;
  from?: string | null;
  to?: string | null;
  date?: string | null;
  asOf?: string | null;
}

export interface AppShellOperatingScopeItem {
  id: string;
  label: string;
  value: string;
  ariaLabel: string;
}

export interface AppShellOperatingScopeQueryParam {
  key: string;
  value: string;
  scopeKey: AppShellOperatingScopeQueryKey;
}

export type AppShellOperatingScopeQueryKey = "symbol" | "fundAccountId" | "runId" | "provider" | "window";

export interface AppShellOperatingScopeState {
  label: string;
  summary: string;
  subjectSymbol: string | null;
  fundAccountId: string | null;
  runId: string | null;
  provider: string | null;
  hasScope: boolean;
  clearAriaLabel: string | null;
  items: AppShellOperatingScopeItem[];
  queryParams: AppShellOperatingScopeQueryParam[];
}

function readSearchValue(search: string, key: string): string | null {
  if (!search) {
    return null;
  }

  try {
    return new URLSearchParams(search).get(key);
  } catch {
    return null;
  }
}

function normalizeSubjectSymbol(value: string | null): string | null {
  const normalized = value?.trim().toUpperCase().replace(/[^A-Z0-9._-]/g, "") ?? "";
  return normalized.length > 0 ? normalized.slice(0, 16) : null;
}

export function readOperatingScopeFromSearch(search: string): AppShellOperatingScopeInput {
  return {
    symbol: normalizeSubjectSymbol(readSearchValue(search, "symbol")),
    fundAccountId: normalizeScopeToken(readSearchValue(search, "fundAccountId"), 72),
    runId: normalizeScopeToken(readSearchValue(search, "runId"), 72),
    provider: normalizeScopeLabel(readSearchValue(search, "provider"), 40),
    from: normalizeDateScopeValue(readSearchValue(search, "from")),
    to: normalizeDateScopeValue(readSearchValue(search, "to")),
    date: normalizeDateScopeValue(readSearchValue(search, "date")),
    asOf: normalizeDateScopeValue(readSearchValue(search, "asOf"))
  };
}

export function buildOperatingScopeFromSearch(
  search: string,
  fallback: AppShellOperatingScopeInput | null = null,
  activeRoute: string | null = null
): AppShellOperatingScopeState {
  const routeScope = readOperatingScopeFromSearch(search);
  const subjectSymbol = routeScope.symbol ?? normalizeSubjectSymbol(fallback?.symbol ?? null);
  const fundAccountId = routeScope.fundAccountId ?? normalizeScopeToken(fallback?.fundAccountId, 72);
  const runId = routeScope.runId ?? normalizeScopeToken(fallback?.runId, 72);
  const provider = routeScope.provider ?? normalizeScopeLabel(fallback?.provider, 40);
  const from = routeScope.from ?? normalizeDateScopeValue(fallback?.from);
  const to = routeScope.to ?? normalizeDateScopeValue(fallback?.to);
  const date = routeScope.date ?? normalizeDateScopeValue(fallback?.date);
  const asOf = routeScope.asOf ?? normalizeDateScopeValue(fallback?.asOf);
  const windowValue = formatOperatingScopeWindow({ from, to, date, asOf });

  const items: AppShellOperatingScopeItem[] = [
    subjectSymbol ? buildOperatingScopeItem("symbol", "Subject", subjectSymbol) : null,
    fundAccountId ? buildOperatingScopeItem("fundAccountId", "Account", fundAccountId) : null,
    runId ? buildOperatingScopeItem("runId", "Run", operatingRunScopeLabel(activeRoute)) : null,
    provider ? buildOperatingScopeItem("provider", "Provider", provider) : null,
    windowValue ? buildOperatingScopeItem("window", "Window", windowValue) : null
  ].filter((item): item is AppShellOperatingScopeItem => Boolean(item));

  const queryParams: AppShellOperatingScopeQueryParam[] = [
    subjectSymbol ? { key: "symbol", value: subjectSymbol, scopeKey: "symbol" as const } : null,
    fundAccountId ? { key: "fundAccountId", value: fundAccountId, scopeKey: "fundAccountId" as const } : null,
    runId ? { key: "runId", value: runId, scopeKey: "runId" as const } : null,
    provider ? { key: "provider", value: provider, scopeKey: "provider" as const } : null,
    from ? { key: "from", value: from, scopeKey: "window" as const } : null,
    to ? { key: "to", value: to, scopeKey: "window" as const } : null,
    date ? { key: "date", value: date, scopeKey: "window" as const } : null,
    asOf ? { key: "asOf", value: asOf, scopeKey: "window" as const } : null
  ].filter((item): item is AppShellOperatingScopeQueryParam => Boolean(item));

  const summary = items.length > 0
    ? items.map((item) => `${item.label}: ${item.value}`).join(" / ")
    : "No operating scope selected";

  return {
    label: "Operating scope",
    summary,
    subjectSymbol,
    fundAccountId,
    runId,
    provider,
    hasScope: items.length > 0,
    clearAriaLabel: buildOperatingScopeClearAriaLabel(items, subjectSymbol),
    items,
    queryParams
  };
}

function operatingRunScopeLabel(route: string | null): string {
  if (!route) {
    return "Selected run";
  }

  const workspaceKey = workspaceForPath(splitContinuityRoute(route).pathname).key;
  switch (workspaceKey) {
    case "accounting":
      return "Selected ledger run";
    case "reporting":
      return "Selected report run";
    default:
      return "Selected run";
  }
}

export function readOperatingContextSymbolFromSearch(search: string): string | null {
  return readOperatingScopeFromSearch(search).symbol ?? null;
}

export function normalizeOperatingContextSymbol(value: string | null | undefined): string | null {
  return normalizeSubjectSymbol(value ?? null);
}

export function appendOperatingScopeToRoute(route: string, operatingScope: AppShellOperatingScopeState): string {
  if (!operatingScope.hasScope) {
    return route;
  }

  const allowedScopeKeys = operatingScopeKeysForRoute(route);
  return operatingScope.queryParams
    .filter((item) => allowedScopeKeys.has(item.scopeKey))
    .reduce((current, item) => appendSearchValue(current, item.key, item.value, true), route);
}

/**
 * Dimensions consumed by the rendered route, distinct from context carried in links.
 * Opt in only where a screen reads the dimension into its request or selection.
 * Unknown routes retain context without claiming that it filters their data.
 */
export function operatingScopeDimensionsForRoute(route: string, scope?: AppShellOperatingScopeInput): AppShellOperatingScopeQueryKey[] {
  const { pathname } = splitContinuityRoute(route);
  const path = pathname.replace(/\/+$/, "") || "/";
  const params = new URLSearchParams(route.split("?", 2)[1]?.split("#", 1)[0] ?? "");
  const appliedQuery = (key: "symbol" | "runId" | "fundAccountId"): AppShellOperatingScopeQueryKey[] => {
    const explicit = params.get(key)?.trim();
    if (!explicit) return [];
    const carried = scope?.[key]?.trim();
    const matches = key === "symbol" ? explicit.toUpperCase() === carried?.toUpperCase() : explicit === carried;
    return !scope || matches ? [key] : [];
  };
  if (path === "/data/quotes") {
    // Alerts only seeds a creation form; watchlist does not filter by this symbol.
    return params.get("view") === "watchlist" || params.get("view") === "alerts" ? [] : appliedQuery("symbol");
  }
  if (path === "/portfolio/cash-ladder") return ["fundAccountId"];
  if (path === "/portfolio/asset-detail" || path === "/accounting/security-master/detail") return params.get("securityId") ? [] : appliedQuery("symbol");
  if (path === "/strategy/promotions" || path === "/strategy/run-ledger") return appliedQuery("runId");
  // Manual drafts and posted-period authority can win over a carried run identity.
  if (path === "/accounting/journal-entries/detail") return [];
  if (path === "/reporting/preview" || path === "/reporting/runs/detail"
    || path === "/accounting/accounts/detail") return appliedQuery("runId");
  if (path === "/accounting" || path === "/accounting/close-calendar") return appliedQuery("fundAccountId");
  // Trading's shared payload and readiness console explicitly consume the account.
  if (path === "/trading" || path === "/trading/orders" || path === "/trading/positions"
    || path === "/trading/risk" || path === "/trading/readiness") {
    return scope && !normalizeFundAccountGuid(scope.fundAccountId) ? [] : ["fundAccountId"];
  }
  return [];
}

export function summarizeOperatingScopeForRoute(
  route: string,
  operatingScope: AppShellOperatingScopeState
): string | null {
  if (!operatingScope.hasScope) {
    return null;
  }

  const allowedScopeKeys = operatingScopeKeysForRoute(route);
  const items = operatingScope.items.filter((item) => allowedScopeKeys.has(scopeKeyForOperatingScopeItem(item)));
  return items.length > 0
    ? items.map((item) => `${item.label}: ${item.value}`).join(" / ")
    : null;
}

export function removeOperatingScopeFromSearch(search: string): string {
  const params = new URLSearchParams(search);
  operatingScopeQueryParamKeys.forEach((key) => params.delete(key));
  const next = params.toString();
  return next ? `?${next}` : "";
}

function buildOperatingScopeItem(id: string, label: string, value: string): AppShellOperatingScopeItem {
  return {
    id,
    label,
    value,
    ariaLabel: `${label}: ${value}`
  };
}

function buildOperatingScopeClearAriaLabel(
  items: AppShellOperatingScopeItem[],
  subjectSymbol: string | null
): string | null {
  if (items.length === 0) {
    return null;
  }

  if (items.length === 1 && subjectSymbol) {
    return `Clear ${subjectSymbol} operating context`;
  }

  return `Clear operating scope: ${items.map((item) => `${item.label} ${item.value}`).join(", ")}`;
}

function scopeKeyForOperatingScopeItem(item: AppShellOperatingScopeItem): AppShellOperatingScopeQueryKey {
  switch (item.id) {
    case "symbol":
      return "symbol";
    case "fundAccountId":
      return "fundAccountId";
    case "runId":
      return "runId";
    case "provider":
      return "provider";
    case "window":
    default:
      return "window";
  }
}

function normalizeScopeToken(value: string | null | undefined, maxLength: number): string | null {
  const normalized = value?.trim().replace(/[^A-Za-z0-9._:-]/g, "") ?? "";
  return normalized.length > 0 ? normalized.slice(0, maxLength) : null;
}

function normalizeScopeLabel(value: string | null | undefined, maxLength: number): string | null {
  const normalized = value?.trim().replace(/[^A-Za-z0-9 ._-]/g, "").replace(/\s+/g, " ") ?? "";
  return normalized.length > 0 ? normalized.slice(0, maxLength) : null;
}

function normalizeDateScopeValue(value: string | null | undefined): string | null {
  const normalized = value?.trim().replace(/[^0-9A-Za-z:._+-]/g, "") ?? "";
  return normalized.length > 0 ? normalized.slice(0, 32) : null;
}

function formatOperatingScopeWindow({
  from,
  to,
  date,
  asOf
}: Pick<AppShellOperatingScopeInput, "from" | "to" | "date" | "asOf">): string | null {
  if (from || to) {
    return `${from ?? "Start"} to ${to ?? "Now"}`;
  }

  if (date) {
    return date;
  }

  return asOf ? `as of ${asOf}` : null;
}

function operatingScopeKeysForRoute(route: string): Set<AppShellOperatingScopeQueryKey> {
  const workspaceKey = workspaceForPath(splitContinuityRoute(route).pathname).key;
  switch (workspaceKey) {
    case "data":
      return new Set(["symbol", "provider", "window"]);
    case "strategy":
      return new Set(["symbol", "runId", "provider", "window"]);
    case "trading":
    case "portfolio":
      return new Set(["symbol", "fundAccountId", "runId", "provider", "window"]);
    case "accounting":
    case "reporting":
      return new Set(["symbol", "fundAccountId", "runId", "provider", "window"]);
    case "settings":
      return new Set(["fundAccountId", "provider"]);
  }
}

function appendSearchValue(route: string, key: string, value: string, preserveExisting = false): string {
  const hashIndex = route.indexOf("#");
  const routeWithoutHash = hashIndex >= 0 ? route.slice(0, hashIndex) : route;
  const hash = hashIndex >= 0 ? route.slice(hashIndex) : "";
  const searchIndex = routeWithoutHash.indexOf("?");
  const pathname = searchIndex >= 0 ? routeWithoutHash.slice(0, searchIndex) : routeWithoutHash;
  const params = new URLSearchParams(searchIndex >= 0 ? routeWithoutHash.slice(searchIndex) : "");
  if (preserveExisting && params.has(key)) {
    return route;
  }

  params.set(key, value);
  const nextSearch = params.toString();
  return `${pathname}${nextSearch ? `?${nextSearch}` : ""}${hash}`;
}

const operatingScopeQueryParamKeys = ["symbol", "fundAccountId", "runId", "provider", "from", "to", "date", "asOf"] as const;
