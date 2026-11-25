import { readOperatingScopeFromSearch } from "@/app-shell.operating-scope";
import { normalizeFundAccountGuid } from "@/lib/fund-account-scope";
import { mergeOperatingScopes, readStoredOperatingScope } from "@/lib/operating-scope/persistence";
import { decodeViewStateEnvelope, encodeViewStateEnvelope } from "@/lib/view-state-envelope";
import {
  appendRouteQuery,
  WORKSTATION_ROUTE_CATALOG as routes,
  workstationRouteWithQuery,
  workspaceForPath,
  type WorkstationRouteQueryValue
} from "@/lib/workspace";

const recordKinds = ["instrument", "account", "fund", "position"] as const;
export type RecordKind = typeof recordKinds[number];

export const RECORD_SCOPE_LABELS = {
  symbol: "Symbol",
  securityId: "Security ID",
  positionKey: "Position ID",
  fundAccountId: "Brokerage account ID",
  financialAccountId: "Financial account ID",
  accountScopeId: "Account scope ID",
  fundProfileId: "Fund profile ID",
  fundId: "Fund ID",
  entityId: "Entity ID",
  ledgerBookId: "Ledger book ID",
  periodId: "Period ID",
  runId: "Run ID",
  provider: "Provider",
  asOf: "Scope as of",
  date: "Scope date",
  from: "Window start",
  to: "Window end"
} as const;

type RecordScope = Partial<Record<keyof typeof RECORD_SCOPE_LABELS, string | null>>;

export interface RecordIdentity {
  kind: RecordKind;
  id: string;
  name: string;
  identifier?: string | null;
  assetClass?: string | null;
  entity?: string | null;
  book?: string | null;
  status?: string | null;
  asOf?: string | null;
  trust?: string | null;
  trustTone?: "ready" | "review" | "unknown";
  scope?: RecordScope;
}

export interface RecordWorkspaceContext {
  record: RecordIdentity;
  origin: string;
  operatingScope: RecordScope;
}

const contextKey = "recordContext";
const originKey = "recordFrom";
const screen = "record-workspace-context";

const originPaths: Record<RecordKind, readonly string[]> = {
  instrument: [routes.portfolioAssetDetail, routes.accountingAssetDetail, routes.accountingSecurityMaster],
  account: [routes.accountingAccountDetail, routes.accountingLedger, routes.portfolioBrokerageSync],
  fund: [routes.accountingLedger, routes.portfolioFamilyOffice],
  position: [routes.portfolio, routes.portfolioBrokerageSync, routes.tradingPositions, routes.trading]
};

const scopeKeys = Object.keys(RECORD_SCOPE_LABELS) as (keyof RecordScope)[];

export function buildRecordWorkspaceContext(
  record: RecordIdentity,
  location: { pathname: string; search: string; hash: string },
  originQuery: Record<string, WorkstationRouteQueryValue>,
  originHash?: string
): RecordWorkspaceContext | null {
  const pathname = location.pathname.toLowerCase().replace(/\/+$/, "");
  const origin = validateOrigin(appendRouteQuery(
    `${pathname}${withoutRecordWorkspaceContext(location.search)}${originHash ?? location.hash}`,
    originQuery
  ), record.kind);
  if (!origin) return null;
  const routeParams = new URLSearchParams(location.search);
  const routeScope = Object.fromEntries(scopeKeys.map((key) => [key, routeParams.get(key)]).filter(([, value]) => value));
  const storedScope = readRecordWorkspaceContext(location.search) ? {} : readStoredOperatingScope();

  return {
    record,
    origin,
    // A sticky scope can differ from the record's own scope. Keep both as context
    // and pass only the record's supported dimensions to the destination.
    operatingScope: {
      ...mergeOperatingScopes(storedScope, readOperatingScopeFromSearch(location.search)),
      ...readScope(routeScope)
    }
  };
}

export function withoutRecordWorkspaceContext(search: string): string {
  const params = new URLSearchParams(search);
  if (!params.has(contextKey) && !params.has(originKey)) return search;
  params.delete(contextKey);
  params.delete(originKey);
  const value = params.toString();
  return value ? `?${value}` : "";
}

export function appendRecordWorkspaceContext(route: string, context: RecordWorkspaceContext): string | null {
  const snapshot = readIdentity(context.record);
  if (!snapshot) return null;
  const origin = validateOrigin(context.origin, snapshot.kind);
  if (!origin) return null;

  const token = encodeViewStateEnvelope({
    v: 1,
    screen,
    state: { record: snapshot, operatingScope: readScope(context.operatingScope) }
  });
  if (!token) return null;

  return appendRouteQuery(route, {
    [contextKey]: token,
    [originKey]: origin,
    scenario: new URL(origin, "https://meridian.local").searchParams.get("scenario")
  });
}

export function readRecordWorkspaceContext(search: string): RecordWorkspaceContext | null {
  const params = new URLSearchParams(search);
  const envelope = decodeViewStateEnvelope(params.get(contextKey));
  if (envelope?.screen !== screen) return null;

  const record = readIdentity(envelope.state.record);
  if (!record) return null;

  const origin = validateOrigin(params.get(originKey), record.kind);
  if (!origin) return null;

  return { record, origin, operatingScope: readScope(envelope.state.operatingScope) };
}

