import type { LedgerBook } from "@/types";

export interface EntityOperationCapabilities {
  canManageAgreements: boolean;
  canManageLedger: boolean;
  canManageLifecycle: boolean;
  canViewLedger: boolean;
}
export interface EntityOperationGraph {
  organizations: { organizationId: string; name: string }[];
  entities: { entityId: string; name: string; baseCurrency: string; lifecycleStatus: string }[];
  ownershipLinks: { ownershipLinkId: string; parentNodeId: string; childNodeId: string; relationshipType: string;
    ownershipPercent: number | null; effectiveFrom: string; effectiveTo: string | null }[];
}
export interface EntityOperationScope {
  book: LedgerBook;
  books: LedgerBook[];
  graph: EntityOperationGraph | null;
  capabilities: EntityOperationCapabilities;
}
export type AgreementKind = "Loan" | "CostRecharge" | "ManagementFee" | "ExpenseShare" | "Guarantee";
export type AgreementStatus = "Draft" | "Submitted" | "Approved" | "Rejected" | "Terminated" | "Novated";
export interface AgreementPartyMapping {
  entityId: string; ledgerBookId: string; receivableAccountNodeId: string; payableAccountNodeId: string;
  settlementAccountNodeId: string | null; revenueAccountNodeId: string | null; expenseAccountNodeId: string | null;
}
export interface SignedAgreementDocument {
  vaultId: string; documentId: string; artifactId: string; contentHashSha256: string; scope: string;
}
export interface IntercompanyAgreement {
  agreementId: string; organizationId: string; facilityReference: string; kind: AgreementKind;
  partyOneEntityId: string; partyTwoEntityId: string; partyOneRole: string; partyTwoRole: string;
  currency: string; settlementCadence: string; effectiveFrom: string; effectiveTo: string | null;
  revisionId: string; revisionNumber: number; version: number; status: AgreementStatus;
  partyMappings: AgreementPartyMapping[]; signedDocument: SignedAgreementDocument | null;
  createdBy: string; createdAt: string; updatedBy: string; updatedAt: string; approvedBy: string | null;
  approvedAt: string | null; terminatedAt: string | null; predecessorRevisionId: string | null; successorAgreementId: string | null;
  settlementDueDays?: number | null;
}
export interface AgreementHistory {
  events: { requestId: string; action: string; actor: string; recordedAt: string; reason: string | null; snapshot: IntercompanyAgreement;
    targetRevisionId?: string | null; targetRevisionSnapshot?: IntercompanyAgreement | null }[];
}
export interface EntityLifecycleState {
  transitionId: string; effectiveFrom: string; effectiveTo: string | null; status: string; eventKind: string; successorEntityId: string | null;
}
export interface EntityLifecycleRevision {
  entityId: string; revision: number; recordedBy: string; recordedAtUtc: string; reason: string;
  evidenceReference: string; contentHash: string; timeline: EntityLifecycleState[]; legacySnapshot: boolean;
}
export interface BookControlProfile {
  ledgerBookId: string; mode: "LegacyWarning" | "Enforced"; version: number; policyVersion: string;
  createdAtUtc: string; activatedAtUtc: string | null; activatedBy: string | null; migrationReceiptId: string | null;
}
export interface BookControlPreflight {
  ledgerBookId: string; profileVersion: number; policyVersion: string; digest: string; preparedAtUtc: string; canActivate: boolean;
  decisions: { control: string; code: string; allowed: boolean; message: string; evidenceRevision: string | null }[];
}
export interface IntercompanyProposal {
  source: { proposalId: string; sourceJournalId: string; sourceBookId: string; sourceEntityId: string; counterpartyEntityId: string; requiresSourceAllocation?: boolean;
    agreementId: string; agreementRevisionId: string; transactionId: string; effectiveDate: string; sourceFunctionalCurrency: string };
  status: "Pending" | "NeedsFix" | "Drafted" | "Linked"; attempts: number; recordedAt: string;
  targetBookId: string | null; targetJournalId: string | null; lastError: string | null; dueDate: string | null;
  sourceDigest: string;
  latestLink?: { tenantId?: string; requestId: string; proposalId: string; targetBookId: string; targetJournalId: string;
    actor: string; reason: string; recordedAt: string; rejectedDraftId?: string | null; rejectedDraftVersion?: number | null } | null;
  resolvedTargetBookId?: string | null;
  resolvedTargetJournalId?: string | null;
}
export interface IntercompanyBreak {
  proposalId: string; agreementId: string; transactionId: string; classification: string; ageFrom: string; ageDays: number;
  agingBasis: string; detail: string; sourceJournalId: string; targetJournalId: string | null;
}
export type FxRateKind = "Closing" | "TransactionDate" | "Historical" | "PeriodAverage";
export interface ConsolidationFxRate {
  rateId: string; fromCurrency: string; toCurrency: string; rateDate: string; rate: string | number; kind: FxRateKind;
  sourceEvidence: string; allowInverse: boolean; periodStart: string | null; periodEnd: string | null;
}
export interface ConsolidationFxRateSet {
  rateSetId: string; revision: number; tenantId: string; companyId: string; rates: ConsolidationFxRate[];
  sourceEvidence: string; preparedBy: string; approvedBy: string; approvedAt: string; previousHash: string | null; contentHash: string | null;
}
export interface ConsolidationFxConfiguration {
  ledgerBookId: string; revision: number; tenantId: string; companyId: string; effectiveFrom: string; effectiveTo: string | null;
  rateSetId: string; rateSetRevision: number; approvedBy: string; approvedAt: string; previousHash: string | null; contentHash: string | null;
  policy: { policyId: string; revision: string; reportingCurrency: string; decimalPlaces: number; translationReserveAccount: string;
    accountRules: { accountPath: string; method: FxRateKind; periodStart: string | null; periodEnd: string | null;
      requiresOpeningTranslatedBalance: boolean }[] | null;
    closingRateDates?: { asOf: string; rateDate: string; evidenceReference: string }[] | null };
  historicalFacts?: { ledgerBookId: string; journalEntryId: string; lineId: string; historicalDate: string | null;
    openingTranslatedSignedAmount: string | number | null; evidenceReference: string }[] | null;
}
export interface EntityTimeline {
  organizationId: string;
  entity: { entityId: string; name: string; baseCurrency: string; lifecycleStatus: string };
  asOf: string; lifecycleRevisions: EntityLifecycleRevision[];
  ownershipLinks: EntityOperationGraph["ownershipLinks"];
  agreements: IntercompanyAgreement[]; books: LedgerBook[]; warnings: string[];
}
export interface ConsolidationTransitionRequest {
  transitionId: string; organizationId: string; ownershipRootId: string; previousEliminationBookId: string;
  newEliminationBookId: string; newPeriodId: string; cutoverDate: string; reason: string; evidenceReference: string;
}
export interface ConsolidationTransitionPreview {
  previewId: string; request: ConsolidationTransitionRequest; rootVersion: number; previousTransitionId: string | null;
  preparedBy: string; preparedAt: string; previousBookEvidenceDigest: string; previousPostedJournalIds: string[];
  previousLatestPostedAsOf: string | null; previousEntityIds: string[]; joinedEntityIds: string[]; removedEntityIds: string[];
  newEvidence: { ruleVersion: string; entityIds: string[] | null; sourceFingerprint: string; perimeterFingerprint: string };
  blockers: string[]; fingerprint: string; canApprove: boolean;
}
export interface ConsolidationTransitionReceipt {
  transitionId: string; previewId: string; rootVersion: number; request: ConsolidationTransitionRequest;
  preparedBy: string; approvedBy: string; approvedAt: string; previewFingerprint: string; contentHash: string;
}
export interface IntercompanyAccountBinding {
  bindingId: string; revisionId: string; revisionNumber: number; ledgerBookId: string; chartAccountNodeId: string;
  role: "Receivable" | "Payable" | "Revenue" | "Expense" | "TranslationReserve";
  accountPath: string; accountName: string; accountType: string; effectiveFrom: string; effectiveTo: string | null;
  historicalNames: string[]; recordedBy: string; recordedAt: string; reason: string; isArchived: boolean;
}
