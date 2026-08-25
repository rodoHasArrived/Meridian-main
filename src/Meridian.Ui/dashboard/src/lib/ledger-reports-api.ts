import { apiGetJson } from "@/lib/api";
import {
  ledgerPeriodPnlSummaryEndpoint,
  ledgerPeriodsEndpoint,
  ledgerPeriodTrialBalanceEndpoint
} from "@/lib/workstation-endpoints";
import type {
  LedgerPeriod,
  LedgerPeriodPnlSummary,
  LedgerPeriodTrialBalanceLine
} from "@/types";

/**
 * Clients for the posted-journal ledger reporting routes — the governed book of
 * record, scoped by ledger period. Kept out of `lib/api.ts` per the file-size
 * ratchet; follows the `reporting-governance-api.ts` domain-module pattern.
 */

export function getLedgerPeriods(query: { ledgerBookId?: string | null; status?: string | null } = {}) {
  return apiGetJson<LedgerPeriod[]>(ledgerPeriodsEndpoint(query));
}

export function getLedgerPeriodTrialBalance(periodId: string) {
  return apiGetJson<LedgerPeriodTrialBalanceLine[]>(ledgerPeriodTrialBalanceEndpoint(periodId));
}

export function getLedgerPeriodPnlSummary(periodId: string) {
  return apiGetJson<LedgerPeriodPnlSummary>(ledgerPeriodPnlSummaryEndpoint(periodId));
}
