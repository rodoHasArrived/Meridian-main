import type { LedgerBook } from "@/types";
import type { PrivateCapitalBankObservationImport, PrivateCapitalCall, PrivateCapitalCommitment, PrivateCapitalWorkbench } from "@/types/private-capital";
export const CAPITAL_BOOK_ID = "3f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e11";
export const CAPITAL_ENTITY_ID = "4f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e22";
export const CAPITAL_PERIOD_ID = "5f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e33";
export const CAPITAL_BANK_LINE_ID = "6f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e44";
export const CAPITAL_EVIDENCE = "evidence://evidence-vault/ev-0123456789abcdef01234567";
export const capitalScope = { tenantId: "tenant-a", companyId: "company-a", entityId: CAPITAL_ENTITY_ID, fundProfileId: "fund-a", ledgerBookId: CAPITAL_BOOK_ID, currency: "USD" };
export const capitalBook: LedgerBook = { ledgerBookId: CAPITAL_BOOK_ID, fundProfileId: "fund-a", fundStructureNodeId: CAPITAL_ENTITY_ID, fundStructureNodeKind: "Entity", displayName: "Seeded Fund A primary book", baseCurrency: "USD", createdAt: "2026-11-01T00:00:00Z", updatedAt: "2026-11-01T00:00:00Z", accountingBasis: "Primary", accountingPolicyId: "policy-a", accountingPolicyVersion: "1" };
export const capitalCommitment: PrivateCapitalCommitment = { commitmentId: "commitment-a", scope: capitalScope, revision: 1, investorId: "investor-a", capitalAccountId: "capital-a", totalCommitment: "1000000.00", commitmentDate: "2026-01-01", effectiveFrom: "2026-01-01", investmentPeriodEndDate: null, isActive: true, approvalStatus: 1, evidenceLinks: [CAPITAL_EVIDENCE], createdBy: "preparer", createdAtUtc: "2026-11-01T00:00:00Z", approvedBy: "reviewer", approvedAtUtc: "2026-11-02T00:00:00Z", calledAmount: "0.00", fundedAmount: "0.00", uncalledAmount: "1000000.00" };
export const capitalCall: PrivateCapitalCall = { callId: "call-a", scope: capitalScope, periodId: CAPITAL_PERIOD_ID, noticeDate: "2026-11-03", dueDate: "2026-11-20", amountToCall: "250000.01", purpose: "Investment", planHash: "retained-hash-a", createdBy: "preparer", createdAtUtc: "2026-11-03T00:00:00Z", lines: [{ installmentId: "call-a:commitment-a", commitmentId: "commitment-a", commitmentRevision: 1, investorId: "investor-a", capitalAccountId: "capital-a", amount: "250000.01", uncalledBefore: "1000000.00", uncalledAfter: "749999.99", issuedAmount: "250000.01", fundedAmount: "100000.00", outstandingAmount: "150000.01", issuanceJournalEntryId: "7f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e55" }] };
export function capitalWorkbench(): PrivateCapitalWorkbench { return { scope: capitalScope, commitments: [capitalCommitment], calls: [], receipts: [], notices: [], generatedAtUtc: "2026-11-03T00:00:00Z" }; }
export const capitalBankObservationImport: PrivateCapitalBankObservationImport = {
  bankTransaction: { bankTransactionId: CAPITAL_BANK_LINE_ID, entityId: CAPITAL_ENTITY_ID, transactionType: "CapitalCallFunding", amount: "50000.01", currency: "USD",
    effectiveDate: "2026-11-10", transactionDate: "2026-11-10", settlementDate: "2026-11-10", externalRef: "Seeded statement credit 2", recordedAt: "2026-11-10T12:00:00Z", isVoided: false },
  evidenceLink: CAPITAL_EVIDENCE, documentId: "bank-observation-document-a", sourceHashSha256: "b".repeat(64), isReplay: false
};
