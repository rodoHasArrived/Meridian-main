import { act, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  useManualJournalEntryWorkbenchViewModel,
} from "@/screens/accounting-screen.view-model";
import type {
  ManualJournalEntryWorkbenchServices,
} from "@/screens/accounting-screen.view-model";
import type {
  JournalEntryLifecycleActionResult,
  ManualJournalEntryDraft,
  ManualJournalEntryWorkbench,
  SecurityMasterEntry,
} from "@/types";

const securityResult: SecurityMasterEntry = {
  securityId: "sec-1",
  displayName: "Apple Inc.",
  status: "Active",
  classification: {
    assetClass: "Equity",
    subType: "CommonStock",
    primaryIdentifierKind: "Ticker",
    primaryIdentifierValue: "AAPL"
  },
  economicDefinition: {
    currency: "USD",
    version: 3,
    effectiveFrom: "2024-01-01T00:00:00Z",
    effectiveTo: null,
    subType: "CommonStock",
    assetFamily: "Equity",
    issuerType: "Corporate"
  }
};

const manualJournalDraft: ManualJournalEntryDraft = {
  journalEntryId: "manual-je-1",
  status: "Draft",
  fundProfileId: "fund-alpha",
  ledgerBookId: "book-alpha",
  accountingBasis: "Primary",
  accountingDate: "2026-06-30",
  periodId: "2026-06",
  entityId: "entity-master",
  fundNodeId: "fund-alpha",
  currency: "USD",
  memo: "Manual close adjustment",
  preparedBy: "browser-user",
  createdAtUtc: "2026-06-30T00:00:00Z",
  updatedAtUtc: "2026-06-30T00:00:00Z",
  version: 1,
  lines: [
    { lineId: "line-debit", side: "Debit", amount: 100, currency: "USD", accountPath: "Assets:Cash", securityId: null, securityDisplayName: null, description: "Cash debit" },
    { lineId: "line-credit", side: "Credit", amount: 100, currency: "USD", accountPath: "Income:Interest", securityId: null, securityDisplayName: null, description: "Interest income credit" }
  ],
  evidenceLinks: [],
  evidenceAttachments: [],
  validationIssues: [
    {
      code: "manual-je.account-missing",
      severity: "Critical",
      message: "GL account was not found.",
      targetId: "line-debit",
      suggestedAction: "Choose an active GL account."
    }
  ],
  totalDebits: 100,
  totalCredits: 100,
  imbalance: 0,
  approvalId: null,
  submittedAtUtc: null,
  submittedBy: null,
  entryType: "CapitalCall",
  treasuryContext: {
    effectiveDate: "2026-06-30",
    idempotencyKey: "browser:fund-alpha:capital-call:manual-je-1",
    fundEventId: "fund-event:fund-alpha:capital-call:20260630",
    fundEventType: "CapitalCall",
    capitalAccountId: "capital-account:fund-alpha:lp-1",
    investorId: "investor:lp-1",
    paymentIntentId: "payment:fund-alpha:capital-call:manual-je-1",
    settlementReference: "settlement:fund-alpha:capital-call:20260630"
  }
};

