// Entity, fund and fund-account options for the operating-scope picker, and the rules that let a
// chosen entity narrow the funds and a chosen fund narrow the accounts.
//
// Source: `GET /api/workstation/scope-options` (`WorkstationScopeOptionsDto` in
// Meridian.Contracts). It is open to every authenticated operator and lists only the legal
// entities (`entities[]`), funds with the entities they hold (`funds[].entityIds`) and accounts
// with their fund and entity (`accounts[].fundId` / `accounts[].entityId`) that the caller's own
// scoped grants let it read; a relationship pointing outside that set arrives as null, with
// `accounts[].fundWithheld` / `accounts[].entityWithheld` set when the account does have that fund or
// entity but the caller may not see it (only that a relationship exists is disclosed, never its id
// or name). The graph fallback sends no such flags, so they read as false there. Its field
// names match the organization graph's summaries, so one parser reads both. A host older than
// that route answers 404, and the picker then falls back to the organization graph,
// `GET /api/fund-structure/graph` (`OrganizationStructureGraphDto`), which is gated to
// fund-structure permissions. The account id in either payload is the same fund-account id the
// brokerage household payload uses. When neither loads, the picker falls back to the household
// accounts only, with no relationships, and says so; it never guesses a relationship the payload
// does not carry.
import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import { ApiError } from "@/lib/api-errors";
import { WORKSTATION_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import type { AppShellOperatingScopeInput, OperatingScopeValueLabels } from "@/app-shell.operating-scope";
import type { ScopeFundAccountOption } from "@/lib/operating-scope/fund-accounts";

/** The caller's authorized scope options (active nodes only), open to every operator. */
export const SCOPE_OPTIONS_ENDPOINT: string = WORKSTATION_API_ENDPOINTS.scopeOptions;

/** Active nodes only: the scope picker offers what can be scoped to today. */
export const SCOPE_STRUCTURE_GRAPH_ENDPOINT = "/api/fund-structure/graph?activeOnly=true";

export interface ScopeEntityOption {
  id: string;
  label: string;
}

export interface ScopeFundOption {
  id: string;
  label: string;
  /** Legal entities the fund holds, as the graph reports them. */
  entityIds: string[];
}

export interface ScopeStructureOptions {
  /** "ready" when the organization graph loaded; otherwise no relationships are known. */
  status: "ready" | "unavailable";
  entities: ScopeEntityOption[];
  funds: ScopeFundOption[];
  /** Accounts with the fund and entity the graph places them under. */
  accounts: ScopeFundAccountOption[];
}

export const UNAVAILABLE_SCOPE_STRUCTURE: ScopeStructureOptions = Object.freeze({
  status: "unavailable",
  entities: [],
  funds: [],
  accounts: []
}) as ScopeStructureOptions;

/** Loads the organization graph. Development fixtures are refused: these are authority relationships. */
export function getScopeStructureGraph(options: ApiRequestOptions = {}): Promise<unknown> {
  return apiGetJson<unknown>(SCOPE_STRUCTURE_GRAPH_ENDPOINT, { ...options, allowDevelopmentFallback: false });
}

/** Loads the caller's scope options. Development fixtures are refused: these are authority relationships. */
export function getScopeOptions(options: ApiRequestOptions = {}): Promise<unknown> {
  return apiGetJson<unknown>(SCOPE_OPTIONS_ENDPOINT, { ...options, allowDevelopmentFallback: false });
}

export interface ScopeStructureSources {
  scopeOptions?: typeof getScopeOptions;
  graph?: typeof getScopeStructureGraph;
}

/**
 * Loads the scope structure: the scope-options read first, and the organization graph only when
 * the host does not have that route (404). Any other failure, a 403 included, is passed on rather
 * than retried against the graph, which the caller could not read either.
 */
export async function loadScopeStructure(
  options: ApiRequestOptions = {},
  { scopeOptions = getScopeOptions, graph = getScopeStructureGraph }: ScopeStructureSources = {}
): Promise<unknown> {
  try {
    return await scopeOptions(options);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404 && !options.signal?.aborted) {
      return graph(options);
    }
    throw error;
  }
}

/**
 * The picker's note under the entity and fund fields: why a list is missing, or null when both
 * lists are there. An empty list is what the operator may see, not a failed load.
 */
