import type { LedgerBook } from "@/types";

export interface AccountingPeriodOption { periodId: string; ledgerBookId: string; label: string; status: string; version?: number; }
export interface AccountingIntegrationScope { book: LedgerBook; period: AccountingPeriodOption; accountId: string; }
export interface AutomatedPostingGrantReference {
  grantId: string; version: number; tenantId: string; financialAccountId: string;
  ledgerBookId: string; periodId: string; policyId: string; policyVersion: string;
  mappingRevision: string; sourceMode: "Synthetic"; companyId?: string | null;
}
export interface AutomatedPostingGrant {
  reference: AutomatedPostingGrantReference; isActive: boolean; activatedBy: string; activatedAtUtc: string;
  revokedBy?: string | null; revokedAtUtc?: string | null;
}
export interface ActivatePostingGrant {
  grantId: string; financialAccountId: string; ledgerBookId: string; periodId: string;
  policyId: string; policyVersion: string; mappingRevision: string; sourceMode: "Synthetic";
  actionOrigin: "HumanOperator";
}
export interface ThreeWaySourceSnapshot {
  source: string; asOfUtc: string; evidenceReference: string; cash: number | string | null;
  positions: { securityId: string; symbol: string; quantity: number | string; costBasis: number | string | null }[];
  costBasisConvention: string; coverageFailures: string[];
  dataProvenanceToken?: string;
}
export interface ThreeWayReconciliation {
  runId: string; operationId: string; cutoffUtc: string; recordedAtUtc: string; status: string;
  scope: { tenantId: string; companyId: string; fundProfileId: string; entityId: string; financialAccountId: string;
    ledgerBookId: string; accountingPeriodId: string; currency: string; providerId?: string; externalAccountId?: string; };
  sources: ThreeWaySourceSnapshot[];
  checks: { checkId: string; measure: string; symbol: string | null; leftSource: string; rightSource: string;
    leftAmount: number | string | null; rightAmount: number | string | null; tolerance: number | string;
    status: string; explanation: string; }[];
  breakIds: string[];
  dataProvenanceToken?: string;
  adjustmentProposals?: ThreeWayAdjustmentProposal[];
  accountingPeriodVersion?: number | null;
}

export interface ThreeWayAdjustmentProposal {
  checkId: string; status: "Ready" | "InsufficientEvidence"; description: string;
  amount: string | number | null; currency: string; evidenceIds: string[]; evidenceHash: string;
}

export interface PrepareThreeWayAdjustment {
  ledgerBookId: string; accountingPeriodId: string; operationId: string;
  checkId: string; expectedPeriodVersion: number; evidenceHash: string;
}

export interface PreparedThreeWayAdjustmentDraft {
  journalEntryId: string; fundProfileId: string; ledgerBookId: string; periodId: string;
  status: string; memo: string;
}