const manualJournalWorkbench: ManualJournalEntryWorkbench = {
  fundProfileId: "fund-alpha",
  ledgerBookId: "book-alpha",
  loadedAtUtc: "2026-06-30T00:00:00Z",
  ledgerBooks: [],
  chartOfAccounts: [
    { nodeId: "cash", path: "Assets:Cash", accountName: "Cash", accountType: "Asset", isArchived: false },
    { nodeId: "interest-income", path: "Income:Interest", accountName: "Interest Income", accountType: "Revenue", isArchived: false }
  ],
  drafts: [manualJournalDraft],
  auditTrail: [],
  privateCapitalActivity: {
    fundProfileId: "fund-alpha",
    ledgerBookId: "book-alpha",
    projectedAtUtc: "2026-06-30T00:00:00Z",
    fundEventCount: 1,
    capitalAccountCount: 1,
    submittedFundEventCount: 0,
    approvalQueueCount: 0,
    postedFundEventCount: 0,
    publishedReportOutputCount: 0,
    netCapitalActivity: 100,
    currency: "USD",
    fundEvents: [
      {
        fundEventId: "fund-event:fund-alpha:capital-call:20260630",
        fundEventType: "CapitalCall",
        entryType: "CapitalCall",
        journalStatus: "Draft",
        journalEntryId: "manual-je-1",
        effectiveDate: "2026-06-30",
        capitalAccountId: "capital-account:fund-alpha:lp-1",
        investorId: "investor:lp-1",
        currency: "USD",
        grossAmount: 100,
        netCapitalActivity: 100,
        memo: "Manual close adjustment",
        paymentIntentId: "payment:fund-alpha:capital-call:manual-je-1",
        settlementReference: "settlement:fund-alpha:capital-call:20260630",
        evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/manual-je-1"],
        validationIssues: [],
        updatedAtUtc: "2026-06-30T00:00:00Z"
      }
    ],
    capitalAccounts: [
      {
        capitalAccountId: "capital-account:fund-alpha:lp-1",
        investorId: "investor:lp-1",
        currency: "USD",
        contributions: 100,
        distributions: 0,
        subscriptions: 0,
        redemptions: 0,
        managementFees: 0,
        netActivity: 100,
        fundEventCount: 1,
        lastEffectiveDate: "2026-06-30",
        lastFundEventType: "CapitalCall",
        fundEventIds: ["fund-event:fund-alpha:capital-call:20260630"]
      }
    ],
    capitalAccountSubledgerEntries: [
      {
        subledgerEntryId: "capital-account-subledger:fund-event:fund-alpha:capital-call:20260630",
        capitalAccountId: "capital-account:fund-alpha:lp-1",
        investorId: "investor:lp-1",
        currency: "USD",
        fundEventId: "fund-event:fund-alpha:capital-call:20260630",
        fundEventType: "CapitalCall",
        entryType: "CapitalCall",
        approvalState: "Draft",
        journalEntryId: "manual-je-1",
        effectiveDate: "2026-06-30",
        grossAmount: 100,
        netCapitalActivity: 100,
        runningNetActivity: 100,
        memo: "Manual close adjustment",
        evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/manual-je-1"],
        validationIssues: [],
        updatedAtUtc: "2026-06-30T00:00:00Z"
      }
    ],
    ledgerImpacts: [
      {
        ledgerImpactId: "ledger-impact:fund-event:fund-alpha:capital-call:20260630:manual-je-1",
        journalEntryId: "manual-je-1",
        fundEventId: "fund-event:fund-alpha:capital-call:20260630",
        fundEventType: "CapitalCall",
        capitalAccountId: "capital-account:fund-alpha:lp-1",
        investorId: "investor:lp-1",
        approvalState: "Draft",
        effectiveDate: "2026-06-30",
        currency: "USD",
        totalDebits: 100,
        totalCredits: 100,
        imbalance: 0,
        lineCount: 2,
        isBalanced: true,
        isPostingReady: false,
        evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/manual-je-1"],
        lines: [
          {
            lineId: "line-debit",
            accountPath: "Assets:Cash",
            side: "Debit",
            amount: 100,
            currency: "USD",
            entityId: null,
            securityId: null,
            securityDisplayName: null,
            evidenceLink: null
          },
          {
            lineId: "line-credit",
            accountPath: "Equity:Capital Contributions",
            side: "Credit",
            amount: 100,
            currency: "USD",
            entityId: null,
            securityId: null,
            securityDisplayName: null,
            evidenceLink: null
          }
        ],
        validationIssues: [
          {
            code: "manual-je.private-capital-ledger-impact-approval-pending",
            severity: "Warning",
            message: "Approval is pending.",
            targetId: "manual-je-1",
            suggestedAction: "Submit approval."
          }
        ]
      }
    ],
    reportOutputs: [
      {
        reportOutputId: "report-output:fund-event:fund-alpha:capital-call:20260630:capitalcallnotice",
        reportOutputType: "CapitalCallNotice",
        displayName: "CapitalCallNotice for CapitalCall",
        reportRoute: "/api/ledger/private-capital/activity?fundProfileId=fund-alpha&fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1",
        reportOutputRoute: "/api/ledger/private-capital/report-output?fundProfileId=fund-alpha&reportOutputId=report-output%3Afund-event%3Afund-alpha%3Acapital-call%3A20260630%3Acapitalcallnotice&fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1",
        fundEventId: "fund-event:fund-alpha:capital-call:20260630",
        fundEventType: "CapitalCall",
        capitalAccountId: "capital-account:fund-alpha:lp-1",
        investorId: "investor:lp-1",
        approvalState: "Draft",
        effectiveDate: "2026-06-30",
        currency: "USD",
        netCapitalActivity: 100,
        evidenceLinkCount: 2,
        evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/manual-je-1"],
        isReportReady: false,
        reportWorkflowState: "Draft",
        reportLineProvenanceCount: 1,
        readinessLabel: "Approval pending",
        readinessReason: "Private-capital report output is not ready because the linked journal entry has not been submitted for approval.",
        nextAction: "Submit or review approval",
        nextActionRoute: "/api/ledger/journal-entry-workbench?fundProfileId=fund-alpha&journalEntryId=manual-je-1",
        validationIssues: [
          {
            code: "manual-je.private-capital-report-approval-pending",
            severity: "Warning",
            message: "Approval is pending.",
            targetId: "fund-event:fund-alpha:capital-call:20260630",
            suggestedAction: "Submit approval."
          }
        ]
      }
    ],
    fundEventRecords: [
      {
        fundEventRecordId: "fund-event-ledger-record:fund-event:fund-alpha:capital-call:20260630",
        fundEventId: "fund-event:fund-alpha:capital-call:20260630",
        fundEventType: "CapitalCall",
        capitalAccountId: "capital-account:fund-alpha:lp-1",
        investorId: "investor:lp-1",
        approvalState: "Draft",
        journalEntryId: "manual-je-1",
        effectiveDate: "2026-06-30",
        currency: "USD",
        grossAmount: 100,
        netCapitalActivity: 100,
        capitalAccountOpeningNetActivity: 0,
        capitalAccountEndingNetActivity: 100,
        memo: "Manual close adjustment",
        paymentIntentId: "payment:fund-alpha:capital-call:manual-je-1",
        settlementReference: "settlement:fund-alpha:capital-call:20260630",
        activityRoute: "/api/ledger/private-capital/activity?fundProfileId=fund-alpha&fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1",
        evidenceRoute: "/api/workstation/evidence/subjects/private-capital-fund-event/fund-event%3Afund-alpha%3Acapital-call%3A20260630/packet",
        approvalId: null,
        approvalRoute: null,
        isPosted: false,
        isPostingReady: false,
        isReportReady: false,
        isPublished: false,
        readiness: "ApprovalPending",
        readinessLabel: "Approval pending",
        readinessReason: "Submit the fund-event journal for approval before posting or stakeholder report output.",
        nextAction: "Submit approval",
        nextActionRoute: "/api/ledger/private-capital/activity?fundProfileId=fund-alpha&fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1",
        evidenceLinkCount: 2,
        capitalAccountSubledgerEntryCount: 1,
        ledgerImpactCount: 1,
        reportOutputCount: 1,
        validationIssueCount: 2,
        primaryReportOutputId: "report-output:fund-event:fund-alpha:capital-call:20260630:capitalcallnotice",
        primaryReportOutputType: "CapitalCallNotice",
        primaryReportRoute: "/api/ledger/private-capital/report-output?fundProfileId=fund-alpha&reportOutputId=report-output%3Afund-event%3Afund-alpha%3Acapital-call%3A20260630%3Acapitalcallnotice&fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1",
        reportWorkflowState: "Draft",
        publicationManifestId: null,
        retainedManifestPath: null,
        reportLineProvenanceCount: 1,
        evidenceLinks: [
          "/api/workstation/evidence/subjects/accounting-record/manual-je-1",
          "/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-capital-call-manual-je-1/packet"
        ],
        evidenceCategories: [
          {
            categoryId: "source-support",
            label: "Source support",
            isReady: true,
            summary: "Source documents or retained evidence links support the fund event.",
            evidenceLinkCount: 2,
            evidenceLinks: [
              "/api/workstation/evidence/subjects/accounting-record/manual-je-1",
              "/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-capital-call-manual-je-1/packet"
            ],
            requiredEvidence: ["Source document or retained evidence link"]
          },
          {
            categoryId: "capital-account-subledger",
            label: "Capital-account subledger",
            isReady: true,
            summary: "Capital-account impact is represented in the subledger.",
            evidenceLinkCount: 1,
            evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/manual-je-1"],
            requiredEvidence: ["Capital-account impact"]
          },
          {
            categoryId: "ledger-impact",
            label: "Ledger impact",
            isReady: false,
            summary: "Balanced ledger impact and line evidence are available for the fund event.",
            evidenceLinkCount: 1,
            evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/manual-je-1"],
            requiredEvidence: ["Balanced ledger impact", "Ledger line evidence"]
          },
          {
            categoryId: "approval-state",
            label: "Approval state",
            isReady: false,
            summary: "Approval reference is missing for the fund event.",
            evidenceLinkCount: 0,
            evidenceLinks: [],
            requiredEvidence: ["Approval reference"]
          },
          {
            categoryId: "payment-intent",
            label: "Payment intent",
            isReady: true,
            summary: "Payment intent and settlement reference are captured.",
            evidenceLinkCount: 2,
            evidenceLinks: [
              "payment:fund-alpha:capital-call:manual-je-1",
              "settlement:fund-alpha:capital-call:20260630"
            ],
            requiredEvidence: ["Payment intent id", "Settlement reference"]
          },
          {
            categoryId: "cash-evidence",
            label: "Cash evidence",
            isReady: true,
            summary: "Payment intent payment:fund-alpha:capital-call:manual-je-1 and settlement settlement:fund-alpha:capital-call:20260630 have 1 retained cash evidence link(s); live execution remains deferred.",
            evidenceLinkCount: 1,
            evidenceLinks: ["/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-capital-call-manual-je-1/packet"],
            requiredEvidence: []
          },
          {
            categoryId: "report-output",
            label: "Report output",
            isReady: false,
            summary: "Governed report output is linked to the fund event.",
            evidenceLinkCount: 1,
            evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/manual-je-1"],
            requiredEvidence: ["Governed report output"]
          }
        ],
        paymentIntentEvidence: {
          paymentIntentId: "payment:fund-alpha:capital-call:manual-je-1",
          settlementReference: "settlement:fund-alpha:capital-call:20260630",
          status: "SettlementMatched",
          isReady: true,
          direction: "Inflow",
          amount: 100,
          currency: "USD",
          effectiveDate: "2026-06-30",
          summary: "Payment intent payment:fund-alpha:capital-call:manual-je-1 and settlement settlement:fund-alpha:capital-call:20260630 have 1 retained cash evidence link(s); live execution remains deferred.",
          cashEvidenceLinkCount: 1,
          cashEvidenceLinks: ["/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-capital-call-manual-je-1/packet"],
          requiredEvidence: [],
          evidenceRoute: "/api/workstation/evidence/subjects/private-capital-fund-event/fund-event%3Afund-alpha%3Acapital-call%3A20260630/packet"
        },
        fundEvent: {
          fundEventId: "fund-event:fund-alpha:capital-call:20260630",
          fundEventType: "CapitalCall",
          entryType: "CapitalCall",
          journalStatus: "Draft",
          journalEntryId: "manual-je-1",
          effectiveDate: "2026-06-30",
          capitalAccountId: "capital-account:fund-alpha:lp-1",
          investorId: "investor:lp-1",
          currency: "USD",
          grossAmount: 100,
          netCapitalActivity: 100,
          memo: "Manual close adjustment",
          paymentIntentId: "payment:fund-alpha:capital-call:manual-je-1",
          settlementReference: "settlement:fund-alpha:capital-call:20260630",
          evidenceLinks: [
            "/api/workstation/evidence/subjects/accounting-record/manual-je-1",
            "/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-capital-call-manual-je-1/packet"
          ],
          validationIssues: [],
          updatedAtUtc: "2026-06-30T00:00:00Z"
        },
        capitalAccountSubledgerEntries: [
          {
            subledgerEntryId: "capital-account-subledger:fund-event:fund-alpha:capital-call:20260630",
            capitalAccountId: "capital-account:fund-alpha:lp-1",
            investorId: "investor:lp-1",
            currency: "USD",
            fundEventId: "fund-event:fund-alpha:capital-call:20260630",
            fundEventType: "CapitalCall",
            entryType: "CapitalCall",
            approvalState: "Draft",
            journalEntryId: "manual-je-1",
            effectiveDate: "2026-06-30",
            grossAmount: 100,
            netCapitalActivity: 100,
            runningNetActivity: 100,
            memo: "Manual close adjustment",
            evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/manual-je-1"],
            validationIssues: [],
            updatedAtUtc: "2026-06-30T00:00:00Z"
          }
        ],
        ledgerImpacts: [
          {
            ledgerImpactId: "ledger-impact:fund-event:fund-alpha:capital-call:20260630:manual-je-1",
            journalEntryId: "manual-je-1",
            fundEventId: "fund-event:fund-alpha:capital-call:20260630",
            fundEventType: "CapitalCall",
            capitalAccountId: "capital-account:fund-alpha:lp-1",
            investorId: "investor:lp-1",
            approvalState: "Draft",
            effectiveDate: "2026-06-30",
            currency: "USD",
            totalDebits: 100,
            totalCredits: 100,
            imbalance: 0,
            lineCount: 2,
            isBalanced: true,
            isPostingReady: false,
            evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/manual-je-1"],
            lines: [
              {
                lineId: "line-debit",
                accountPath: "Assets:Cash",
                side: "Debit",
                amount: 100,
                currency: "USD",
                entityId: null,
                securityId: null,
                securityDisplayName: null,
                evidenceLink: null
              },
              {
                lineId: "line-credit",
                accountPath: "Equity:Capital Contributions",
                side: "Credit",
                amount: 100,
                currency: "USD",
                entityId: null,
                securityId: null,
                securityDisplayName: null,
                evidenceLink: null
              }
            ],
            validationIssues: [
              {
                code: "manual-je.private-capital-ledger-impact-approval-pending",
                severity: "Warning",
                message: "Approval is pending.",
                targetId: "manual-je-1",
                suggestedAction: "Submit approval."
              }
            ]
          }
        ],
        reportOutputs: [
          {
            reportOutputId: "report-output:fund-event:fund-alpha:capital-call:20260630:capitalcallnotice",
            reportOutputType: "CapitalCallNotice",
            displayName: "CapitalCallNotice for CapitalCall",
            reportRoute: "/api/ledger/private-capital/activity?fundProfileId=fund-alpha&fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1",
            fundEventId: "fund-event:fund-alpha:capital-call:20260630",
            fundEventType: "CapitalCall",
            capitalAccountId: "capital-account:fund-alpha:lp-1",
            investorId: "investor:lp-1",
            approvalState: "Draft",
            effectiveDate: "2026-06-30",
            currency: "USD",
            netCapitalActivity: 100,
            evidenceLinkCount: 1,
            evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/manual-je-1"],
            isReportReady: false,
            reportWorkflowState: "Draft",
            reportLineProvenanceCount: 1,
            readinessLabel: "Approval pending",
            readinessReason: "Private-capital report output is not ready because the linked journal entry has not been submitted for approval.",
            nextAction: "Submit or review approval",
            nextActionRoute: "/api/ledger/journal-entry-workbench?fundProfileId=fund-alpha&journalEntryId=manual-je-1",
            validationIssues: [
              {
                code: "manual-je.private-capital-report-approval-pending",
                severity: "Warning",
                message: "Approval is pending.",
                targetId: "fund-event:fund-alpha:capital-call:20260630",
                suggestedAction: "Submit approval."
              }
            ]
          }
        ],
        validationIssues: [
          {
            code: "manual-je.private-capital-ledger-impact-approval-pending",
            severity: "Warning",
            message: "Approval is pending.",
            targetId: "manual-je-1",
            suggestedAction: "Submit approval."
          },
          {
            code: "manual-je.private-capital-report-approval-pending",
            severity: "Warning",
            message: "Approval is pending.",
            targetId: "fund-event:fund-alpha:capital-call:20260630",
            suggestedAction: "Submit approval."
          }
        ]
      }
    ],
    capitalAccountSubledgers: [
      {
        subledgerId: "capital-account-subledger:capital-account:fund-alpha:lp-1:investor:lp-1:usd",
        fundProfileId: "fund-alpha",
        ledgerBookId: "book-alpha",
        projectedAtUtc: "2026-06-30T00:00:00Z",
        capitalAccountId: "capital-account:fund-alpha:lp-1",
        investorId: "investor:lp-1",
        currency: "USD",
        activityRoute: "/api/ledger/private-capital/capital-account-subledger?fundProfileId=fund-alpha&ledgerBookId=book-alpha&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1&currency=USD",
        contributions: 100,
        distributions: 0,
        subscriptions: 0,
        redemptions: 0,
        managementFees: 0,
        openingNetActivity: 0,
        endingNetActivity: 100,
        netCapitalActivity: 100,
        fundEventCount: 1,
        approvalQueueCount: 0,
        postedFundEventCount: 0,
        publishedReportOutputCount: 0,
        evidenceLinkCount: 2,
        validationIssueCount: 2,
        firstEffectiveDate: "2026-06-30",
        lastEffectiveDate: "2026-06-30",
        lastFundEventType: "CapitalCall",
        readiness: "ApprovalPending",
        readinessLabel: "Approval pending",
        readinessReason: "One or more fund events require approval before the capital-account subledger can be treated as posting ready.",
        nextAction: "Submit or review approval",
        nextActionRoute: "/api/ledger/private-capital/activity?fundProfileId=fund-alpha&fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1",
        evidenceLinks: [
          "/api/workstation/evidence/subjects/accounting-record/manual-je-1",
          "/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-capital-call-manual-je-1/packet"
        ],
        evidenceCategories: [
          {
            categoryId: "source-support",
            label: "Source support",
            isReady: true,
            summary: "Source support is retained for this capital account's fund events.",
            evidenceLinkCount: 2,
            evidenceLinks: [
              "/api/workstation/evidence/subjects/accounting-record/manual-je-1",
              "/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-capital-call-manual-je-1/packet"
            ],
            requiredEvidence: ["Source document or retained evidence link"]
          },
          {
            categoryId: "capital-account-subledger",
            label: "Capital-account subledger",
            isReady: true,
            summary: "Capital-account impacts are represented in the running subledger.",
            evidenceLinkCount: 1,
            evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/manual-je-1"],
            requiredEvidence: ["Capital-account impact"]
          },
          {
            categoryId: "ledger-impact",
            label: "Ledger impact",
            isReady: false,
            summary: "Ledger impacts are linked to this capital account.",
            evidenceLinkCount: 1,
            evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/manual-je-1"],
            requiredEvidence: ["Balanced ledger impact", "Ledger line evidence"]
          },
          {
            categoryId: "approval-state",
            label: "Approval state",
            isReady: false,
            summary: "Approval references are missing for this capital account.",
            evidenceLinkCount: 0,
            evidenceLinks: [],
            requiredEvidence: ["Approval reference"]
          },
          {
            categoryId: "payment-intent",
            label: "Payment intent",
            isReady: true,
            summary: "Payment intent and settlement reference are captured.",
            evidenceLinkCount: 2,
            evidenceLinks: [
              "payment:fund-alpha:capital-call:manual-je-1",
              "settlement:fund-alpha:capital-call:20260630"
            ],
            requiredEvidence: ["Payment intent id", "Settlement reference"]
          },
          {
            categoryId: "cash-evidence",
            label: "Cash evidence",
            isReady: true,
            summary: "1 payment intent(s), 1 settlement reference(s), and 1 retained cash evidence link(s) support this subledger; live execution remains deferred.",
            evidenceLinkCount: 1,
            evidenceLinks: ["/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-capital-call-manual-je-1/packet"],
            requiredEvidence: []
          },
          {
            categoryId: "report-output",
            label: "Report output",
            isReady: false,
            summary: "Governed report outputs are linked to this capital account.",
            evidenceLinkCount: 1,
            evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/manual-je-1"],
            requiredEvidence: ["Governed report output"]
          }
        ],
        paymentIntentEvidence: {
          paymentIntentId: "payment:fund-alpha:capital-call:manual-je-1",
          settlementReference: "settlement:fund-alpha:capital-call:20260630",
          status: "SettlementMatched",
          isReady: true,
          direction: "Inflow",
          amount: 100,
          currency: "USD",
          effectiveDate: "2026-06-30",
          summary: "1 payment intent(s), 1 settlement reference(s), and 1 retained cash evidence link(s) support this subledger; live execution remains deferred.",
          cashEvidenceLinkCount: 1,
          cashEvidenceLinks: ["/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-capital-call-manual-je-1/packet"],
          requiredEvidence: [],
          evidenceRoute: "/api/ledger/private-capital/capital-account-subledger?fundProfileId=fund-alpha&ledgerBookId=book-alpha&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1&currency=USD"
        },
        capitalAccount: null,
        fundEventRecords: [],
        subledgerEntries: [],
        ledgerImpacts: [],
        reportOutputs: [],
        validationIssues: [
          {
            code: "manual-je.private-capital-ledger-impact-approval-pending",
            severity: "Warning",
            message: "Approval is pending.",
            targetId: "manual-je-1",
            suggestedAction: "Submit approval."
          },
          {
            code: "manual-je.private-capital-report-approval-pending",
            severity: "Warning",
            message: "Approval is pending.",
            targetId: "fund-event:fund-alpha:capital-call:20260630",
            suggestedAction: "Submit approval."
          }
        ]
      }
    ],
    paymentIntents: [
      {
        paymentIntentId: "payment:fund-alpha:capital-call:manual-je-1",
        settlementReference: "settlement:fund-alpha:capital-call:20260630",
        fundProfileId: "fund-alpha",
        ledgerBookId: "book-alpha",
        fundEventId: "fund-event:fund-alpha:capital-call:20260630",
        journalEntryId: "manual-je-1",
        requester: "ops-user",
        requestedAtUtc: "2026-06-30T00:00:00Z",
        status: "ApprovalPending",
        statusLabel: "Approval pending",
        readinessReason: "Requester and expected movement are captured, but controller approval is not complete.",
        executionDeferredReason: "Full payment execution is explicitly deferred in v0.18; this layer only retains intent, control, cash-evidence, reconciliation, and audit history before any bank-side instruction.",
        expectedCashMovement: {
          paymentIntentId: "payment:fund-alpha:capital-call:manual-je-1",
          direction: "Inflow",
          amount: 100,
          currency: "USD",
          effectiveDate: "2026-06-30",
          settlementReference: "settlement:fund-alpha:capital-call:20260630",
          fundEventId: "fund-event:fund-alpha:capital-call:20260630",
          fundEventType: "CapitalCall",
          capitalAccountId: "capital-account:fund-alpha:lp-1",
          investorId: "investor:lp-1",
          purpose: "Capital call for Fund Alpha LP",
          payee: "fund:fund-alpha",
          accountScope: "fund:fund-alpha / book:book-alpha / capital-account:fund-alpha:lp-1 / investor:lp-1",
          businessPurpose: "Capital call for Fund Alpha LP",
          approvalPolicy: "Controller approval pending before execution-deferred reliance",
          sourceEvidenceLinks: [
            "/api/workstation/evidence/subjects/accounting-record/manual-je",
            "/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-capital-call-manual-je-1/packet"
          ]
        },
        evidenceRoute: "/api/workstation/evidence/subjects/payment-intent/payment%3Afund-alpha%3Acapital-call%3Amanual-je-1/packet",
        workbenchRoute: "/api/ledger/private-capital/activity?fundProfileId=fund-alpha&paymentIntentId=payment%3Afund-alpha%3Acapital-call%3Amanual-je-1",
        approvalChain: [
          { sequence: 1, role: "Requester", actor: "ops-user", status: "Requested", decidedAtUtc: "2026-06-30T00:00:00Z", evidenceRoute: "/api/ledger/private-capital/activity?fundProfileId=fund-alpha" },
          { sequence: 2, role: "Controller approval", actor: "controller", status: "Pending", decidedAtUtc: null, evidenceRoute: null }
        ],
        bankEvidence: [
          {
            evidenceId: "retained-cash-evidence:capital-call",
            evidenceKind: "RetainedCashEvidence",
            status: "Retained",
            summary: "Retained wire evidence supports the expected cash movement.",
            amount: 100,
            currency: "USD",
            effectiveDate: "2026-06-30",
          recordedAtUtc: "2026-06-30T00:00:00Z",
          externalRef: "settlement:fund-alpha:capital-call:20260630",
          recordedBy: "cash-ops@example.com",
          evidenceRoute: "/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-capital-call-manual-je-1/packet"
        }
      ],
        reconciliationLinks: [
          {
            linkId: "reconciliation:capital-call",
            status: "Ready",
            summary: "Cash evidence is linked to reconciliation review.",
            evidenceRoute: "/api/reconciliation/runs/capital-call"
          }
        ],
        auditHistory: [
          {
            auditEventId: "payment-intent-requested:manual-je-1",
            recordedAtUtc: "2026-06-30T00:00:00Z",
            actor: "ops-user",
            action: "payment-intent.requested",
            summary: "Payment intent was requested.",
            evidenceLinks: ["/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-capital-call-manual-je-1/packet"]
          },
          {
            auditEventId: "payment-intent-execution-deferred:manual-je-1",
            recordedAtUtc: "2026-06-30T00:00:00Z",
            actor: "system",
            action: "payment-intent.execution-deferred",
            summary: "Payment execution remains deferred.",
            evidenceLinks: []
          }
        ]
      }
    ],
    validationIssues: []
  }
};

