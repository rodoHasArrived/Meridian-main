import { apiGetJson } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import {
  WORKSTATION_API_ENDPOINTS,
  ledgerPeriodJournalEntriesEndpoint,
  ledgerPeriodPnlSummaryEndpoint,
  ledgerPeriodsEndpoint,
  ledgerPeriodTrialBalanceEndpoint,
  routeWithParam
} from "@/lib/workstation-endpoints";
import type {
  LedgerAccountRunningBalance,
  LedgerBook,
  LedgerDimensionSet,
  LedgerPeriod,
  LedgerPeriodPnlSummary,
  LedgerPeriodTrialBalanceLine,
  LedgerPostedJournalEntry
} from "@/types";

/**
 * Clients for the posted-journal ledger reporting routes — the governed book of
 * record, scoped by ledger period. Kept out of `lib/api.ts` per the file-size
 * ratchet; follows the `reporting-governance-api.ts` domain-module pattern.
 */

export function getLedgerBooks() {
  return apiGetJson<LedgerBook[]>(WORKSTATION_API_ENDPOINTS.ledgerBooks);
}

export function getLedgerPeriods(query: { ledgerBookId?: string | null; status?: string | null } = {}) {
  return apiGetJson<LedgerPeriod[]>(ledgerPeriodsEndpoint(query));
}

export function getLedgerPeriodTrialBalance(periodId: string) {
  return apiGetJson<LedgerPeriodTrialBalanceLine[]>(ledgerPeriodTrialBalanceEndpoint(periodId));
}

export function getLedgerPeriodPnlSummary(periodId: string) {
  return apiGetJson<LedgerPeriodPnlSummary>(ledgerPeriodPnlSummaryEndpoint(periodId));
}

export function getLedgerPeriodJournalEntries(periodId: string) {
  return apiGetJson<LedgerPostedJournalEntry[]>(ledgerPeriodJournalEntriesEndpoint(periodId));
}

export interface LedgerAccountRunningBalanceQuery {
  account: string;
  symbol?: string | null;
  financialAccountId?: string | null;
  dimensions?: LedgerDimensionSet | null;
}

/**
 * Route for one account's posting history in a ledger period. Lives here rather than in
 * `workstation-endpoints.ts`, which is at its file-size limit.
 */
export function ledgerPeriodAccountRunningBalanceEndpoint(
  periodId: string,
  query: LedgerAccountRunningBalanceQuery
): string {
  // `undefined` omits a filter (any value); `null` or "" is sent empty and selects postings that
  // have no symbol / no financial account, so an unscoped trial-balance row does not also return
  // the account-scoped postings of the same account name.
  const params = new URLSearchParams({ account: query.account });
  if (query.symbol !== undefined) {
    params.set("symbol", query.symbol ?? "");
  }
  if (query.financialAccountId !== undefined) {
    params.set("financialAccountId", query.financialAccountId ?? "");
  }
  // The trial balance has one row per account per dimension set. Sending the selected row's set
  // (JSON; `null` for a row without one) limits the postings to that row; omitting it returns
  // every dimension set of the account.
  if (query.dimensions !== undefined) {
    params.set("dimensions", JSON.stringify(query.dimensions));
  }

  return `${routeWithParam(UI_API_ROUTES.LedgerPeriodAccountRunningBalance, "periodId", periodId)}?${params.toString()}`;
}

/** Posting history and running balance for one account of a ledger period (durable book of record). */
export function getLedgerPeriodAccountRunningBalance(
  periodId: string,
  query: LedgerAccountRunningBalanceQuery
) {
  return apiGetJson<LedgerAccountRunningBalance>(ledgerPeriodAccountRunningBalanceEndpoint(periodId, query));
}
