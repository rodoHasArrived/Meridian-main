import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApiError } from "@/lib/api-errors";
import * as api from "@/lib/api";
import { AccountingScreen } from "@/screens/accounting-screen";
import { TestMemoryRouter, renderWithRouter, waitForAsyncEffects } from "@/test/render";
import type {
  AccountingSystemImportDetail,
  AccountingSystemProvider,
  AccountingSystemReconciliationSummary,
  CorporateAction,
  AccountingWorkspaceResponse,
  LedgerTrialBalanceLine,
  ReconciliationCalibrationSummary,
  OperationsContinuityWorkflow,
  OperationsContinuityWorkflowSummary,
  CapitalAccountWorkbench,
  ManualJournalEntryDraft,
  ManualJournalEntryWorkbench,
  SecurityMasterConflict,
  SecurityMasterTrustSnapshot
} from "@/types";

vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  const createFinancialRecordExplorer = (explorerId: string) => ({
    explorerId,
    title: explorerId === "security-instrument" ? "Security & Instrument Explorer" : "Ledger Explorer",
    description: "Explore retained financial records and proof links.",
    sourceState: `Source-backed ${explorerId} projection from run run-42.`,
    isBlocked: false,
    blockedReason: "",
    scopeItems: [
      { label: "Workstream", value: "Accounting", tone: "Info" },
      { label: "Source", value: explorerId === "security-instrument" ? "Security Master instruments" : "Journal entries and ledger detail", tone: "Default" }
    ],
    savedViews: [
      {
        viewId: `system-${explorerId}-default`,
        label: explorerId === "security-instrument" ? "Instrument proof" : "Controller review",
        description: "Source-backed system view.",
        isSystem: true,
        isActive: true,
        filters: [],
        searchText: ""
      }
    ],
    summaryItems: [
      { label: explorerId === "security-instrument" ? "Security coverage" : "Records", value: "1", detail: "Retained source-backed rows.", tone: "Success" }
    ],
    filters: [
      { filterId: "all-records", label: explorerId === "security-instrument" ? "No selection" : "All accounts", value: explorerId === "security-instrument" ? "No selection" : "All accounts", operator: "equals", tone: "Info" }
    ],
    columns: [
      { columnId: "name", header: explorerId === "security-instrument" ? "Security" : "Account", cellKind: "text", width: 220, isRightAligned: false },
      { columnId: "status", header: "Status", cellKind: "text", width: 110, isRightAligned: false }
    ],
    rows: [
      {
        recordId: `${explorerId}:run-42:1`,
        recordType: explorerId,
        label: explorerId === "security-instrument" ? "Apple Inc." : "Cash",
        source: explorerId === "security-instrument" ? "Security Master" : "Trial balance",
        status: "Ready",
        tone: "Success",
        cells: [
          { columnId: "name", displayValue: explorerId === "security-instrument" ? "Apple Inc." : "Cash", rawValue: "", tone: "Success", linkHref: "" },
          { columnId: "status", displayValue: "Ready", rawValue: "Ready", tone: "Success", linkHref: "" }
        ],
        detail: {
          recordId: `${explorerId}:run-42:1`,
          recordType: explorerId === "security-instrument" ? "Security instrument" : "Ledger account",
          title: explorerId === "security-instrument" ? "Apple Inc." : "Cash",
          subtitle: "run-42",
          description: "Source-backed record detail.",
          tone: "Success",
          fields: [{ label: "Status", value: "Ready", detail: "Retained source projection.", tone: "Success" }],
          proofActions: [
            {
              actionId: "open-source",
              label: "Open source record",
              description: "Open retained source.",
              href: "/accounting/ledger",
              isEnabled: true,
              disabledReason: "",
              tone: "Info"
            }
          ],
          usedIn: [{ relationshipId: "accounting", label: "Accounting", description: "Used by Accounting close.", href: "/accounting", tone: "Info" }],
          impacts: [{ relationshipId: "audit", label: "Audit trail", description: "Supports retained proof.", href: "/reporting/evidence", tone: "Info" }],
          fullRecordHref: "/accounting/ledger"
        }
      }
    ],
    selectedRecord: null,
    proofActions: [
      {
        actionId: "evidence",
        label: explorerId === "security-instrument" ? "Open search" : "Evidence packet",
        description: "Open retained evidence.",
        href: "/reporting/evidence",
        isEnabled: true,
        disabledReason: "",
        tone: "Info"
      }
    ],
    recordGraph: { nodes: [], edges: [] }
  });
  return {
    ...actual,
    getFinancialRecordExplorer: vi.fn((explorerId: string) => Promise.resolve(createFinancialRecordExplorer(explorerId))),
    saveFinancialRecordExplorerView: vi.fn((_explorerId: string, request: { label: string; description: string; filters: unknown[]; searchText: string }) =>
      Promise.resolve({
        viewId: "operator-test-view",
        label: request.label,
        description: request.description,
        isSystem: false,
        isActive: false,
        filters: request.filters,
        searchText: request.searchText
      })
    ),
    searchSecurities: vi.fn().mockResolvedValue([
      {
        securityId: "22222222-2222-2222-2222-222222222222",
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
          version: 1,
          effectiveFrom: "2026-01-01T00:00:00Z",
          effectiveTo: null,
          subType: "CommonStock",
          assetFamily: "Equity",
          issuerType: "Corporate"
        }
      }
    ]),
    getSecurityIdentity: vi.fn().mockResolvedValue(null),
    getOperatorOverrides: vi.fn().mockResolvedValue({
      securityId: "sec-1",
      values: {},
      updatedBy: "",
      updatedAt: ""
    }),
    patchOperatorOverrides: vi.fn(),
    getSecurityConflicts: vi.fn().mockResolvedValue([]),
    getReconciliationBreakQueue: vi.fn().mockResolvedValue([]),
    getReconciliationStatementRuns: vi.fn().mockResolvedValue([]),
    getReconciliationStatementRun: vi.fn(),
    getReconciliationCalibrationSummary: vi.fn().mockResolvedValue({
      asOf: "2026-01-01T00:00:00Z",
      status: "Ready",
      summary: "Calibration metadata is available for reconciliation workflows.",
      totalBreakCount: 1,
      activeBreakCount: 1,
      openBreakCount: 1,
      inReviewBreakCount: 0,
      resolvedBreakCount: 0,
      dismissedBreakCount: 0,
      criticalOpenBreakCount: 0,
      pendingSignoffCount: 0,
      signedOffCount: 0,
      missingCalibrationMetadataCount: 0,
      profiles: []
    }),
    resolveReconciliationBreak: vi.fn(),
    reviewReconciliationBreak: vi.fn(),
    runAnalysisExport: vi.fn(),
    getRunTrialBalance: vi.fn().mockResolvedValue([]),
    getAccountingSystemProviders: vi.fn().mockResolvedValue([]),
    previewAccountingSystemImport: vi.fn(),
    getLatestAccountingSystemImport: vi.fn().mockResolvedValue(null),
    getLatestAccountingSystemReconciliation: vi.fn().mockResolvedValue(null),
    getManualJournalEntryWorkbench: vi.fn(),
    getCapitalAccountWorkbench: vi.fn(),
    saveManualJournalEntryDraft: vi.fn(),
    validateManualJournalEntryDraft: vi.fn(),
    submitManualJournalEntryApproval: vi.fn(),
    getCorporateActions: vi.fn().mockResolvedValue([]),
    getOperationsContinuityWorkflows: vi.fn().mockResolvedValue([]),
    getOperationsContinuityWorkflow: vi.fn(),
    approveOperationsContinuityWorkflow: vi.fn(),
    rejectOperationsContinuityWorkflow: vi.fn(),
    getTradingParameters: vi.fn().mockResolvedValue(null),
    getSecurityTrustSnapshot: vi.fn().mockResolvedValue({
      securityId: "sec-1",
      retrievedAtUtc: "2026-05-21T15:00:00Z",
      scheduleBook: null,
      openLotReadModel: null
    }),
    resolveSecurityConflict: vi.fn()
  };
});

const data: AccountingWorkspaceResponse = {
  metrics: [
    { id: "m1", label: "Open Breaks", value: "2", delta: "+1", tone: "warning" },
    { id: "m2", label: "Timing Drift", value: "1", delta: "0%", tone: "warning" },
    { id: "m3", label: "Security Gaps", value: "0", delta: "0%", tone: "success" },
    { id: "m4", label: "Audit Ready", value: "4", delta: "+2", tone: "success" }
  ],
  reconciliationQueue: [
    {
      runId: "run-42",
      strategyName: "Paper Index Mean Reversion",
      mode: "paper",
      status: "Running",
      lastUpdated: "3m ago",
      breakCount: 2,
      openBreakCount: 1,
      reconciliationStatus: "BreaksOpen"
    },
    {
      runId: "run-57",
      strategyName: "Intraday Vol Carry",
      mode: "paper",
      status: "Paused",
      lastUpdated: "7m ago",
      breakCount: 1,
      openBreakCount: 0,
      reconciliationStatus: "Resolved"
    }
  ],
  breakQueue: [
    {
      breakId: "run-42:cash",
      runId: "run-42",
      strategyName: "Paper Index Mean Reversion",
      category: "AmountMismatch",
      status: "Open",
      variance: 500,
      reason: "Cash variance over tolerance.",
      assignedTo: null,
      detectedAt: "2026-01-01T00:00:00Z",
      lastUpdatedAt: "2026-01-01T00:00:00Z",
      reviewedBy: null,
      reviewedAt: null,
      resolvedBy: null,
      resolvedAt: null,
      resolutionNote: null,
      routingTarget: "FundTrialBalance",
      routingDetail: "Open the accounting trial balance for evidence review.",
      recommendedAction: "Review cash ledger entries before resolving.",
      commentCount: 2,
      evidenceCount: 3,
      signoffStatus: "Pending"
    }
  ],
  cashFlow: {
    totalCash: 120000,
    totalLedgerCash: 120500,
    netVariance: 500,
    totalFinancing: 1400,
    runsWithCashSignals: 4,
    runsWithCashVariance: 1,
    tone: "warning",
    summary: "Cash-flow coverage is available for 4 runs; 1 run needs variance review."
  },
  reporting: {
    profileCount: 4,
    recommendedProfiles: ["excel"],
    profiles: [
      {
        id: "excel",
        name: "Excel",
        targetTool: "Excel",
        format: "Xlsx",
        description: "Board-ready workbook export.",
        loaderScript: false,
        dataDictionary: true
      }
    ],
    reportPackTargets: ["board"],
    summary: "4 export/reporting profiles are available for Accounting and Reporting workflows."
  }
};

const calibrationSummary: ReconciliationCalibrationSummary = {
  asOf: "2026-01-01T00:00:00Z",
  status: "ReviewRequired",
  summary: "Two tolerance profiles loaded for operator review.",
  totalBreakCount: 5,
  activeBreakCount: 2,
  openBreakCount: 1,
  inReviewBreakCount: 1,
  resolvedBreakCount: 3,
  dismissedBreakCount: 0,
  criticalOpenBreakCount: 1,
  pendingSignoffCount: 1,
  signedOffCount: 2,
  missingCalibrationMetadataCount: 0,
  profiles: [
    {
      toleranceProfileId: "tp-cash-variance",
      exceptionRoute: "cash",
      highestSeverity: "Critical",
      maxToleranceBand: 250,
      totalBreakCount: 2,
      openBreakCount: 1,
      inReviewBreakCount: 0,
      resolvedBreakCount: 1,
      dismissedBreakCount: 0,
      pendingSignoffCount: 1,
      signedOffCount: 1,
      lastUpdatedAt: "2026-01-01T00:00:00Z"
    },
    {
      toleranceProfileId: "tp-settlement-lag",
      exceptionRoute: "settlement",
      highestSeverity: "Info",
      maxToleranceBand: null,
      totalBreakCount: 3,
      openBreakCount: 0,
      inReviewBreakCount: 1,
      resolvedBreakCount: 2,
      dismissedBreakCount: 0,
      pendingSignoffCount: 0,
      signedOffCount: 1,
      lastUpdatedAt: "2026-01-01T00:05:00Z"
    }
  ]
};

const approvalWorkflowSummary: OperationsContinuityWorkflowSummary = {
  workflowId: "workflow-approval-1",
  fundAccountId: "fund-alpha",
  periodId: "2026-05",
  securityMasterSnapshotId: "sm-snapshot-1",
  brokerSource: "Northern Trust",
  status: "ApprovalPending",
  version: 7,
  createdAtUtc: "2026-05-31T10:00:00Z",
  updatedAtUtc: "2026-06-01T12:00:00Z",
  gates: [
    {
      gateKey: "Approval",
      displayName: "Approval",
      status: "ReviewRequired",
      isRequired: true,
      description: "Controller sign-off is required.",
      blockers: [],
      nextActions: [
        {
          code: "approve-close",
          label: "Approve close package",
          route: "/accounting/approvals?approvalId=approval-close-1",
          gate: "Approval"
        }
      ],
      completedAtUtc: null,
      completedBy: null
    }
  ],
  nextActions: [
    {
      code: "approve-close",
      label: "Approve close package",
      route: "/accounting/approvals?approvalId=approval-close-1",
      gate: "Approval"
    }
  ]
};

