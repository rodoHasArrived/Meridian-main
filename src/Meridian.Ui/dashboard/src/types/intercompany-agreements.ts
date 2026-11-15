/** Shared FundStructure agreement wire contracts. Tenant and actor are server owned. */
export type AgreementType = "Loan" | "ManagementFee" | "CostShare" | "CashSweep" | "Guarantee" | "Other";
export type AgreementAction = "Activate" | "Amend" | "Terminate";
export type AgreementProposalState = "Draft" | "Submitted" | "Approved" | "Rejected" | "Withdrawn";
export type AgreementStatus = "Unapproved" | "Scheduled" | "Active" | "Expired" | "Terminated";

export interface AgreementTerms {
  structuredTerms?: StructuredAgreementTerms | null;
  fixedAnnualRate?: string | number | null;
  rateDescription?: string | null;
  scheduleDescription?: string | null;
  allocationDescription?: string | null;
  settlementDays?: number | null;
  notes?: string | null;
}

export interface AgreementEvidence { reference: string; artifactId?: string | null; contentHash?: string | null }
export interface AgreementDraft {
  code: string; name: string;
  partyOneId: string; partyOneRole: string; partyTwoId: string; partyTwoRole: string;
  agreementType: AgreementType; currency: string; effectiveFrom: string; effectiveTo?: string | null;
  terms?: AgreementTerms | null; evidence?: AgreementEvidence[] | null;
  action: AgreementAction; terminationEffectiveDate?: string | null; purpose?: string | null;
}
export interface AgreementProposal {
  proposalId: string; action: AgreementAction; state: AgreementProposalState; draft: AgreementDraft;
  baseAgreementVersion?: number | null; authors: string[]; submitter?: string | null;
  submittedAtUtc?: string | null; submittedHash?: string | null;
  createdAtUtc: string; updatedAtUtc: string; rationale?: string | null;
}
export interface AgreementRevision {
  agreementId: string; agreementVersion: number; predecessorAgreementVersion?: number | null;
  baseAgreementVersion?: number | null; action: AgreementAction; terms: AgreementDraft; proposalId: string;
  payloadHash: string; authors: string[]; submitter: string; approver: string; approvedAtUtc: string; rationale: string; externalReview?: AgreementExternalReviewAttestation | null;
}
export interface AgreementAudit {
  eventId: string; proposalId: string; action: string; actor: string; recordedAtUtc: string;
  aggregateVersion: number; rationale?: string | null; snapshot?: AgreementDraft | null;
  migrationWarnings?: string[] | null;
  externalReview?: AgreementExternalReviewAttestation | null;
}
export interface AgreementCapabilities {
  canEdit: boolean; canSubmit: boolean; canApprove: boolean; canReject: boolean; canWithdraw: boolean;
  canAmend: boolean; canTerminate: boolean; blockingReason?: string | null;
  canCreateProposal?: boolean; canAttestExternalReview?: boolean;
}
export interface AgreementDetail {
  agreementId: string; aggregateVersion: number; code: string; name: string;
  partyOneId: string; partyOneRole: string; partyTwoId: string; partyTwoRole: string;
  agreementType: AgreementType; currency: string; asOf: string; effectiveStatus: AgreementStatus;
  latestApprovedVersion?: number | null; applicableRevision?: AgreementRevision | null;
  proposals: AgreementProposal[]; revisions: AgreementRevision[]; history: AgreementAudit[];
  capabilities: AgreementCapabilities;
  migrationWarnings?: string[] | null;
}
export interface AgreementList { items: AgreementDetail[]; nextCursor?: string | null; canCreate?: boolean; referencePostingSupported?: boolean }
export interface AgreementCommand {
  commandId: string; agreementId: string; expectedAggregateVersion: number; proposalId: string;
  draft?: AgreementDraft; rationale?: string; submittedHash?: string | null; externalReview?: AgreementExternalReview;
}
export interface AgreementCommandResult {
  detail: AgreementDetail;
  receipt: { commandId: string; action: string; payloadHash: string; actor: string; recordedAtUtc: string; aggregateVersion: number; detail: AgreementDetail };
  replayed: boolean;
}
export interface AgreementQuery { entityId?: string; agreementType?: AgreementType; status?: AgreementStatus; asOf?: string; cursor?: string }
export interface AgreementJournalLine {
  journalEntryId: string; lineId: string; periodId: string; sequence: number; accountingDate: string;
  postingEntityId: string; counterpartyId?: string | null; accountName: string; description: string;
  functionalCurrency: string; debit: string; credit: string;
  transactionCurrency: string; transactionDebit: string; transactionCredit: string;
  intercompanyAgreementId?: string | null; intercompanyAgreementVersion?: number | null;
  coverageState: string; warning?: string | null; sourceReference: string;
}
export interface AgreementJournalLines {
  agreementId: string; ledgerBookId: string; asOf: string; coverageState: string; lines: AgreementJournalLine[]; nextCursor?: string | null;
}

/** Exact decimal text is retained through authoring; only the shared server validates economics. */
export interface StructuredAgreementTerms {
  schemaVersion: number;
  schedule?: { startDate: string; endDate: string; frequency: "Monthly" | "Quarterly" | "SemiAnnual" | "Annual" | "AtMaturity"; calendarId: string; businessDayConvention: "Unadjusted" | "Following" | "ModifiedFollowing" | "Preceding" | "ModifiedPreceding"; settlementBusinessDays: number } | null;
  rateBasis?: "Fixed" | "Floating" | null; fixedAnnualRate?: string | null; floatingIndexReference?: string | null; spreadBps?: string | null;
  dayCount?: "Actual360" | "Actual365" | "Thirty360" | "Business252" | "ActualActualIsda" | "ThirtyE360" | "ThirtyE360Isda" | "ActualActualIcma" | "Nl365" | "Actual36525" | null;
  compounding?: "Simple" | "Monthly" | "Quarterly" | "SemiAnnual" | "Annual" | "Daily" | null;
  fixedFeeAmount?: string | null; annualFeeRate?: string | null; calculationBaseReference?: string | null;
  allocations?: { entityId: string; percent: string }[] | null; allocationDriverReference?: string | null;
  guarantee?: { beneficiaryReference: string; limitAmount?: string | null } | null;
}
export interface AgreementSchedulePreview {
  status: string; termsFormat: string; subjectHash: string; kernelVersion: string; calendarId?: string | null; calendarHash?: string | null;
  periods: { sequence: number; accrualStart: string; accrualEnd: string; paymentDate: string }[]; diagnostics: string[];
}
export interface AgreementExternalReview {
  externalReviewer: string; reviewerReference: string; reviewedOn: string; evidence: AgreementEvidence; rationale: string; submittedHash: string;
}
export interface AgreementExternalReviewAttestation extends AgreementExternalReview { operator: string; recordedAtUtc: string }
