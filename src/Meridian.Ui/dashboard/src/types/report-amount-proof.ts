import type { LedgerAmountScope } from "./ledger-amount-proof";

/** Retained ReportLedgerAmountBindingDto, projected from the certified run checkpoint. */
export interface ReportLedgerAmountBinding {
  amountId: string;
  subjectId: string;
  label: string;
  amount: number;
  currency: string;
  scope: LedgerAmountScope;
  journalEntryIds: string[];
  ledgerEntryIds: string[];
  sourceSnapshotHash: string;
  /** Server-owned: whether this caller may open the amount's proof. Absent means denied. */
  proofAvailable?: boolean;
}