const approvalWorkflowDetail: OperationsContinuityWorkflow = {
  ...approvalWorkflowSummary,
  brokerIntakeState: "Complete",
  securityMasterState: "Complete",
  ledgerPostingState: "Complete",
  reconciliationState: "Complete",
  approvalState: "Submitted",
  timeline: [
    {
      auditId: "audit-approval-1",
      occurredAtUtc: "2026-06-01T12:00:00Z",
      workflowId: "workflow-approval-1",
      fundAccountId: "fund-alpha",
      periodId: "2026-05",
      eventType: "approval-submitted",
      fromState: "ReconciliationActive",
      toState: "ApprovalPending",
      gate: "Approval",
      fromGateStatus: "InProgress",
      toGateStatus: "ReviewRequired",
      actor: "ops.controller",
      rationale: "Submitted for controller sign-off.",
      correlationId: "corr-approval-1",
      references: [],
      previousHash: "prev-hash",
      currentHash: "current-hash"
    }
  ],
  breakCases: [],
  ledgerPreview: null,
  approvals: [
    {
      approvalId: "approval-close-1",
      status: "Submitted",
      operator: "ops.operator",
      reviewer: "ops.controller",
      rationale: "Controller sign-off required before release.",
      submittedAtUtc: "2026-06-01T12:00:00Z",
      decidedAtUtc: null,
      evidenceLinks: [
        {
          evidenceId: "evidence-close-1",
          label: "Close packet",
          route: "/reporting/evidence?subject=workflow-approval-1",
          source: "operations-continuity",
          capturedAtUtc: "2026-06-01T12:00:00Z"
        }
      ]
    }
  ],
  reportPackReadiness: {
    isReady: true,
    reportPackId: "report-pack-2026-05",
    blockingReason: null,
    evidenceLinks: []
  },
  closeChecklist: [],
  closeReadiness: null,
  closePackage: null,
  accountingRecordSummary: null,
  evidenceLinks: [],
  blockers: []
};

const securityTrustSnapshot: SecurityMasterTrustSnapshot = {
  securityId: "sec-1",
  retrievedAtUtc: "2026-05-21T15:00:00Z",
  scheduleBook: {
    supportsCashflowSchedule: true,
    supportsFactorHistory: true,
    hasEconomicScheduleTerms: true,
    currency: "USD",
    currentFactor: 0.9,
    currentFactorDate: "2026-11-15",
    nextLifecycleDate: "2031-12-15",
    sourceSummary: "Schedule source EDM-123 is current.",
    summary: "Schedule book includes current cash-flow and factor evidence.",
    events: [
      {
        eventId: "sched-1-coupon",
        eventType: "Coupon",
        effectiveDate: "2026-05-15",
        payDate: "2026-05-15",
        accrualStartDate: "2025-11-15",
        accrualEndDate: "2026-05-15",
        expectedAmount: 26250,
        actualAmount: 26250,
        varianceAmount: 0,
        factorStart: 1,
        factorEnd: 1,
        currency: "USD",
        postingStatus: "Posted",
        sourceSystem: "golden-edm",
        sourceRecordId: "EDM-123",
        sourceAsOfUtc: "2026-05-21T14:00:00Z",
        sourceUpdatedBy: "workflow.bot",
        sourceReason: "Trustee schedule matched.",
        isDerivedFromEconomicTerms: true,
        isCurrentProjection: false
      },
      {
        eventId: "sched-1-principal",
        eventType: "Principal",
        effectiveDate: "2026-11-15",
        payDate: "2026-11-15",
        accrualStartDate: "2026-05-15",
        accrualEndDate: "2026-11-15",
        expectedAmount: 126250,
        actualAmount: null,
        varianceAmount: null,
        factorStart: 1,
        factorEnd: 0.9,
        currency: "USD",
        postingStatus: "Pending",
        sourceSystem: "golden-edm",
        sourceRecordId: "EDM-123",
        sourceAsOfUtc: "2026-05-21T14:00:00Z",
        sourceUpdatedBy: "workflow.bot",
        sourceReason: "Projected amortization row.",
        isDerivedFromEconomicTerms: true,
        isCurrentProjection: true
      }
    ],
    factorHistory: [],
    provenanceHistory: [
      {
        provenanceId: "schedule-source-1",
        category: "Schedule",
        summary: "Loaded from golden EDM schedule source.",
        effectiveDate: "2026-05-15",
        sourceSystem: "golden-edm",
        sourceRecordId: "EDM-123",
        sourceAsOfUtc: "2026-05-21T14:00:00Z",
        sourceUpdatedBy: "workflow.bot",
        sourceReason: "Trustee schedule matched.",
        streamVersion: 4,
        eventType: "SecurityAmended"
      }
    ]
  },
  openLotReadModel: {
    quantityModel: "FactorAdjustedFace",
    lotSize: 1,
    contractMultiplier: null,
    usesFaceValue: true,
    supportsFactorAdjustedExposure: true,
    requiresResolvedSecurityId: true,
    currentFactor: 0.9,
    currentFactorDate: "2026-11-15",
    asOfUtc: "2026-05-21T15:00:00Z",
    summary: "Open lots reconcile by factor-adjusted face for account scope Fund Alpha.",
    lots: [
      {
        securityId: "sec-1",
        portfolioId: "portfolio-alpha",
        runId: "run-42",
        accountScopeId: "acct-alpha",
        accountScopeDisplayName: "Fund Alpha - Main",
        vehicleScopeId: null,
        vehicleScopeDisplayName: null,
        lotId: "lot-1",
        symbol: "AAPL",
        tradeDate: "2026-04-20T14:30:00Z",
        settleDate: "2026-04-22T00:00:00Z",
        originalQuantity: 100000,
        currentQuantity: 95000,
        originalFace: 100000,
        currentFace: 95000,
        factorAdjustedQuantity: 85500,
        factorAdjustedFace: 85500,
        costBasis: 99000,
        entryPrice: 99,
        unrealizedPnl: 1250,
        currency: "USD",
        lotStatus: "Open",
        sourceSystem: "ledger",
        sourceRecordId: "LOT-1",
        asOfUtc: "2026-05-21T15:00:00Z",
        sourceUpdatedBy: "workflow.bot",
        sourceReason: "Latest paper ledger lot.",
        isLongTerm: false,
        notes: "Primary scoped lot."
      }
    ],
    provenanceHistory: [
      {
        provenanceId: "lot-source-1",
        runId: "run-42",
        portfolioId: "portfolio-alpha",
        accountScopeId: "acct-alpha",
        accountScopeDisplayName: "Fund Alpha - Main",
        sourceSystem: "ledger",
        sourceRecordId: "LOT-1",
        asOfUtc: "2026-05-21T15:00:00Z",
        summary: "Lot sourced from latest ledger read model."
      }
    ]
  }
};

const securityConflict: SecurityMasterConflict = {
  conflictId: "conflict-1",
  securityId: "sec-1",
  conflictKind: "IdentifierCollision",
  fieldPath: "identifiers.CUSIP",
  providerA: "Bloomberg",
  valueA: "sec-1",
  providerB: "Refinitiv",
  valueB: "sec-2",
  detectedAt: "2026-01-01T00:00:00Z",
  status: "Open"
};

const corporateActions: CorporateAction[] = [
  {
    corpActId: "ca-div-1",
    securityId: "sec-1",
    eventType: "Dividend",
    exDate: "2026-05-01T00:00:00Z",
    payDate: "2026-05-15T00:00:00Z",
    dividendPerShare: 0.24,
    currency: "USD",
    splitRatio: null,
    newSecurityId: null,
    distributionRatio: null,
    acquirerSecurityId: null,
    exchangeRatio: null,
    subscriptionPricePerShare: null,
    rightsPerShare: null
  },
  {
    corpActId: "ca-split-1",
    securityId: "sec-1",
    eventType: "StockSplit",
    exDate: "2026-06-01T00:00:00Z",
    payDate: null,
    dividendPerShare: null,
    currency: null,
    splitRatio: 4,
    newSecurityId: null,
    distributionRatio: null,
    acquirerSecurityId: null,
    exchangeRatio: null,
    subscriptionPricePerShare: null,
    rightsPerShare: null
  }
];

