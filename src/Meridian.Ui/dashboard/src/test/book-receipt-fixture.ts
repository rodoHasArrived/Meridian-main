import type { LedgerBook } from "@/types";
import type { BookReceiptExpectation, BookReceiptQueueItem, BookReceiptRun } from "@/types/income-receipt-queue.types";

export const receiptBooks: LedgerBook[] = ["Income fund", "Credit fund"].map((displayName, index) => ({ ledgerBookId: `book-${index + 1}`, displayName, baseCurrency: "USD", fundProfileId: "fund", fundStructureNodeId: "entity", fundStructureNodeKind: "Fund", createdAt: "2026-11-01", updatedAt: "2026-11-01", accountingBasis: "Gaap", accountingPolicyId: "policy", accountingPolicyVersion: "1" }));

export function receiptRun(overrides: Partial<BookReceiptRun> = {}): BookReceiptRun {
  const expectation: BookReceiptExpectation = {
    obligationKey: "position:coupon:2026-11-18", positionId: "position-1", securityId: "security-1", displayName: "Atlas 5.25% 2028", flowType: "Coupon", dueDate: "2026-11-18", currency: "USD", quantity: "100000", expectedAmount: "1250.00", termsVersionId: "terms-2", termsHash: "terms-hash-2", projectionRunId: "accepted-projection", projectedCashFlowId: "cash-flow-1", evidenceLinks: ["/data/security-master?securityId=security-1", "opaque:evidence-1"], retainedEvidence: []
  };
  const item: BookReceiptQueueItem = {
    ...expectation, status: "PartiallyReceived", receivedAmount: "500.00", postedAmount: "250.00", remainingAmount: "750.00", agingDays: 2, owner: "Morgan Lee", exceptionId: "receipt-exception-1", evidenceComplete: true, evidenceIssues: [],
    expectationHistory: [
      { versionId: "version-1", firstObservedDate: "2026-11-17", expectation: { ...expectation, expectedAmount: "1200.00", termsVersionId: "terms-1", termsHash: "terms-hash-1" } },
      { versionId: "version-2", firstObservedDate: "2026-11-20", expectation }
    ],
    receipts: [{ receiptId: "receipt-1", securityId: "security-1", effectiveDate: "2026-11-19", currency: "USD", amount: "1500.00", sourceSystem: "Custodian", sourceReference: "statement-line-4", allocations: [{ obligationKey: expectation.obligationKey, amount: "500.00" }, { obligationKey: "position:principal:2026-11-18", amount: "1000.00" }], reversesReceiptId: null, evidenceLinks: ["https://custodian.example/statement-4"] }],
    postings: [{ postingId: "posting-1", receiptId: "receipt-1", obligationKey: expectation.obligationKey, amount: "250.00", currency: "USD", journalEntryId: "journal-1", effectiveDate: "2026-11-20", isReversed: false, evidenceLinks: ["/accounting/journals?journalEntryId=journal-1"], journalCashAmount: "1250.00", isInferredAllocation: true }]
  };
  return { runId: "daily-run-1", engineVersion: "receipt-queue/1", scope: { tenantId: "tenant-1", companyId: "company-1", ledgerBookId: "book-1" }, asOfDate: "2026-11-20", inputHash: "sha256-source-snapshot", sequence: 1, items: [item], coverage: [{ area: "Holdings", isComplete: true, reason: null }, { area: "Accepted projections", isComplete: true, reason: null }, { area: "Custodian receipts", isComplete: true, reason: null }], sourceSnapshot: { expectations: [expectation], receipts: item.receipts, postings: item.postings, coverage: [] }, ...overrides };
}
