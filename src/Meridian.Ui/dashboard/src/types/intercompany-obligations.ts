import type { AgreementDetail } from "./intercompany-agreements";
export interface IntercompanyBalanceRow {
  ledgerBookId: string; entityId: string; counterpartyEntityId?: string | null;
  accountingArrangementId?: string | null; accountingArrangementRevisionId?: string | null; accountingTemplateKind?: string | null;
  chartAccountNodeId?: string | null; role?: string | number | null; transactionCurrency: string; functionalCurrency: string;
  transactionSignedBalance: string | number; functionalSignedBalance: string | number; mappingVerified: boolean;
  journalEntryIds: string[]; bindingRevisionIds: string[]; intercompanyAgreementId?: string | null;
  intercompanyAgreementVersion?: number | null; intercompanyAgreementPayloadHash?: string | null;
}
export interface IntercompanyBalanceSchedule {
  fundProfileId?: string | null; ledgerBookId?: string | null; asOf: string; available: boolean; rows: IntercompanyBalanceRow[];
  issues: { code: string; message: string; blocksMapping: boolean; ledgerBookId?: string | null; journalEntryId?: string | null }[];
  contentHash: string; legacyAttributionIds: string[]; isMappingComplete: boolean; isBalanced: boolean; isReady: boolean;
}
export interface IntercompanyObligationMonitor {
  asOf: string; expiryHorizonDays: number; evaluatedAtUtc: string; status: "Current" | "Partial" | "Stale" | "Unavailable";
  isComplete: boolean; sourceFingerprint: string;
  sourceVersions: { sourceKind: string; sourceId: string; version: string; contentHash: string }[];
  findings: { code: string; severity: string; message: string; entityId?: string | null; agreementId?: string | null;
    agreementVersion?: number | null; ledgerBookId?: string | null; journalEntryId?: string | null; drillThrough?: string | null }[];
  balanceSchedule?: IntercompanyBalanceSchedule | null; guarantees: AgreementDetail[];
}
export interface LegacyAttributionCandidate {
  ledgerBookId: string; journalEntryId: string; entryId: string; sourceLineDigest: string; accountName: string; accountType: string;
  debit: string | number; credit: string | number; effectiveDate: string; dimensions?: { intercompanyTransactionId?: string | null } | null;
}
export interface LegacyAttribution {
  attributionId: string; revision: number; ledgerBookId: string; journalEntryId: string; lineId: string; sourceLineDigest: string;
  accountingArrangementId: string; accountingArrangementRevisionId: string; accountBindingRevisionId: string; chartAccountNodeId: string;
  intercompanyTransactionId: string; preparedBy: string; approvedBy: string; approvedAt: string; evidenceReference: string;
  tenantId: string; companyId: string | null; previousAttributionId: string | null;
  intercompanyAgreementId: string; intercompanyAgreementVersion: number; intercompanyAgreementPayloadHash: string;
  reviewMode?: "Independent" | "OperatorReviewed"; reviewRationale?: string | null; revoked?: boolean;
}
