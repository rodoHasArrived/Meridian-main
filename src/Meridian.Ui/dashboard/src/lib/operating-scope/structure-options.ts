// Entity, fund and fund-account options for the operating-scope picker, and the rules that let a
// chosen entity narrow the funds and a chosen fund narrow the accounts.
//
// Source: the existing shared organization read model, `GET /api/fund-structure/graph`
// (`OrganizationStructureGraphDto` in Meridian.Contracts). It already lists the legal entities
// (`entities[]`), the funds with the entities they hold (`funds[].entityIds`) and the accounts
// with the fund and entity they belong to (`accounts[].fundId` / `accounts[].entityId`), filtered
// server-side to what the operator may see. The account id there is the same fund-account id the
// brokerage household payload uses. That endpoint is gated to fund-structure permissions, so for
// other operators the picker falls back to the household accounts only, with no relationships,
// and says so; it never guesses a relationship the payload does not carry.
import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import type { AppShellOperatingScopeInput, OperatingScopeValueLabels } from "@/app-shell.operating-scope";
import type { ScopeFundAccountOption } from "@/lib/operating-scope/fund-accounts";

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

/** Reads entities, funds and accounts from an organization graph payload, defensively. */
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
      return id
        ? {
            id,
            label: readLabel(account.displayName, id),
            fundId: readId(account.fundId),
            entityId: readId(account.entityId)
          }
        : null;
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
}

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
    accountsWithoutKnownRelationship: 0
  };
  if (structure.status !== "ready" || (!entityId && !fundId)) {
    return unconstrained;
  }

  const fundsForEntity = entityId ? fundIdsForEntity(structure, accounts, entityId) : null;
  const funds = fundsForEntity
    ? structure.funds.filter((fund) => fundsForEntity.has(normalizeId(fund.id)))
    : structure.funds;

  let withoutKnownRelationship = 0;
  const narrowedAccounts = accounts.filter((account) => {
    const accountFund = normalizeOptional(account.fundId);
    const accountEntity = normalizeOptional(account.entityId);
    if (fundId) {
      if (!accountFund) {
        withoutKnownRelationship += 1;
        return false;
      }
      if (accountFund !== fundId) {
        return false;
      }
    }
    if (entityId) {
      if (accountEntity) {
        return accountEntity === entityId;
      }
      if (accountFund) {
        return fundsForEntity?.has(accountFund) ?? false;
      }
      if (!fundId) {
        withoutKnownRelationship += 1;
      }
      return false;
    }
    return true;
  });

  return {
    entities: structure.entities,
    funds,
    accounts: narrowedAccounts,
    fundsConstrained: Boolean(entityId),
    accountsConstrained: true,
    accountsWithoutKnownRelationship: withoutKnownRelationship
  };
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
  return !constrainScopeOptions(structure, [account], selection).accounts.length;
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
