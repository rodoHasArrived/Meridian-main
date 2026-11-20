import type { RetainedEvidenceIdentity } from "./workstation-5";

/** Financial values are exact decimal wire strings; never convert receipt evidence to Number. */
export interface BookReceiptScope { tenantId: string; companyId: string; ledgerBookId: string }
export interface BookReceiptCoverage { area: string; isComplete: boolean; reason: string | null }
export interface BookReceiptExpectation {
  obligationKey: string; positionId: string; securityId: string; displayName: string;
  flowType: string; dueDate: string; currency: string; quantity: string; expectedAmount: string;
  termsVersionId: string; termsHash: string; projectionRunId: string; projectedCashFlowId: string;
  evidenceLinks: string[]; retainedEvidence: RetainedEvidenceIdentity[];
}
export interface BookReceipt {
  receiptId: string; securityId: string; effectiveDate: string; currency: string; amount: string;
  sourceSystem: string; sourceReference: string; allocations: { obligationKey: string; amount: string }[];
  reversesReceiptId: string | null; evidenceLinks: string[]; flowType?: string | null;
}
export interface BookReceiptPosting {
  postingId: string; receiptId: string; obligationKey: string; amount: string; currency: string;
  journalEntryId: string; effectiveDate: string; isReversed: boolean; evidenceLinks: string[];
  journalCashAmount?: string | null; isInferredAllocation?: boolean;
}
export interface BookReceiptExpectationVersion {
  versionId: string; firstObservedDate: string; expectation: BookReceiptExpectation;
}
export interface BookReceiptQueueItem {
  obligationKey: string; positionId: string; securityId: string; displayName: string; flowType: string;
  dueDate: string; currency: string; status: string; expectedAmount: string; receivedAmount: string;
  postedAmount: string; remainingAmount: string; agingDays: number; owner: string | null;
  exceptionId: string | null; evidenceComplete: boolean; evidenceIssues: string[]; evidenceLinks: string[];
  expectationHistory: BookReceiptExpectationVersion[]; receipts: BookReceipt[]; postings: BookReceiptPosting[];
}
export interface BookReceiptRun {
  runId: string; engineVersion: string; scope: BookReceiptScope; asOfDate: string; inputHash: string;
  items: BookReceiptQueueItem[]; coverage: BookReceiptCoverage[]; sequence: number;
  sourceSnapshot: {
    expectations: BookReceiptExpectation[]; receipts: BookReceipt[];
    postings: BookReceiptPosting[]; coverage: BookReceiptCoverage[];
  };
}