function createJournalServices(
  overrides: Partial<ManualJournalEntryWorkbenchServices> = {}
): ManualJournalEntryWorkbenchServices {
  return {
    getWorkbench: vi.fn().mockResolvedValue(manualJournalWorkbench),
    searchSecurities: vi.fn().mockResolvedValue([]),
    saveDraft: vi.fn((request) => Promise.resolve({
      ...request.draft,
      version: request.draft.version + 1,
      updatedAtUtc: "2026-06-30T00:01:00Z"
    })),
    validateDraft: vi.fn((request) => Promise.resolve(request.draft)),
    submitApproval: vi.fn((request) => Promise.resolve({
      ...manualJournalDraft,
      journalEntryId: request.journalEntryId,
      status: "Submitted" as const,
      version: request.version + 1
    })),
    attachEvidence: vi.fn().mockResolvedValue(manualJournalDraft),
    applyLifecycleAction: vi.fn(),
    ...overrides
  };
}

beforeEach(() => {
  window.localStorage.clear();
});

afterEach(() => {
  vi.useRealTimers();
  window.localStorage.clear();
});

describe("accounting-screen journal-entry view model", () => {
  it("keeps editing unavailable when the governed workbench cannot load", async () => {
    const services = {
      getWorkbench: vi.fn().mockRejectedValue(new Error("journal workbench offline"))
    } as unknown as ManualJournalEntryWorkbenchServices;

    const { result } = renderHook(() => useManualJournalEntryWorkbenchViewModel(true, services));

    await waitFor(() => expect(result.current.errorText).toBeTruthy());
    expect(result.current.available).toBe(false);
  });

  it("keeps approval submission fail-closed until the current draft has completed validation", async () => {
    const readyDraft: ManualJournalEntryDraft = {
      ...manualJournalDraft,
      evidenceLinks: ["evidence://manual-je/source-support"],
      validationIssues: []
    };
    const workbench: ManualJournalEntryWorkbench = {
      ...manualJournalWorkbench,
      drafts: [readyDraft]
    };
    const services: ManualJournalEntryWorkbenchServices = {
      getWorkbench: vi.fn().mockResolvedValue(workbench),
      searchSecurities: vi.fn().mockResolvedValue([]),
      saveDraft: vi.fn((request) => Promise.resolve(request.draft)),
      validateDraft: vi.fn((request) => Promise.resolve(request.draft)),
      submitApproval: vi.fn((request) => Promise.resolve({
        ...readyDraft,
        journalEntryId: request.journalEntryId,
        status: "Submitted" as const,
        version: request.version + 1
      })),
      attachEvidence: vi.fn().mockResolvedValue(readyDraft),
      applyLifecycleAction: vi.fn()
    };

    const { result } = renderHook(() => useManualJournalEntryWorkbenchViewModel(true, services));
    await waitFor(() => expect(result.current.draft.journalEntryId).toBe("manual-je-1"));

    expect(result.current.validationIsCurrent).toBe(false);
    expect(result.current.canSubmit).toBe(false);
    expect(result.current.submitDisabledReason).toBe("Run Validate on the current draft before approval submission.");

    await act(async () => {
      await result.current.submit();
    });
    expect(services.submitApproval).not.toHaveBeenCalled();
    expect(result.current.errorText).toBe("Run Validate on the current draft before approval submission.");

    await act(async () => {
      await result.current.validate();
    });
    expect(result.current.validationIsCurrent).toBe(true);
    expect(result.current.canSubmit).toBe(true);

    act(() => result.current.updateHeader("memo", "Edited after validation"));
    expect(result.current.validationIsCurrent).toBe(false);
    expect(result.current.canSubmit).toBe(false);

    await act(async () => {
      await result.current.validate();
    });
    await act(async () => {
      await result.current.submit();
    });
    expect(services.submitApproval).toHaveBeenCalledTimes(1);
  });

  it("builds manual journal line badges, Security Master picks, and evidence attachments", async () => {
    const savedDraft = {
      ...manualJournalDraft,
      validationIssues: [],
      evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/source-doc"],
      evidenceAttachments: [{
        attachmentId: "source-doc-1",
        displayName: "Source support",
        evidenceKind: "SourceDocument",
        uri: "/api/workstation/evidence/subjects/accounting-record/source-doc",
        sourceSystem: "EvidenceVault",
        addedAtUtc: "2026-06-30T00:00:00Z",
        addedBy: "browser-user",
        lineId: "line-debit",
        description: null
      }]
    } satisfies ManualJournalEntryDraft;
    const services: ManualJournalEntryWorkbenchServices = {
      getWorkbench: vi.fn().mockResolvedValue(manualJournalWorkbench),
      searchSecurities: vi.fn().mockResolvedValue([securityResult]),
      saveDraft: vi.fn().mockResolvedValue(savedDraft),
      validateDraft: vi.fn().mockResolvedValue(savedDraft),
      submitApproval: vi.fn().mockResolvedValue({ ...savedDraft, status: "Submitted" }),
      attachEvidence: vi.fn().mockResolvedValue(savedDraft),
      applyLifecycleAction: vi.fn().mockResolvedValue({
        journalEntry: {
          ...savedDraft,
          status: "Posted",
          version: 4,
          postedAtUtc: "2026-06-30T02:00:00Z",
          postedBy: "browser-user",
          lifecycleTransitions: [{
            transitionId: "transition-post",
            fromStatus: "Approved",
            toStatus: "Posted",
            action: "Post",
            actor: "browser-user",
            recordedAtUtc: "2026-06-30T02:00:00Z",
            notes: "Post approved journal entry manual-je-1.",
            correlationId: "manual-je-post",
            evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/source-doc"]
          }]
        },
        transition: {
          transitionId: "transition-post",
          fromStatus: "Approved",
          toStatus: "Posted",
          action: "Post",
          actor: "browser-user",
          recordedAtUtc: "2026-06-30T02:00:00Z",
          notes: "Post approved journal entry manual-je-1.",
          correlationId: "manual-je-post",
          evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/source-doc"]
        },
        generatedJournalEntries: [{
          ...savedDraft,
          journalEntryId: "manual-je-reversal-1",
          status: "Draft",
          version: 1,
          memo: "Reversal for journal entry manual-je-1",
          entryType: "Reversal",
          reversalOfJournalEntryId: "manual-je-1",
          lifecycleTransitions: []
        }]
      })
    };

    const { result } = renderHook(() => useManualJournalEntryWorkbenchViewModel(true, services));
    await waitFor(() => expect(result.current.draft.journalEntryId).toBe("manual-je-1"));

    expect(result.current.totalDebitsLabel).toBe("$100");
    expect(result.current.totalCreditsLabel).toBe("$100");
    expect(result.current.balanceStatusLabel).toBe("Balanced");
    expect(result.current.balanceImpactRows).toEqual([
      expect.objectContaining({
        accountPath: "Assets:Cash",
        accountName: "Cash",
        debitLabel: "$100",
        creditLabel: "$0",
        netEffectLabel: "+$100",
        balanceDirectionLabel: "This draft increases the debit-normal account balance by $100."
      }),
      expect.objectContaining({
        accountPath: "Income:Interest",
        accountName: "Interest Income",
        debitLabel: "$0",
        creditLabel: "$100",
        netEffectLabel: "+$100",
        balanceDirectionLabel: "This draft increases the credit-normal account balance by $100."
      })
    ]);
    act(() => result.current.updateLine("line-debit", { amount: 125 }));
    expect(result.current.totalDebitsLabel).toBe("$125");
    expect(result.current.totalCreditsLabel).toBe("$100");
    expect(result.current.balanceStatusLabel).toBe("Out by $25");
    expect(result.current.balanceImpactRows.find((row) => row.accountPath === "Assets:Cash")).toMatchObject({
      netEffectLabel: "+$125",
      balanceDirectionLabel: "This draft increases the debit-normal account balance by $125."
    });
    expect(result.current.canSubmit).toBe(false);
    act(() => result.current.updateLine("line-credit", { amount: 125 }));
    expect(result.current.totalDebitsLabel).toBe("$125");
    expect(result.current.totalCreditsLabel).toBe("$125");
    expect(result.current.balanceStatusLabel).toBe("Balanced");

    expect(result.current.getLineBadges("line-debit").map((badge) => badge.label)).toContain("Blocked");
    expect(result.current.privateCapitalActivity.statusLabel).toBe("1 fund events / 1 capital accounts");
    expect(result.current.privateCapitalActivity.summaryCards).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "net-activity", value: "+$100.00 USD" }),
      expect.objectContaining({ id: "ledger-impacts", value: "1" }),
      expect.objectContaining({ id: "fund-event-ledger-records", value: "1" }),
      expect.objectContaining({ id: "capital-account-subledger", value: "1" }),
      expect.objectContaining({ id: "report-outputs", value: "1" }),
      expect.objectContaining({ id: "payment-intents", value: "1", detail: "0 execution-deferred / 0 blocked or returned" })
    ]));
    expect(result.current.privateCapitalActivity.capitalAccounts[0]).toMatchObject({
      title: "capital-account:fund-alpha:lp-1",
      netActivityLabel: "+$100.00 USD",
      contributionLabel: "$100 USD"
    });
    expect(result.current.privateCapitalActivity.capitalAccountSubledgers[0]).toMatchObject({
      title: "capital-account:fund-alpha:lp-1",
      subtitle: "investor:lp-1 / USD",
      statusLabel: "Approval pending",
      statusTone: "warning",
      readinessLabel: "Approval pending",
      readinessTone: "warning",
      readinessReasonLabel: "One or more fund events require approval before the capital-account subledger can be treated as posting ready.",
      nextActionLabel: "Submit or review approval",
      nextActionRouteLabel: "/api/ledger/private-capital/activity?fundProfileId=fund-alpha&fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1",
      activityRouteLabel: "/api/ledger/private-capital/capital-account-subledger?fundProfileId=fund-alpha&ledgerBookId=book-alpha&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1&currency=USD",
      netActivityLabel: "+$100.00 USD",
      openingLabel: "$0 USD",
      endingLabel: "+$100.00 USD",
      contributionLabel: "$100 USD",
      distributionLabel: "$0 USD",
      eventCountLabel: "1 fund event(s)",
      approvalQueueLabel: "0 approval queue",
      postedEventLabel: "0 posted event(s)",
      publishedReportLabel: "0 published report output(s)",
      dateRangeLabel: "first 2026-06-30 / last 2026-06-30 / CapitalCall",
      evidenceLabel: "2 evidence",
      paymentEvidenceLabel: "Settlement matched / Inflow / $100 USD / 1 cash evidence / settlement linked",
      paymentEvidenceTone: "success",
      paymentEvidenceRequiredLabel: "No additional payment evidence required",
      evidenceCategorySummaryLabel: "4/7 evidence categories ready",
      evidenceCategories: expect.arrayContaining([
        expect.objectContaining({ id: "capital-account-subledger", statusLabel: "Ready" }),
        expect.objectContaining({ id: "approval-state", statusLabel: "Missing" })
      ]),
      issueLabel: "2 subledger issue(s)"
    });
    expect(result.current.privateCapitalActivity.capitalAccountSubledgerEntries[0]).toMatchObject({
      title: "CapitalCall",
      statusLabel: "Draft",
      netActivityLabel: "+$100.00 USD",
      runningBalanceLabel: "+$100.00 USD",
      evidenceLabel: "1 evidence"
    });
    expect(result.current.privateCapitalActivity.ledgerImpacts[0]).toMatchObject({
      title: "CapitalCall",
      readinessLabel: "Review",
      debitLabel: "$100 USD",
      creditLabel: "$100 USD",
      lineLabel: "2 GL lines"
    });
    expect(result.current.privateCapitalActivity.reportOutputs[0]).toMatchObject({
      title: "CapitalCallNotice for CapitalCall",
      readinessLabel: "Approval pending",
      readinessReasonLabel: "Private-capital report output is not ready because the linked journal entry has not been submitted for approval.",
      nextActionLabel: "Submit or review approval",
      nextActionRouteLabel: "/api/ledger/journal-entry-workbench?fundProfileId=fund-alpha&journalEntryId=manual-je-1",
      evidenceLabel: "2 evidence",
      workflowLabel: "Draft",
      publicationLabel: "No publication manifest",
      provenanceLabel: "1 provenance line(s)"
    });
    expect(result.current.privateCapitalActivity.paymentIntents[0]).toMatchObject({
      title: "payment:fund-alpha:capital-call:manual-je-1",
      statusLabel: "Approval pending",
      statusTone: "warning",
      expectedCashLabel: "Inflow / $100 USD / 2026-06-30 / settlement:fund-alpha:capital-call:20260630",
      requestMetadataLabel: "payee fund:fund-alpha / scope fund:fund-alpha / book:book-alpha / capital-account:fund-alpha:lp-1 / investor:lp-1 / purpose Capital call for Fund Alpha LP / policy Controller approval pending before execution-deferred reliance",
      sourceEvidenceLabel: "2 source evidence link(s)",
      approvalLabel: "0/2 approved",
      bankEvidenceLabel: "0 confirmed / 1 retained / 0 returned",
      reconciliationLabel: "1/1 reconciliation ready",
      auditLabel: "2 audit event(s)",
      evidenceRouteLabel: "/api/workstation/evidence/subjects/payment-intent/payment%3Afund-alpha%3Acapital-call%3Amanual-je-1/packet",
      approvalSteps: [
        {
          sequenceLabel: "Step 1",
          roleLabel: "Requester",
          actorLabel: "ops-user",
          statusLabel: "Requested",
          decidedLabel: "Jun 30, 00:00 UTC"
        },
        {
          sequenceLabel: "Step 2",
          roleLabel: "Controller approval",
          actorLabel: "controller",
          statusLabel: "Pending",
          decidedLabel: "Decision pending"
        }
      ],
      bankEvidence: [
        {
          title: "RetainedCashEvidence",
          statusLabel: "Retained",
          amountLabel: "$100 USD",
          effectiveDateLabel: "2026-06-30",
          recorderLabel: "Recorded by cash-ops@example.com",
          referenceLabel: "settlement:fund-alpha:capital-call:20260630"
        }
      ],
      reconciliationLinks: [
        {
          id: "reconciliation:capital-call",
          statusLabel: "Ready",
          routeLabel: "/api/reconciliation/runs/capital-call"
        }
      ],
      auditEvents: [
        {
          actionLabel: "payment-intent.requested",
          actorLabel: "ops-user",
          evidenceLabel: "1 evidence link(s)"
        },
        {
          actionLabel: "payment-intent.execution-deferred",
          actorLabel: "system",
          evidenceLabel: "0 evidence link(s)"
        }
      ]
    });
    expect(result.current.privateCapitalActivity.fundEventLedgerRecords[0]).toMatchObject({
      title: "CapitalCall",
      statusLabel: "Draft",
      statusTone: "warning",
      readinessLabel: "Approval pending",
      readinessTone: "warning",
      readinessReasonLabel: "Submit the fund-event journal for approval before posting or stakeholder report output.",
      nextActionLabel: "Submit approval",
      nextActionRouteLabel: "/api/ledger/private-capital/activity?fundProfileId=fund-alpha&fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1",
      netActivityLabel: "+$100.00 USD",
      grossActivityLabel: "$100 USD",
      capitalAccountRollForwardLabel: "$0 USD opening -> +$100.00 USD ending",
      memoLabel: "Manual close adjustment",
      referenceLabel: "payment:fund-alpha:capital-call:manual-je-1 / settlement:fund-alpha:capital-call:20260630",
      activityRouteLabel: "/api/ledger/private-capital/activity?fundProfileId=fund-alpha&fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1",
      commandCenterRouteLabel: "/api/ledger/private-capital/fund-event-command-center?fundProfileId=fund-alpha&fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630",
      evidenceRouteLabel: "/api/workstation/evidence/subjects/private-capital-fund-event/fund-event%3Afund-alpha%3Acapital-call%3A20260630/packet",
      approvalRouteLabel: "No approval route",
      evidenceLabel: "2 evidence",
      ledgerImpactLabel: "1 ledger impact(s)",
      subledgerLabel: "1 subledger movement(s)",
      reportOutputLabel: "1 report output(s)",
      reportOutputDetailLabel: "CapitalCallNotice / Draft / 1 provenance",
      reportOutputRouteLabel: "/api/ledger/private-capital/report-output?fundProfileId=fund-alpha&reportOutputId=report-output%3Afund-event%3Afund-alpha%3Acapital-call%3A20260630%3Acapitalcallnotice&fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1",
      paymentEvidenceLabel: "Settlement matched / Inflow / $100 USD / 1 cash evidence / settlement linked",
      paymentEvidenceTone: "success",
      paymentEvidenceRequiredLabel: "No additional payment evidence required",
      evidenceCategorySummaryLabel: "4/7 evidence categories ready",
      evidenceCategories: expect.arrayContaining([
        expect.objectContaining({
          id: "source-support",
          label: "Source support",
          statusLabel: "Ready",
          tone: "success",
          evidenceLabel: "2 evidence",
          requiredEvidenceLabel: "Source document or retained evidence link"
        }),
        expect.objectContaining({
          id: "approval-state",
          label: "Approval state",
          statusLabel: "Missing",
          tone: "warning",
          evidenceLabel: "0 evidence",
          requiredEvidenceLabel: "Approval reference"
        }),
        expect.objectContaining({
          id: "report-output",
          label: "Report output",
          statusLabel: "Missing",
          tone: "warning",
          evidenceLabel: "1 evidence",
          requiredEvidenceLabel: "Governed report output"
        })
      ]),
      issueLabel: "2 record issue(s)"
    });

    act(() => result.current.updateSecuritySearchQuery("AAPL"));
    await act(async () => {
      await result.current.searchSecurityMaster();
    });
    act(() => result.current.selectSecurity("line-debit", securityResult));
    expect(result.current.draft.lines[0].securityDisplayName).toBe("Apple Inc.");
    expect(result.current.getLineBadges("line-debit").map((badge) => badge.label)).toContain("Security");

    act(() => result.current.updateAttachmentDraft({
      displayName: "Source support",
      uri: "/api/workstation/evidence/subjects/accounting-record/source-doc",
      lineId: "line-debit"
    }));
    await act(async () => {
      await result.current.addAttachment();
    });
    expect(services.attachEvidence).toHaveBeenCalledWith(expect.objectContaining({
      journalEntryId: "manual-je-1",
      fundProfileId: "fund-alpha",
      actor: "browser-user",
      version: 1,
      correlationId: expect.stringMatching(/^manual-je-attach-evidence-/),
      evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/source-doc"],
      actionOrigin: "HumanOperator",
      periodIsLocked: false,
      attachment: expect.objectContaining({
        displayName: "Source support",
        uri: "/api/workstation/evidence/subjects/accounting-record/source-doc",
        evidenceKind: "SourceDocument",
        sourceSystem: "ManualUpload",
        lineId: "line-debit"
      })
    }));
    expect(result.current.draft.evidenceAttachments).toHaveLength(1);
    expect(result.current.draft.evidenceLinks).toContain("/api/workstation/evidence/subjects/accounting-record/source-doc");
    expect(result.current.attachEvidenceStatusText).toBe("Evidence attached: Source support.");
    expect(result.current.getLineBadges("line-debit").map((badge) => badge.label)).toContain("Evidence");
    expect(result.current.lifecycleChecklist).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "draft",
        label: "Draft",
        value: "v1",
        tone: "success"
      }),
      expect.objectContaining({
        id: "validate",
        label: "Validate",
        value: "Clear",
        tone: "success"
      }),
      expect.objectContaining({
        id: "evidence",
        label: "Attach evidence",
        value: "1 evidence link",
        tone: "success"
      }),
      expect.objectContaining({
        id: "post",
        label: "Post",
        value: "Blocked",
        detail: "Requires Approved status; current status is Draft.",
        tone: "warning"
      }),
      expect.objectContaining({
        id: "audit",
        label: "Audit transitions",
        value: "None",
        tone: "warning"
      })
    ]));

    await act(async () => {
      await result.current.applyLifecycleAction("Post");
    });

    expect(services.applyLifecycleAction).toHaveBeenCalledWith(expect.objectContaining({
      journalEntryId: "manual-je-1",
      fundProfileId: "fund-alpha",
      action: "Post",
      actor: "browser-user",
      version: 1,
      actionOrigin: "HumanOperator",
      evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/source-doc"]
    }));
    expect(result.current.draft.status).toBe("Posted");
    expect(result.current.lifecycleStatusText).toBe("Post recorded: Approved -> Posted");
    expect(result.current.lifecycleTransitions[0]).toMatchObject({
      title: "Post: Approved -> Posted",
      auditLabel: "Audit transition-post",
      correlationLabel: "Correlation manual-je-post",
      evidenceLabel: "1 evidence link(s)",
      evidenceTone: "success",
      evidenceRows: ["/api/workstation/evidence/subjects/accounting-record/source-doc"]
    });
    expect(result.current.lifecycleCorrectionRows[0]).toMatchObject({
      id: "manual-je-reversal-1",
      sourceLabel: "Reversal of manual-je-1"
    });
    expect(result.current.lifecycleChecklist).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "post",
        value: "Posted",
        detail: "browser-user posted the immutable entry on Jun 30, 02:00 UTC.",
        tone: "success"
      }),
      expect.objectContaining({
        id: "reverse",
        value: "Linked",
        detail: "A reversal relationship or generated reversal draft is retained; the posted entry was not edited in place.",
        tone: "success"
      }),
      expect.objectContaining({
        id: "rebook",
        value: "Ready",
        detail: "Generate a separate rebook draft using the current posted lines.",
        tone: "warning"
      }),
      expect.objectContaining({
        id: "lock-after-close",
        value: "Ready",
        detail: "Lock a posted journal entry after close.",
        tone: "warning"
      }),
      expect.objectContaining({
        id: "audit",
        value: "1 transition",
        detail: "Lifecycle transitions retain audit id, correlation id, actor, notes, and evidence routes.",
        tone: "success"
      })
    ]));
  });

  it("treats absent private-capital activity collections as empty rows", async () => {
    const workbench: ManualJournalEntryWorkbench = {
      ...manualJournalWorkbench,
      privateCapitalActivity: {
        ...manualJournalWorkbench.privateCapitalActivity!,
        fundEvents: null,
        capitalAccounts: null,
        capitalAccountSubledgers: null,
        capitalAccountSubledgerEntries: null,
        ledgerImpacts: null,
        reportOutputs: null,
        fundEventRecords: null,
        paymentIntents: null,
        validationIssues: null
      } as unknown as ManualJournalEntryWorkbench["privateCapitalActivity"]
    };
    const services: ManualJournalEntryWorkbenchServices = {
      getWorkbench: vi.fn().mockResolvedValue(workbench),
      searchSecurities: vi.fn().mockResolvedValue([]),
      saveDraft: vi.fn().mockResolvedValue(manualJournalDraft),
      validateDraft: vi.fn().mockResolvedValue(manualJournalDraft),
      submitApproval: vi.fn().mockResolvedValue(manualJournalDraft),
      attachEvidence: vi.fn().mockResolvedValue(manualJournalDraft),
      applyLifecycleAction: vi.fn().mockResolvedValue({
        journalEntry: manualJournalDraft,
        transition: {
          transitionId: "transition-approve",
          fromStatus: "Submitted",
          toStatus: "Approved",
          action: "Approve",
          actor: "browser-user",
          recordedAtUtc: "2026-06-30T02:00:00Z",
          notes: null,
          correlationId: "manual-je-approve",
          evidenceLinks: []
        },
        generatedJournalEntries: []
      })
    };

    const { result } = renderHook(() => useManualJournalEntryWorkbenchViewModel(true, services));

    await waitFor(() => expect(result.current.privateCapitalActivity.statusLabel).toBe("1 fund events / 1 capital accounts"));
    expect(result.current.privateCapitalActivity.fundEvents).toEqual([]);
    expect(result.current.privateCapitalActivity.capitalAccounts).toEqual([]);
    expect(result.current.privateCapitalActivity.capitalAccountSubledgers).toEqual([]);
    expect(result.current.privateCapitalActivity.capitalAccountSubledgerEntries).toEqual([]);
    expect(result.current.privateCapitalActivity.ledgerImpacts).toEqual([]);
    expect(result.current.privateCapitalActivity.reportOutputs).toEqual([]);
    expect(result.current.privateCapitalActivity.fundEventLedgerRecords).toEqual([]);
    expect(result.current.privateCapitalActivity.paymentIntents).toEqual([]);
    expect(result.current.privateCapitalActivity.validationIssues).toEqual([]);
  });

  it("loads manual journal entries with route ledger-book scope and preserves scope on mutations", async () => {
    const scopedWorkbench: ManualJournalEntryWorkbench = {
      ...manualJournalWorkbench,
      fundProfileId: "fund-route",
      ledgerBookId: null,
      drafts: []
    };
    const services: ManualJournalEntryWorkbenchServices = {
      getWorkbench: vi.fn().mockResolvedValue(scopedWorkbench),
      searchSecurities: vi.fn().mockResolvedValue([]),
      saveDraft: vi.fn((request) => Promise.resolve(request.draft)),
      validateDraft: vi.fn((request) => Promise.resolve(request.draft)),
      submitApproval: vi.fn((request) => Promise.resolve({
        ...manualJournalDraft,
        journalEntryId: request.journalEntryId,
        fundProfileId: request.fundProfileId,
        ledgerBookId: request.ledgerBookId ?? null,
        status: "Submitted" as const,
        version: request.version + 1
      })),
      attachEvidence: vi.fn((request) => Promise.resolve({
        ...manualJournalDraft,
        journalEntryId: request.journalEntryId,
        fundProfileId: request.fundProfileId,
        ledgerBookId: request.ledgerBookId ?? null,
        version: request.version + 1,
        evidenceLinks: request.evidenceLinks,
        evidenceAttachments: [request.attachment],
        validationIssues: []
      })),
      applyLifecycleAction: vi.fn((request) => Promise.resolve({
        journalEntry: {
          ...manualJournalDraft,
          journalEntryId: request.journalEntryId,
          fundProfileId: request.fundProfileId,
          ledgerBookId: request.ledgerBookId ?? null,
          status: "Approved" as const,
          version: request.version + 1
        },
        transition: {
          transitionId: "transition-approve",
          fromStatus: "Submitted" as const,
          toStatus: "Approved" as const,
          action: request.action,
          actor: request.actor,
          recordedAtUtc: "2026-06-30T02:00:00Z",
          notes: request.notes,
          correlationId: request.correlationId,
          evidenceLinks: request.evidenceLinks
        },
        generatedJournalEntries: []
      }))
    };

    const { result } = renderHook(() =>
      useManualJournalEntryWorkbenchViewModel(
        true,
        services,
        "?fundProfileId=fund-route&ledgerBookId=book-route"
      )
    );

    await waitFor(() => expect(result.current.draft.fundProfileId).toBe("fund-route"));
    expect(result.current.draft.ledgerBookId).toBe("book-route");
    expect(services.getWorkbench).toHaveBeenCalledWith({
      fundProfileId: "fund-route",
      ledgerBookId: "book-route"
    });

    await act(async () => {
      await result.current.save();
    });
    act(() => {
      result.current.updateAttachmentDraft({
        displayName: "Book scoped support",
        uri: "evidence://manual-je/book-route/support",
        lineId: "line-1"
      });
    });
    await act(async () => {
      await result.current.addAttachment();
    });
    await act(async () => {
      await result.current.validate();
    });
    await act(async () => {
      await result.current.submit();
    });
    await act(async () => {
      await result.current.applyLifecycleAction("Approve");
    });

    expect(services.saveDraft).toHaveBeenCalledWith(expect.objectContaining({
      ledgerBookId: "book-route",
      draft: expect.objectContaining({
        fundProfileId: "fund-route",
        ledgerBookId: "book-route"
      })
    }));
    expect(services.validateDraft).toHaveBeenCalledWith(expect.objectContaining({
      ledgerBookId: "book-route",
      draft: expect.objectContaining({
        fundProfileId: "fund-route",
        ledgerBookId: "book-route"
      })
    }));
    expect(services.submitApproval).toHaveBeenCalledWith(expect.objectContaining({
      fundProfileId: "fund-route",
      ledgerBookId: "book-route"
    }));
    expect(services.attachEvidence).toHaveBeenCalledWith(expect.objectContaining({
      fundProfileId: "fund-route",
      ledgerBookId: "book-route"
    }));
    expect(services.applyLifecycleAction).toHaveBeenCalledWith(expect.objectContaining({
      fundProfileId: "fund-route",
      ledgerBookId: "book-route",
      action: "Approve"
    }));
  });

  it("selects an exact scoped linked draft instead of the first returned draft", async () => {
    const target = { ...manualJournalDraft, journalEntryId: "created-paydown" };
    const services = createJournalServices({ getWorkbench: vi.fn().mockResolvedValue({ ...manualJournalWorkbench, drafts: [manualJournalDraft, target] }) });
    const { result } = renderHook(() => useManualJournalEntryWorkbenchViewModel(true, services,
      "?fundProfileId=fund-alpha&ledgerBookId=book-alpha&periodId=2026-06&journalEntryId=created-paydown"));
    await waitFor(() => expect(result.current.available).toBe(true));
    expect(result.current.draft.journalEntryId).toBe("created-paydown");
    expect(result.current.selectedLineId).toBe(target.lines[0].lineId);
  });

  it.each([
    { fundProfileId: "foreign" }, { ledgerBookId: "foreign" }, { periodId: "foreign" },
    { journalEntryId: "missing" }
  ])("refuses a linked draft with mismatched or absent scope (%j)", async (mismatch) => {
    const target = { ...manualJournalDraft, journalEntryId: "created-paydown", ...mismatch };
    const services = createJournalServices({ getWorkbench: vi.fn().mockResolvedValue({ ...manualJournalWorkbench, drafts: [manualJournalDraft, target] }) });
    const { result } = renderHook(() => useManualJournalEntryWorkbenchViewModel(true, services,
      "?fundProfileId=fund-alpha&ledgerBookId=book-alpha&periodId=2026-06&journalEntryId=created-paydown"));
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.available).toBe(false);
    expect(result.current.errorText).toContain("requested journal draft is unavailable");
  });

  it.each(["fundProfileId", "ledgerBookId"])("refuses linked selection from a foreign workbench %s", async (field) => {
    const services = createJournalServices({ getWorkbench: vi.fn().mockResolvedValue({ ...manualJournalWorkbench, [field]: "foreign" }) });
    const { result } = renderHook(() => useManualJournalEntryWorkbenchViewModel(true, services,
      "?fundProfileId=fund-alpha&ledgerBookId=book-alpha&periodId=2026-06&journalEntryId=manual-je-1"));
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.available).toBe(false);
    expect(result.current.errorText).toContain("requested journal draft is unavailable");
  });

  it("requires an explicit period for exact linked selection", async () => {
    const services = createJournalServices();
    const { result } = renderHook(() => useManualJournalEntryWorkbenchViewModel(true, services,
      "?fundProfileId=fund-alpha&ledgerBookId=book-alpha&journalEntryId=manual-je-1"));
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.available).toBe(false);
  });

  it("does not recover another draft over a linked selection and preserves its unsaved snapshot", async () => {
    const target = { ...manualJournalDraft, journalEntryId: "created-paydown" };
    const services = createJournalServices({ getWorkbench: vi.fn().mockResolvedValue({ ...manualJournalWorkbench, drafts: [manualJournalDraft, target] }) });
    const view = renderHook(({ search }) => useManualJournalEntryWorkbenchViewModel(true, services, search), { initialProps: { search: "" } });
    await waitFor(() => expect(view.result.current.available).toBe(true));
    act(() => view.result.current.updateHeader("memo", "Unsaved original draft"));
    view.rerender({ search: "?fundProfileId=fund-alpha&ledgerBookId=book-alpha&periodId=2026-06&journalEntryId=created-paydown" });
    await waitFor(() => expect(view.result.current.draft.journalEntryId).toBe("created-paydown"));
    expect(view.result.current.draft.memo).toBe("Manual close adjustment");
    const snapshot = JSON.parse(window.localStorage.getItem("meridian.manual-journal-recovery.v1:fund-alpha:book-alpha")!);
    expect(snapshot.draft.memo).toBe("Unsaved original draft");
    expect(snapshot.draft.journalEntryId).toBe("manual-je-1");
  });

  it("discards late linked-workbench reads after navigating to a different exact draft", async () => {
    const a = { ...manualJournalDraft, journalEntryId: "paydown-a" };
    const b = { ...manualJournalDraft, journalEntryId: "paydown-b" };
    let resolveA!: (value: ManualJournalEntryWorkbench) => void;
    const services = createJournalServices({ getWorkbench: vi.fn().mockReturnValueOnce(new Promise((resolve) => { resolveA = resolve; }))
      .mockResolvedValueOnce({ ...manualJournalWorkbench, drafts: [b] }) });
    const search = (id: string) => `?fundProfileId=fund-alpha&ledgerBookId=book-alpha&periodId=2026-06&journalEntryId=${id}`;
    const view = renderHook(({ query }) => useManualJournalEntryWorkbenchViewModel(true, services, query), { initialProps: { query: search(a.journalEntryId) } });
    view.rerender({ query: search(b.journalEntryId) });
    await waitFor(() => expect(view.result.current.draft.journalEntryId).toBe("paydown-b"));
    await act(async () => resolveA({ ...manualJournalWorkbench, drafts: [a] }));
    expect(view.result.current.draft.journalEntryId).toBe("paydown-b");
  });

  it("does not apply a late save receipt to the newly linked draft", async () => {
    const target = { ...manualJournalDraft, journalEntryId: "created-paydown" };
    let resolveSave!: (value: ManualJournalEntryDraft) => void;
    const services = createJournalServices({
      getWorkbench: vi.fn().mockResolvedValue({ ...manualJournalWorkbench, drafts: [manualJournalDraft, target] }),
      saveDraft: vi.fn().mockReturnValue(new Promise((resolve) => { resolveSave = resolve; }))
    });
    const search = (id: string) => `?fundProfileId=fund-alpha&ledgerBookId=book-alpha&periodId=2026-06&journalEntryId=${id}`;
    const view = renderHook(({ query }) => useManualJournalEntryWorkbenchViewModel(true, services, query), { initialProps: { query: search("manual-je-1") } });
    await waitFor(() => expect(view.result.current.available).toBe(true));
    act(() => view.result.current.updateHeader("memo", "Save original draft"));
    let pendingSave!: Promise<void>;
    act(() => { pendingSave = view.result.current.save(); });
    view.rerender({ query: search("created-paydown") });
    await waitFor(() => expect(view.result.current.draft.journalEntryId).toBe("created-paydown"));
    await act(async () => { resolveSave({ ...manualJournalDraft, version: 22, memo: "Save original draft" }); await pendingSave; });
    expect(view.result.current.draft.journalEntryId).toBe("created-paydown");
    expect(view.result.current.draft.version).toBe(1);
    expect(view.result.current.draft.memo).toBe("Manual close adjustment");
  });

  it("recovers only the exact linked draft in its retained period", async () => {
    const target = { ...manualJournalDraft, journalEntryId: "created-paydown" };
    window.localStorage.setItem("meridian.manual-journal-recovery.v1:fund-alpha:book-alpha", JSON.stringify({
      capturedAtUtc: "2026-06-30T00:00:00Z", draft: { ...target, memo: "Recovered paydown memo" }
    }));
    const services = createJournalServices({ getWorkbench: vi.fn().mockResolvedValue({ ...manualJournalWorkbench, drafts: [target] }) });
    const { result } = renderHook(() => useManualJournalEntryWorkbenchViewModel(true, services,
      "?fundProfileId=fund-alpha&ledgerBookId=book-alpha&periodId=2026-06&journalEntryId=created-paydown"));
    await waitFor(() => expect(result.current.available).toBe(true));
    expect(result.current.draft.memo).toBe("Recovered paydown memo");
    expect(result.current.saveState).toBe("recovered");
    act(() => result.current.discardRecoveredDraft());
    expect(result.current.draft.memo).toBe("Manual close adjustment");
  });

  it("keeps retained asset drafts immutable and blocks unsupported lifecycle requests", async () => {
    const assetDraft = { ...manualJournalDraft, validationIssues: [], evidenceLinks: ["evidence://retained"], assetAccountingDraft: { spine: { eventId: "asset-event" }, candidate: {} } };
    const services = createJournalServices({ getWorkbench: vi.fn().mockResolvedValue({ ...manualJournalWorkbench, drafts: [assetDraft] }) });
    const { result } = renderHook(() => useManualJournalEntryWorkbenchViewModel(true, services));
    await waitFor(() => expect(result.current.available).toBe(true));
    act(() => {
      result.current.updateHeader("memo", "Unreviewed mutation");
      result.current.updateLine("line-debit", { amount: 999 });
      result.current.updateDraftDimensions({ entityId: "foreign" });
      result.current.addLine("Debit");
      result.current.removeLine("line-debit");
    });
    await act(async () => { await result.current.save(); await result.current.applyLifecycleAction("Approve"); await result.current.applyLifecycleAction("Post"); });
    expect(result.current.draft.memo).toBe(assetDraft.memo);
    expect(result.current.draft.lines).toEqual(assetDraft.lines);
    expect(services.saveDraft).not.toHaveBeenCalled();
    expect(services.applyLifecycleAction).not.toHaveBeenCalled();
    for (const action of ["Approve", "Post", "Reverse", "Rebook", "LockAfterClose"]) {
      expect(result.current.lifecycleCommands.find((command) => command.action === action)?.disabledReason).toContain("governed asset workflow");
    }
    expect(result.current.lifecycleStatusText).toContain("generic approval, posting and corrections are unavailable");
  });

  it("allows a retained asset draft to validate and submit for review without enabling approval", async () => {
    const assetDraft = { ...manualJournalDraft, validationIssues: [], evidenceLinks: ["evidence://retained"], assetAccountingDraft: { spine: { eventId: "asset-event" }, candidate: {} } };
    const services = createJournalServices({ getWorkbench: vi.fn().mockResolvedValue({ ...manualJournalWorkbench, drafts: [assetDraft] }),
      submitApproval: vi.fn().mockResolvedValue({ ...assetDraft, status: "Submitted", version: 2 }) });
    const { result } = renderHook(() => useManualJournalEntryWorkbenchViewModel(true, services));
    await waitFor(() => expect(result.current.available).toBe(true));
    await act(async () => { await result.current.validate(); });
    expect(result.current.canSubmit).toBe(true);
    await act(async () => { await result.current.submit(); });
    expect(services.submitApproval).toHaveBeenCalledTimes(1);
    expect(result.current.draft.status).toBe("Submitted");
    expect(result.current.lifecycleCommands.find((command) => command.action === "Approve")?.disabledReason).toContain("governed asset workflow");
    expect(result.current.lifecycleCommands.find((command) => command.action === "Reject")?.disabledReason).toBeNull();
  });

  it("enables the governed Approve adapter only after scoped server support and explicit review", async () => {
    const assetDraft = { ...manualJournalDraft, status: "Submitted" as const, validationIssues: [], evidenceLinks: ["retained:source"], assetAccountingDraft: {} };
    const services = createJournalServices({ getWorkbench: vi.fn().mockResolvedValue({ ...manualJournalWorkbench, drafts: [assetDraft] }),
      getAssetCapabilities: vi.fn().mockImplementation(scope => Promise.resolve({ ...scope, protocolVersion: 1, authenticatedActor: "independent-checker", candidateFingerprint: "a".repeat(64), canApprove: true, canPost: false, approveBlockedReason: null, postBlockedReason: "Approval required", approval: null, hasPendingPost: false, pendingPostCommand: null, postedJournal: null })),
      applyLifecycleAction: vi.fn().mockImplementation(request => Promise.resolve({ journalEntry: { ...assetDraft, status: "Approved", version: assetDraft.version + 1 }, transition: { transitionId: "governed-approve", fromStatus: "Submitted", toStatus: "Approved", action: "Approve", actor: "independent-checker", recordedAtUtc: "2026-10-28T00:00:00Z", correlationId: request.correlationId, evidenceLinks: [] }, generatedJournalEntries: [] })) });
    const { result } = renderHook(() => useManualJournalEntryWorkbenchViewModel(true, services, "", "checker-session"));
    await waitFor(() => expect(result.current.assetWorkflow?.capability).not.toBeNull());
    expect(result.current.lifecycleCommands.find(command => command.action === "Approve")?.disabledReason).toContain("Confirm review");
    act(() => { result.current.assetWorkflow!.setReviewNote("Reviewed exact retained candidate"); result.current.assetWorkflow!.setConfirmed(true); });
    expect(result.current.lifecycleCommands.find(command => command.action === "Approve")?.disabledReason).toBeNull();
    await act(async () => result.current.applyLifecycleAction("Approve"));
    expect(services.applyLifecycleAction).toHaveBeenCalledWith(expect.objectContaining({ journalEntryId: assetDraft.journalEntryId, fundProfileId: assetDraft.fundProfileId, ledgerBookId: assetDraft.ledgerBookId, version: assetDraft.version, action: "Approve", notes: "Reviewed exact retained candidate" }));
    expect(result.current.draft.status).toBe("Approved");
    expect(result.current.lifecycleCommands.find(command => command.action === "Reverse")?.disabledReason).not.toBeNull();
  });

  it("blocks validation, submission and rejection while any canonical post intent is pending", async () => {
    const assetDraft = { ...manualJournalDraft, validationIssues: [], assetAccountingDraft: {} };
    const services = createJournalServices({ getWorkbench: vi.fn().mockResolvedValue({ ...manualJournalWorkbench, drafts: [assetDraft] }),
      getAssetCapabilities: vi.fn().mockImplementation(scope => Promise.resolve({ ...scope, protocolVersion: 1, authenticatedActor: "independent-checker", candidateFingerprint: "a".repeat(64), canApprove: false, canPost: false, approveBlockedReason: "Posting recovery pending", postBlockedReason: "Only original approver can recover", approval: null, hasPendingPost: true, pendingPostCommand: null, postedJournal: null })) });
    const { result } = renderHook(() => useManualJournalEntryWorkbenchViewModel(true, services));
    await waitFor(() => expect(result.current.assetWorkflow?.hasPendingPost).toBe(true));
    await act(async () => { await result.current.validate(); await result.current.submit(); await result.current.applyLifecycleAction("Reject"); });
    expect(services.validateDraft).not.toHaveBeenCalled(); expect(services.submitApproval).not.toHaveBeenCalled(); expect(services.applyLifecycleAction).not.toHaveBeenCalled();
    expect(result.current.lifecycleCommands.find(command => command.action === "Reject")?.disabledReason).not.toBeNull();
  });

  it("deactivates asset decisions immediately when a new route scope is still loading", async () => {
    const assetDraft = { ...manualJournalDraft, status: "Submitted" as const, validationIssues: [], assetAccountingDraft: {} };
    let resolveCommand!: (result: JournalEntryLifecycleActionResult) => void;
    let rejectRefresh!: (reason: Error) => void;
    const services = createJournalServices({
      getWorkbench: vi.fn().mockResolvedValueOnce({ ...manualJournalWorkbench, drafts: [assetDraft] })
        .mockImplementationOnce(() => new Promise((_resolve, reject) => { rejectRefresh = reject; })),
      getAssetCapabilities: vi.fn().mockImplementation(scope => Promise.resolve({ ...scope, protocolVersion: 1, authenticatedActor: "independent-checker", candidateFingerprint: "a".repeat(64), canApprove: true, canPost: false, approveBlockedReason: null, postBlockedReason: "Approval required", approval: null, hasPendingPost: false, pendingPostCommand: null, postedJournal: null })),
      applyLifecycleAction: vi.fn().mockImplementation(() => new Promise(resolve => { resolveCommand = resolve; }))
    });
    const view = renderHook(({ search }) => useManualJournalEntryWorkbenchViewModel(true, services, search, "checker-session"), { initialProps: { search: "" } });
    await waitFor(() => expect(view.result.current.assetWorkflow?.capability).not.toBeNull());
    act(() => { view.result.current.assetWorkflow!.setReviewNote("Review current route candidate"); view.result.current.assetWorkflow!.setConfirmed(true); });
    const capturedRun = view.result.current.assetWorkflow!.run;
    let command!: Promise<void>;
    act(() => { command = view.result.current.applyLifecycleAction("Approve"); });
    view.rerender({ search: "?fundProfileId=fund-beta&ledgerBookId=book-beta" });
    expect(view.result.current.assetWorkflow?.capability).toBeNull();
    const request = vi.mocked(services.applyLifecycleAction).mock.calls[0][0];
    await act(async () => {
      await capturedRun("Approve");
      resolveCommand({ journalEntry: { ...assetDraft, status: "Approved", version: assetDraft.version + 1 }, transition: { transitionId: "late-approve", fromStatus: "Submitted", toStatus: "Approved", action: "Approve", actor: "independent-checker", recordedAtUtc: "2026-10-28T00:00:00Z", correlationId: request.correlationId, evidenceLinks: [] }, generatedJournalEntries: [] });
      await command;
    });
    expect(services.applyLifecycleAction).toHaveBeenCalledTimes(1);
    expect(view.result.current.draft.version).toBe(assetDraft.version);
    expect(view.result.current.draft.status).toBe("Submitted");
    await act(async () => { rejectRefresh(new Error("new scoped workbench unavailable")); });
    expect(view.result.current.assetWorkflow?.capability).toBeNull();
    expect(view.result.current.available).toBe(false);
  });

  it("autosaves editable changes and reports saved health after the debounce", async () => {
    const services = createJournalServices();
    const { result } = renderHook(() => useManualJournalEntryWorkbenchViewModel(true, services));
    await waitFor(() => expect(result.current.available).toBe(true));

    vi.useFakeTimers();
    act(() => result.current.updateHeader("memo", "Autosaved close adjustment"));
    expect(result.current.saveState).toBe("unsaved");
    expect(result.current.saveStatusLabel).toBe("Unsaved changes");

    await act(async () => {
      await vi.advanceTimersByTimeAsync(1_200);
    });

    expect(services.saveDraft).toHaveBeenCalledWith(expect.objectContaining({
      correlationId: "manual-je-autosave",
      draft: expect.objectContaining({ memo: "Autosaved close adjustment" })
    }));
    expect(result.current.saveState).toBe("saved");
    expect(result.current.draft.version).toBe(2);
  });

  it("recovers an unsaved local snapshot and can discard it for the server draft", async () => {
    vi.useFakeTimers();
    const services = createJournalServices();
    const first = renderHook(() => useManualJournalEntryWorkbenchViewModel(true, services));
    await vi.waitFor(() => expect(first.result.current.available).toBe(true));

    act(() => first.result.current.updateHeader("memo", "Recovered operator memo"));
    expect(first.result.current.saveState).toBe("unsaved");
    first.unmount();

    const second = renderHook(() => useManualJournalEntryWorkbenchViewModel(true, services));
    await vi.waitFor(() => expect(second.result.current.saveState).toBe("recovered"));
    expect(second.result.current.draft.memo).toBe("Recovered operator memo");
    expect(second.result.current.recoveryStatusText).toContain("Recovered unsaved changes");

    act(() => second.result.current.discardRecoveredDraft());
    expect(second.result.current.saveState).toBe("saved");
    expect(second.result.current.draft.memo).toBe("Manual close adjustment");
  });

  it("duplicates and inserts lines while keeping selected dimensions editable", async () => {
    const services = createJournalServices();
    const { result } = renderHook(() => useManualJournalEntryWorkbenchViewModel(true, services));
    await waitFor(() => expect(result.current.available).toBe(true));

    let duplicateId: string | null = null;
    act(() => {
      duplicateId = result.current.duplicateLine("line-debit");
    });
    expect(duplicateId).toBeTruthy();
    expect(result.current.draft.lines).toHaveLength(3);
    expect(result.current.draft.lines[1]).toMatchObject({
      lineId: duplicateId,
      side: "Debit",
      amount: 100,
      accountPath: "Assets:Cash",
      description: "Cash debit (copy)"
    });

    let insertedId = "";
    act(() => {
      insertedId = result.current.insertLineAfter(duplicateId!, "Credit");
      result.current.updateLine(duplicateId!, {
        entityId: "entity-line",
        fundAllocationId: "allocation-1",
        taxLotId: "lot-1"
      });
      result.current.updateDraftDimensions({
        strategyId: "strategy-1",
        portfolioId: "portfolio-1",
        costCenterId: "cost-center-1"
      });
    });

    expect(result.current.selectedLineId).toBe(insertedId);
    expect(result.current.draft.lines).toHaveLength(4);
    expect(result.current.draft.lines.find((line) => line.lineId === duplicateId)).toMatchObject({
      entityId: "entity-line",
      fundAllocationId: "allocation-1",
      taxLotId: "lot-1"
    });
    expect(result.current.draft.dimensions).toMatchObject({
      strategyId: "strategy-1",
      portfolioId: "portfolio-1",
      costCenterId: "cost-center-1"
    });
    expect(result.current.validationIsCurrent).toBe(false);
  });


});
