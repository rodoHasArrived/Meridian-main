export type CapitalDecimal = string | number;

/** Native commitment authority. Approval status is a numeric .NET enum on this wire. */
export interface PrivateCapitalScope {
  tenantId: string; companyId: string; entityId: string; fundProfileId: string;
  ledgerBookId: string; currency: string;
}
export interface PrivateCapitalCommitment {
  commitmentId: string; scope: PrivateCapitalScope; revision: number; investorId: string;
  capitalAccountId: string; totalCommitment: CapitalDecimal; commitmentDate: string; effectiveFrom: string;
  investmentPeriodEndDate: string | null; isActive: boolean; approvalStatus: number;
  evidenceLinks: string[]; createdBy: string; createdAtUtc: string; approvedBy: string | null;
  approvedAtUtc: string | null; calledAmount: CapitalDecimal; fundedAmount: CapitalDecimal; uncalledAmount: CapitalDecimal;
}
export interface PrivateCapitalCallLine {
  installmentId: string; commitmentId: string; commitmentRevision: number; investorId: string;
  capitalAccountId: string; amount: CapitalDecimal; uncalledBefore: CapitalDecimal; uncalledAfter: CapitalDecimal;
  issuedAmount: CapitalDecimal; fundedAmount: CapitalDecimal; outstandingAmount: CapitalDecimal; issuanceJournalEntryId: string | null;
}
export interface PrivateCapitalCall {
  callId: string; scope: PrivateCapitalScope; periodId: string; noticeDate: string; dueDate: string;
  amountToCall: CapitalDecimal; purpose: string | null; planHash: string; lines: PrivateCapitalCallLine[];
  createdBy: string; createdAtUtc: string;
}
export interface PrivateCapitalPostingReceipt {
  postingId: string; scope: PrivateCapitalScope; commitmentId: string; callId: string; installmentId: string;
  kind: string; amount: CapitalDecimal; journalEntryId: string; effectiveDate: string;
  fundingReceiptId: string | null; bankLineId: string | null;
}
export interface PrivateCapitalWorkbench {
  scope: PrivateCapitalScope; commitments: PrivateCapitalCommitment[]; calls: PrivateCapitalCall[];
  receipts: PrivateCapitalPostingReceipt[]; generatedAtUtc: string; notices: PrivateCapitalNotice[];
}
export interface PrivateCapitalNotice {
  runId: string; callId: string; investorId: string; governanceState: string;
  packageId: string | null; deliveryState: string; route: string;
}
export interface GeneratePrivateCapitalNoticeRequest {
  readonly operationId: string;
}
export interface PrivateCapitalNoticeGenerationResult { run: { runId: string } }
export interface CreatePrivateCapitalCommitment {
  commitmentId: string; investorId: string; capitalAccountId: string; totalCommitment: CapitalDecimal;
  commitmentDate: string; effectiveFrom: string; investmentPeriodEndDate: string | null;
  evidenceLinks: string[]; expectedRevision: number; isActive: boolean;
}
export interface PreviewPrivateCapitalCall {
  callId: string; amountToCall: CapitalDecimal; noticeDate: string; dueDate: string; periodId: string;
  commitmentIds: string[]; purpose: string | null;
}
export interface PrivateCapitalCallPreview {
  call: PrivateCapitalCall | null; isReady: boolean; blockers: string[];
}
export interface PrivateCapitalFundingRequest {
  fundProfileId: string; currency: string; callId: string; receivedDate: string; ledgerBookId: string;
  periodId: string; entityId: string;
  fundings: Array<{ commitmentId: string; capitalAccountId: string; investorId: string; fundedAmount: CapitalDecimal;
    evidenceLinks: string[]; fundingReceiptId: string; bankLineId: string }>;
}
export interface ImportPrivateCapitalBankObservation {
  evidenceLink: string; documentId: string;
}
export interface PrivateCapitalBankObservationImport {
  bankTransaction: {
    bankTransactionId: string; entityId: string; transactionType: string; amount: CapitalDecimal;
    currency: string; effectiveDate: string; transactionDate: string; settlementDate: string;
    externalRef: string | null; recordedAt: string; isVoided: boolean;
  };
  evidenceLink: string; documentId: string; sourceHashSha256: string; isReplay: boolean;
}