export function relatedRecordWork(record: RecordIdentity, pathname: string) {
  const { kind, scope } = record;
  const securityId = kind === "instrument" ? record.id : scope?.securityId;
  const symbol = scope?.symbol;
  const fundAccountId = scope?.fundAccountId;
  const ledgerBookId = scope?.ledgerBookId;
  const positionRef = scope?.positionKey ? `id:${scope.positionKey}` : null;
  const currentWorkspace = workspaceForPath(pathname).key;
  const portfolioWork = securityId
    ? { label: "Portfolio · Asset detail", route: workstationRouteWithQuery("portfolioAssetDetail", { securityId }) }
    : { label: "Portfolio · Holdings", route: workstationRouteWithQuery("portfolio", { positionRef }) };
  const brokerWork = { label: "Portfolio · Brokerage sync", route: workstationRouteWithQuery("portfolioBrokerageSync", { brokerageAccount: fundAccountId }) };
  const accountingSecurityWork = { label: "Accounting · Security record", route: workstationRouteWithQuery("accountingAssetDetail", { securityId, symbol }) };
  const accountingLedgerWork = { label: "Accounting · Ledger", route: workstationRouteWithQuery("accountingLedger", { ledgerBookId, periodId: scope?.periodId }) };

  const work = [
    fundAccountId && kind === "account" ? brokerWork : portfolioWork,
    (securityId || symbol) && (kind === "instrument" || !ledgerBookId) ? accountingSecurityWork : accountingLedgerWork,
    symbol
      ? { label: "Data · Live quotes", route: workstationRouteWithQuery("dataQuotes", { symbol }) }
      : { label: "Data · Assurance", route: routes.dataAssurance },
    { label: "Trading · Positions", route: workstationRouteWithQuery("tradingPositions", { positionRef, fundAccountId: normalizeFundAccountGuid(fundAccountId) }) },
    { label: "Reporting · Report library", route: routes.reportingLibrary }
  ];

  return work.filter(({ route }) => workspaceForPath(route.split("?")[0]).key !== currentWorkspace);
}

/** Return to a stable server key, or an unambiguous symbol and side on older responses. */
export function positionReference(position: { positionKey?: string | null; symbol: string; side: string }): string {
  return position.positionKey?.trim()
    ? `id:${position.positionKey.trim()}`
    : `symbol:${position.symbol}:${position.side}`;
}

export function findUniqueRecord<T>(rows: readonly T[], matches: (row: T) => boolean): T | null {
  const matching = rows.filter(matches);
  return matching.length === 1 ? matching[0] : null;
}

export function positionRecordIdentity(
  position: { positionKey?: string | null; symbol: string; side: string },
  mark: { reviewRequired: boolean; valuationDate: string },
  fundAccountId?: string | null
): RecordIdentity {
  return {
    kind: "position",
    id: position.positionKey?.trim() || `${position.symbol} · ${position.side}`,
    name: `${position.symbol} · ${position.side}`,
    identifier: position.positionKey ? position.symbol : null,
    status: "Open",
    asOf: mark.valuationDate === "Unknown" ? null : mark.valuationDate,
    trust: mark.reviewRequired ? "Mark review required" : "Mark current",
    trustTone: mark.reviewRequired ? "review" : "ready",
    scope: { symbol: position.symbol, positionKey: position.positionKey?.trim(), fundAccountId }
  };
}

function validateOrigin(value: string | null, kind: RecordKind): string | null {
  if (!value || value.length > 4096 || /[\\\s]/.test(value)) return null;
  const path = value.split(/[?#]/, 1)[0];
  return originPaths[kind].includes(path) || kind === "instrument" && /^\/records\/instrument\/[a-z0-9_-]+$/i.test(path) ? value : null;
}

function readIdentity(value: unknown): RecordIdentity | null {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  const record = value as Record<string, unknown>;
  const kind = recordKinds.find((candidate) => candidate === record.kind);
  const id = readText(record.id);
  const name = readText(record.name);
  if (!kind || !id || !name) return null;

  return {
    kind,
    id,
    name,
    identifier: readText(record.identifier),
    assetClass: readText(record.assetClass),
    entity: readText(record.entity),
    book: readText(record.book),
    status: readText(record.status),
    asOf: readText(record.asOf),
    trust: readText(record.trust),
    trustTone: record.trustTone === "ready" || record.trustTone === "review" ? record.trustTone : "unknown",
    scope: readScope(record.scope)
  };
}

function readScope(value: unknown): RecordScope {
  if (!value || typeof value !== "object" || Array.isArray(value)) return {};
  const scope = value as Record<string, unknown>;
  return Object.fromEntries(scopeKeys.flatMap((key) => {
    const text = readText(scope[key]);
    return text ? [[key, text]] : [];
  }));
}

function readText(value: unknown): string | null {
  return typeof value === "string" && value.trim().length > 0 && value.length <= 320
    && !/\p{Cc}/u.test(value) ? value.trim() : null;
}
