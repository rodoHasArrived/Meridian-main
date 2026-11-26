import type { LedgerBook, ManualJournalEntryWorkbench } from "../types";

/**
 * The manual journal workbench for the `accounting.non-usd` scenario: one balanced draft in a
 * EUR ledger book. It mirrors the Journal Entries screenshot fixture (same memo, accounts and
 * balance, so the capture's readiness texts still hold) with every amount booked in EUR, so a
 * screen that still assumes "$" shows up in that scenario's screenshots.
 */
const EUR_BOOK_ID = "book-primary";

const eurLedgerBook: LedgerBook = {
  ledgerBookId: EUR_BOOK_ID,
  fundProfileId: "default-fund",
  fundStructureNodeId: "fund-alpha",
  fundStructureNodeKind: "Fund",
  displayName: "Default fund primary book (EUR)",
  baseCurrency: "EUR",
  createdAt: "2026-01-01T00:00:00Z",
  updatedAt: "2026-06-30T17:00:00Z",
  description: "Scenario book: the accounting payload rebooked in EUR.",
  accountingBasis: "Primary",
  accountingPolicyId: "primary-default",
  accountingPolicyVersion: "v1"
};

export const eurManualJournalWorkbench: ManualJournalEntryWorkbench = {
  fundProfileId: "default-fund",
  ledgerBookId: EUR_BOOK_ID,
  loadedAtUtc: "2026-06-30T17:00:00Z",
  ledgerBooks: [eurLedgerBook],
  chartOfAccounts: [
    { nodeId: "cash", path: "Assets:Cash", accountName: "Cash", accountType: "Asset", isArchived: false },
    { nodeId: "investment-income", path: "Income:Investment", accountName: "Investment income", accountType: "Revenue", isArchived: false }
  ],
  drafts: [
    {
      journalEntryId: "manual-je-fixture",
      status: "Draft",
      fundProfileId: "default-fund",
      ledgerBookId: EUR_BOOK_ID,
      accountingBasis: "Primary",
      accountingDate: "2026-06-30",
      periodId: "2026-06",
      entityId: "fund-alpha",
      fundNodeId: "fund-alpha",
      currency: "EUR",
      memo: "Month-end investment income adjustment",
      preparedBy: "Ops Desk",
      createdAtUtc: "2026-06-30T16:45:00Z",
      updatedAtUtc: "2026-06-30T17:00:00Z",
      version: 1,
      lines: [
        {
          lineId: "manual-je-fixture-debit",
          side: "Debit",
          amount: 1250,
          currency: "EUR",
          accountPath: "Assets:Cash",
          entityId: "fund-alpha",
          securityId: null,
          securityDisplayName: null,
          description: "Cash adjustment"
        },
        {
          lineId: "manual-je-fixture-credit",
          side: "Credit",
          amount: 1250,
          currency: "EUR",
          accountPath: "Income:Investment",
          entityId: "fund-alpha",
          securityId: null,
          securityDisplayName: null,
          description: "Investment income adjustment"
        }
      ],
      evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/manual-je-fixture"],
      evidenceAttachments: [],
      validationIssues: [],
      totalDebits: 1250,
      totalCredits: 1250,
      imbalance: 0,
      approvalId: null,
      submittedAtUtc: null,
      submittedBy: null,
      entryType: "General",
      treasuryContext: null
    }
  ],
  auditTrail: [],
  privateCapitalActivity: null
};
