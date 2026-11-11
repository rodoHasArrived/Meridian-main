import { apiGetJson } from "@/lib/api";
import {
  WORKSTATION_API_ENDPOINTS,
  ledgerPeriodAccountRunningBalanceEndpoint,
  ledgerPeriodJournalEntriesEndpoint,
  ledgerPeriodPnlSummaryEndpoint,
  ledgerPeriodsEndpoint,
  ledgerPeriodTrialBalanceEndpoint
} from "@/lib/workstation-endpoints";
import type {
  LedgerAccountRunningBalance,
  LedgerBook,
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

/** Posting history and running balance for one account of a ledger period (durable book of record). */
export function getLedgerPeriodAccountRunningBalance(
  periodId: string,
  query: { account: string; symbol?: string | null; financialAccountId?: string | null }
) {
  return apiGetJson<LedgerAccountRunningBalance>(ledgerPeriodAccountRunningBalanceEndpoint(periodId, query));
}
