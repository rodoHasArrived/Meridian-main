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

/** A selected immutable posting line, never an account-name or symbol lookup. */
export interface LedgerAmountSelection {
  subjectId: string;
  ledgerBookId: string;
  periodId: string;
  fundProfileId: string;
  amount: number;
  currency: string;
  label: string;
}