export function scopeStructureHelp(structure: ScopeStructureOptions, loading: boolean): string | null {
  if (loading) return "Loading entities and funds…";
  if (structure.status !== "ready") {
    return "Entity and fund lists could not be loaded, so enter an id. Accounts are not narrowed by entity or fund.";
  }
  const noEntities = structure.entities.length === 0;
  const noFunds = structure.funds.length === 0;
  if (noEntities && noFunds) return "No entities or funds are visible to you, so enter an id.";
  if (noEntities) return "No entities are visible to you, so enter an entity id.";
  if (noFunds) return "No funds are visible to you, so enter a fund id.";
  return null;
}

/** Reads entities, funds and accounts from a scope-options or organization graph payload, defensively. */
export function collectScopeStructureOptions(graph: unknown): ScopeStructureOptions {
  if (!isRecord(graph) || !(Array.isArray(graph.entities) || Array.isArray(graph.funds) || Array.isArray(graph.accounts))) {
    return UNAVAILABLE_SCOPE_STRUCTURE;
  }

  const entities = distinctById(
    readArray(graph.entities).map((entity) => {
      const id = readId(entity.entityId);
      return id ? { id, label: readLabel(entity.name, id) } : null;
    })
  );
  const funds = distinctById(
    readArray(graph.funds).map((fund) => {
      const id = readId(fund.fundId);
      return id
        ? { id, label: readLabel(fund.name, id), entityIds: readIdList(fund.entityIds) }
        : null;
    })
  );
  const accounts = distinctById(
    readArray(graph.accounts).map((account) => {
      const id = readId(account.accountId);
      if (!id) return null;
      const option: ScopeFundAccountOption = {
        id,
        label: readLabel(account.displayName, id),
        fundId: readId(account.fundId),
        entityId: readId(account.entityId)
      };
      // Only a literal true counts, and only beside a null relationship; the flags are left off
      // otherwise so an account the caller can fully place looks the same from either payload.
      if (account.fundWithheld === true && !option.fundId) option.fundWithheld = true;
      if (account.entityWithheld === true && !option.entityId) option.entityWithheld = true;
      return option;
    })
  );

  return { status: "ready", entities, funds, accounts };
}

/**
 * Every account the picker can offer: the graph's accounts (with their fund and entity), plus any
 * household account the graph does not list, which carries no known relationship.
 */
export function mergeScopeAccountOptions(
  structure: ScopeStructureOptions,
  householdAccounts: readonly ScopeFundAccountOption[]
): ScopeFundAccountOption[] {
  const byId = new Map<string, ScopeFundAccountOption>();
  for (const account of structure.accounts) {
    byId.set(normalizeId(account.id), account);
  }
  for (const account of householdAccounts) {
    const key = normalizeId(account.id);
    if (!byId.has(key)) {
      byId.set(key, account);
    }
  }
  return [...byId.values()].sort((a, b) => a.label.localeCompare(b.label));
}

export interface ConstrainedScopeOptions {
  entities: ScopeEntityOption[];
  funds: ScopeFundOption[];
  accounts: ScopeFundAccountOption[];
  /** True when a chosen entity narrowed the fund list. */
  fundsConstrained: boolean;
  /** True when a chosen entity or fund narrowed the account list. */
  accountsConstrained: boolean;
  /** Accounts left out because the data does not say which fund or entity they belong to. */
  accountsWithoutKnownRelationship: number;
  /**
   * Accounts left out because their fund (or entity) exists but is not visible to the caller, so
   * they cannot be placed. Counted apart from `accountsWithoutKnownRelationship`: they do have one.
   */
  accountsWithWithheldRelationship: number;
}

/**
 * Where an account sits against a chosen entity/fund:
 *
 * - `inside`: under the chosen fund (or entity);
 * - `outside`: the data places it under another one;
 * - `withheld`: it has a fund (or entity) the caller may not see, so it cannot be placed;
 * - `unrecorded`: the data records no fund (or entity) for it.
 */
export type ScopeAccountPlacement = "inside" | "outside" | "withheld" | "unrecorded";

