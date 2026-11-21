import type { LedgerTrialBalanceLine } from "@/types";
import type { LedgerAmountSelection } from "@/types/ledger-amount-proof";

/** Only the server's population binding establishes aggregate proof identity. */
export function trialBalanceAmountPassport(row: LedgerTrialBalanceLine): LedgerAmountSelection | null {
  const scope = row.balanceProofScope;
  if (!row.balanceProofSubjectId?.startsWith("trial-balance:") || !scope?.tenantId || !scope.companyId
    || !scope.fundProfileId || !scope.ledgerBookId || !scope.periodId || !row.balanceProofCurrency
    || !row.journalEntryIds?.length || !row.ledgerEntryIds?.length) return null;
  return { ...scope, subjectId: row.balanceProofSubjectId, amount: row.balanceExact ?? row.balance,
    currency: row.balanceProofCurrency, label: `${row.accountName} net balance`,
    journalEntryIds: row.journalEntryIds, ledgerEntryIds: row.ledgerEntryIds };
}
