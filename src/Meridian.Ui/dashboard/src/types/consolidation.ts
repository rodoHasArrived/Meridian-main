import type { ManualJournalEntryLine } from "@/types";

/** Mirrors Meridian.Contracts.Ledger.ConsolidationDtos; authority remains server-owned. */
export interface ConsolidationRequest {
  organizationId: string;
  ownershipRootId: string;
  eliminationBookId: string;
  periodId: string;
  asOf: string;
}

export interface ConsolidationSource {
  ledgerBookId: string;
  journalEntryId: string;
  lineId: string;
  entityId: string;
  counterpartyId: string | null;
  accountPath: string;
  debit: number;
  credit: number;
  effectiveDate: string;
  drillThrough: string;
  originalAccountPath?: string | null;
  currency?: string | null;
  chartAccountNodeId?: string | null;
  accountBindingRevisionId?: string | null;
  agreementId?: string | null;
  agreementRevisionId?: string | null;
  intercompanyTransactionId?: string | null;
  transactionCurrency?: string | null;
  transactionDebit?: number | null;
  transactionCredit?: number | null;
  counterpartyDisposition?: string | null;
}

export interface ConsolidationBalance {
  accountPath: string;
  accountType: string;
  grossBalance: number;
  proposedEliminations: number;
  postedEliminations: number;
  consolidatedBalance: number;
  previewBalance: number;
  sources: ConsolidationSource[];
}

export interface ConsolidationMatch {
  postingEntityId: string;
  counterpartyId: string;
  receivable: number;
  payable: number;
  matchedAmount: number;
  unmatchedReceivable: number;
  unmatchedPayable: number;
  sources: ConsolidationSource[];
  agreementId?: string | null;
  treatment?: string | null;
  matchingCurrency?: string | null;
  matchedTransactionAmount?: number | null;
  unmatchedFirstTransactionAmount?: number | null;
  unmatchedSecondTransactionAmount?: number | null;
  translationDifference?: number | null;
  intercompanyTransactionId?: string | null;
}

export interface ConsolidationDraftSummary {
  journalEntryId: string;
  status: string;
  requiresRenewedReview: boolean;
  adjustsJournalEntryId: string | null;
  lines: ManualJournalEntryLine[];
}

export interface ConsolidationView {
  request: ConsolidationRequest;
  fundProfileId: string;
  currency: string;
  scopeLimitation: string;
  entityIds: string[];
  ownershipLinkIds: string[];
  balances: ConsolidationBalance[];
  matches: ConsolidationMatch[];
  drafts: ConsolidationDraftSummary[];
  blockers: string[];
  sourceFingerprint: string;
  canCreateDrafts?: boolean;
  ruleVersion?: string | null;
  translationFingerprint?: string | null;
  translationReserve?: Record<string, number> | null;
  translationDifferences?: Record<string, number> | null;
  roundingDifferences?: Record<string, number> | null;
  entityNames?: Record<string, string> | null;
}