const trialBalanceLines: LedgerTrialBalanceLine[] = [
  {
    accountName: "Cash",
    accountType: "Asset",
    symbol: null,
    financialAccountId: "acct-cash",
    balance: 120500,
    entryCount: 12,
    security: null,
    sourceJournalEntryId: "je-cash-1",
    sourceEventIds: ["evt-cash-1"],
    approvalIds: ["approval-cash-1"]
  },
  {
    accountName: "Financing payable",
    accountType: "Liability",
    symbol: null,
    financialAccountId: "acct-financing",
    balance: -500,
    entryCount: 2,
    security: null
  }
];

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
    { lineId: "line-debit", side: "Debit", amount: 100, currency: "USD", accountPath: "Assets:Cash", securityId: "11111111-1111-1111-1111-111111111111", securityDisplayName: "Microsoft Corp.", description: "Security-linked cash debit" },
    { lineId: "line-credit", side: "Credit", amount: 100, currency: "USD", accountPath: "Income:Interest", securityId: null, description: "Interest income credit" }
  ],
  evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/manual-je-1"],
  evidenceAttachments: [
    {
      attachmentId: "source-doc-1",
      displayName: "Controller support package",
      evidenceKind: "SourceDocument",
      uri: "/api/workstation/evidence/subjects/accounting-record/manual-je-1",
      sourceSystem: "EvidenceVault",
      addedAtUtc: "2026-06-30T00:00:00Z",
      addedBy: "browser-user",
      lineId: "line-debit",
      description: null
    }
  ],
  validationIssues: [],
  totalDebits: 100,
  totalCredits: 100,
  imbalance: 0,
  approvalId: null,
  submittedAtUtc: null,
  submittedBy: null,
  entryType: "Distribution",
  treasuryContext: {
    effectiveDate: "2026-06-30",
    idempotencyKey: "browser:fund-alpha:distribution:manual-je-1",
    fundEventId: "fund-event:fund-alpha:distribution:20260630",
    fundEventType: "Distribution",
    capitalAccountId: "capital-account:fund-alpha:lp-1",
    investorId: "investor:lp-1",
    paymentIntentId: "payment:fund-alpha:distribution:manual-je-1",
    settlementReference: "settlement:fund-alpha:distribution:20260630"
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
    netCapitalActivity: -100,
    currency: "USD",
    fundEvents: [
      {
        fundEventId: "fund-event:fund-alpha:distribution:20260630",
        fundEventType: "Distribution",
        entryType: "Distribution",
        journalStatus: "Draft",
        journalEntryId: "manual-je-1",
        effectiveDate: "2026-06-30",
        capitalAccountId: "capital-account:fund-alpha:lp-1",
        investorId: "investor:lp-1",
        currency: "USD",
        grossAmount: 100,
        netCapitalActivity: -100,
        memo: "Manual close adjustment",
        paymentIntentId: "payment:fund-alpha:distribution:manual-je-1",
        settlementReference: "settlement:fund-alpha:distribution:20260630",
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
        contributions: 0,
        distributions: 100,
        subscriptions: 0,
        redemptions: 0,
        managementFees: 0,
        netActivity: -100,
        fundEventCount: 1,
        lastEffectiveDate: "2026-06-30",
        lastFundEventType: "Distribution",
        fundEventIds: ["fund-event:fund-alpha:distribution:20260630"]
      }
    ],
    capitalAccountSubledgerEntries: [
      {
        subledgerEntryId: "capital-account-subledger:fund-event:fund-alpha:distribution:20260630",
        capitalAccountId: "capital-account:fund-alpha:lp-1",
        investorId: "investor:lp-1",
        currency: "USD",
        fundEventId: "fund-event:fund-alpha:distribution:20260630",
        fundEventType: "Distribution",
        entryType: "Distribution",
        approvalState: "Draft",
        journalEntryId: "manual-je-1",
        effectiveDate: "2026-06-30",
        grossAmount: 100,
        netCapitalActivity: -100,
        runningNetActivity: -100,
        memo: "Manual close adjustment",
        evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/manual-je-1"],
        validationIssues: [],
        updatedAtUtc: "2026-06-30T00:00:00Z"
      }
    ],
    ledgerImpacts: [
      {
        ledgerImpactId: "ledger-impact:fund-event:fund-alpha:distribution:20260630:manual-je-1",
        journalEntryId: "manual-je-1",
        fundEventId: "fund-event:fund-alpha:distribution:20260630",
        fundEventType: "Distribution",
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
            accountPath: "Equity:Distributions",
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
            accountPath: "Assets:Cash",
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
        reportOutputId: "report-output:fund-event:fund-alpha:distribution:20260630:distributionnotice",
        reportOutputType: "DistributionNotice",
        displayName: "DistributionNotice for Distribution",
        reportRoute: "/api/ledger/private-capital/activity?fundProfileId=fund-alpha&fundEventId=fund-event%3Afund-alpha%3Adistribution%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1",
        reportOutputRoute: "/api/ledger/private-capital/report-output?fundProfileId=fund-alpha&reportOutputId=report-output%3Afund-event%3Afund-alpha%3Adistribution%3A20260630%3Adistributionnotice&fundEventId=fund-event%3Afund-alpha%3Adistribution%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1",
        fundEventId: "fund-event:fund-alpha:distribution:20260630",
        fundEventType: "Distribution",
        capitalAccountId: "capital-account:fund-alpha:lp-1",
        investorId: "investor:lp-1",
        approvalState: "Draft",
        effectiveDate: "2026-06-30",
        currency: "USD",
        netCapitalActivity: -100,
        evidenceLinkCount: 2,
        evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/manual-je-1"],
        isReportReady: false,
        reportWorkflowState: "Draft",
        reportLineProvenanceCount: 1,
        validationIssues: [
          {
            code: "manual-je.private-capital-report-approval-pending",
            severity: "Warning",
            message: "Approval is pending.",
            targetId: "fund-event:fund-alpha:distribution:20260630",
            suggestedAction: "Submit approval."
          }
        ]
      }
    ],
    fundEventRecords: [
      {
        fundEventRecordId: "fund-event-ledger-record:fund-event:fund-alpha:distribution:20260630",
        fundEventId: "fund-event:fund-alpha:distribution:20260630",
        fundEventType: "Distribution",
        capitalAccountId: "capital-account:fund-alpha:lp-1",
        investorId: "investor:lp-1",
        approvalState: "Draft",
        journalEntryId: "manual-je-1",
        effectiveDate: "2026-06-30",
        currency: "USD",
        grossAmount: 100,
        netCapitalActivity: -100,
        capitalAccountOpeningNetActivity: 0,
        capitalAccountEndingNetActivity: -100,
        memo: "Manual close adjustment",
        paymentIntentId: "payment:fund-alpha:distribution:manual-je-1",
        settlementReference: "settlement:fund-alpha:distribution:20260630",
        activityRoute: "/api/ledger/private-capital/activity?fundProfileId=fund-alpha&fundEventId=fund-event%3Afund-alpha%3Adistribution%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1",
        evidenceRoute: "/api/workstation/evidence/subjects/private-capital-fund-event/fund-event%3Afund-alpha%3Adistribution%3A20260630/packet",
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
        nextActionRoute: "/api/ledger/private-capital/activity?fundProfileId=fund-alpha&fundEventId=fund-event%3Afund-alpha%3Adistribution%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1",
        evidenceLinkCount: 2,
        capitalAccountSubledgerEntryCount: 1,
        ledgerImpactCount: 1,
        reportOutputCount: 1,
        validationIssueCount: 2,
        primaryReportOutputId: "report-output:fund-event:fund-alpha:distribution:20260630:distributionnotice",
        primaryReportOutputType: "DistributionNotice",
        primaryReportRoute: "/api/ledger/private-capital/report-output?fundProfileId=fund-alpha&reportOutputId=report-output%3Afund-event%3Afund-alpha%3Adistribution%3A20260630%3Adistributionnotice&fundEventId=fund-event%3Afund-alpha%3Adistribution%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1",
        reportWorkflowState: "Draft",
        publicationManifestId: null,
        retainedManifestPath: null,
        reportLineProvenanceCount: 1,
        evidenceLinks: [
          "/api/workstation/evidence/subjects/accounting-record/manual-je-1",
          "/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-distribution-manual-je-1/packet"
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
              "/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-distribution-manual-je-1/packet"
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
              "payment:fund-alpha:distribution:manual-je-1",
              "settlement:fund-alpha:distribution:20260630"
            ],
            requiredEvidence: ["Payment intent id", "Settlement reference"]
          },
          {
            categoryId: "cash-evidence",
            label: "Cash evidence",
            isReady: true,
            summary: "Payment intent payment:fund-alpha:distribution:manual-je-1 and settlement settlement:fund-alpha:distribution:20260630 have 1 retained cash evidence link(s); live execution remains deferred.",
            evidenceLinkCount: 1,
            evidenceLinks: ["/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-distribution-manual-je-1/packet"],
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
          paymentIntentId: "payment:fund-alpha:distribution:manual-je-1",
          settlementReference: "settlement:fund-alpha:distribution:20260630",
          status: "SettlementMatched",
          isReady: true,
          direction: "Outflow",
          amount: 100,
          currency: "USD",
          effectiveDate: "2026-06-30",
          summary: "Payment intent payment:fund-alpha:distribution:manual-je-1 and settlement settlement:fund-alpha:distribution:20260630 have 1 retained cash evidence link(s); live execution remains deferred.",
          cashEvidenceLinkCount: 1,
          cashEvidenceLinks: ["/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-distribution-manual-je-1/packet"],
          requiredEvidence: [],
          evidenceRoute: "/api/workstation/evidence/subjects/private-capital-fund-event/fund-event%3Afund-alpha%3Adistribution%3A20260630/packet"
        },
        fundEvent: {
          fundEventId: "fund-event:fund-alpha:distribution:20260630",
          fundEventType: "Distribution",
          entryType: "Distribution",
          journalStatus: "Draft",
          journalEntryId: "manual-je-1",
          effectiveDate: "2026-06-30",
          capitalAccountId: "capital-account:fund-alpha:lp-1",
          investorId: "investor:lp-1",
          currency: "USD",
          grossAmount: 100,
          netCapitalActivity: -100,
          memo: "Manual close adjustment",
          paymentIntentId: "payment:fund-alpha:distribution:manual-je-1",
          settlementReference: "settlement:fund-alpha:distribution:20260630",
          evidenceLinks: [
            "/api/workstation/evidence/subjects/accounting-record/manual-je-1",
            "/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-distribution-manual-je-1/packet"
          ],
          validationIssues: [],
          updatedAtUtc: "2026-06-30T00:00:00Z"
        },
        capitalAccountSubledgerEntries: [
          {
            subledgerEntryId: "capital-account-subledger:fund-event:fund-alpha:distribution:20260630",
            capitalAccountId: "capital-account:fund-alpha:lp-1",
            investorId: "investor:lp-1",
            currency: "USD",
            fundEventId: "fund-event:fund-alpha:distribution:20260630",
            fundEventType: "Distribution",
            entryType: "Distribution",
            approvalState: "Draft",
            journalEntryId: "manual-je-1",
            effectiveDate: "2026-06-30",
            grossAmount: 100,
            netCapitalActivity: -100,
            runningNetActivity: -100,
            memo: "Manual close adjustment",
            evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/manual-je-1"],
            validationIssues: [],
            updatedAtUtc: "2026-06-30T00:00:00Z"
          }
        ],
        ledgerImpacts: [
          {
            ledgerImpactId: "ledger-impact:fund-event:fund-alpha:distribution:20260630:manual-je-1",
            journalEntryId: "manual-je-1",
            fundEventId: "fund-event:fund-alpha:distribution:20260630",
            fundEventType: "Distribution",
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
                accountPath: "Equity:Distributions",
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
                accountPath: "Assets:Cash",
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
            reportOutputId: "report-output:fund-event:fund-alpha:distribution:20260630:distributionnotice",
            reportOutputType: "DistributionNotice",
            displayName: "DistributionNotice for Distribution",
            reportRoute: "/api/ledger/private-capital/activity?fundProfileId=fund-alpha&fundEventId=fund-event%3Afund-alpha%3Adistribution%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1",
            fundEventId: "fund-event:fund-alpha:distribution:20260630",
            fundEventType: "Distribution",
            capitalAccountId: "capital-account:fund-alpha:lp-1",
            investorId: "investor:lp-1",
            approvalState: "Draft",
            effectiveDate: "2026-06-30",
            currency: "USD",
            netCapitalActivity: -100,
            evidenceLinkCount: 1,
            evidenceLinks: ["/api/workstation/evidence/subjects/accounting-record/manual-je-1"],
            isReportReady: false,
            reportWorkflowState: "Draft",
            reportLineProvenanceCount: 1,
            validationIssues: [
              {
                code: "manual-je.private-capital-report-approval-pending",
                severity: "Warning",
                message: "Approval is pending.",
                targetId: "fund-event:fund-alpha:distribution:20260630",
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
            targetId: "fund-event:fund-alpha:distribution:20260630",
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
        contributions: 0,
        distributions: 100,
        subscriptions: 0,
        redemptions: 0,
        managementFees: 0,
        openingNetActivity: 0,
        endingNetActivity: -100,
        netCapitalActivity: -100,
        fundEventCount: 1,
        approvalQueueCount: 0,
        postedFundEventCount: 0,
        publishedReportOutputCount: 0,
        evidenceLinkCount: 1,
        validationIssueCount: 2,
        firstEffectiveDate: "2026-06-30",
        lastEffectiveDate: "2026-06-30",
        lastFundEventType: "Distribution",
        evidenceLinks: [
          "/api/workstation/evidence/subjects/accounting-record/manual-je-1",
          "/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-distribution-manual-je-1/packet"
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
              "/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-distribution-manual-je-1/packet"
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
              "payment:fund-alpha:distribution:manual-je-1",
              "settlement:fund-alpha:distribution:20260630"
            ],
            requiredEvidence: ["Payment intent id", "Settlement reference"]
          },
          {
            categoryId: "cash-evidence",
            label: "Cash evidence",
            isReady: true,
            summary: "1 payment intent(s), 1 settlement reference(s), and 1 retained cash evidence link(s) support this subledger; live execution remains deferred.",
            evidenceLinkCount: 1,
            evidenceLinks: ["/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-distribution-manual-je-1/packet"],
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
          paymentIntentId: "payment:fund-alpha:distribution:manual-je-1",
          settlementReference: "settlement:fund-alpha:distribution:20260630",
          status: "SettlementMatched",
          isReady: true,
          direction: "Outflow",
          amount: 100,
          currency: "USD",
          effectiveDate: "2026-06-30",
          summary: "1 payment intent(s), 1 settlement reference(s), and 1 retained cash evidence link(s) support this subledger; live execution remains deferred.",
          cashEvidenceLinkCount: 1,
          cashEvidenceLinks: ["/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-distribution-manual-je-1/packet"],
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
            targetId: "fund-event:fund-alpha:distribution:20260630",
            suggestedAction: "Submit approval."
          }
        ]
      }
    ],
    paymentIntents: [
      {
        paymentIntentId: "payment:fund-alpha:distribution:manual-je-1",
        settlementReference: "settlement:fund-alpha:distribution:20260630",
        fundProfileId: "fund-alpha",
        ledgerBookId: "book-alpha",
        fundEventId: "fund-event:fund-alpha:distribution:20260630",
        journalEntryId: "manual-je-1",
        requester: "ops-user",
        requestedAtUtc: "2026-06-30T00:00:00Z",
        status: "ApprovalPending",
        statusLabel: "Approval pending",
        readinessReason: "Requester and expected movement are captured, but controller approval is not complete.",
        executionDeferredReason: "Full payment execution is explicitly deferred in v0.18; this layer only retains intent, control, cash-evidence, reconciliation, and audit history before any bank-side instruction.",
        expectedCashMovement: {
          paymentIntentId: "payment:fund-alpha:distribution:manual-je-1",
          direction: "Outflow",
          amount: 100,
          currency: "USD",
          effectiveDate: "2026-06-30",
          settlementReference: "settlement:fund-alpha:distribution:20260630",
          fundEventId: "fund-event:fund-alpha:distribution:20260630",
          fundEventType: "Distribution",
          capitalAccountId: "capital-account:fund-alpha:lp-1",
          investorId: "investor:lp-1",
          purpose: "Distribution for Fund Alpha LP",
          payee: "investor:lp-1",
          accountScope: "fund:fund-alpha / book:book-alpha / capital-account:fund-alpha:lp-1 / investor:lp-1",
          businessPurpose: "Distribution for Fund Alpha LP",
          approvalPolicy: "Controller approval pending before execution-deferred reliance",
          sourceEvidenceLinks: [
            "/api/workstation/evidence/subjects/accounting-record/manual-je",
            "/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-distribution-manual-je-1/packet"
          ]
        },
        evidenceRoute: "/api/workstation/evidence/subjects/payment-intent/payment%3Afund-alpha%3Adistribution%3Amanual-je-1/packet",
        workbenchRoute: "/api/ledger/private-capital/activity?fundProfileId=fund-alpha&paymentIntentId=payment%3Afund-alpha%3Adistribution%3Amanual-je-1",
        approvalChain: [
          { sequence: 1, role: "Requester", actor: "ops-user", status: "Requested", decidedAtUtc: "2026-06-30T00:00:00Z", evidenceRoute: "/api/ledger/private-capital/activity?fundProfileId=fund-alpha" },
          { sequence: 2, role: "Controller approval", actor: "controller", status: "Pending", decidedAtUtc: null, evidenceRoute: null }
        ],
        bankEvidence: [
          {
            evidenceId: "retained-cash-evidence:distribution",
            evidenceKind: "RetainedCashEvidence",
            status: "Retained",
            summary: "Retained wire evidence supports the expected distribution cash movement.",
            amount: 100,
            currency: "USD",
            effectiveDate: "2026-06-30",
            recordedAtUtc: "2026-06-30T00:00:00Z",
            externalRef: "settlement:fund-alpha:distribution:20260630",
            recordedBy: "cash-ops@example.com",
            evidenceRoute: "/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-distribution-manual-je-1/packet"
          }
        ],
        reconciliationLinks: [
          {
            linkId: "reconciliation:distribution",
            status: "Ready",
            summary: "Cash evidence is linked to reconciliation review.",
            evidenceRoute: "/api/reconciliation/runs/distribution"
          }
        ],
        auditHistory: [
          {
            auditEventId: "payment-intent-requested:manual-je-1",
            recordedAtUtc: "2026-06-30T00:00:00Z",
            actor: "ops-user",
            action: "payment-intent.requested",
            summary: "Payment intent was requested.",
            evidenceLinks: ["/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-distribution-manual-je-1/packet"]
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

async function renderAccountingScreen(
  screenData: AccountingWorkspaceResponse = data,
  initialEntry = "/accounting"
) {
  const result = renderWithRouter(<AccountingScreen data={screenData} />, { initialEntries: [initialEntry] });
  await waitForAsyncEffects();
  return result;
}

describe("AccountingScreen", () => {
  it("renders actionable Accounting loading work while workstation payloads bootstrap", async () => {
    renderWithRouter(<AccountingScreen data={null} />, { initialEntries: ["/accounting/reconciliation"] });
    await waitForAsyncEffects();

    expect(screen.getByRole("status", { name: "Loading Accounting" })).toHaveTextContent(
      "Waiting for ledger, reconciliation, cash-flow, and Security Master summaries"
    );
    expect(screen.getByRole("group", { name: "Route /accounting/reconciliation" })).toBeInTheDocument();
    expect(screen.getByRole("group", { name: "Workstream Reconciliation" })).toBeInTheDocument();
    expect(screen.getByText("Ledger and reconciliation")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open Accounting operations continuity while Accounting loads" })).toHaveAttribute(
      "href",
      "/accounting/operations-continuity"
    );
    expect(screen.getByRole("link", { name: "Open Data provider posture while Accounting loads" })).toHaveAttribute(
      "href",
      "/data/providers"
    );
  });

  it("transitions from Accounting bootstrap loading to loaded content without changing hook order", async () => {
    const { rerender } = renderWithRouter(<AccountingScreen data={null} />, { initialEntries: ["/accounting"] });
    await waitForAsyncEffects();

    expect(screen.getByRole("status", { name: "Loading Accounting" })).toBeInTheDocument();

    rerender(
      <TestMemoryRouter initialEntries={["/accounting"]}>
        <AccountingScreen data={data} />
      </TestMemoryRouter>
    );
    await waitForAsyncEffects();

    expect(screen.getByRole("region", { name: "Accounting workbench context" })).toBeInTheDocument();
    expect(screen.getByText("Reconciliation queue")).toBeInTheDocument();
  });

  it("renders external GL evidence package posture from the reconciliation response", async () => {
    const provider: AccountingSystemProvider = {
      providerId: "quickbooks-fixture",
      displayName: "QuickBooks Fixture",
      state: "Available",
      requiresCredentials: false,
      supportsChartOfAccounts: true,
      supportsJournalEntries: true,
      supportsTrialBalance: true,
      supportsPosting: false,
      statusLabel: "Ready for read-only import",
      statusDetail: "Fixture provider ready.",
      evidenceKinds: ["QuickBooksTrialBalance"]
    };
    const importDetail: AccountingSystemImportDetail = {
      summary: {
        importId: "qbo-fixture-20260131",
        providerId: "quickbooks-fixture",
        providerDisplayName: "QuickBooks Fixture",
        fundProfileId: "default-fund",
        ledgerBookId: null,
        state: "Imported",
        periodStart: "2026-01-01",
        periodEnd: "2026-01-31",
        importedAtUtc: "2026-02-01T00:00:00Z",
        chartAccountCount: 1,
        journalEntryCount: 1,
        trialBalanceLineCount: 1,
        evidenceReferences: ["quickbooks-fixture:trial-balance"],
        warnings: []
      },
      chartAccounts: [],
      journalEntries: [],
      trialBalance: []
    };
    const reconciliation: AccountingSystemReconciliationSummary = {
      reconciliationId: "gl-recon-qbo-fixture-20260131",
      importId: "qbo-fixture-20260131",
      providerId: "quickbooks-fixture",
      fundProfileId: "default-fund",
      periodStart: "2026-01-01",
      periodEnd: "2026-01-31",
      generatedAtUtc: "2026-02-01T00:05:00Z",
      matchedCount: 0,
      breakCount: 1,
      totalExternalDebits: 100,
      totalExternalCredits: 0,
      totalMeridianDebits: 0,
      totalMeridianCredits: 0,
      postingEnabled: false,
      postingDisabledReason: "Posting/export remains disabled.",
      evidenceReferences: ["quickbooks-fixture:trial-balance"],
      evidencePackages: [
        {
          packageId: "gl-external-evidence:qbo-fixture-20260131",
          label: "External GL import evidence",
          status: "Ready",
          evidenceReferenceCount: 1,
          evidenceReferences: ["quickbooks-fixture:trial-balance"],
          requiredActions: []
        },
        {
          packageId: "gl-meridian-ledger-evidence:qbo-fixture-20260131",
          label: "Meridian ledger evidence",
          status: "Missing",
          evidenceReferenceCount: 0,
          evidenceReferences: [],
          requiredActions: ["Load Meridian ledger journal evidence for the fund, book, and period before close approval."]
        },
        {
          packageId: "gl-reconciliation-tie-out:qbo-fixture-20260131",
          label: "GL reconciliation tie-out",
          status: "ReviewRequired",
          evidenceReferenceCount: 1,
          evidenceReferences: ["quickbooks-fixture:trial-balance"],
          requiredActions: ["Resolve GL reconciliation breaks before approving close evidence."]
        }
      ],
      rows: [
        {
          rowId: "gl-recon-cash",
          accountCode: "Assets:Cash",
          accountName: "Cash",
          currency: "USD",
          status: "MissingMeridian",
          externalDebit: 100,
          externalCredit: 0,
          meridianDebit: 0,
          meridianCredit: 0,
          variance: 100,
          detail: "External GL evidence is absent from Meridian-owned ledger truth.",
          evidenceRef: "quickbooks-fixture:trial-balance:cash",
          externalEvidenceReferences: ["quickbooks-fixture:trial-balance:cash"],
          meridianEvidenceReferences: [],
          evidenceReferences: ["quickbooks-fixture:trial-balance:cash"]
        }
      ]
    };

    vi.mocked(api.getAccountingSystemProviders).mockResolvedValueOnce([provider]);
    vi.mocked(api.getLatestAccountingSystemImport).mockResolvedValueOnce(importDetail);
    vi.mocked(api.getLatestAccountingSystemReconciliation).mockResolvedValueOnce(reconciliation);

    await renderAccountingScreen(data, "/accounting");

    const packages = await screen.findByLabelText("External GL evidence packages");
    expect(packages).toHaveTextContent("External GL import evidence");
    expect(packages).toHaveTextContent("Ready");
    expect(packages).toHaveTextContent("Meridian ledger evidence");
    expect(packages).toHaveTextContent("Missing");
    expect(packages).toHaveTextContent("Load Meridian ledger journal evidence");
    expect(packages).toHaveTextContent("GL reconciliation tie-out");
    expect(packages).toHaveTextContent("Resolve GL reconciliation breaks before approving close evidence.");
  });

  it("renders reconciliation, cash-flow, and reporting summaries", async () => {
    await renderAccountingScreen();

    expect(screen.getByRole("region", { name: "Accounting workbench context" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Ledger Explorer" })).toBeInTheDocument();
    expect(screen.getByLabelText("Explorer scope")).toHaveTextContent("Accounting");
    expect(screen.getByLabelText("Explorer scope")).toHaveTextContent("Journal entries and ledger detail");
    expect(screen.getByLabelText("Saved explorer views")).toHaveTextContent("Controller review");
    expect(screen.getByLabelText("Applied explorer filters")).toHaveTextContent("All accounts");
    expect(screen.getByLabelText("Ledger Explorer proof actions")).toHaveTextContent("Evidence packet");
    expect(screen.getByText("Reconciliation queue")).toBeInTheDocument();
    const workflow = screen.getByRole("region", { name: "Accounting workflow launch paths" });
    expect(within(workflow).getByRole("link", { name: "Review ledger: Ledger authority, current Accounting workstream" })).toHaveAttribute(
      "href",
      "/accounting/ledger"
    );
    expect(within(workflow).getByRole("link", { name: "Review ledger: Ledger authority, current Accounting workstream" })).toHaveAttribute(
      "aria-current",
      "page"
    );
    expect(within(workflow).getByRole("link", { name: "Open Accounting journal entry workbench" })).toHaveAttribute(
      "href",
      "/accounting/journal-entries"
    );
    expect(within(workflow).getByRole("link", { name: "Open retained accounting record evidence" })).toHaveAttribute(
      "href",
      "/reporting/evidence"
    );
    expect(screen.getByRole("link", { name: "Open Accounting reconciliation workstream" })).toHaveAttribute(
      "href",
      "/accounting/reconciliation"
    );
    expect(screen.getByRole("table", { name: "Reconciliation runs" })).toHaveTextContent("Paper Index Mean Reversion");
    expect(screen.getByRole("row", { name: /Paper Index Mean Reversion.*BreaksOpen.*1 open/i })).not.toHaveAttribute(
      "aria-controls"
    );
    expect(screen.getByText("Reporting profiles")).toBeInTheDocument();
    expect(screen.getByText("Cash-flow coverage is available for 4 runs; 1 run needs variance review.")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Cash-flow evidence for Ledger context at /accounting" })).toBeInTheDocument();
    expect(screen.getByLabelText("Cash-flow status Variance review. Net variance $500.")).toHaveTextContent("Variance review");
    expect(screen.getByLabelText("Runs with variance: 1")).toHaveTextContent("1");
    expect(screen.getAllByText("Paper Index Mean Reversion").length).toBeGreaterThanOrEqual(1);
  });

  it("renders the manual journal entry workbench with GL and Security Master line fields", async () => {
    vi.mocked(api.getManualJournalEntryWorkbench).mockResolvedValueOnce(manualJournalWorkbench);

    await renderAccountingScreen(data, "/accounting/journal-entries");

    expect(screen.getByRole("region", { name: "Accounting workbench context" })).toHaveTextContent("Journal entry workbench");
    expect(screen.getByRole("heading", { name: "Manual journal entry workbench" })).toBeInTheDocument();
    expect(screen.getByDisplayValue("Manual close adjustment")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Microsoft Corp./ })).toBeInTheDocument();
    expect(screen.getByText("Controller support package")).toBeInTheDocument();
    expect(screen.getByText("Private-capital activity")).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Private-capital fund event ledger records" })).toHaveTextContent("Distribution");
    expect(screen.getByRole("table", { name: "Private-capital fund event ledger records" })).toHaveTextContent("Manual close adjustment");
    expect(screen.getByRole("table", { name: "Private-capital fund event ledger records" })).toHaveTextContent("payment:fund-alpha:distribution:manual-je-1");
    expect(screen.getByRole("table", { name: "Private-capital fund event ledger records" })).toHaveTextContent("$100 USD gross");
    expect(screen.getByRole("table", { name: "Private-capital fund event ledger records" })).toHaveTextContent("$0 USD opening -> -$100.00 USD ending");
    expect(screen.getByRole("table", { name: "Private-capital fund event ledger records" })).toHaveTextContent("2 record issue");
    expect(screen.getByRole("table", { name: "Private-capital fund event ledger records" })).toHaveTextContent("Approval pending");
    expect(screen.getByRole("table", { name: "Private-capital fund event ledger records" })).toHaveTextContent("Submit the fund-event journal for approval");
    expect(screen.getByRole("table", { name: "Private-capital fund event ledger records" })).toHaveTextContent("1 report output");
    expect(screen.getByRole("table", { name: "Private-capital fund event ledger records" })).toHaveTextContent("DistributionNotice / Draft / 1 provenance");
    expect(screen.getByRole("table", { name: "Private-capital fund event ledger records" })).toHaveTextContent("fund-event%3Afund-alpha%3Adistribution%3A20260630");
    expect(screen.getByRole("table", { name: "Payment intent and cash evidence workflows" })).toHaveTextContent("payment:fund-alpha:distribution:manual-je-1");
    expect(screen.getByRole("table", { name: "Payment intent and cash evidence workflows" })).toHaveTextContent("Approval pending");
    expect(screen.getByRole("table", { name: "Payment intent and cash evidence workflows" })).toHaveTextContent("Outflow / $100 USD / 2026-06-30 / settlement:fund-alpha:distribution:20260630");
    expect(screen.getByRole("table", { name: "Payment intent and cash evidence workflows" })).toHaveTextContent("payee investor:lp-1");
    expect(screen.getByRole("table", { name: "Payment intent and cash evidence workflows" })).toHaveTextContent("2 source evidence link(s)");
    expect(screen.getByRole("table", { name: "Payment intent and cash evidence workflows" })).toHaveTextContent("0/2 approved");
    expect(screen.getByRole("table", { name: "Payment intent and cash evidence workflows" })).toHaveTextContent("0 confirmed / 1 retained / 0 returned");
    expect(screen.getByRole("table", { name: "Payment intent and cash evidence workflows" })).toHaveTextContent("Full payment execution is explicitly deferred");
    expect(screen.getByRole("link", { name: "Open payment intent evidence packet for payment:fund-alpha:distribution:manual-je-1" })).toHaveAttribute(
      "href",
      "/api/workstation/evidence/subjects/payment-intent/payment%3Afund-alpha%3Adistribution%3Amanual-je-1/packet"
    );
    const paymentIntentDrilldown = screen.getByRole("region", {
      name: "Cash evidence drilldown for payment:fund-alpha:distribution:manual-je-1"
    });
    expect(paymentIntentDrilldown).toHaveTextContent("Approval chain");
    expect(paymentIntentDrilldown).toHaveTextContent("Controller approval / controller");
    expect(paymentIntentDrilldown).toHaveTextContent("Decision pending");
    expect(paymentIntentDrilldown).toHaveTextContent("Bank evidence");
    expect(paymentIntentDrilldown).toHaveTextContent("Retained wire evidence supports the expected distribution cash movement.");
    expect(paymentIntentDrilldown).toHaveTextContent("$100 USD / 2026-06-30");
    expect(paymentIntentDrilldown).toHaveTextContent("Recorded by cash-ops@example.com");
    expect(paymentIntentDrilldown).toHaveTextContent("Reconciliation");
    expect(paymentIntentDrilldown).toHaveTextContent("Cash evidence is linked to reconciliation review.");
    expect(paymentIntentDrilldown).toHaveTextContent("Audit trail");
    expect(paymentIntentDrilldown).toHaveTextContent("payment-intent.execution-deferred");
    expect(screen.getByRole("link", {
      name: "Open bank evidence for payment:fund-alpha:distribution:manual-je-1 RetainedCashEvidence"
    })).toHaveAttribute(
      "href",
      "/api/workstation/evidence/subjects/cash-evidence/payment-fund-alpha-distribution-manual-je-1/packet"
    );
    expect(screen.getByRole("link", {
      name: "Open reconciliation evidence for payment:fund-alpha:distribution:manual-je-1 reconciliation:distribution"
    })).toHaveAttribute("href", "/api/reconciliation/runs/distribution");
    expect(screen.getByRole("table", { name: "Private-capital fund event ledger records" })).toHaveTextContent("Settlement matched / Outflow / $100 USD / 1 cash evidence / settlement linked");
    expect(screen.getByRole("table", { name: "Private-capital fund event ledger records" })).toHaveTextContent("live execution remains deferred");
    expect(screen.getByRole("table", { name: "Private-capital fund event ledger records" })).toHaveTextContent("4/7 evidence categories ready");
    expect(screen.getByRole("table", { name: "Private-capital fund event ledger records" })).toHaveTextContent("Source support");
    expect(screen.getByRole("table", { name: "Private-capital fund event ledger records" })).toHaveTextContent("Approval state");
    expect(screen.getByRole("table", { name: "Private-capital fund event ledger records" })).toHaveTextContent("Approval reference is missing for the fund event.");
    expect(screen.getByRole("table", { name: "Private-capital fund event ledger records" })).toHaveTextContent("Governed report output");
    expect(screen.getByRole("link", { name: "Open private-capital activity record for Distribution" })).toHaveAttribute(
      "href",
      "/api/ledger/private-capital/activity?fundProfileId=fund-alpha&fundEventId=fund-event%3Afund-alpha%3Adistribution%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1"
    );
    expect(screen.getByRole("link", { name: "Open fund event command center for Distribution" })).toHaveAttribute(
      "href",
      "/api/ledger/private-capital/fund-event-command-center?fundProfileId=fund-alpha&fundEventId=fund-event%3Afund-alpha%3Adistribution%3A20260630"
    );
    expect(screen.getByRole("link", { name: "Open next action for Distribution" })).toHaveAttribute(
      "href",
      "/api/ledger/private-capital/activity?fundProfileId=fund-alpha&fundEventId=fund-event%3Afund-alpha%3Adistribution%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1"
    );
    expect(screen.getByRole("link", { name: "Open evidence packet for Distribution" })).toHaveAttribute(
      "href",
      "/api/workstation/evidence/subjects/private-capital-fund-event/fund-event%3Afund-alpha%3Adistribution%3A20260630/packet"
    );
    expect(screen.queryByRole("link", { name: "Open approval route for Distribution" })).not.toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Private-capital capital account activity" })).toHaveTextContent("capital-account:fund-alpha:lp-1");
    expect(screen.getByRole("table", { name: "Private-capital capital account subledgers" })).toHaveTextContent("capital-account:fund-alpha:lp-1");
    expect(screen.getByRole("table", { name: "Private-capital capital account subledgers" })).toHaveTextContent("Review");
    expect(screen.getByRole("table", { name: "Private-capital capital account subledgers" })).toHaveTextContent("$0 USD opening");
    expect(screen.getByRole("table", { name: "Private-capital capital account subledgers" })).toHaveTextContent("-$100.00 USD net");
    expect(screen.getByRole("table", { name: "Private-capital capital account subledgers" })).toHaveTextContent("1 fund event");
    expect(screen.getByRole("table", { name: "Private-capital capital account subledgers" })).toHaveTextContent("0 published report output");
    expect(screen.getByRole("table", { name: "Private-capital capital account subledgers" })).toHaveTextContent("Settlement matched / Outflow / $100 USD / 1 cash evidence / settlement linked");
    expect(screen.getByRole("table", { name: "Private-capital capital account subledgers" })).toHaveTextContent("4/7 evidence categories ready");
    expect(screen.getByRole("link", { name: "Open capital-account subledger for capital-account:fund-alpha:lp-1" })).toHaveAttribute(
      "href",
      "/api/ledger/private-capital/capital-account-subledger?fundProfileId=fund-alpha&ledgerBookId=book-alpha&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1&currency=USD"
    );
    expect(screen.getByRole("table", { name: "Private-capital capital account subledger" })).toHaveTextContent("Distribution");
    expect(screen.getByRole("table", { name: "Private-capital capital account subledger" })).toHaveTextContent("-$100.00 USD");
    expect(screen.getByRole("table", { name: "Private-capital ledger impacts" })).toHaveTextContent("Distribution");
    expect(screen.getByRole("table", { name: "Private-capital report outputs" })).toHaveTextContent("DistributionNotice for Distribution");
    expect(screen.getByRole("table", { name: "Private-capital report outputs" })).toHaveTextContent("Draft");
    expect(screen.getByRole("table", { name: "Private-capital report outputs" })).toHaveTextContent("1 provenance line");
    expect(screen.getAllByText("-$100.00 USD").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Distribution").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Security").length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: "Save draft" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Submit approval" })).toBeEnabled();
    expect(api.getManualJournalEntryWorkbench).toHaveBeenCalled();
  });

  it("renders the Capital Account Workbench route from the shared endpoint", async () => {
    const workbench: CapitalAccountWorkbench = {
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-alpha",
      projectedAtUtc: "2026-06-30T17:00:00Z",
      capitalAccountId: "capital-account:fund-alpha:lp-1",
      investorId: "investor:lp-1",
      currency: "USD",
      workbenchRoute: "/api/ledger/private-capital/capital-account-workbench?capitalAccountId=capital-account%3Afund-alpha%3Alp-1",
      statusLabel: "Restated lineage",
      statusReason: "Statement lineage includes retained restatement metadata and audit evidence.",
      investorAccountCount: 1,
      fundEventCount: 1,
      statementCount: 1,
      restatementLineageCount: 1,
      auditDrillThroughCount: 1,
      netCapitalActivity: 100,
      investorAccounts: [{
        accountKey: "capital-account:fund-alpha:lp-1|investor:lp-1|USD",
        capitalAccountId: "capital-account:fund-alpha:lp-1",
        investorId: "investor:lp-1",
        currency: "USD",
        activityRoute: "/api/ledger/private-capital/capital-account-subledger?capitalAccountId=capital-account%3Afund-alpha%3Alp-1",
        readiness: "Ready",
        readinessLabel: "Ready",
        readinessReason: "Retained evidence is available.",
        nextAction: "Open statement",
        nextActionRoute: "/api/ledger/private-capital/report-output?reportOutputId=report-output-1",
        openingNetActivity: 0,
        endingNetActivity: 100,
        netCapitalActivity: 100,
        contributions: 100,
        distributions: 0,
        subscriptions: 0,
        redemptions: 0,
        managementFees: 0,
        fundEventCount: 1,
        postedFundEventCount: 1,
        approvalQueueCount: 0,
        publishedReportOutputCount: 1,
        evidenceLinkCount: 1,
        validationIssueCount: 0,
        evidenceCategorySummary: "1/1 allocation evidence categories ready.",
        evidenceLinks: ["/evidence/source"],
        evidenceCategories: [],
        fundEventRecords: [{ effectiveDate: "2026-06-30" } as never],
        subledgerEntries: [],
        ledgerImpacts: [],
        reportOutputs: [],
        validationIssues: [],
        paymentIntentEvidence: {
          paymentIntentId: "payment-1",
          settlementReference: "settlement-1",
          status: "SettlementMatched",
          isReady: true,
          direction: "Inflow",
          amount: 100,
          currency: "USD",
          effectiveDate: "2026-06-30",
          summary: "Cash evidence matched.",
          cashEvidenceLinkCount: 1,
          cashEvidenceLinks: ["/evidence/source"],
          requiredEvidence: [],
          evidenceRoute: "/evidence/payment-1"
        }
      }],
      allocationRules: [{
        ruleId: "rule-source",
        capitalAccountId: "capital-account:fund-alpha:lp-1",
        investorId: "investor:lp-1",
        categoryId: "source-support",
        label: "Source support",
        basis: "Fund event source support must be retained.",
        isSatisfied: true,
        reason: "Source support is retained.",
        route: "/evidence/source",
        evidenceLinkCount: 1,
        evidenceLinks: ["/evidence/source"],
        requiredEvidence: ["Source document"],
        ruleVersion: "source-support:projection:1.1.1.1",
        effectiveFrom: "2026-06-30",
        effectiveTo: "2026-06-30",
        formula: "fund_event.source_evidence_count > 0",
        approvalState: "Approved",
        approvalReference: "approval-lp-1 / /accounting/approvals?approvalId=approval-lp-1",
        replayTrace: "Trace uses 1 fund event(s), 1 subledger entry(ies), 1 ledger impact(s), 1 report output(s), and 1 allocation input(s).",
        inputs: [{
          inputId: "allocation-input:fund-event:fund-event:fund-alpha:capital-call",
          kind: "fund-event",
          sourceId: "fund-event:fund-alpha:capital-call",
          label: "CapitalCall / Capital call",
          amount: 100,
          currency: "USD",
          effectiveDate: "2026-06-30",
          evidenceRoute: "/evidence/source"
        }],
        relatedFundEventIds: ["fund-event:fund-alpha:capital-call"]
      }],
      statementLineage: [{
        lineageId: "lineage-1",
        capitalAccountId: "capital-account:fund-alpha:lp-1",
        investorId: "investor:lp-1",
        reportOutputId: "report-output-1",
        reportOutputType: "CapitalAccountStatement",
        displayName: "LP 1 Statement",
        reportRoute: "/reporting/report-packs/lp-1",
        reportPackId: "report-pack-1",
        reportWorkflowState: "Restated",
        isPublished: true,
        isReportReady: true,
        publicationManifestId: "manifest-1",
        retainedManifestPath: "/evidence/manifest.json",
        publicationEvidenceHash: "hash-1",
        publishedAtUtc: "2026-06-30T17:00:00Z",
        publishedBy: "publisher",
        reportLineProvenanceCount: 2,
        hasRestatementLineage: true,
        restatementStatus: "Restatement lineage retained.",
        restatementReasonCode: "capital-account-correction",
        restatementPriorVersionReportId: "prior-report",
        restatementApprover: "audit-partner",
        restatementChangedLineCount: 1,
        restatementEvidenceLinkCount: 1,
        reportOutputRoute: "/api/ledger/private-capital/report-output?reportOutputId=report-output-1",
        evidenceRoute: "/evidence/report",
        capitalAccountSubledgerRoute: "/api/ledger/private-capital/capital-account-subledger?capitalAccountId=capital-account%3Afund-alpha%3Alp-1",
        evidenceLinks: ["/evidence/report"],
        restatementEvidenceLinks: ["/evidence/restatement"],
        restatementChangedLines: [{
          lineKey: "capital-account-ending",
          previousValue: "100.00",
          currentValue: "125.00",
          evidenceLinkCount: 1,
          evidenceLinks: ["/evidence/restatement"]
        }]
      }],
      auditDrillThroughs: [{
        drillThroughId: "drill-subledger",
        kind: "subledger",
        label: "LP 1 subledger",
        summary: "Open capital-account subledger.",
        route: "/api/ledger/private-capital/capital-account-subledger?capitalAccountId=capital-account%3Afund-alpha%3Alp-1",
        isAvailable: true,
        evidenceLinkCount: 1,
        evidenceLinks: ["/evidence/source"],
        relatedIds: ["fund-event-1"]
      }],
      validationIssues: [],
      liveCapabilities: ["Investor-level capital account evidence"],
      plannedCapabilities: ["Full cap-table administration"]
    };
    vi.mocked(api.getCapitalAccountWorkbench).mockResolvedValueOnce(workbench);

    await renderAccountingScreen(data, "/accounting/capital-accounts?capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1");

    await waitFor(() => expect(api.getCapitalAccountWorkbench).toHaveBeenCalledWith(expect.objectContaining({
      capitalAccountId: "capital-account:fund-alpha:lp-1",
      investorId: "investor:lp-1"
    })));
    const region = screen.getByRole("region", { name: "Capital Account Workbench" });
    expect(region).toHaveTextContent("Investor capital accounts");
    expect(region).toHaveTextContent("Source support");
    expect(region).toHaveTextContent("source-support:projection:1.1.1.1");
    expect(region).toHaveTextContent("fund_event.source_evidence_count > 0");
    expect(region).toHaveTextContent("Trace uses 1 fund event(s)");
    expect(region).toHaveTextContent("Statement lineage");
    expect(region).toHaveTextContent("capital-account-ending");
    expect(region).toHaveTextContent("100.00 -> 125.00");
    expect(region).toHaveTextContent("1 changed-line evidence");
    expect(region).toHaveTextContent("Audit drill-through");
    expect(region).toHaveTextContent("Live in v0.18 slice");
    expect(region).toHaveTextContent("Still planned");
    expect(region).toHaveTextContent("Full cap-table administration");
  });

  it("selects Security Master results and adds source evidence on the manual journal entry workbench", async () => {
    const user = userEvent.setup();
    vi.mocked(api.getManualJournalEntryWorkbench).mockResolvedValueOnce({
      ...manualJournalWorkbench,
      drafts: [{
        ...manualJournalDraft,
        lines: manualJournalDraft.lines.map((line) => line.lineId === "line-debit" ? { ...line, securityId: null, securityDisplayName: null } : line),
        evidenceLinks: [],
        evidenceAttachments: []
      }]
    });

    await renderAccountingScreen(data, "/accounting/journal-entries");

    await user.type(screen.getByPlaceholderText("Ticker, ISIN, CUSIP, FIGI, name"), "AAPL");
    await user.click(screen.getByRole("button", { name: "Search Security Master" }));
    await user.click(await screen.findByRole("button", { name: /Apple Inc./ }));
    expect(screen.getByRole("button", { name: /Apple Inc./ })).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Label"), { target: { value: "Trade blotter" } });
    fireEvent.change(screen.getByLabelText("Route or path"), {
      target: { value: "/api/workstation/evidence/subjects/accounting-record/trade-blotter" }
    });
    await user.click(screen.getByRole("button", { name: "Attach" }));

    expect(screen.getByText("Trade blotter")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Submit approval" })).toBeEnabled();
  });

  it("renders approvals as a dedicated workstream and posts approval decisions", async () => {
    const user = userEvent.setup();
    vi.mocked(api.getOperationsContinuityWorkflows).mockResolvedValue([approvalWorkflowSummary]);
    vi.mocked(api.getOperationsContinuityWorkflow).mockResolvedValue(approvalWorkflowDetail);
    vi.mocked(api.approveOperationsContinuityWorkflow).mockResolvedValue({
      success: true,
      workflow: approvalWorkflowDetail,
      blockers: [],
      message: null
    });

    await renderAccountingScreen(data, "/accounting/approvals?approvalId=approval-close-1");

    expect(await screen.findByRole("heading", { name: "Approval queue and audit gate" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Approval gate", level: 2 })).toBeInTheDocument();
    expect(screen.queryByRole("table", { name: "Primary trial balance lines for run-42" })).not.toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Accounting approval queue" })).toHaveTextContent("2026-05");
    expect(screen.getByRole("region", { name: "Selected approval detail" })).toHaveTextContent("approval-close-1");
    expect(screen.getByRole("region", { name: "Selected approval detail" })).toHaveTextContent("ops.controller");
    expect(screen.getByRole("region", { name: "Approval audit trail" })).toHaveTextContent("Submitted for controller sign-off.");

    await user.click(screen.getByRole("button", { name: "Approve" }));

    expect(api.approveOperationsContinuityWorkflow).toHaveBeenCalledWith("workflow-approval-1", expect.objectContaining({
      expectedVersion: 7,
      reportPackId: "report-pack-2026-05",
      rationale: "Approved from Accounting approvals workstream."
    }));
  });

  it("renders reconciliation strong panels with view-model presentation state", async () => {
    await renderAccountingScreen(data, "/accounting/reconciliation");

    expect(screen.getAllByRole("table", { name: "Reconciliation runs" })).toHaveLength(1);
    expect(screen.queryByRole("link", { name: "Open Accounting reconciliation workstream" })).not.toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Reconciliation detail for Paper Index Mean Reversion" })).toBeInTheDocument();
    const selectedRun = screen.getByRole("row", { name: "Inspect reconciliation run Paper Index Mean Reversion" });
    expect(selectedRun).toHaveAttribute("aria-selected", "true");
    expect(selectedRun).toHaveAttribute("aria-expanded", "true");
    expect(selectedRun).toHaveAttribute("aria-controls", "reconciliation-run-detail-panel");
    expect(screen.getByRole("table", { name: "Reconciliation runs" })).toBeInTheDocument();
    expect(screen.getByLabelText("Open breaks: 1")).toHaveTextContent("1");
    expect(screen.getByLabelText("Reconciliation narrative for Paper Index Mean Reversion")).toHaveTextContent(
      "Open reconciliation breaks remain on this run."
    );
  });

  it("renders operational exception workbench with queue, comment, audit, and workflow handoffs", async () => {
    vi.mocked(api.getReconciliationBreakQueue).mockResolvedValueOnce(data.breakQueue);

    await renderAccountingScreen(data, "/accounting/exceptions");

    expect(screen.getAllByRole("heading", { name: "Operational exception workbench" }).length).toBeGreaterThan(0);
    expect(screen.getByRole("region", { name: "Unified operational exception queue" })).toHaveTextContent("Paper Index Mean Reversion / AmountMismatch");
    expect(screen.getByRole("region", { name: "Unified operational exception queue" })).toHaveTextContent("2 comments");
    expect(screen.getByRole("region", { name: "Unified operational exception queue" })).toHaveTextContent("3 evidence links");
    expect(screen.getAllByRole("link", { name: "Approval gate" })[0]).toHaveAttribute("href", "/accounting/approvals");
    expect(screen.getByRole("link", { name: "Open exception evidence packet" })).toHaveAttribute(
      "href",
      "/reporting/evidence?subjectKind=accounting-exceptions&subjectId=active"
    );
    expect(screen.getByRole("table", { name: "Reconciliation break queue" })).toBeInTheDocument();
  });

  it("renders calibration tolerance profiles as selectable row-detail evidence", async () => {
    const user = userEvent.setup();
    vi.mocked(api.getReconciliationCalibrationSummary).mockResolvedValueOnce(calibrationSummary);

    await renderAccountingScreen(data, "/accounting/reconciliation");

    const table = await screen.findByRole("table", { name: "Tolerance profile health by reconciliation route" });
    expect(table).toHaveTextContent("tp-cash-variance");
    const firstProfile = screen.getByRole("row", {
      name: "Inspect tolerance profile tp-cash-variance: Operator review required"
    });
    expect(firstProfile).toHaveAttribute("aria-selected", "true");
    expect(firstProfile).toHaveAttribute("aria-expanded", "true");
    expect(firstProfile).toHaveAttribute("aria-controls", "calibration-profile-detail-panel");
    expect(screen.getByRole("region", { name: "Tolerance profile detail for tp-cash-variance" })).toHaveTextContent(
      "Selected tolerance profile - tp-cash-variance"
    );

    const nextProfile = screen.getByRole("row", {
      name: "Inspect tolerance profile tp-settlement-lag: Within tolerance"
    });
    await user.click(nextProfile);

    expect(firstProfile).not.toHaveAttribute("aria-selected");
    expect(nextProfile).toHaveAttribute("aria-selected", "true");
    expect(nextProfile).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("region", { name: "Tolerance profile detail for tp-settlement-lag" })).toHaveTextContent(
      "Policy default"
    );
  });

  it("supports keyboard selection for calibration tolerance profiles", async () => {
    const user = userEvent.setup();
    vi.mocked(api.getReconciliationCalibrationSummary).mockResolvedValueOnce(calibrationSummary);

    await renderAccountingScreen(data, "/accounting/reconciliation");

    const nextProfile = await screen.findByRole("row", {
      name: "Inspect tolerance profile tp-settlement-lag: Within tolerance"
    });
    nextProfile.focus();
    await user.keyboard(" ");

    expect(nextProfile).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("region", { name: "Tolerance profile detail for tp-settlement-lag" })).toBeInTheDocument();
  });

  it("recovers calibration summary failures through the visible retry command", async () => {
    const user = userEvent.setup();
    vi.mocked(api.getReconciliationCalibrationSummary)
      .mockRejectedValueOnce(new ApiError({
        path: "/api/reconciliation/calibration-summary",
        status: 503,
        title: "Provider unavailable",
        detail: "Calibration API offline"
      }))
      .mockResolvedValueOnce(calibrationSummary);

    await renderAccountingScreen(data, "/accounting/reconciliation");

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Calibration API offline");
    expect(alert).toHaveTextContent("Endpoint returned 503 for /api/reconciliation/calibration-summary.");
    expect(alert).toHaveTextContent("Provider unavailable");
    const retry = screen.getByRole("button", { name: "Retry calibration summary load" });

    await user.click(retry);

    expect(await screen.findByRole("table", { name: "Tolerance profile health by reconciliation route" })).toHaveTextContent(
      "tp-cash-variance"
    );
    expect(screen.getByRole("button", { name: "Refresh calibration summary" })).toBeEnabled();
  });

  it("renders Accounting statement runs with endpoint counts and detail tabs", async () => {
    vi.mocked(api.getReconciliationStatementRuns).mockResolvedValueOnce([
      {
        runId: "run-42",
        importId: "import-42",
        startedAtUtc: "2026-05-01T00:00:00Z",
        completedAtUtc: "2026-05-01T00:03:00Z",
        positionMatches: 8,
        cashMatches: 3,
        transactionMatches: 13,
        openExceptionCount: 2,
        brokerCustodian: "Northern Trust",
        account: "Fund A - Prime",
        period: "2026-04",
        status: "ReviewRequired",
        validationIssueCount: 4,
        breakCount: 2,
        caseCount: 1,
        importedAtUtc: "2026-05-01T00:04:00Z"
      }
    ]);

    await renderAccountingScreen(data, "/accounting/reconciliation");

    const table = await screen.findByRole("table", { name: "Accounting statement runs" });
    expect(table).toHaveTextContent("Northern Trust");
    expect(table).toHaveTextContent("Fund A - Prime");
    expect(table).toHaveTextContent("2026-04");
    expect(table).toHaveTextContent("ReviewRequired");
    expect(table).toHaveTextContent("24");
    expect(screen.getByRole("tab", { name: /Overview tab for statement run run-42/ })).toBeEnabled();
    expect(screen.getByRole("tab", { name: /Breaks & Cases tab for statement run run-42/ })).toHaveTextContent("2");
    expect(screen.getByText(/Matching, tolerance, validation, and case-state decisions remain in the shared reconciliation services/)).toBeInTheDocument();
  });

  it("updates reconciliation detail queue selection with accessible expanded state", async () => {
    const user = userEvent.setup();
    vi.mocked(api.getReconciliationBreakQueue).mockResolvedValueOnce(data.breakQueue);

    await renderAccountingScreen(data, "/accounting/reconciliation");

    expect(screen.getByRole("link", { name: "Open routing target for reconciliation break run-42:cash" })).toHaveAttribute("href", "/accounting/ledger");
    expect(screen.getByText("Review cash ledger entries before resolving.")).toBeInTheDocument();

    const nextRun = screen.getByRole("row", { name: "Inspect reconciliation run Intraday Vol Carry" });
    expect(nextRun).not.toHaveAttribute("aria-selected");
    expect(nextRun).toHaveAttribute("aria-expanded", "false");

    await user.click(nextRun);

    expect(nextRun).toHaveAttribute("aria-selected", "true");
    expect(nextRun).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("region", { name: "Reconciliation detail for Intraday Vol Carry" })).toBeInTheDocument();
  });

  it("supports keyboard selection from the reconciliation detail queue table", async () => {
    const user = userEvent.setup();
    await renderAccountingScreen(data, "/accounting/reconciliation");

    const nextRun = screen.getByRole("row", { name: "Inspect reconciliation run Intraday Vol Carry" });
    nextRun.focus();

    await user.keyboard("{Enter}");

    expect(nextRun).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("region", { name: "Reconciliation detail for Intraday Vol Carry" })).toBeInTheDocument();
  });

  it("renders reconciliation detail queue empty state when no runs are available", async () => {
    await renderAccountingScreen({ ...data, reconciliationQueue: [] }, "/accounting/reconciliation");

    expect(screen.getByText("No reconciliation runs are available for this accounting scope.")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "No reconciliation run selected" })).toHaveTextContent(
      "Reconciliation evidence is unavailable until the workspace payload includes at least one run."
    );
  });

  it("renders trial-balance rows with accessible table evidence", async () => {
    const user = userEvent.setup();
    vi.mocked(api.getRunTrialBalance).mockResolvedValueOnce(trialBalanceLines);

    await renderAccountingScreen(data, "/accounting");

    const table = await screen.findByRole("table", { name: "Primary trial balance lines for run-42" });
    expect(table).toBeInTheDocument();
    const cashRow = screen.getByRole("row", { name: "Inspect trial-balance account Cash for Asset" });
    const financingRow = screen.getByRole("row", { name: "Inspect trial-balance account Financing payable for Liability" });
    expect(cashRow).toHaveAttribute("aria-selected", "true");
    expect(cashRow).toHaveAttribute("aria-expanded", "true");
    expect(cashRow).toHaveAttribute("aria-controls", "trial-balance-account-detail");
    expect(screen.getByRole("region", { name: "Trial-balance detail for Cash" })).toHaveTextContent("$120,500");
    expect(screen.getByLabelText("Filter by General Ledger account")).toHaveValue("");
    expect(screen.getAllByText("2 GL account rows").length).toBeGreaterThanOrEqual(1);
    expect(screen.getByRole("list", { name: "Ledger lines for selected account" })).toHaveTextContent("je-cash-1");
    expect(screen.getByRole("link", { name: "Open source event evt-cash-1 for Cash" })).toHaveAttribute(
      "href",
      "/accounting/audit?sourceEventId=evt-cash-1"
    );
    expect(screen.getByRole("link", { name: "Open journal entry je-cash-1 for Cash" })).toHaveAttribute(
      "href",
      "/accounting/ledger?journalEntryId=je-cash-1"
    );
    expect(screen.getByRole("link", { name: "Open approval approval-cash-1 for Cash" })).toHaveAttribute(
      "href",
      "/accounting/approvals?approvalId=approval-cash-1"
    );
    expect(financingRow).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByText("-$500")).toHaveClass("text-danger");

    await user.type(screen.getByLabelText("Filter by General Ledger account"), "financing");

    expect(screen.getAllByText("1 of 2 GL account rows").length).toBeGreaterThanOrEqual(1);
    expect(screen.queryByRole("row", { name: "Inspect trial-balance account Cash for Asset" })).not.toBeInTheDocument();
    const filteredFinancingRow = screen.getByRole("row", { name: "Inspect trial-balance account Financing payable for Liability" });
    expect(filteredFinancingRow).toHaveAttribute("aria-selected", "true");

    await user.click(filteredFinancingRow);

    expect(screen.getByRole("region", { name: "Trial-balance detail for Financing payable" })).toHaveTextContent("Credit / payable");
    expect(filteredFinancingRow).toHaveAttribute("aria-selected", "true");
  });

  it("renders a useful trial-balance empty state instead of a blank table", async () => {
    vi.mocked(api.getRunTrialBalance).mockResolvedValueOnce([]);

    await renderAccountingScreen(data, "/accounting");

    expect(await screen.findByText("No trial balance lines")).toBeInTheDocument();
    expect(screen.queryByRole("table", { name: "Primary trial balance lines for run-42" })).not.toBeInTheDocument();
  });

  it("renders structured trial-balance api-errors with endpoint and validation detail", async () => {
    vi.mocked(api.getRunTrialBalance).mockRejectedValueOnce(new ApiError({
      path: "/api/workstation/runs/run-42/trial-balance",
      status: 422,
      title: "Validation failed",
      detail: "Fund account is required.",
      validationIssues: [
        {
          field: "fundAccountId",
          label: "Fund account",
          messages: ["Select a fund account before loading accounting evidence."]
        }
      ]
    }));

    await renderAccountingScreen(data, "/accounting");

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Fund account is required.");
    expect(alert).toHaveTextContent("Endpoint returned 422 for /api/workstation/runs/run-42/trial-balance.");
    expect(alert).toHaveTextContent("Validation failed");
    expect(alert).toHaveTextContent("Fund account: Select a fund account before loading accounting evidence.");
  });

  it("runs ledger reporting export through the POST mutation instead of a GET link", async () => {
    const user = userEvent.setup();
    vi.mocked(api.runAnalysisExport).mockResolvedValueOnce({
      jobId: "export-1",
      success: true,
      status: "completed",
      profileId: "excel",
      symbols: [],
      filesGenerated: 2,
      totalRecords: 12,
      totalBytes: 2048,
      outputDirectory: "artifacts/exports/export-1",
      durationSeconds: 1.5,
      error: null,
      warnings: [],
      files: [],
      timestamp: "2026-01-01T00:00:00Z"
    });

    await renderAccountingScreen(data, "/accounting");

    await user.click(screen.getByRole("button", { name: "Run reporting export for Excel" }));

    expect(api.runAnalysisExport).toHaveBeenCalledWith("excel");
    expect(await screen.findByText("Export export-1 completed with 2 file(s), 12 record(s), and 2 KB. Output artifacts/exports/export-1.")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Run reporting export" })).not.toBeInTheDocument();
  });

  it("surfaces the reporting export busy reason on the command button", async () => {
    const user = userEvent.setup();
    let finishExport!: () => void;
    vi.mocked(api.runAnalysisExport).mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finishExport = () => resolve({
            jobId: "export-busy",
            success: true,
            status: "completed",
            profileId: "excel",
            symbols: [],
            filesGenerated: 1,
            totalRecords: 4,
            totalBytes: 1024,
            outputDirectory: "artifacts/exports/export-busy",
            durationSeconds: 1,
            error: null,
            warnings: [],
            files: [],
            timestamp: "2026-01-01T00:00:00Z"
          });
        })
    );

    await renderAccountingScreen(data, "/accounting");

    await user.click(screen.getByRole("button", { name: "Run reporting export for Excel" }));

    const busyButton = await screen.findByRole("button", { name: "Excel reporting export is already running" });
    expect(busyButton).toBeDisabled();
    expect(busyButton).toHaveAttribute("aria-busy", "true");
    expect(busyButton).toHaveAttribute("title", "Excel reporting export is already running.");
    expect(busyButton).toHaveTextContent("Export running...");

    finishExport();

    await waitFor(() => {
      expect(screen.getByText("Export export-busy completed with 1 file(s), 4 record(s), and 1 KB. Output artifacts/exports/export-busy.")).toBeInTheDocument();
    });
  });

  it("renders reporting profile detail state and updates selected profile", async () => {
    const user = userEvent.setup();
    const reportingData: AccountingWorkspaceResponse = {
      ...data,
      reporting: {
        ...data.reporting,
        profileCount: 2,
        recommendedProfiles: ["board"],
        reportPackTargets: ["board", "audit"],
        profiles: [
          ...data.reporting.profiles,
          {
            id: "board",
            name: "Board packet",
            targetTool: "Board",
            format: "Markdown",
            description: "Owner sign-off packet.",
            loaderScript: true,
            dataDictionary: false
          }
        ]
      }
    };

    await renderAccountingScreen(reportingData, "/reporting");

    expect(screen.getByText("Report packet posture")).toBeInTheDocument();
    expect(screen.getAllByText(/Board, Audit/).length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: "Inspect reporting profile Excel for Excel Xlsx" })).toHaveAttribute("aria-pressed", "true");

    await user.click(screen.getByRole("button", { name: "Inspect reporting profile Board packet for Board Markdown" }));

    expect(screen.getAllByText("Selected reporting profile - Board packet").length).toBeGreaterThan(0);
    expect(screen.getAllByText("MARKDOWN - Board").length).toBeGreaterThan(0);
    expect(screen.getByText("Dictionary missing")).toBeInTheDocument();
    expect(screen.getAllByText("Loader script").length).toBeGreaterThan(0);

    const detailPanel = screen.getByTestId("reporting-profile-detail");
    expect(detailPanel).toHaveClass("min-w-0", "overflow-hidden");
    expect(detailPanel.querySelector("dl > div")).toHaveClass("grid", "min-w-0");
  });

  it("adapts the hero copy for security-master deep links", async () => {
    await renderAccountingScreen(data, "/accounting/security-master");

    expect(screen.getByRole("heading", { name: "Security & Instrument Explorer" })).toBeInTheDocument();
    expect(screen.getByLabelText("Explorer scope")).toHaveTextContent("Accounting");
    expect(screen.getByLabelText("Explorer scope")).toHaveTextContent("Security Master instruments");
    expect(screen.getByLabelText("Saved explorer views")).toHaveTextContent("Instrument proof");
    expect(screen.getByLabelText("Applied explorer filters")).toHaveTextContent("No selection");
    expect(screen.getByLabelText("Security & Instrument Explorer proof actions")).toHaveTextContent("Open search");
    expect(screen.getAllByText("Security coverage").length).toBeGreaterThan(0);
  });

  it("announces security search failures as alerts", async () => {
    const user = userEvent.setup();
    vi.mocked(api.searchSecurities).mockRejectedValueOnce(new Error("Provider offline"));

    await renderAccountingScreen(data, "/accounting/security-master");

    await user.type(screen.getByLabelText("Search securities"), "AAPL");

    expect(await screen.findByRole("alert")).toHaveTextContent("Security search failed: Provider offline");
  });

  it("accepts and renders alias rows inside identity drill-in for accounting workflows", async () => {
    const user = userEvent.setup();
    vi.mocked(api.searchSecurities).mockResolvedValueOnce([
      {
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
      }
    ]);
    vi.mocked(api.getSecurityIdentity).mockResolvedValueOnce({
      securityId: "sec-1",
      displayName: "Apple Inc.",
      assetClass: "Equity",
      status: "Active",
      version: 3,
      effectiveFrom: "2024-01-01T00:00:00Z",
      effectiveTo: null,
      identifiers: [
        {
          kind: "Ticker",
          value: "AAPL",
          isPrimary: true,
          validFrom: "2024-01-01T00:00:00Z",
          validTo: null,
          provider: "Bloomberg"
        }
      ],
      aliases: [
        {
          aliasId: "alias-1",
          securityId: "sec-1",
          aliasKind: "ProviderSymbol",
          aliasValue: "AAPL.OQ",
          provider: "Nasdaq",
          scope: "Collector",
          reason: "Market data source mapping",
          createdBy: "ops.gov",
          createdAt: "2025-01-01T00:00:00Z",
          validFrom: "2025-01-01T00:00:00Z",
          validTo: null,
          isEnabled: true
        }
      ]
    });
    await renderAccountingScreen(data, "/accounting/security-master");

    await user.type(screen.getByPlaceholderText("Search securities…"), "AAPL");
    const securityRow = await screen.findByRole("row", { name: "Open identity drill-in for Apple Inc." });
    expect(securityRow).toHaveAttribute("aria-controls", "security-master-identity-detail");
    expect(securityRow).toHaveAttribute("aria-expanded", "false");
    await user.click(securityRow);

    expect(securityRow).toHaveAttribute("aria-expanded", "true");
    expect(securityRow).toHaveAttribute("aria-selected", "true");
    expect(await screen.findByText(/Identity drill-in · Apple Inc\./i)).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Security identity detail for Apple Inc." })).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Identifiers for Apple Inc." })).toBeInTheDocument();
    expect(screen.getByRole("row", {
      name: "Ticker AAPL, Primary, provider Bloomberg, valid 2024-01-01 -> active"
    })).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Aliases for Apple Inc." })).toBeInTheDocument();
    expect(screen.getByText("AAPL.OQ")).toBeInTheDocument();
    expect(screen.getByText("Collector")).toBeInTheDocument();
  });

  it("selects Security Master search rows with keyboard-expanded detail linkage", async () => {
    const user = userEvent.setup();
    vi.mocked(api.getOperatorOverrides).mockResolvedValueOnce({
      securityId: "sec-1",
      values: {
        issuer: "Apple Inc.",
        couponRate: "5.25",
        finalMaturity: "2032-06-30"
      },
      updatedBy: "ops",
      updatedAt: "2026-05-12T10:00:00Z"
    });
    vi.mocked(api.searchSecurities).mockResolvedValueOnce([
      {
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
      }
    ]);
    vi.mocked(api.getSecurityIdentity).mockResolvedValueOnce({
      securityId: "sec-1",
      displayName: "Apple Inc.",
      assetClass: "Equity",
      status: "Active",
      version: 3,
      effectiveFrom: "2024-01-01T00:00:00Z",
      effectiveTo: null,
      identifiers: [],
      aliases: []
    });
    vi.mocked(api.getSecurityTrustSnapshot).mockResolvedValue(securityTrustSnapshot);

    await renderAccountingScreen(data, "/accounting/security-master");

    await user.type(screen.getByPlaceholderText("Search securities…"), "AAPL");
    const securityRow = await screen.findByRole("row", { name: "Open identity drill-in for Apple Inc." });

    securityRow.focus();
    await user.keyboard("[Enter]");

    expect(securityRow).toHaveAttribute("aria-controls", "security-master-identity-detail");
    expect(securityRow).toHaveAttribute("aria-expanded", "true");
    expect(await screen.findByRole("region", { name: "Security identity detail for Apple Inc." })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Security detail page" })).toHaveTextContent("sec-1 · Equity");
    expect(screen.getByRole("toolbar", { name: "Security detail sections for Apple Inc." })).toHaveTextContent("Schedules");
    expect(screen.getByText("Security details")).toBeInTheDocument();
    expect(await screen.findByText("2 hidden overrides")).toBeInTheDocument();
    expect(screen.queryByText("Coupon Rate (%)")).not.toBeInTheDocument();
    expect(screen.queryByText("Final Maturity")).not.toBeInTheDocument();
    expect(screen.queryByText("S&P Rating")).not.toBeInTheDocument();
  });

  it("renders cash-flow schedules as selectable dense evidence with a detail panel", async () => {
    const user = userEvent.setup();
    vi.mocked(api.searchSecurities).mockResolvedValueOnce([
      {
        securityId: "sec-1",
        displayName: "Apple Inc.",
        status: "Active",
        classification: {
          assetClass: "Fixed Income",
          subType: "CorporateBond",
          primaryIdentifierKind: "CUSIP",
          primaryIdentifierValue: "037833AB1"
        },
        economicDefinition: {
          currency: "USD",
          version: 3,
          effectiveFrom: "2024-01-01T00:00:00Z",
          effectiveTo: null,
          subType: "CorporateBond",
          assetFamily: "Credit",
          issuerType: "Corporate"
        }
      }
    ]);
    vi.mocked(api.getSecurityIdentity).mockResolvedValueOnce({
      securityId: "sec-1",
      displayName: "Apple Inc.",
      assetClass: "Fixed Income",
      status: "Active",
      version: 3,
      effectiveFrom: "2024-01-01T00:00:00Z",
      effectiveTo: null,
      identifiers: [],
      aliases: []
    });

    await renderAccountingScreen(data, "/accounting/security-master");

    await user.type(screen.getByPlaceholderText("Search securities…"), "AAPL");
    await user.click(await screen.findByRole("row", { name: "Open identity drill-in for Apple Inc." }));

    const table = await screen.findByRole("table", { name: "Cash-flow and factor schedules for sec-1" });
    expect(table).toHaveTextContent("sched-1-coupon");
    const couponRow = screen.getByRole("row", { name: "Inspect schedule event Coupon for sec-1 on 2026-05-15" });
    const principalRow = screen.getByRole("row", { name: "Inspect schedule event Principal for sec-1 on 2026-11-15" });
    expect(couponRow).toHaveAttribute("aria-selected", "true");
    expect(couponRow).toHaveAttribute("aria-controls", "security-schedule-detail-panel");
    expect(screen.getByRole("region", { name: "Cash-flow schedule detail for Coupon on sec-1" })).toHaveTextContent("Posted");

    principalRow.focus();
    await user.keyboard("{Enter}");

    expect(principalRow).toHaveAttribute("aria-selected", "true");
    expect(principalRow).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("region", { name: "Cash-flow schedule detail for Principal on sec-1" })).toHaveTextContent("126,250 USD");
    expect(screen.getByRole("toolbar", { name: "Cash-flow schedule status for sec-1" })).toHaveTextContent("2");
    expect(screen.getByRole("table", { name: "Open lot read model for sec-1" })).toHaveTextContent("lot-1");
    expect(screen.getByRole("row", { name: "Inspect open lot lot-1 for AAPL" })).toHaveAttribute("aria-controls", "security-open-lot-detail-panel");
    expect(screen.getByRole("region", { name: "Open lot detail for lot-1 on AAPL" })).toHaveTextContent("85,500");
  });

  it("renders corporate actions as selectable dense evidence with a detail panel", async () => {
    const user = userEvent.setup();
    vi.mocked(api.searchSecurities).mockResolvedValueOnce([
      {
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
      }
    ]);
    vi.mocked(api.getSecurityIdentity).mockResolvedValueOnce({
      securityId: "sec-1",
      displayName: "Apple Inc.",
      assetClass: "Equity",
      status: "Active",
      version: 3,
      effectiveFrom: "2024-01-01T00:00:00Z",
      effectiveTo: null,
      identifiers: [],
      aliases: []
    });
    vi.mocked(api.getCorporateActions).mockResolvedValueOnce(corporateActions);

    await renderAccountingScreen(data, "/accounting/security-master");

    await user.type(screen.getByPlaceholderText("Search securities…"), "AAPL");
    await user.click(await screen.findByRole("row", { name: "Open identity drill-in for Apple Inc." }));

    const table = await screen.findByRole("table", { name: "Corporate actions for sec-1" });
    expect(table).toBeInTheDocument();
    const dividendRow = screen.getByRole("row", { name: "Inspect corporate action Dividend for sec-1" });
    const splitRow = screen.getByRole("row", { name: "Inspect corporate action Stock split for sec-1" });
    expect(dividendRow).toHaveAttribute("aria-selected", "true");
    expect(dividendRow).toHaveAttribute("aria-controls", "corporate-action-detail-panel");
    expect(screen.getByRole("region", { name: "Corporate action detail for Dividend on sec-1" })).toHaveTextContent("0.24 USD / share");

    splitRow.focus();
    await user.keyboard("{Enter}");

    expect(splitRow).toHaveAttribute("aria-selected", "true");
    expect(splitRow).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("region", { name: "Corporate action detail for Stock split on sec-1" })).toHaveTextContent("4:1 split");
    expect(screen.getByRole("region", { name: "Corporate action detail for Stock split on sec-1" })).toHaveTextContent("Pay date unavailable");
  });

  it("renders provider-specific security conflict actions", async () => {
    const user = userEvent.setup();
    vi.mocked(api.getSecurityConflicts).mockResolvedValueOnce([securityConflict]);
    vi.mocked(api.resolveSecurityConflict).mockResolvedValueOnce({
      ...securityConflict,
      status: "Resolved"
    });

    await renderAccountingScreen(data, "/accounting/security-master");

    expect(await screen.findByRole("group", { name: /Identifier conflict conflict-1/i })).toBeInTheDocument();
    expect(screen.getByText("Bloomberg -> security sec-1")).toBeInTheDocument();
    expect(screen.getByText("Refinitiv -> security sec-2")).toBeInTheDocument();

    const useBloomberg = screen.getByRole("button", {
      name: "Resolve identifier conflict conflict-1 on identifiers.CUSIP with Bloomberg value sec-1"
    });
    expect(useBloomberg).toHaveTextContent("Use Bloomberg");

    await user.click(useBloomberg);

    expect(api.resolveSecurityConflict).toHaveBeenCalledWith({
      conflictId: "conflict-1",
      resolution: "AcceptA",
      resolvedBy: "operator"
    });
  });

  it("surfaces Security Master conflict action disabled reasons while resolving", async () => {
    const user = userEvent.setup();
    let finishResolve!: () => void;
    vi.mocked(api.getSecurityConflicts).mockResolvedValueOnce([securityConflict]);
    vi.mocked(api.resolveSecurityConflict).mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finishResolve = () => resolve({
            ...securityConflict,
            status: "Resolved"
          });
        })
    );

    await renderAccountingScreen(data, "/accounting/security-master");

    const useBloomberg = await screen.findByRole("button", {
      name: "Resolve identifier conflict conflict-1 on identifiers.CUSIP with Bloomberg value sec-1"
    });

    await user.click(useBloomberg);

    const disabledReason = "Resolution is already in progress for identifier conflict conflict-1.";
    const disabledUseBloomberg = await screen.findByRole("button", {
      name: `Resolve identifier conflict conflict-1 on identifiers.CUSIP with Bloomberg value sec-1. Disabled: ${disabledReason}`
    });
    expect(disabledUseBloomberg).toBeDisabled();
    expect(disabledUseBloomberg).toHaveAttribute("title", disabledReason);
    expect(screen.getByRole("button", {
      name: `Dismiss identifier conflict conflict-1 on identifiers.CUSIP. Disabled: ${disabledReason}`
    })).toHaveAttribute("title", disabledReason);
    expect(screen.getByText("Resolving identifier conflict conflict-1.")).toHaveAttribute("role", "status");
    const refreshDisabledReason = "Wait until identifier conflict conflict-1 finishes resolving before refreshing the conflict queue.";
    const refreshButton = screen.getByRole("button", {
      name: "Refresh disabled while identifier conflict conflict-1 is resolving"
    });
    expect(refreshButton).toBeDisabled();
    expect(refreshButton).toHaveAttribute("aria-describedby", "security-conflict-refresh-feedback");
    expect(refreshButton).toHaveAttribute("title", refreshDisabledReason);
    expect(screen.getByText(refreshDisabledReason)).toHaveAttribute("role", "status");

    finishResolve();

    await waitFor(() => {
      expect(screen.queryByRole("button", { name: /Disabled: Resolution is already in progress/ })).not.toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Refresh Security Master identifier conflicts" })).toBeEnabled();
    });
  });

  it("recovers Security Master conflict loading failures with a retry command", async () => {
    const user = userEvent.setup();
    vi.mocked(api.getSecurityConflicts)
      .mockRejectedValueOnce(new ApiError({
        path: "/api/workstation/security-master/conflicts",
        status: 503,
        detail: "Conflict API offline"
      }))
      .mockResolvedValueOnce([securityConflict]);

    await renderAccountingScreen(data, "/accounting/security-master");

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Conflict API offline");
    expect(within(alert).getByText("Endpoint returned 503 for /api/workstation/security-master/conflicts.")).toBeInTheDocument();
    const retry = screen.getByRole("button", { name: "Retry loading Security Master identifier conflicts" });
    expect(retry).toHaveTextContent("Retry conflicts");

    await user.click(retry);

    expect(await screen.findByRole("group", { name: /Identifier conflict conflict-1/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Refresh Security Master identifier conflicts" })).toHaveTextContent("Refresh conflicts");
  });

  it("announces Security Master conflict resolution failures with structured details", async () => {
    const user = userEvent.setup();

    vi.mocked(api.getSecurityConflicts).mockResolvedValueOnce([securityConflict]);
    vi.mocked(api.resolveSecurityConflict).mockRejectedValueOnce(
      new ApiError({
        path: "/api/workstation/security-master/conflicts/conflict-1/resolve",
        status: 409,
        detail: "Resolution requires a newer conflict snapshot.",
        validationIssues: [
          {
            field: "resolution",
            label: "resolution",
            messages: ["Choose a resolution that matches the active provider record."]
          }
        ]
      })
    );

    await renderAccountingScreen(data, "/accounting/security-master");

    await user.click(await screen.findByRole("button", {
      name: "Resolve identifier conflict conflict-1 on identifiers.CUSIP with Bloomberg value sec-1"
    }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Resolution requires a newer conflict snapshot.");
    expect(within(alert).getByText("Endpoint returned 409 for /api/workstation/security-master/conflicts/conflict-1/resolve.")).toBeInTheDocument();
    expect(within(alert).getByText("resolution: Choose a resolution that matches the active provider record.")).toBeInTheDocument();
  });

  it("renders reconciliation detail on deep-link routes and updates selection", async () => {
    const user = userEvent.setup();

    await renderAccountingScreen(data, "/accounting/reconciliation");

    expect(screen.getByRole("region", { name: "Reconciliation detail for Paper Index Mean Reversion" })).toBeInTheDocument();
    expect(screen.getByLabelText("Reconciliation narrative for Paper Index Mean Reversion")).toHaveTextContent(
      /Open reconciliation breaks remain on this run/
    );
    expect(screen.getByRole("link", { name: "Open break checklist for Paper Index Mean Reversion; 1 open break" }))
      .toHaveAttribute("href", "#reconciliation-break-queue");
    expect(screen.getByRole("link", { name: "Review audit packet for Paper Index Mean Reversion" }))
      .toHaveAttribute("href", "/api/workstation/runs/run-42/review-packet");
    expect(screen.getByRole("region", { name: "Reconciliation break checklist" }))
      .toHaveAttribute("id", "reconciliation-break-queue");

    await user.click(screen.getByRole("row", { name: "Inspect reconciliation run Intraday Vol Carry" }));

    expect(screen.getByText(/Historical breaks have been worked through/)).toBeInTheDocument();
  });

  it("assigns reconciliation breaks through the view model workflow", async () => {
    const user = userEvent.setup();
    const updatedBreak = {
      ...data.breakQueue[0],
      status: "InReview" as const,
      assignedTo: "ops.gov",
      reviewedBy: "ops.gov",
      reviewedAt: "2026-01-01T00:05:00Z"
    };

    vi.mocked(api.getReconciliationBreakQueue).mockResolvedValueOnce(data.breakQueue);
    vi.mocked(api.reviewReconciliationBreak).mockResolvedValueOnce(updatedBreak);

    await renderAccountingScreen(data, "/accounting/reconciliation");

    await user.click(await screen.findByRole("button", { name: "Assign reconciliation break run-42:cash" }));

    expect(api.reviewReconciliationBreak).toHaveBeenCalledWith({
      breakId: "run-42:cash",
      assignedTo: "ops.gov",
      reviewedBy: "ops.gov"
    });
    const detail = await screen.findByRole("region", { name: "Reconciliation break detail for run-42:cash" });
    expect(within(detail).getByText("InReview")).toBeInTheDocument();
  });

  it("surfaces view-model disabled reasons for reconciliation queue actions", async () => {
    const user = userEvent.setup();
    const resolvedBreak = {
      ...data.breakQueue[0],
      breakId: "run-42:resolved",
      status: "Resolved" as const,
      resolvedBy: "ops.gov",
      resolvedAt: "2026-01-01T00:10:00Z",
      resolutionNote: "Matched ledger adjustment.",
      exceptionRoute: "fund-ops-review",
      toleranceProfileId: "cash-variance-ops",
      toleranceBand: 250,
      requiredSignoffRole: "Fund operations lead",
      signoffStatus: "Pending Signoff"
    };
    const dismissedBreak = {
      ...data.breakQueue[0],
      breakId: "run-42:dismissed",
      status: "Dismissed" as const,
      resolvedBy: "ops.gov",
      resolvedAt: "2026-01-01T00:12:00Z",
      resolutionNote: "Vendor duplicate ignored."
    };

    vi.mocked(api.getReconciliationBreakQueue).mockResolvedValueOnce([
      data.breakQueue[0],
      resolvedBreak,
      dismissedBreak
    ]);

    await renderAccountingScreen(data, "/accounting/reconciliation");

    expect(await screen.findByRole("button", { name: "Assign reconciliation break run-42:resolved" }))
      .toHaveAttribute("title", "Only open breaks can be assigned; this break is Resolved.");
    expect(screen.getByRole("button", { name: "Resolve reconciliation break run-42:resolved" }))
      .toHaveAttribute("title", "This break is already resolved.");
    expect(screen.getByRole("button", { name: "Dismiss reconciliation break run-42:dismissed" }))
      .toHaveAttribute("title", "This break is already dismissed.");

    await user.click(screen.getByRole("row", { name: "Inspect reconciliation break run-42:resolved" }));
    const resolvedDetail = screen.getByRole("region", { name: "Reconciliation break detail for run-42:resolved" });
    expect(resolvedDetail).toHaveTextContent("Decision captured; sign-off: Pending Signoff by Fund operations lead. Close approval remains blocked.");
    expect(resolvedDetail).toHaveTextContent("Matched ledger adjustment.");

    await user.click(screen.getByRole("button", { name: "Resolve reconciliation break run-42:cash" }));

    expect(screen.getByRole("button", { name: "Resolve reconciliation break run-42:cash" }))
      .toHaveAttribute("title", "Enter the rationale or cancel the open queue action before choosing another action.");
    expect(screen.getByRole("button", { name: "Confirm resolve for reconciliation break run-42:cash" }))
      .toHaveAttribute("title", "Enter an operator rationale before confirming this queue action.");
  });

  it("selects a reconciliation break before opening its queue action", async () => {
    const user = userEvent.setup();
    const feeBreak = {
      ...data.breakQueue[0],
      breakId: "run-57:fees",
      runId: "run-57",
      strategyName: "Intraday Vol Carry",
      category: "FeeMismatch",
      variance: -125,
      reason: "Fee accrual differs from broker statement."
    };

    vi.mocked(api.getReconciliationBreakQueue).mockResolvedValueOnce([
      data.breakQueue[0],
      feeBreak
    ]);

    await renderAccountingScreen(data, "/accounting/reconciliation");

    expect(await screen.findByRole("region", { name: "Reconciliation break detail for run-42:cash" }))
      .toHaveTextContent("Paper Index Mean Reversion - AmountMismatch");

    await user.click(screen.getByRole("button", { name: "Resolve reconciliation break run-57:fees" }));

    expect(screen.getByRole("region", { name: "Reconciliation break detail for run-57:fees" }))
      .toHaveTextContent("Intraday Vol Carry - FeeMismatch");
    expect(screen.getByRole("textbox", { name: "Resolve rationale" })).toBeInTheDocument();
  });

  it("announces reconciliation break action failures", async () => {
    const user = userEvent.setup();

    vi.mocked(api.getReconciliationBreakQueue).mockResolvedValueOnce(data.breakQueue);
    vi.mocked(api.resolveReconciliationBreak).mockRejectedValueOnce(
      new ApiError({
        path: "/api/workstation/reconciliation/break-queue/run-42:cash/resolve",
        status: 409,
        detail: "Ledger write rejected",
        validationIssues: [
          {
            field: "operatorRationale",
            label: "operatorRationale",
            messages: ["Operator rationale must cite the balancing ledger entry."]
          }
        ]
      })
    );

    await renderAccountingScreen(data, "/accounting/reconciliation");

    await user.click(await screen.findByRole("button", { name: "Resolve reconciliation break run-42:cash" }));

    // The inline rationale form appears; fill in the rationale and submit
    const rationaleInput = await screen.findByLabelText(/resolve rationale/i);
    await user.type(rationaleInput, "Reviewed cash mismatch");
    await user.click(screen.getByRole("button", { name: /confirm resolve/i }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Break action failed: Ledger write rejected");
    expect(within(alert).getByText("Endpoint returned 409 for /api/workstation/reconciliation/break-queue/run-42:cash/resolve.")).toBeInTheDocument();
    expect(within(alert).getByText("operatorRationale: Operator rationale must cite the balancing ledger entry.")).toBeInTheDocument();
  });
});
