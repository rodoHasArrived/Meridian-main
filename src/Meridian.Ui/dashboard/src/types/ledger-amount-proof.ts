import type { EvidenceStatus } from "./workstation-2";

/** Shared LedgerAmountProofDto from the subject-addressed evidence API. */
export interface LedgerAmountScope {
  tenantId: string;
  companyId: string;
  fundProfileId: string;
  ledgerBookId: string;
  periodId: string;
}

export interface LedgerAmountProofEvidence {
  evidenceId: string;
  kind: string;
  label: string;
  route: string | null;
  sourceSystem: string;
  retainedAt: string | null;
  status: EvidenceStatus;
  contentHash: string | null;
  reason: string | null;
  /** Original retained posting identity for a generated report's source evidence. */
  sourceSubjectId?: string | null;
  sourceScope?: LedgerAmountScope | null;
}

export interface LedgerAmountProof {
  subjectId: string;
  scope: LedgerAmountScope;
  amount: number;
  currency: string;
  status: EvidenceStatus;
  evidence: LedgerAmountProofEvidence[];
  warnings: string[];
}

/** A selected immutable posting line or retained report amount, never a name or symbol lookup. */
export interface LedgerAmountSelection {
  subjectId: string;
  ledgerBookId: string;
  periodId: string;
  fundProfileId: string;
  amount: number;
  currency: string;
  label: string;
  /** Authenticated retained report scope, in addition to the fund/book/period guard. */
  tenantId?: string;
  companyId?: string;
  /** Exact retained contributor identities required for generated report selections. */
  journalEntryIds?: string[];
  ledgerEntryIds?: string[];
}
