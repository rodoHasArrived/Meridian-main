// Narrowing by the operating scope's entity and fund, for screens whose rows are keyed by fund
// account and for the inline account picker. Relationships come only from the organization graph
// (`structure-options.ts`): an account the graph places under another fund or entity is left out;
// an account the graph does not place is left out of a narrowed list too, but always counted so the
// screen can say so — apart from one whose fund or entity exists but is withheld from the caller,
// which is counted as such rather than as having none; and without the graph (not loaded, or the operator lacks fund-structure
// access) nothing is narrowed and the screen says why.
import type { AppShellOperatingScopeInput, OperatingScopeValueLabels } from "@/app-shell.operating-scope";
import type { BrokerageHouseholdPortfolio } from "@/types";
import type { ScopeFundAccountOption } from "@/lib/operating-scope/fund-accounts";
import {
  accountConflictsWithScope,
  constrainScopeOptions,
  mergeScopeAccountOptions,
  placeAccountsInScope,
  type ConstrainedScopeOptions,
  type ScopeStructureOptions
} from "@/lib/operating-scope/structure-options";

export type FundScopeSelection = Pick<AppShellOperatingScopeInput, "entityId" | "fundId">;

/**
 * - `inactive`: no entity or fund is chosen, so there is nothing to narrow by.
 * - `loading`: one is chosen and the organization graph is still loading; rows are not narrowed yet.
 * - `unavailable`: one is chosen but the graph could not be read; rows are not narrowed.
 * - `applied`: rows were narrowed to the chosen fund (or entity).
 */
export type FundScopeNarrowingStatus = "inactive" | "loading" | "unavailable" | "applied";

export interface FundScopeRowNarrowing<T> {
  status: FundScopeNarrowingStatus;
  /** The rows to show: every row unless `status` is `applied`. */
  rows: T[];
  /** Rows before narrowing. */
  totalCount: number;
  /** Rows left out because the graph places their account under another fund or entity. */
  outsideCount: number;
  /** Rows left out because the graph does not say which fund (or entity) their account is under. */
  unknownCount: number;
  /**
   * Rows left out because their account's fund (or entity) exists but is not visible to the
   * caller, so it cannot be placed. Optional so hand-built narrowings stay valid; absent means 0.
   */
  withheldCount?: number;
  /** Which dimension did the narrowing: the fund when one is chosen, else the entity. */
  dimension: "fund" | "entity" | null;
}

/** The status for a chosen entity/fund against the graph's load state. */
export function fundScopeNarrowingStatus(
  structure: ScopeStructureOptions,
  selection: FundScopeSelection,
  loading = false
): FundScopeNarrowingStatus {
  if (!normalizeOptional(selection.entityId) && !normalizeOptional(selection.fundId)) return "inactive";
  if (structure.status === "ready") return "applied";
  return loading ? "loading" : "unavailable";
}

/**
 * Keeps the rows whose fund account the organization graph places under the chosen fund (or, with
 * only an entity chosen, booked to that entity or held by a fund that holds it) — the same rule the
 * scope picker narrows its account list by (`constrainScopeOptions`). Rows are never narrowed
 * without the graph.
 */
export function narrowRowsToFundScope<T>(
  rows: readonly T[],
  getFundAccountId: (row: T) => string | null | undefined,
  structure: ScopeStructureOptions,
  selection: FundScopeSelection,
  loading = false
): FundScopeRowNarrowing<T> {
  const status = fundScopeNarrowingStatus(structure, selection, loading);
  const fundId = normalizeOptional(selection.fundId);
  const dimension = status === "inactive" ? null : fundId ? "fund" : "entity";
  if (status !== "applied") {
    return { status, rows: [...rows], totalCount: rows.length, outsideCount: 0, unknownCount: 0, withheldCount: 0, dimension };
  }

  const rowAccounts = rows
    .map((row) => getFundAccountId(row)?.trim())
    .filter((id): id is string => Boolean(id))
    .map((id) => ({ id, label: id }));
  // The same placement the scope picker narrows its account list by (`constrainScopeOptions`).
  const placements = placeAccountsInScope(structure, mergeScopeAccountOptions(structure, rowAccounts), selection);

  let outsideCount = 0;
  let unknownCount = 0;
  let withheldCount = 0;
  const narrowed = rows.filter((row) => {
    const id = normalizeOptional(getFundAccountId(row));
    const placement = id ? placements?.get(id) : undefined;
    if (placement === "inside") return true;
    if (placement === "outside") {
      outsideCount += 1;
    } else if (placement === "withheld") {
      withheldCount += 1;
    } else {
      unknownCount += 1;
    }
    return false;
  });

  return { status, rows: narrowed, totalCount: rows.length, outsideCount, unknownCount, withheldCount, dimension };
}

/**
 * One sentence on what the narrowing did, for the screen to print next to the rows; null when no
 * entity or fund is chosen. `noun` is the plural row noun ("accounts", "close workflows").
 */
export function describeFundScopeNarrowing(
  narrowing: Pick<FundScopeRowNarrowing<unknown>, "status" | "rows" | "totalCount" | "unknownCount" | "withheldCount" | "dimension">,
  noun: string,
  /** Display names (or ids) for the chosen entity and fund, as the scope bar prints them. */
  names: Pick<OperatingScopeValueLabels, "entityId" | "fundId"> = {}
): string | null {
  const dimension = narrowing.dimension ?? "fund";
  switch (narrowing.status) {
    case "inactive":
      return null;
    case "loading":
      return `Loading the organization structure; ${noun} are not narrowed by ${dimension} yet.`;
    case "unavailable":
      return `Not narrowed by entity or fund: the organization structure could not be loaded, so all authorized ${noun} are shown.`;
    case "applied": {
      const name = (dimension === "fund" ? names.fundId : names.entityId)?.trim();
      const target = name ? `${dimension} ${name}` : `the chosen ${dimension}`;
      const shown = `Narrowed to ${target}: showing ${narrowing.rows.length} of ${narrowing.totalCount} ${singularize(noun, narrowing.totalCount)}.`;
      return `${shown}${describeLeftOut(narrowing.unknownCount, narrowing.withheldCount ?? 0, dimension)}`;
    }
  }
}