/**
 * The choices left once an entity and/or fund is picked:
 *
 * - an entity keeps the funds that hold it (`funds[].entityIds`) or that own an account booked to
 *   it;
 * - an entity keeps the accounts booked to it, and accounts with no entity of their own whose fund
 *   holds it;
 * - a fund keeps its own accounts (`accounts[].fundId`).
 *
 * Without the organization graph nothing is known, so nothing is narrowed. With it, an account
 * whose fund (or entity) the data does not state is left out of a narrowed list and counted, so
 * the picker can say how many it hid rather than guess.
 */
export function constrainScopeOptions(
  structure: ScopeStructureOptions,
  accounts: readonly ScopeFundAccountOption[],
  selection: Pick<AppShellOperatingScopeInput, "entityId" | "fundId">
): ConstrainedScopeOptions {
  const entityId = normalizeOptional(selection.entityId);
  const fundId = normalizeOptional(selection.fundId);
  const unconstrained: ConstrainedScopeOptions = {
    entities: structure.entities,
    funds: structure.funds,
    accounts: [...accounts],
    fundsConstrained: false,
    accountsConstrained: false,
    accountsWithoutKnownRelationship: 0,
    accountsWithWithheldRelationship: 0
  };
  if (structure.status !== "ready" || (!entityId && !fundId)) {
    return unconstrained;
  }

  const fundsForEntity = entityId ? fundIdsForEntity(structure, accounts, entityId) : null;
  const funds = fundsForEntity
    ? structure.funds.filter((fund) => fundsForEntity.has(normalizeId(fund.id)))
    : structure.funds;

  let withoutKnownRelationship = 0;
  let withWithheldRelationship = 0;
  const narrowedAccounts = accounts.filter((account) => {
    const placement = placeAccount(account, fundId, entityId, fundsForEntity);
    if (placement === "unrecorded") withoutKnownRelationship += 1;
    if (placement === "withheld") withWithheldRelationship += 1;
    return placement === "inside";
  });

  return {
    entities: structure.entities,
    funds,
    accounts: narrowedAccounts,
    fundsConstrained: Boolean(entityId),
    accountsConstrained: true,
    accountsWithoutKnownRelationship: withoutKnownRelationship,
    accountsWithWithheldRelationship: withWithheldRelationship
  };
}

/**
 * Each account's placement against the chosen entity/fund (keyed by lower-cased id), by the same
 * rule `constrainScopeOptions` narrows with; null when nothing is narrowed (no selection, or no
 * graph).
 */
export function placeAccountsInScope(
  structure: ScopeStructureOptions,
  accounts: readonly ScopeFundAccountOption[],
  selection: Pick<AppShellOperatingScopeInput, "entityId" | "fundId">
): Map<string, ScopeAccountPlacement> | null {
  const entityId = normalizeOptional(selection.entityId);
  const fundId = normalizeOptional(selection.fundId);
  if (structure.status !== "ready" || (!entityId && !fundId)) {
    return null;
  }
  const fundsForEntity = entityId ? fundIdsForEntity(structure, accounts, entityId) : null;
  const placements = new Map<string, ScopeAccountPlacement>();
  for (const account of accounts) {
    placements.set(normalizeId(account.id), placeAccount(account, fundId, entityId, fundsForEntity));
  }
  return placements;
}

/**
 * Whether a picked fund is known not to hold the picked entity (so choosing the entity should
 * clear it). Unknown funds or an unloaded graph are never treated as conflicts.
 */
export function fundConflictsWithEntity(
  structure: ScopeStructureOptions,
  accounts: readonly ScopeFundAccountOption[],
  fundId: string | null | undefined,
  entityId: string | null | undefined
): boolean {
  const fund = normalizeOptional(fundId);
  const entity = normalizeOptional(entityId);
  if (structure.status !== "ready" || !fund || !entity) {
    return false;
  }
  if (!structure.funds.some((candidate) => normalizeId(candidate.id) === fund)) {
    return false;
  }
  return !fundIdsForEntity(structure, accounts, entity).has(fund);
}

/**
 * Whether a picked account is known to sit outside the picked fund or entity (so it should be
 * cleared). An account whose relationships the data does not state is never treated as a conflict.
 */
