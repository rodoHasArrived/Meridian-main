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
}
