import type { ManualJournalEntryDraft } from "./workstation-5";

/** How a declared fixed management fee is earned: per schedule period, or per year under a declared day count. */
export type IntercompanyFixedFeeBasis = "PerPeriod" | "PerAnnum";

/** Operator selection only. The fee, basis, schedule, direction and amount are always server-resolved. */
export interface IntercompanyFeeAccrualRequest {
  sourceBookId: string; feeArrangementId: string;
  /** Half-open window: accrualFrom is the first accrued day, accrualTo the first day not accrued. */
  accrualFrom: string; accrualTo: string;
}
/** The digest proves the operator is drafting exactly the computation they previewed. */
export interface CreateIntercompanyFeeAccrualDraftRequest { accrual: IntercompanyFeeAccrualRequest; expectedEvidenceDigest: string }

/** One legal revision, one profile revision, one schedule period. Exact server decimals arrive as text. */
export interface IntercompanyFeeAccrualSlice {
  sequence: number; from: string; to: string; accountingArrangementRevisionId: string; agreementVersion: number;
  agreementPayloadHash: string; schedulePeriodSequence: number; periodStart: string; periodEnd: string;
  basis: IntercompanyFixedFeeBasis; declaredFee: string; dayCount?: string | null; fraction: string; unroundedAmount: string;
}
export interface IntercompanyFeeAccrualComputation {
  status: "Ready" | "Unavailable" | string; ruleVersion: string; agreementId?: string | null; currency?: string | null;
  accrualFrom: string; accrualTo: string; amount?: string | null; unroundedAmount?: string | null; kernelVersion: string;
  calendars: { calendarId: string; contentHash: string }[]; slices: IntercompanyFeeAccrualSlice[]; diagnostics: string[];
}
/** Server-retained fee provenance. `sourceRole` is derived from the agreement's declared roles, never chosen. */
export interface IntercompanyFeeAccrualEvidence {
  ruleVersion: string; draftId: string; sourceBookId: string; sourceEntityId?: string; counterpartyEntityId?: string;
  accountingArrangementId?: string; postingArrangementRevisionId: string; agreementId?: string;
  postingAgreementVersion: number; postingAgreementPayloadHash?: string;
  /** "Receivable" for the service provider; "Payable" for the recipient. */
  sourceRole: "Receivable" | "Payable" | string;
  transactionId: string; postingDate: string; functionalCurrency: string; functionalAmount: string;
  computation: IntercompanyFeeAccrualComputation;
  fx?: ManualJournalEntryDraft["intercompanyFx"];
}
/** Preview and draft share one shape so the operator reviews exactly what would be retained. */
export interface IntercompanyFeeAccrualProposal {
  computation: IntercompanyFeeAccrualComputation; diagnostics: string[];
  evidence?: IntercompanyFeeAccrualEvidence | null;
  evidenceDigest?: string | null;
  draft?: ManualJournalEntryDraft | null;
}