export function accountConflictsWithScope(
  structure: ScopeStructureOptions,
  accounts: readonly ScopeFundAccountOption[],
  accountId: string | null | undefined,
  selection: Pick<AppShellOperatingScopeInput, "entityId" | "fundId">
): boolean {
  const id = normalizeOptional(accountId);
  if (structure.status !== "ready" || !id) {
    return false;
  }
  const account = accounts.find((candidate) => normalizeId(candidate.id) === id);
  if (!account || (!normalizeOptional(account.fundId) && !normalizeOptional(account.entityId))) {
    return false;
  }
  // Place against every account: a fund's link to an entity can come from another account, and
  // the picker builds its options the same way, so an account it offers is never treated as a
  // conflict. A withheld fund or entity might be the chosen one, so that is never a conflict either.
  const placement = placeAccountsInScope(structure, accounts, selection)?.get(id);
  return placement === "outside" || placement === "unrecorded";
}

/** Display names for the scope's entity, fund and account ids, where a loaded payload gave one. */
export function scopeValueLabels(
  scope: Pick<AppShellOperatingScopeInput, "entityId" | "fundId" | "fundAccountId">,
  structure: ScopeStructureOptions,
  accounts: readonly ScopeFundAccountOption[]
): OperatingScopeValueLabels {
  const find = (options: readonly { id: string; label: string }[], id: string | null | undefined) => {
    const key = normalizeOptional(id);
    const match = key ? options.find((option) => normalizeId(option.id) === key) : undefined;
    return match && match.label !== match.id ? match.label : undefined;
  };
  return {
    entityId: find(structure.entities, scope.entityId),
    fundId: find(structure.funds, scope.fundId),
    fundAccountId: find(accounts, scope.fundAccountId)
  };
}

/** One account against the chosen (normalized) fund/entity; see `ScopeAccountPlacement`. */
function placeAccount(
  account: ScopeFundAccountOption,
  fundId: string | null,
  entityId: string | null,
  fundsForEntity: Set<string> | null
): ScopeAccountPlacement {
  const accountFund = normalizeOptional(account.fundId);
  const accountEntity = normalizeOptional(account.entityId);
  if (fundId) {
    if (!accountFund) {
      return account.fundWithheld === true ? "withheld" : "unrecorded";
    }
    if (accountFund !== fundId) {
      return "outside";
    }
  }
  if (entityId) {
    if (accountEntity) {
      return accountEntity === entityId ? "inside" : "outside";
    }
    if (accountFund) {
      return fundsForEntity?.has(accountFund) ? "inside" : "outside";
    }
    // Only reachable without a chosen fund: a chosen fund already returned for a fundless account.
    return account.entityWithheld === true || account.fundWithheld === true ? "withheld" : "unrecorded";
  }
  return "inside";
}

function fundIdsForEntity(
  structure: ScopeStructureOptions,
  accounts: readonly ScopeFundAccountOption[],
  entityId: string
): Set<string> {
  const ids = new Set<string>();
  for (const fund of structure.funds) {
    if (fund.entityIds.some((candidate) => normalizeId(candidate) === entityId)) {
      ids.add(normalizeId(fund.id));
    }
  }
  for (const account of accounts) {
    const accountFund = normalizeOptional(account.fundId);
    if (accountFund && normalizeOptional(account.entityId) === entityId) {
      ids.add(accountFund);
    }
  }
  return ids;
}

function distinctById<T extends { id: string; label: string }>(items: (T | null)[]): T[] {
  const byId = new Map<string, T>();
  for (const item of items) {
    if (item && !byId.has(normalizeId(item.id))) {
      byId.set(normalizeId(item.id), item);
    }
  }
  return [...byId.values()].sort((a, b) => a.label.localeCompare(b.label));
}

function readArray(value: unknown): Record<string, unknown>[] {
  return Array.isArray(value) ? value.filter(isRecord) : [];
}

function readId(value: unknown): string | null {
  return typeof value === "string" && value.trim() ? value.trim() : null;
}

function readIdList(value: unknown): string[] {
  return Array.isArray(value) ? value.map(readId).filter((id): id is string => id !== null) : [];
}

function readLabel(value: unknown, fallback: string): string {
  return typeof value === "string" && value.trim() ? value.trim() : fallback;
}

/** Ids compare case-insensitively: the graph and the URL may differ only in GUID case. */
function normalizeId(id: string): string {
  return id.trim().toLowerCase();
}

function normalizeOptional(id: string | null | undefined): string | null {
  return id && id.trim() ? normalizeId(id) : null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value);
}
