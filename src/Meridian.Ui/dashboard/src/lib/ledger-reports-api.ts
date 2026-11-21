import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import {
  WORKSTATION_API_ENDPOINTS,
  ledgerPeriodJournalEntriesEndpoint,
  ledgerPeriodPnlSummaryEndpoint,
  ledgerPeriodsEndpoint,
  ledgerPeriodTrialBalanceEndpoint
} from "@/lib/workstation-endpoints";
import type {
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

export function getLedgerBooks(options: ApiRequestOptions = {}) {
  return apiGetJson<LedgerBook[]>(WORKSTATION_API_ENDPOINTS.ledgerBooks, options);
}

export function getLedgerPeriods(query: { ledgerBookId?: string | null; status?: string | null } = {}, options: ApiRequestOptions = {}) {
  return apiGetJson<LedgerPeriod[]>(ledgerPeriodsEndpoint(query), options);
}

export function getLedgerPeriodTrialBalance(periodId: string) {
  return apiGetJson<LedgerPeriodTrialBalanceLine[]>(ledgerPeriodTrialBalanceEndpoint(periodId));
}

export function getLedgerPeriodPnlSummary(periodId: string) {
  return apiGetJson<LedgerPeriodPnlSummary>(ledgerPeriodPnlSummaryEndpoint(periodId));
}

export function getLedgerPeriodJournalEntries(periodId: string, query: { instrumentId?: string | null; accountId?: string | null } = {}, options: ApiRequestOptions = {}) {
  return apiGetJson<LedgerPostedJournalEntry[]>(ledgerPeriodJournalEntriesEndpoint(periodId, query), options);
}