export interface NarrowedHouseholdPortfolio {
  /** The household narrowed to the chosen fund or entity, or the payload unchanged. */
  portfolio: BrokerageHouseholdPortfolio | null | undefined;
  /** How the household's accounts were narrowed (positions follow their account). */
  accounts: FundScopeRowNarrowing<BrokerageHouseholdPortfolio["accounts"][number]>;
}

/**
 * The brokerage household narrowed to the chosen fund or entity: its accounts, the positions held
 * in them, and household totals re-summed over the accounts kept. Unchanged (same object) unless
 * the narrowing applied.
 */
export function narrowHouseholdPortfolioToFundScope(
  portfolio: BrokerageHouseholdPortfolio | null | undefined,
  structure: ScopeStructureOptions,
  selection: FundScopeSelection,
  loading = false
): NarrowedHouseholdPortfolio {
  const accountRows = Array.isArray(portfolio?.accounts) ? portfolio.accounts : [];
  const accounts = narrowRowsToFundScope(accountRows, (account) => account?.fundAccountId, structure, selection, loading);
  if (!portfolio || accounts.status !== "applied") {
    return { portfolio, accounts };
  }

  const positions = narrowRowsToFundScope(
    Array.isArray(portfolio.positions) ? portfolio.positions : [],
    (position) => position?.fundAccountId,
    structure,
    selection
  ).rows;
  const sum = (pick: (account: BrokerageHouseholdPortfolio["accounts"][number]) => number) =>
    accounts.rows.reduce((total, account) => total + (Number.isFinite(pick(account)) ? pick(account) : 0), 0);

  return {
    portfolio: {
      ...portfolio,
      accounts: accounts.rows,
      positions,
      totalCash: sum((account) => account.cash),
      totalEquity: sum((account) => account.equity),
      totalBuyingPower: sum((account) => account.buyingPower)
    },
    accounts
  };
}

export interface InlineAccountNarrowing {
  options: ScopeFundAccountOption[];
  /** The help line the scope picker prints for its account list; null when nothing narrowed. */
  help: string | null;
  constrained: ConstrainedScopeOptions;
}

/**
 * The inline account picker's suggestions, narrowed exactly as the scope picker narrows its
 * account list: the graph's accounts merged with the loaded ones, then constrained by the chosen
 * entity and fund.
 */
export function narrowInlineAccountOptions(
  options: readonly ScopeFundAccountOption[],
  structure: ScopeStructureOptions,
  selection: FundScopeSelection
): InlineAccountNarrowing {
  const constrained = constrainScopeOptions(structure, mergeScopeAccountOptions(structure, options), selection);
  return {
    options: constrained.accounts,
    help: describeAccountListNarrowing(constrained, selection),
    constrained
  };
}

/** The scope picker's account-list help ("Showing 2 accounts under the chosen fund. …"). */
export function describeAccountListNarrowing(
  constrained: Pick<ConstrainedScopeOptions, "accounts" | "accountsConstrained" | "accountsWithoutKnownRelationship">
    & Partial<Pick<ConstrainedScopeOptions, "accountsWithWithheldRelationship">>,
  selection: FundScopeSelection
): string | null {
  if (!constrained.accountsConstrained) return null;
  const dimension = normalizeOptional(selection.fundId) ? "fund" : "entity";
  const count = constrained.accounts.length;
  return `Showing ${count} account${count === 1 ? "" : "s"} under the chosen ${dimension}.${describeLeftOut(
    constrained.accountsWithoutKnownRelationship,
    constrained.accountsWithWithheldRelationship ?? 0,
    dimension
  )}`;
}

/**
 * The trailing sentences on what a narrowing left out and could not place (with a leading space),
 * or "" when nothing was: accounts with no recorded fund/entity, and accounts whose fund/entity
 * exists but is not visible to the caller. The two are never merged: the second do have one.
 */
function describeLeftOut(unknownCount: number, withheldCount: number, dimension: "fund" | "entity"): string {
  let text = "";
  if (unknownCount > 0) text += ` ${unknownCount} with no recorded ${dimension} not shown.`;
  // With an entity chosen, an account can go unplaced because its fund is withheld as well as its
  // entity, so that note names both.
  if (withheldCount > 0) {
    text += ` ${withheldCount} whose ${dimension === "fund" ? "fund" : "fund or entity"} is not visible to you not shown.`;
  }
  return text;
}

/**
 * Whether an account is known to sit outside the chosen fund or entity, judged against the same
 * merged options the scope picker uses. Unknown accounts and an unloaded graph are never flagged.
 */
export function accountKnownOutsideFundScope(
  accountId: string | null | undefined,
  options: readonly ScopeFundAccountOption[],
  structure: ScopeStructureOptions,
  selection: FundScopeSelection
): boolean {
  if (!normalizeOptional(selection.entityId) && !normalizeOptional(selection.fundId)) return false;
  return accountConflictsWithScope(structure, mergeScopeAccountOptions(structure, options), accountId, selection);
}

function singularize(noun: string, count: number): string {
  return count === 1 && noun.endsWith("s") ? noun.slice(0, -1) : noun;
}

function normalizeId(id: string): string {
  return id.trim().toLowerCase();
}

function normalizeOptional(id: string | null | undefined): string | null {
  return id && id.trim() ? normalizeId(id) : null;
}
