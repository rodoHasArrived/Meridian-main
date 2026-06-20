import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ApiError as MeridianApiError, describeApiError } from "@/lib/api-errors";
import {
  buildCalibrationSummaryViewState,
  buildCorporateActionsViewState,
  buildAccountingCashFlowViewState,
  buildAccountingLoadingViewState,
  buildAccountingReportingViewState,
  buildAccountingWorkflowLaunchViewState,
  buildCloseCommandCenterViewState,
  buildAccountingTrialBalanceViewState,
  buildSecurityScheduleRows,
  buildSecuritySchedulesViewState,
  buildSecurityOpenLotReadModelViewState,
  buildSecurityOpenLotRows,
  mapScheduleBookToCashFlowScheduleEvents,
  formatReportingExportResult,
  buildReconciliationBreakQueueState,
  buildReconciliationBreakRows,
  buildOperationalExceptionWorkbenchState,
  buildReconciliationDetailActions,
  buildReconciliationQueuePanelViewState,
  buildReconciliationComparisonViewState,
  buildReconciliationStatementRunsViewState,
  buildReconciliationDetailViewState,
  buildReconciliationNarrative,
  buildReconciliationResolveDialogState,
  buildSecurityConflictRows,
  buildSecurityConflictRefreshCommand,
  buildSecurityIdentityDrillInState,
  buildInstrumentPassportViewState,
  buildReferenceDataWorkbenchViewState,
  buildSecurityMasterPageViewState,
  buildSecuritySearchResultRows,
  buildSecuritySearchState,
  countOpenSecurityConflicts,
  resolveSecurityScheduleEvents,
  resolveAccountingWorkstream,
  resolveSelectedReconciliation,
  useCapitalAccountWorkbenchViewModel,
  useAccountingCloseReportPackageViewModel,
  useManualJournalEntryWorkbenchViewModel,
  useAccountingConfigurationViewModel,
  useAccountingReconciliationViewModel,
  useSecurityMasterViewModel
} from "@/screens/accounting-screen.view-model";
import type {
  AccountingConfigurationServices,
  AccountingCloseReportPackageServices,
  AccountingReconciliationServices,
  CapitalAccountWorkbenchServices,
  ManualJournalEntryWorkbenchServices,
  SecurityCashFlowScheduleEvent,
  SecurityMasterDrillInServices,
  SecurityMasterServices
} from "@/screens/accounting-screen.view-model";
import type {
  CorporateAction,
  AccountingCashFlowSummary,
  AccountingSystemProvider,
  AccountingSystemReconciliationSummary,
  AccountingWorkspaceResponse,
  AccountingConfigurationWorkspace,
  AccountingProductionReadiness,
  AccountingReportPackageBundle,
  CapitalAccountWorkbench,
  ClosePeriodPlan,
  RuleDryRunResult,
  ManualJournalEntryDraft,
  ManualJournalEntryWorkbench,
  LedgerTrialBalanceLine,
  MultiAssetCoverageSummary,
  OperationsContinuityWorkflow,
  ReconciliationCalibrationSummary,
  ReconciliationBreakQueueItem,
  ReportExportArtifactManifest,
  SecurityMasterConflict,
  SecurityMasterEntry,
  SecurityIdentityDrillIn,
  SecurityMasterTrustSnapshot,
  InvestmentAccountingTransactionLabPreview,
  TradingParameters,
  InstrumentPassport
} from "@/types";

const reconciliationQueue: AccountingWorkspaceResponse["reconciliationQueue"] = [
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
];

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

const securityIdentity: SecurityIdentityDrillIn = {
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
      provider: null,
      scope: "Collector",
      reason: "Market data source mapping",
      createdBy: "ops.gov",
      createdAt: "2025-01-01T00:00:00Z",
      validFrom: "2025-01-01T00:00:00Z",
      validTo: null,
      isEnabled: true
    }
  ]
};

const tradingParameters: TradingParameters = {
  securityId: "sec-1",
  lotSize: 100,
  tickSize: 0.01,
  contractMultiplier: 1,
  marginRequirementPct: 25,
  tradingHoursUtc: "14:30-21:00",
  circuitBreakerThresholdPct: 7,
  asOf: "2026-05-10T00:00:00Z"
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

const referenceDataCoverage = {
  requestedAtUtc: "2026-05-10T12:00:00Z",
  endpoints: []
};
const instrumentPassport: InstrumentPassport = {
  securityId: "sec-1",
  identity: securityIdentity,
  economicDefinition: {},
  identifierSummary: { summary: "Primary identifiers are aligned." },
  providerMappings: [],
  lifecycleEvents: [],
  corporateActions,
  pricing: {
    status: "Ready",
    summary: "Trading parameters are active.",
    tradingParameters,
    lotSize: tradingParameters.lotSize,
    tickSize: tradingParameters.tickSize,
    contractMultiplier: tradingParameters.contractMultiplier,
    tradingHoursUtc: tradingParameters.tradingHoursUtc,
    circuitBreakerThresholdPct: tradingParameters.circuitBreakerThresholdPct
  },
  usage: { summary: "Used by accounting and trading workflows." },
  trustPosture: { tone: "Trusted", summary: "Approved Security Master record." },
  retrievedAtUtc: "2026-05-10T12:00:00Z",
  providerConfidence: [
    {
      provider: "Bloomberg",
      providerSource: "blp-reference",
      mappingKind: "Ticker",
      symbol: "AAPL US Equity",
      normalizedSymbol: "AAPL",
      isPrimary: true,
      isActive: true,
      freshnessAsOf: "2026-05-10T11:55:00Z",
      freshnessMinutes: 5,
      confidenceScore: 0.87,
      confidenceReason: "Primary ticker matches the golden copy.",
      identifierConflictIds: [],
      identifierConflictSummaries: [],
      overrideHistory: []
    },
    {
      provider: "Reuters",
      providerSource: "refinitiv",
      mappingKind: "RIC",
      symbol: "AAPL.O",
      normalizedSymbol: "AAPL.O",
      isPrimary: false,
      isActive: false,
      freshnessAsOf: null,
      freshnessMinutes: null,
      confidenceScore: 87,
      confidenceReason: "Inactive mapping retained for audit evidence.",
      identifierConflictIds: ["conflict-1"],
      identifierConflictSummaries: ["RIC mapping is disabled."],
      overrideHistory: []
    }
  ]
};
const cashFlowSchedules: SecurityCashFlowScheduleEvent[] = [
  {
    eventId: "sched-1-coupon",
    securityId: "sec-1",
    scheduleFamily: "bond",
    eventType: "Coupon",
    paymentDate: "2026-05-15T00:00:00Z",
    accrualStartDate: "2025-11-15T00:00:00Z",
    accrualEndDate: "2026-05-15T00:00:00Z",
    couponRatePct: 5.25,
    expectedAmount: 26250,
    actualAmount: 26250,
    principalAmount: null,
    interestAmount: 26250,
    factorStart: 1,
    factorEnd: 1,
    currency: "USD",
    postingStatus: "Posted",
    auditReference: "fixture/schedule/coupon",
    note: "Coupon posted."
  },
  {
    eventId: "sched-1-paydown",
    securityId: "sec-1",
    scheduleFamily: "structured",
    eventType: "Paydown",
    paymentDate: "2026-11-15T00:00:00Z",
    accrualStartDate: "2026-05-15T00:00:00Z",
    accrualEndDate: "2026-11-15T00:00:00Z",
    couponRatePct: 5.25,
    expectedAmount: 126250,
    actualAmount: 124900,
    principalAmount: 100000,
    interestAmount: 26250,
    factorStart: 1,
    factorEnd: 0.9,
    currency: "USD",
    postingStatus: "Variance",
    auditReference: "fixture/schedule/paydown",
    note: "Expected-versus-actual variance."
  }
];

const securityTrustSnapshot: SecurityMasterTrustSnapshot = {
  securityId: "sec-1",
  retrievedAtUtc: "2026-05-21T15:00:00Z",
  scheduleSummary: {
    supportsCashflowSchedule: true,
    supportsFactorHistory: true,
    hasEconomicScheduleTerms: true,
    currentFactor: 0.9,
    currentFactorDate: "2026-11-15",
    nextLifecycleDate: "2031-12-15",
    sourceSummary: "Schedule source EDM-123 is current.",
    summary: "Schedule book includes current cash-flow and factor evidence."
  },
  lotModel: {
    quantityModel: "FactorAdjustedFace",
    lotSize: 1,
    contractMultiplier: null,
    usesFaceValue: true,
    supportsFactorAdjustedExposure: true,
    requiresResolvedSecurityId: true,
    summary: "Open lots reconcile by factor-adjusted face."
  },
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
        eventId: "sched-1-paydown",
        eventType: "Paydown",
        effectiveDate: "2026-11-15",
        payDate: "2026-11-15",
        accrualStartDate: "2026-05-15",
        accrualEndDate: "2026-11-15",
        expectedAmount: 126250,
        actualAmount: 124900,
        varianceAmount: -1350,
        factorStart: 1,
        factorEnd: 0.9,
        currency: "USD",
        postingStatus: "Variance",
        sourceSystem: "golden-edm",
        sourceRecordId: "EDM-123",
        sourceAsOfUtc: "2026-05-21T14:00:00Z",
        sourceUpdatedBy: "workflow.bot",
        sourceReason: "Expected-versus-actual variance.",
        isDerivedFromEconomicTerms: true,
        isCurrentProjection: true
      }
    ],
    factorHistory: [
      {
        pointId: "factor-1",
        effectiveDate: "2026-11-15",
        factor: 0.9,
        previousFactor: 1,
        sourceSystem: "golden-edm",
        sourceRecordId: "EDM-123",
        sourceAsOfUtc: "2026-05-21T14:00:00Z",
        sourceUpdatedBy: "workflow.bot",
        sourceReason: "Trustee factor update.",
        isCurrentFactor: true
      }
    ],
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

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });

  return { promise, resolve, reject };
}

function createSecurityMasterServices(overrides: Partial<SecurityMasterServices> = {}): SecurityMasterServices {
  return {
    search: vi.fn().mockResolvedValue([]),
    getIdentity: vi.fn().mockResolvedValue(securityIdentity),
    getConflicts: vi.fn().mockResolvedValue([]),
    resolveConflict: vi.fn().mockResolvedValue(conflicts[0]),
    ...overrides
  };
}

function createSecurityMasterDrillInServices(
  overrides: Partial<SecurityMasterDrillInServices> = {}
): SecurityMasterDrillInServices {
  return {
    getCorporateActions: vi.fn().mockResolvedValue([] as CorporateAction[]),
    getInstrumentPassport: vi.fn().mockResolvedValue(instrumentPassport),
    getReferenceDataCoverage: vi.fn().mockResolvedValue(referenceDataCoverage),
    getTradingParameters: vi.fn().mockResolvedValue(tradingParameters),
    getTrustSnapshot: vi.fn().mockResolvedValue(securityTrustSnapshot),
    ...overrides
  };
}

const conflicts: SecurityMasterConflict[] = [
  {
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
  },
  {
    conflictId: "conflict-2",
    securityId: "sec-3",
    conflictKind: "IdentifierCollision",
    fieldPath: "identifiers.ISIN",
    providerA: "Bloomberg",
    valueA: "sec-3",
    providerB: "FactSet",
    valueB: "sec-3",
    detectedAt: "2026-01-02T00:00:00Z",
    status: "Resolved"
  }
];

const breakQueue: ReconciliationBreakQueueItem[] = [
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
    resolutionNote: null
  },
  {
    breakId: "run-57:fees",
    runId: "run-57",
    strategyName: "Intraday Vol Carry",
    category: "FeeMismatch",
    status: "Resolved",
    variance: 0,
    reason: "Fee variance resolved.",
    assignedTo: "ops.gov",
    detectedAt: "2026-01-02T00:00:00Z",
    lastUpdatedAt: "2026-01-02T00:00:00Z",
    reviewedBy: "ops.gov",
    reviewedAt: "2026-01-02T00:05:00Z",
    resolvedBy: "ops.gov",
    resolvedAt: "2026-01-02T00:10:00Z",
    resolutionNote: "Reviewed in accounting panel.",
    exceptionRoute: "fund-ops-review",
    toleranceProfileId: "fee-variance-ops",
    toleranceBand: 100,
    requiredSignoffRole: "Fund operations lead",
    signoffStatus: "Pending Signoff",
    routingTarget: "FundTrialBalance",
    routingDetail: "Open the accounting trial balance for evidence review.",
    recommendedAction: "Review matched fee entries before closing.",
    breakExplanation: {
      summary: "Provider fees and Meridian ledger fees now match after operator review.",
      sourceSystems: ["Provider activity", "Meridian ledger"],
      probableCause: "The provider posted fees after the first reconciliation pass.",
      ledgerImpact: "No remaining ledger adjustment is required.",
      suggestedNextAction: "Attach the provider activity evidence before close sign-off.",
      evidenceLinks: ["/accounting/evidence/provider-fees"]
    }
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

const accountingWorkspace: AccountingWorkspaceResponse = {
  metrics: [],
  reconciliationQueue,
  breakQueue,
  cashFlow: {
    totalCash: 120000,
    totalLedgerCash: 119500,
    netVariance: 500,
    totalFinancing: 0,
    runsWithCashSignals: 2,
    runsWithCashVariance: 1,
    tone: "warning",
    summary: "Cash variance is under controller review."
  },
  reporting: {
    profileCount: 1,
    recommendedProfiles: ["controller"],
    reportPackTargets: ["monthly-close"],
    summary: "Controller reporting profile is available.",
    profiles: []
  },
  controlCenter: {
    closeReadiness: "Blocked",
    portfolioFilterOptions: ["all-portfolios"],
    accountFilterOptions: ["fund-alpha"],
    blockerSeverityDistribution: [{ severity: "Critical", count: 1 }],
    agingCurves: [],
    ownerWorkload: [],
    slaBreachCount: 1,
    trendSnapshots: [],
    drillLinks: [],
    alerts: [{ tone: "danger", message: "Ledger validation is blocking close." }]
  }
};

const closeWorkflow: OperationsContinuityWorkflow = {
  workflowId: "workflow-close-1",
  fundAccountId: "fund-alpha",
  periodId: "2026-05",
  securityMasterSnapshotId: "snapshot-1",
  brokerSource: "broker-fixture",
  status: "Blocked",
  version: 3,
  createdAtUtc: "2026-05-31T00:00:00Z",
  updatedAtUtc: "2026-06-01T02:00:00Z",
  brokerIntakeState: "Complete",
  securityMasterState: "Complete",
  ledgerPostingState: "Drafted",
  reconciliationState: "ExceptionsOpen",
  approvalState: "Pending",
  gates: [
    {
      gateKey: "LedgerPosting",
      displayName: "Ledger posting",
      status: "Blocked",
      isRequired: true,
      description: "Ledger validation must pass before close.",
      blockers: [
        {
          code: "LEDGER_VALIDATION_REQUIRED",
          message: "Ledger posting requires a balanced journal draft.",
          gate: "LedgerPosting",
          severity: "Critical",
          evidenceLinks: []
        }
      ],
      nextActions: [],
      completedAtUtc: null,
      completedBy: null
    }
  ],
  nextActions: [
    { code: "FIX_LEDGER", label: "Resolve ledger blockers", route: "/accounting/ledger", gate: "LedgerPosting" }
  ],
  timeline: [],
  breakCases: [
    {
      breakId: "break-close-1",
      checkId: "cash-check",
      category: "Cash",
      severity: "Critical",
      status: "Open",
      owner: null,
      dueDate: "2026-06-02",
      expectedSource: "custodian",
      actualSource: null,
      expectedAmount: 100,
      actualAmount: null,
      variance: 100,
      securityId: null,
      symbol: null,
      suggestedAction: "Attach custodian source file.",
      evidenceLinks: []
    }
  ],
  ledgerPreview: {
    previewId: "ledger-preview-1",
    status: "Drafted",
    ledgerBatchId: null,
    generatedAtUtc: "2026-06-01T01:00:00Z",
    evidenceLinks: []
  },
  approvals: [
    {
      approvalId: "approval-1",
      status: "Pending",
      operator: "controller",
      reviewer: null,
      rationale: null,
      submittedAtUtc: "2026-06-01T01:30:00Z",
      decidedAtUtc: null,
      evidenceLinks: []
    }
  ],
  reportPackReadiness: {
    isReady: false,
    reportPackId: null,
    blockingReason: "Report pack is waiting on ledger validation.",
    evidenceLinks: []
  },
  closeChecklist: [
    {
      taskId: "source-file",
      gate: "BrokerIngest",
      label: "Source file retained",
      owner: "controller",
      requiredEvidence: "Custodian source file",
      dueDate: "2026-06-02",
      requiredApprovalCount: 1,
      expiresOn: null,
      status: "Open",
      blockingReason: "Source file missing.",
      evidencePointer: null,
      remediationRoute: "/accounting/reconciliation",
      canAcknowledge: false,
      acknowledgedAtUtc: null,
      acknowledgedBy: null
    }
  ],
  closeReadiness: {
    isReadyToClose: false,
    severity: "Critical",
    score: 42,
    components: [],
    blockers: [
      {
        code: "SOURCE_FILE_MISSING",
        category: "Evidence",
        severity: "Warning",
        message: "Custodian source file is missing.",
        gate: "BrokerIngest",
        routeHint: "/accounting/reconciliation"
      }
    ],
    nextActions: []
  },
  closePackage: null,
  accountingRecordSummary: {
    recordId: "acct-record-1",
    isAuditReady: false,
    completeCategoryCount: 1,
    requiredCategoryCount: 3,
    summary: "Accounting record is incomplete.",
    evidenceCategories: [
      {
        key: "source-records",
        label: "Retained source records",
        isComplete: false,
        status: "Missing",
        routeHint: "/reporting/evidence",
        evidenceLinks: [],
        requiredEvidence: ["custodian activity file"]
      },
      {
        key: "ledger-evidence",
        label: "Ledger evidence",
        isComplete: true,
        status: "Complete",
        routeHint: "/accounting/ledger",
        evidenceLinks: [],
        requiredEvidence: ["journal preview"]
      }
    ],
    evidenceLinks: []
  },
  evidenceLinks: [],
  blockers: [
    {
      code: "CLOSE_BLOCKED",
      message: "Close workflow has unresolved blockers.",
      gate: "LedgerPosting",
      severity: "Critical",
      evidenceLinks: []
    }
  ]
};

const closePeriodPlan: ClosePeriodPlan = {
  closePlanId: "close-plan-alpha-202605",
  fundProfileId: "fund-alpha",
  ledgerBookId: "book-alpha",
  periodId: "2026-05",
  periodStart: "2026-05-01",
  periodEnd: "2026-05-31",
  closeDueDate: "2026-06-05",
  isPeriodLocked: false,
  materialityPolicy: {
    policyId: "materiality-alpha",
    amountThreshold: 2500,
    percentThreshold: 0.5,
    currency: "USD",
    reviewRole: "controller",
    requiresLateAdjustmentApproval: true
  },
  tasks: [
    {
      taskId: "task-nav",
      displayName: "Finalize NAV package",
      status: "ReadyForSignOff",
      owner: "fund-accounting",
      dueDate: "2026-06-04",
      dependencies: [
        {
          dependencyId: "dependency-recon",
          dependsOnTaskId: "task-reconciliation",
          reason: "Reconciliation must clear before NAV package sign-off."
        }
      ],
      signOffs: [
        {
          signOffId: "signoff-controller",
          role: "controller",
          actor: "ops-user",
          approvalState: "Approved",
          signedAtUtc: "2026-06-02T05:00:00Z",
          evidenceLinks: ["evidence/nav-signoff"],
          notes: "Controller retained NAV package sign-off."
        }
      ],
      evidenceLinks: ["evidence/nav-package", "evidence/nav-signoff"],
      blockerReason: "Controller sign-off pending.",
      signOffRequirements: [
        {
          requirementId: "requirement-task-nav-controller",
          role: "controller",
          requiredApprovalCount: 1,
          approvedCount: 1,
          isSatisfied: true,
          evidenceRequirement: "Controller NAV sign-off evidence"
        }
      ]
    }
  ],
  lateAdjustments: [
    {
      requestId: "late-adjustment-1",
      journalEntryId: "manual-je-late-1",
      requestedBy: "controller",
      requestedAtUtc: "2026-06-02T03:00:00Z",
      amount: 1250,
      currency: "USD",
      reason: "Late custodian fee accrual.",
      approvalState: "Approved",
      materialityPolicy: {
        policyId: "materiality-alpha",
        amountThreshold: 2500,
        percentThreshold: 0.5,
        currency: "USD",
        reviewRole: "controller",
        requiresLateAdjustmentApproval: true
      },
      evidenceLinks: ["evidence/late-adjustment", "evidence/late-adjustment-approval"],
      decidedBy: "ops-user",
      decidedAtUtc: "2026-06-02T04:00:00Z",
      decisionNotes: "Controller approval retained."
    }
  ],
  validationIssues: [
    {
      code: "CLOSE_TASK_PENDING",
      severity: "Warning",
      message: "NAV package still needs controller sign-off.",
      targetId: "task-nav"
    }
  ],
  closeCalendar: [
    {
      milestoneId: "close-calendar-task-nav",
      taskId: "task-nav",
      displayName: "Finalize NAV package",
      owner: "fund-accounting",
      dueDate: "2026-06-04",
      status: "ReadyForSignOff",
      isBlocked: false,
      isSatisfied: true,
      isPeriodLocked: false,
      dependencyCount: 1,
      requiredSignOffCount: 1,
      approvedSignOffCount: 1,
      evidenceLinks: ["evidence/nav-package", "evidence/nav-signoff"],
      blockerReason: "Controller sign-off pending."
    }
  ]
};

const accountingReportPackage: AccountingReportPackageBundle = {
  financialStatements: {
    packageId: "accounting-report-package-alpha-202605",
    fundProfileId: "fund-alpha",
    ledgerBookId: "book-alpha",
    periodId: "2026-05",
    certificationState: "ReadyForReview",
    statementIds: ["balance-sheet-alpha"],
    evidenceLinks: ["evidence/financial-statements"],
    certification: null,
    restatement: {
      restatementId: "restatement-alpha-1",
      priorPackageId: "accounting-report-package-alpha-202604",
      reasonCode: "late-fee-accrual",
      approvalState: "Submitted",
      requestedBy: "controller",
      requestedAtUtc: "2026-06-02T04:00:00Z",
      evidenceLinks: ["evidence/restatement"]
    }
  },
  investorCapitalStatements: [
    {
      statementId: "investor-capital-alpha-1",
      fundProfileId: "fund-alpha",
      capitalAccountId: "capital-alpha-1",
      investorId: "investor-alpha",
      periodId: "2026-05",
      beginningCapital: 100000,
      contributions: 25000,
      distributions: 10000,
      realizedGainLoss: 4500,
      endingCapital: 119500,
      currency: "USD",
      certificationState: "ReadyForReview",
      evidenceLinks: ["evidence/investor-statement"]
    }
  ],
  realizedGainLoss: {
    reportId: "rgl-alpha-202605",
    fundProfileId: "fund-alpha",
    ledgerBookId: "book-alpha",
    periodId: "2026-05",
    dimensions: {
      fundId: "fund-alpha",
      entityId: "entity-alpha",
      capitalAccountId: "capital-alpha-1",
      externalGlDimensions: { class: "private-fund" }
    },
    realizedGainLoss: 4500,
    currency: "USD",
    certificationState: "ReadyForReview",
    evidenceLinks: ["evidence/rgl"]
  },
  navPackage: {
    packageId: "nav-alpha-202605",
    fundProfileId: "fund-alpha",
    ledgerBookId: "book-alpha",
    periodId: "2026-05",
    nav: 119500,
    currency: "USD",
    certificationState: "ReadyForReview",
    evidenceLinks: ["evidence/nav"],
    certification: null,
    restatement: null
  },
  certification: {
    certificationId: "cert-alpha-202605",
    state: "ReadyForReview",
    actor: "controller",
    recordedAtUtc: "2026-06-02T04:30:00Z",
    summary: "Controller package ready for review.",
    evidenceLinks: ["evidence/certification"]
  },
  validationIssues: [
    {
      code: "PACKAGE_REVIEW_PENDING",
      severity: "Warning",
      message: "Package is not certified yet.",
      targetId: "cert-alpha-202605"
    }
  ],
  exportArtifacts: [
    {
      artifactId: "report-export-financial-statements",
      artifactKind: "financial-statements",
      displayName: "Financial statement package",
      format: "pdf",
      route: "/api/ledger/reports/accounting-packages/accounting-report-package-alpha-202605/exports/report-export-financial-statements",
      certificationState: "ReadyForReview",
      generatedAtUtc: "2026-06-02T04:30:00Z",
      contentHash: "hash-financial-statements",
      evidenceLinks: ["evidence/financial-statements"],
      sourceStatementId: "accounting-report-package-alpha-202605"
    },
    {
      artifactId: "report-export-nav",
      artifactKind: "nav-package",
      displayName: "NAV package",
      format: "pdf",
      route: "/api/ledger/reports/accounting-packages/accounting-report-package-alpha-202605/exports/report-export-nav",
      certificationState: "Certified",
      generatedAtUtc: "2026-06-02T04:30:00Z",
      contentHash: "hash-nav",
      evidenceLinks: ["evidence/nav"],
      sourceStatementId: "nav-alpha-202605"
    }
  ]
};

const accountingReportExportManifest: ReportExportArtifactManifest = {
  packageId: "accounting-report-package-alpha-202605",
  artifactId: "report-export-financial-statements",
  artifactKind: "financial-statements",
  displayName: "Financial statement package",
  format: "pdf",
  route: "/api/ledger/reports/accounting-packages/accounting-report-package-alpha-202605/exports/report-export-financial-statements",
  certificationState: "ReadyForReview",
  generatedAtUtc: "2026-06-02T04:30:00Z",
  contentHash: "hash-financial-statements",
  contentType: "application/pdf",
  fileName: "financial-statements-alpha-202605.pdf",
  externalPostingAllowed: false,
  payload: "{\"packageId\":\"accounting-report-package-alpha-202605\"}",
  evidenceLinks: ["evidence/financial-statements"],
  sourceStatementId: "accounting-report-package-alpha-202605"
};

const accountingSystemProvider: AccountingSystemProvider = {
  providerId: "quickbooks-fixture",
  displayName: "QuickBooks fixture",
  state: "Planned",
  requiresCredentials: false,
  supportsChartOfAccounts: true,
  supportsJournalEntries: true,
  supportsTrialBalance: true,
  supportsPosting: false,
  statusLabel: "Planned",
  statusDetail: "Posting is disabled for fixture evidence.",
  evidenceKinds: ["trial-balance"]
};

const accountingSystemReconciliation: AccountingSystemReconciliationSummary = {
  reconciliationId: "gl-recon-1",
  importId: "gl-import-1",
  providerId: "quickbooks-fixture",
  fundProfileId: "fund-alpha",
  periodStart: "2026-05-01",
  periodEnd: "2026-05-31",
  generatedAtUtc: "2026-06-01T03:00:00Z",
  matchedCount: 1,
  breakCount: 1,
  totalExternalDebits: 100,
  totalExternalCredits: 100,
  totalMeridianDebits: 95,
  totalMeridianCredits: 95,
  postingEnabled: false,
  postingDisabledReason: "Read-only fixture.",
  evidenceReferences: [],
  rows: [
    {
      rowId: "row-1",
      accountCode: "1000",
      accountName: "Cash",
      currency: "USD",
      status: "ReviewRequired",
      externalDebit: 100,
      externalCredit: 0,
      meridianDebit: 95,
      meridianCredit: 0,
      variance: 5,
      detail: "Cash variance requires controller review.",
      evidenceRef: null
    }
  ]
};

const multiAssetCoverage: MultiAssetCoverageSummary = {
  fundAccountId: "fund-alpha",
  entity: "Fund Alpha",
  asOfUtc: "2026-06-01T03:00:00Z",
  metrics: [],
  drillThroughRoutes: { coverage: "/api/workstation/portfolio/multi-asset-coverage" },
  assetClasses: [
    {
      assetClass: "PrivateCredit",
      displayName: "Private credit",
      status: "ReviewRequired",
      statusLabel: "Review required",
      summary: "Valuation source is stale.",
      evidenceRequirements: [],
      blockers: [
        {
          code: "VALUATION_STALE",
          severity: "Warning",
          message: "Private credit valuation is stale.",
          source: "valuation-feed",
          evidenceRoute: "/accounting/security-master"
        }
      ],
      ledgerClassification: {},
      reconciliationSignals: {}
    }
  ]
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

describe("accounting-screen view model", () => {
  it("derives a route-aware accounting workflow launch model", () => {
    const closeCommandCenter = buildCloseCommandCenterViewState({
      data: accountingWorkspace,
      workflow: closeWorkflow,
      workflowLoading: false,
      workflowError: null,
      accountingSystemProviders: [],
      accountingSystemImport: null,
      accountingSystemReconciliation: null,
      multiAssetCoverage: null
    });

    const state = buildAccountingWorkflowLaunchViewState({
      data: accountingWorkspace,
      workstream: "reconciliation",
      closeCommandCenter
    });

    expect(state).toMatchObject({
      title: "Accounting workflow",
      activeLabel: "Reconciliation active",
      statusLabel: "Blocked",
      statusTone: "danger",
      ariaLabel: "Accounting workflow launch paths"
    });
    expect(state.steps.map((step) => step.href)).toEqual([
      "/accounting/configure",
      "/accounting/journal-entries",
      "/accounting/capital-accounts",
      "/accounting/ledger",
      "/accounting/reconciliation",
      "/accounting/exceptions",
      "/accounting/security-master",
      "/accounting/approvals",
      "/reporting/evidence"
    ]);
    expect(state.steps.find((step) => step.id === "reconciliation")).toMatchObject({
      metricLabel: "Open breaks",
      metricValue: "1",
      statusLabel: "Review breaks",
      tone: "warning",
      isActive: true
    });
    expect(state.steps.find((step) => step.id === "approvals")).toMatchObject({
      metricValue: "2",
      statusLabel: "Signer review",
      tone: "warning"
    });
    expect(state.actionRows.map((action) => action.href)).toEqual([
      "/accounting/reconciliation",
      "/accounting/journal-entries",
      "/accounting/approvals",
      "/reporting/evidence"
    ]);
    expect(state.actionRows.find((action) => action.id === "evidence")).toMatchObject({
      label: "Attach evidence",
      tone: "warning"
    });
  });

  it("loads shared close plans and builds certified accounting report package requests", async () => {
    const getClosePlan = vi.fn(async () => closePeriodPlan);
    const createLateAdjustment = vi.fn(async () => closePeriodPlan);
    const reviewLateAdjustment = vi.fn(async () => closePeriodPlan);
    const signOffCloseTask = vi.fn(async () => closePeriodPlan);
    const buildPackage = vi.fn(async () => accountingReportPackage);
    const certifyPackage = vi.fn(async () => ({
      ...accountingReportPackage,
      financialStatements: {
        ...accountingReportPackage.financialStatements,
        certificationState: "Certified" as const
      },
      investorCapitalStatements: accountingReportPackage.investorCapitalStatements.map((statement) => ({
        ...statement,
        certificationState: "Certified" as const
      })),
      realizedGainLoss: {
        ...accountingReportPackage.realizedGainLoss,
        certificationState: "Certified" as const
      },
      navPackage: {
        ...accountingReportPackage.navPackage,
        certificationState: "Certified" as const
      },
      certification: {
        ...accountingReportPackage.certification,
        state: "Certified" as const,
        summary: "Controller certified retained report package."
      }
    }));
    const getExportManifest = vi.fn(async () => accountingReportExportManifest);
    const listPackages = vi.fn(async () => [accountingReportPackage]);
    const services: AccountingCloseReportPackageServices = {
      getClosePlan,
      createLateAdjustment,
      reviewLateAdjustment,
      signOffCloseTask,
      buildPackage,
      certifyPackage,
      getExportManifest,
      listPackages
    };

    const { result } = renderHook(() => useAccountingCloseReportPackageViewModel(closeWorkflow, services));

    await waitFor(() => expect(result.current.packageRows).toHaveLength(1));

    expect(getClosePlan).toHaveBeenCalledWith("workflow-close-1");
    expect(listPackages).toHaveBeenCalledWith({
      fundProfileId: "fund-alpha",
      periodId: "2026-05"
    });
    expect(result.current).toMatchObject({
      statusLabel: "Ready for review package",
      statusTone: "warning",
      fundLabel: "fund-alpha",
      lockLabel: "Period open",
      certifyButtonLabel: "Certify package",
      certifyDisabledReason: null
    });
    expect(result.current.materialityLabel).toContain("controller");
    expect(result.current.metrics.find((metric) => metric.id === "calendar")).toMatchObject({
      label: "Calendar",
      value: "1",
      detail: "1 calendar milestone sequenced.",
      tone: "success"
    });
    expect(result.current.closeCalendar[0]).toMatchObject({
      displayName: "Finalize NAV package",
      ownerLabel: "fund-accounting",
      dueDateLabel: "2026-06-04",
      statusLabel: "Ready for sign-off",
      statusTone: "success",
      dependencyLabel: "1 dependency",
      signOffLabel: "1/1 sign-offs",
      evidenceLabel: "2 evidence links",
      lockedLabel: "Open period",
      blockerLabel: "Controller sign-off pending."
    });
    expect(result.current.tasks[0]).toMatchObject({
      displayName: "Finalize NAV package",
      statusLabel: "Ready for sign-off",
      dependencyLabel: expect.stringContaining("task-reconciliation"),
      signOffLabel: "1/1 required sign-offs approved",
      signOffDetailLabel: "Approved by ops-user on Jun 2, 05:00 UTC | Controller retained NAV package sign-off.",
      signOffRequirementLabel: "controller: 1/1",
      evidenceLabel: "2 evidence links",
      blockerLabel: "Controller sign-off pending."
    });
    expect(result.current.lateAdjustments[0]).toMatchObject({
      journalEntryId: "manual-je-late-1",
      reason: "Late custodian fee accrual.",
      decisionLabel: "Approved by ops-user on Jun 2, 04:00 UTC",
      evidenceLabel: "2 evidence links",
      materialityLabel: "Within materiality: at or below $2,500 USD; controller review policy",
      materialityTone: "success"
    });
    expect(result.current.packageRows[0]).toMatchObject({
      packageId: "accounting-report-package-alpha-202605",
      certificationLabel: "Ready for review",
      navLabel: "$119,500 USD",
      investorStatementLabel: "1 investor statement",
      realizedGainLossLabel: "+$4,500.00 USD",
      restatementLabel: "late-fee-accrual | Submitted",
      exportArtifactLabel: "1/2 exports certified",
      exportArtifactTone: "warning",
      evidenceLabel: "6 evidence links",
      validationLabel: "1 validation issue",
      selected: true
    });
    expect(result.current.exportManifestButtonLabel).toBe("Inspect Financial statement package");
    expect(result.current.exportManifestDisabledReason).toBeNull();
    expect(result.current.validationIssues.map((issue) => issue.label)).toEqual([
      "Warning | CLOSE_TASK_PENDING",
      "Warning | PACKAGE_REVIEW_PENDING"
    ]);
    expect(result.current.certificationSafeguards).toEqual([
      expect.objectContaining({
        id: "checklist-signoff",
        label: "Checklist sign-off",
        value: "0/1",
        detail: "1 close task still requires retained sign-off evidence.",
        tone: "warning"
      }),
      expect.objectContaining({
        id: "period-lock",
        label: "Period lock",
        value: "Open for adjustments",
        detail: "Period remains open, so late adjustments can still change the package evidence set.",
        tone: "warning"
      }),
      expect.objectContaining({
        id: "critical-validation",
        label: "Critical validation blockers",
        value: "Clear",
        detail: "No critical close or report package validation issues are surfaced.",
        tone: "success"
      }),
      expect.objectContaining({
        id: "export-certification",
        label: "Export certification",
        value: "1/2",
        detail: "1 export artifact remains ready for review.",
        tone: "warning"
      }),
      expect.objectContaining({
        id: "restatement-workflow",
        label: "Restatement workflow",
        value: "Submitted",
        detail: "late-fee-accrual restates accounting-report-package-alpha-202604; approval evidence is retained with the package.",
        tone: "warning"
      }),
      expect.objectContaining({
        id: "evidence-package",
        label: "Evidence package",
        value: "7 evidence links",
        detail: "Certification will submit the retained financial statement, investor capital, realized gain/loss, NAV, restatement, and package evidence links.",
        tone: "success"
      })
    ]);

    await act(async () => {
      await result.current.inspectSelectedPackageExport();
    });

    expect(getExportManifest).toHaveBeenCalledWith(
      "accounting-report-package-alpha-202605",
      "report-export-financial-statements"
    );
    expect(result.current.exportManifestStatusText).toBe("Loaded export manifest report-export-financial-statements.");
    expect(result.current.exportManifestStatusTone).toBe("success");
    expect(result.current.exportManifest).toMatchObject({
      packageId: "accounting-report-package-alpha-202605",
      artifactId: "report-export-financial-statements",
      displayName: "Financial statement package",
      formatLabel: "pdf | application/pdf",
      fileName: "financial-statements-alpha-202605.pdf",
      certificationLabel: "Ready for review",
      evidenceLabel: "1 evidence link",
      postingLabel: "External posting disabled",
      routeLabel: "/api/ledger/reports/accounting-packages/accounting-report-package-alpha-202605/exports/report-export-financial-statements"
    });

    await act(async () => {
      await result.current.buildReportPackage();
    });

    expect(buildPackage).toHaveBeenCalledWith(expect.objectContaining({
      actor: "browser-accounting-operator",
      closeWorkflowId: "workflow-close-1",
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-alpha",
      periodId: "2026-05",
      capitalAccountId: "capital-alpha-1",
      investorId: "investor-alpha",
      beginningCapital: 100000,
      contributions: 25000,
      distributions: 10000,
      realizedGainLoss: 4500,
      nav: 119500,
      currency: "USD",
      correlationId: "browser-close-report-workflow-close-1",
      evidenceLinks: expect.arrayContaining(["evidence/nav-package", "evidence/late-adjustment"])
    }));
    expect(result.current.buildStatusText).toContain("accounting-report-package-alpha-202605");

    await act(async () => {
      await result.current.certifyPackage();
    });

    expect(certifyPackage).toHaveBeenCalledWith(expect.objectContaining({
      packageId: "accounting-report-package-alpha-202605",
      actor: "browser-accounting-controller",
      notes: expect.stringContaining("Certified accounting report package accounting-report-package-alpha-202605"),
      correlationId: "browser-certify-report-accounting-report-package-alpha-202605",
      evidenceLinks: expect.arrayContaining([
        "evidence/certification",
        "evidence/nav",
        "evidence:report-certification:accounting-report-package-alpha-202605"
      ])
    }));
    expect(result.current.certifyStatusText).toContain("Certified report package accounting-report-package-alpha-202605");
    expect(result.current.packageRows[0]).toMatchObject({
      certificationLabel: "Certified",
      certificationTone: "success"
    });
    expect(createLateAdjustment).not.toHaveBeenCalled();
    expect(reviewLateAdjustment).not.toHaveBeenCalled();
    expect(signOffCloseTask).not.toHaveBeenCalled();
  });

  it("reviews pending late adjustments through the shared close-management endpoint", async () => {
    const pendingLateAdjustmentPlan: ClosePeriodPlan = {
      ...closePeriodPlan,
      lateAdjustments: closePeriodPlan.lateAdjustments.map((adjustment) => ({
        ...adjustment,
        amount: 4000,
        approvalState: "Submitted" as const,
        decidedBy: null,
        decidedAtUtc: null,
        decisionNotes: null
      }))
    };
    const reviewedLateAdjustmentPlan: ClosePeriodPlan = {
      ...pendingLateAdjustmentPlan,
      lateAdjustments: pendingLateAdjustmentPlan.lateAdjustments.map((adjustment) => ({
        ...adjustment,
        approvalState: "Approved" as const,
        decidedBy: "browser-accounting-controller",
        decidedAtUtc: "2026-06-03T12:45:00Z",
        decisionNotes: "Controller approved material late adjustment from browser close cockpit."
      }))
    };
    const getClosePlan = vi.fn(async () => pendingLateAdjustmentPlan);
    const createLateAdjustment = vi.fn(async () => pendingLateAdjustmentPlan);
    const reviewLateAdjustment = vi.fn(async () => reviewedLateAdjustmentPlan);
    const signOffCloseTask = vi.fn(async () => pendingLateAdjustmentPlan);
    const buildPackage = vi.fn(async () => accountingReportPackage);
    const certifyPackage = vi.fn(async () => accountingReportPackage);
    const getExportManifest = vi.fn(async () => accountingReportExportManifest);
    const listPackages = vi.fn(async () => [accountingReportPackage]);
    const services: AccountingCloseReportPackageServices = {
      getClosePlan,
      createLateAdjustment,
      reviewLateAdjustment,
      signOffCloseTask,
      buildPackage,
      certifyPackage,
      getExportManifest,
      listPackages
    };

    const { result } = renderHook(() => useAccountingCloseReportPackageViewModel(closeWorkflow, services));

    await waitFor(() => expect(result.current.lateAdjustments).toHaveLength(1));

    expect(result.current.lateAdjustments[0]).toMatchObject({
      requestId: "late-adjustment-1",
      statusLabel: "Submitted",
      decisionLabel: null,
      materialityLabel: "Material adjustment: exceeds $2,500 USD; controller review required",
      materialityTone: "warning",
      reviewDisabledReason: null
    });

    await act(async () => {
      await result.current.reviewLateAdjustment("late-adjustment-1", "Approved");
    });

    expect(reviewLateAdjustment).toHaveBeenCalledWith(expect.objectContaining({
      workflowId: "workflow-close-1",
      requestId: "late-adjustment-1",
      decision: "Approved",
      actor: "browser-accounting-controller",
      notes: "Approved late adjustment late-adjustment-1 from the Accounting close cockpit.",
      correlationId: "browser-late-adjustment-review-workflow-close-1-late-adjustment-1-approved",
      evidenceLinks: expect.arrayContaining([
        "browser://accounting/close/late-adjustments/review/workflow-close-1/late-adjustment-1/approved",
        "evidence/late-adjustment"
      ])
    }));
    expect(result.current.reviewLateAdjustmentStatusText).toBe("Late adjustment late-adjustment-1 approved.");
    expect(result.current.reviewLateAdjustmentStatusTone).toBe("success");
    expect(result.current.lateAdjustments[0]).toMatchObject({
      statusLabel: "Approved",
      decisionLabel: "Approved by browser-accounting-controller on Jun 3, 12:45 UTC",
      reviewDisabledReason: "Late adjustment is already approved."
    });
    expect(createLateAdjustment).not.toHaveBeenCalled();
    expect(signOffCloseTask).not.toHaveBeenCalled();
    expect(buildPackage).not.toHaveBeenCalled();
    expect(certifyPackage).not.toHaveBeenCalled();
  });

  it("signs off the next ready close checklist task through the shared close endpoint", async () => {
    const unsignedClosePlan: ClosePeriodPlan = {
      ...closePeriodPlan,
      tasks: closePeriodPlan.tasks.map((task) => ({
        ...task,
        signOffs: [],
        signOffRequirements: task.signOffRequirements?.map((requirement) => ({
          ...requirement,
          approvedCount: 0,
          isSatisfied: false
        })) ?? null
      }))
    };
    const signedClosePlan: ClosePeriodPlan = {
      ...unsignedClosePlan,
      tasks: unsignedClosePlan.tasks.map((task) => ({
        ...task,
        status: "SignedOff" as const,
        signOffs: [
          {
            signOffId: "signoff-browser-controller",
            role: "controller",
            actor: "browser-accounting-controller",
            approvalState: "Approved" as const,
            signedAtUtc: "2026-06-03T12:30:00Z",
            evidenceLinks: ["browser://accounting/close/sign-off/workflow-close-1/task-nav"],
            notes: "Approved from browser close cockpit."
          }
        ],
        signOffRequirements: task.signOffRequirements?.map((requirement) => ({
          ...requirement,
          approvedCount: requirement.requiredApprovalCount,
          isSatisfied: true
        })) ?? null
      }))
    };
    const getClosePlan = vi.fn(async () => unsignedClosePlan);
    const createLateAdjustment = vi.fn(async () => unsignedClosePlan);
    const reviewLateAdjustment = vi.fn(async () => unsignedClosePlan);
    const signOffCloseTask = vi.fn(async () => signedClosePlan);
    const buildPackage = vi.fn(async () => accountingReportPackage);
    const certifyPackage = vi.fn(async () => accountingReportPackage);
    const getExportManifest = vi.fn(async () => accountingReportExportManifest);
    const listPackages = vi.fn(async () => [accountingReportPackage]);
    const services: AccountingCloseReportPackageServices = {
      getClosePlan,
      createLateAdjustment,
      reviewLateAdjustment,
      signOffCloseTask,
      buildPackage,
      certifyPackage,
      getExportManifest,
      listPackages
    };

    const { result } = renderHook(() => useAccountingCloseReportPackageViewModel(closeWorkflow, services));

    await waitFor(() => expect(result.current.tasks).toHaveLength(1));

    expect(result.current.signOffButtonLabel).toBe("Sign off Finalize NAV package");
    expect(result.current.signOffDisabledReason).toBeNull();
    expect(result.current.tasks[0]).toMatchObject({
      signOffLabel: "0/1 required sign-offs approved",
      signOffRequirementLabel: "controller: 0/1"
    });

    await act(async () => {
      await result.current.signOffNextTask();
    });

    expect(signOffCloseTask).toHaveBeenCalledWith(expect.objectContaining({
      workflowId: "workflow-close-1",
      taskId: "task-nav",
      role: "controller",
      decision: "Approved",
      actor: "browser-accounting-controller",
      notes: "Approved Finalize NAV package close checklist task from the Accounting close cockpit.",
      correlationId: "browser-close-signoff-workflow-close-1-task-nav",
      evidenceLinks: expect.arrayContaining([
        "browser://accounting/close/sign-off/workflow-close-1/task-nav",
        "evidence/nav-package"
      ])
    }));
    expect(result.current.signOffStatusText).toBe("Signed off Finalize NAV package.");
    expect(result.current.signOffStatusTone).toBe("success");
    expect(result.current.tasks[0]).toMatchObject({
      statusLabel: "Signed off",
      signOffLabel: "1/1 required sign-offs approved",
      signOffRequirementLabel: "controller: 1/1"
    });
    expect(result.current.signOffDisabledReason).toBe("No close checklist task is ready for sign-off.");
    expect(createLateAdjustment).not.toHaveBeenCalled();
    expect(reviewLateAdjustment).not.toHaveBeenCalled();
    expect(buildPackage).not.toHaveBeenCalled();
    expect(certifyPackage).not.toHaveBeenCalled();
  });

  it("derives the accounting workstream and selected reconciliation run", () => {
    expect(resolveAccountingWorkstream("/accounting/security-master")).toBe("security-master");
    expect(resolveAccountingWorkstream("/accounting/reconciliation")).toBe("reconciliation");
    expect(resolveAccountingWorkstream("/accounting/exceptions")).toBe("exceptions");
    expect(resolveAccountingWorkstream("/accounting/approvals")).toBe("approvals");
    expect(resolveAccountingWorkstream("/accounting/capital-accounts")).toBe("capital-accounts");
    expect(resolveAccountingWorkstream("/accounting")).toBe("ledger");
    expect(resolveAccountingWorkstream("/accounting/ledger")).toBe("ledger");
    expect(resolveAccountingWorkstream("/reporting")).toBe("reporting");
    expect(resolveAccountingWorkstream("/governance/security-master")).toBe("security-master");
    expect(resolveAccountingWorkstream("/governance/reconciliation")).toBe("reconciliation");
    expect(resolveAccountingWorkstream("/governance")).toBe("ledger");

    expect(resolveSelectedReconciliation(reconciliationQueue, "run-57")?.runId).toBe("run-57");
    expect(resolveSelectedReconciliation(reconciliationQueue, null)?.runId).toBe("run-42");
    expect(resolveSelectedReconciliation([], null)).toBeNull();
  });

  it("loads Accounting Rules Studio rules and runs shared dry-run previews", async () => {
    const workspace: AccountingConfigurationWorkspace = {
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      status: "Draft",
      configurationVersion: "v4",
      updatedAtUtc: "2026-06-30T12:00:00Z",
      ledgerBooks: [{
        ledgerBookId: "book-primary",
        fundProfileId: "fund-alpha",
        fundStructureNodeId: "entity-master",
        fundStructureNodeKind: "Entity",
        displayName: "Primary book",
        baseCurrency: "USD",
        createdAt: "2026-01-01T00:00:00Z",
        updatedAt: "2026-06-30T12:00:00Z",
        description: "Primary accounting book.",
        accountingBasis: "Gaap",
        accountingPolicyId: "policy-gaap",
        accountingPolicyVersion: "2026.06"
      }],
      chartOfAccounts: [
        {
          nodeId: "coa-cash",
          path: "1000.Cash",
          accountName: "Cash",
          accountType: "Asset",
          parentPath: null,
          isArchived: false
        },
        {
          nodeId: "coa-investment",
          path: "1200.Investments",
          accountName: "Investments",
          accountType: "Asset",
          parentPath: null,
          isArchived: false
        }
      ],
      journalTemplates: [{
        templateId: "template-trade-buy",
        displayName: "Trade buy settlement",
        description: "Balanced trade settlement posting.",
        isArchived: false,
        version: "v2",
        lines: [
          {
            lineId: "line-investment",
            accountPath: "1200.Investments",
            side: "Debit",
            amount: 250000,
            currency: "USD",
            description: "Investment cost"
          },
          {
            lineId: "line-cash",
            accountPath: "1000.Cash",
            side: "Credit",
            amount: 250000,
            currency: "USD",
            description: "Cash settlement"
          }
        ]
      }],
      postingRules: [{
        ruleId: "rule-trade-buy",
        displayName: "Trade buy posting",
        sourceEventType: "TradeExecuted",
        templateId: "template-trade-buy",
        ruleVersion: "v3",
        isArchived: false,
        description: "Generate trade buy settlement postings.",
        effectiveFrom: "2026-01-01",
        effectiveTo: "2026-12-31",
        priority: 10,
        scope: {
          fundId: "fund-alpha",
          entityId: "entity-master",
          strategyId: "strategy-long-only",
          counterpartyId: "cp-001",
          externalGlDimensions: {
            class: "FundAlpha"
          }
        },
        conditions: [
          {
            conditionId: "cond-event",
            field: "event.kind",
            operator: "Equals",
            value: "TradeExecuted",
            isRequired: true,
            description: "Only trade events use this rule."
          },
          {
            conditionId: "cond-amount",
            field: "event.notional",
            operator: "AmountGreaterThanOrEqual",
            value: "100000",
            isRequired: true,
            description: "Controller review threshold."
          }
        ],
        conditionGroups: [{
          groupId: "group-trade-source",
          operator: "Any",
          isRequired: true,
          description: "Allow broker or internal execution sources.",
          conditions: [
            {
              conditionId: "cond-broker-source",
              field: "event.source",
              operator: "Equals",
              value: "Broker",
              isRequired: false,
              description: "Broker feed."
            },
            {
              conditionId: "cond-ops-source",
              field: "event.source",
              operator: "Equals",
              value: "Operations",
              isRequired: false,
              description: "Operations upload."
            }
          ]
        }],
        formulas: [{
          formulaId: "formula-source",
          kind: "SourceAmount",
          value: 250000,
          currency: "USD",
          description: "Use source trade amount."
        }],
        allocations: [{
          allocationRuleId: "alloc-strategy",
          basis: "StrategyWeight",
          weight: 1,
          formulaId: "formula-source",
          targetDimensions: {
            sleeveId: "sleeve-core",
            strategyId: "strategy-long-only"
          },
          description: "Allocate to the core strategy sleeve."
        }],
        generatedPostings: [
          {
            lineId: "generated-investment",
            accountPath: "1200.Investments",
            side: "Debit",
            amountFormulaId: "formula-source",
            amount: 250000,
            currency: "USD",
            dimensions: {
              fundId: "fund-alpha",
              instrumentId: "AAPL"
            },
            description: "Debit investment cost."
          },
          {
            lineId: "generated-cash",
            accountPath: "1000.Cash",
            side: "Credit",
            amountFormulaId: "formula-source",
            amount: 250000,
            currency: "USD",
            dimensions: {
              fundId: "fund-alpha",
              counterpartyId: "cp-001"
            },
            description: "Credit cash settlement."
          }
        ],
        versions: [{
          version: "v3",
          createdAtUtc: "2026-06-15T10:00:00Z",
          createdBy: "controller",
          changeSummary: "Added counterparty scope and generated postings.",
          promotionApproval: null,
          evidenceLinks: ["evidence://rule/v3"]
        }],
        promotionApproval: {
          approvalId: "approval-rule-trade-buy",
          requestedBy: "controller",
          requestedAtUtc: "2026-06-15T10:00:00Z",
          approvalState: "Approved",
          approvedBy: "cfo",
          approvedAtUtc: "2026-06-15T11:00:00Z",
          notes: "Approved for production dry-run.",
          evidenceLinks: ["evidence://approval"]
        },
        requiresPromotionApproval: true
      }],
      validationIssues: [],
      rulesStudio: {
        summary: {
          totalRules: 1,
          activeRules: 1,
          archivedRules: 0,
          effectiveDatedRules: 1,
          generatedPostingRules: 1,
          templateMappingRules: 0,
          rulesWithConditions: 1,
          rulesWithFormulas: 1,
          rulesWithAllocations: 1,
          rulesRequiringPromotionApproval: 1,
          approvedPromotionRules: 1,
          pendingPromotionApprovalRules: 0,
          savedTestCaseCount: 1,
          rulesWithSavedRegressionTests: 1,
          rulesMissingCurrentVersionRegressionTests: 0,
          criticalIssueCount: 0,
          warningIssueCount: 0
        },
        rules: [{
          ruleId: "rule-trade-buy",
          displayName: "Trade buy posting",
          sourceEventType: "TradeExecuted",
          ruleVersion: "v3",
          priority: 10,
          effectiveFrom: "2026-01-01",
          effectiveTo: "2026-12-31",
          templateId: "template-trade-buy",
          isArchived: false,
          usesGeneratedPostings: true,
          conditionCount: 2,
          conditionGroupCount: 1,
          formulaCount: 1,
          allocationCount: 1,
          generatedPostingLineCount: 2,
          versionCount: 1,
          savedTestCaseCount: 1,
          savedTestEvidenceLinkCount: 1,
          requiresPromotionApproval: true,
          isPromotionApproved: true,
          promotionApprovalState: "Approved",
          promotionApprovalId: "approval-rule-trade-buy",
          criticalIssueCount: 0,
          warningIssueCount: 0,
          canDryRun: true,
          canRequestPromotion: false,
          canActivate: true
        }],
        promotionQueue: []
      },
      auditTrail: [{
        auditEventId: "audit-rule-trade-buy",
        recordedAtUtc: "2026-06-15T11:00:00Z",
        actor: "controller",
        action: "rule.promoted",
        fundProfileId: "fund-alpha",
        ledgerBookId: "book-primary",
        correlationId: "corr-rule",
        beforeHash: "before-hash-123456",
        afterHash: "after-hash-654321",
        validationIssues: [],
        evidenceLinks: ["evidence://approval"]
      }],
      ruleTestCases: [{
        testCaseId: "rule-test-trade-buy-saved",
        displayName: "Saved trade buy regression",
        request: {
          fundProfileId: "fund-alpha",
          ledgerBookId: "book-primary",
          sourceEventType: "TradeExecuted",
          eventAmount: 250000,
          currency: "USD",
          effectiveDate: "2026-06-30",
          actor: "controller",
          dimensions: {
            fundId: "fund-alpha",
            entityId: "entity-master",
            counterpartyId: "cp-001"
          },
          counterpartyId: "cp-001"
        },
        expectedRuleId: "rule-trade-buy",
        expectedRuleVersion: "v3",
        expectBalancedPosting: true,
        expectedIssueCodes: [],
        evidenceLinks: ["evidence://accounting/rule-tests/trade-buy"]
      }]
    };
    const dryRunResult: RuleDryRunResult = {
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      sourceEventType: "TradeExecuted",
      effectiveDate: "2026-01-01",
      eventAmount: 250000,
      currency: "USD",
      isPostingBalanced: true,
      selectedRuleId: "rule-trade-buy",
      ruleMatches: [{
        ruleId: "rule-trade-buy",
        displayName: "Trade buy posting",
        ruleVersion: "v3",
        priority: 10,
        isMatched: true,
        explanations: ["Effective date and source event predicates matched."],
        validationIssues: []
      }],
      generatedLines: [
        {
          accountPath: "1200.Investments",
          accountName: "Investments",
          side: "Debit",
          amount: 250000,
          currency: "USD",
          description: "Debit investment cost."
        },
        {
          accountPath: "1000.Cash",
          accountName: "Cash",
          side: "Credit",
          amount: 250000,
          currency: "USD",
          description: "Credit cash settlement."
        }
      ],
      generatedPostingLines: workspace.postingRules[0].generatedPostings,
      validationIssues: []
    };
    const productionReadiness: AccountingProductionReadiness = {
      generatedAtUtc: "2026-06-30T12:15:00Z",
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      status: "ReviewRequired",
      score: 78,
      externalGlProviderCount: 3,
      certifiedExternalGlMappingProfileCount: 1,
      externalGlLivePostingEnabled: false,
      criticalIssueCount: 0,
      warningIssueCount: 1,
      ledgerBookRollout: {
        generatedAtUtc: "2026-06-30T12:15:00Z",
        fundProfileId: "fund-alpha",
        fundStructureNodeId: "entity-master",
        fundStructureNodeKind: "Entity",
        accountingBasis: "Gaap",
        books: [],
        issues: [],
        isReady: true,
        criticalIssueCount: 0,
        warningIssueCount: 0,
        bookCount: 1,
        openPeriodCount: 1
      },
      rulesStudioSummary: workspace.rulesStudio.summary,
      components: [
        {
          area: "RulesStudio",
          label: "Rules Studio",
          status: "Ready",
          score: 92,
          summary: "Rule versions, dry-run regression cases, and promotion approvals are retained.",
          issues: [],
          evidenceReferences: ["evidence://rule/v3"],
          route: "/accounting/configure"
        },
        {
          area: "TenantAdministration",
          label: "Tenant administration",
          status: "ReviewRequired",
          score: 50,
          summary: "Tenant setup operator workflow still needs completion.",
          issues: [
            {
              code: "tenant-admin.operator-surface-required",
              area: "TenantAdministration",
              severity: "Warning",
              message: "Production rollout still needs tenant setup controls.",
              suggestedAction: "Bind admin setup screens to this shared readiness contract.",
              evidenceReferences: ["evidence://tenant-admin/gap"]
            }
          ],
          evidenceReferences: ["evidence://tenant-admin/gap"],
          route: "/settings"
        }
      ],
      issues: [
        {
          code: "tenant-admin.operator-surface-required",
          area: "TenantAdministration",
          severity: "Warning",
          message: "Production rollout still needs tenant setup controls.",
          suggestedAction: "Bind admin setup screens to this shared readiness contract.",
          evidenceReferences: ["evidence://tenant-admin/gap"]
        }
      ]
    };
    let retainedWorkspace = workspace;
    const upsertRule = vi.fn(async (request: Parameters<AccountingConfigurationServices["upsertRule"]>[0]) => {
      const existingRuleIndex = retainedWorkspace.postingRules.findIndex((rule) => rule.ruleId === request.rule.ruleId);
      const postingRules = existingRuleIndex >= 0
        ? retainedWorkspace.postingRules.map((rule, index) => index === existingRuleIndex ? request.rule : rule)
        : [...retainedWorkspace.postingRules, request.rule];
      retainedWorkspace = {
        ...retainedWorkspace,
        postingRules,
        auditTrail: [
          ...retainedWorkspace.auditTrail,
          {
            auditEventId: `audit-rule-upsert-${retainedWorkspace.auditTrail.length + 1}`,
            action: "posting-rule.upsert",
            actor: request.actor,
            fundProfileId: request.fundProfileId,
            ledgerBookId: null,
            correlationId: request.correlationId ?? null,
            recordedAtUtc: "2026-06-30T12:10:00Z",
            beforeHash: "before-rule-upsert",
            afterHash: "after-rule-upsert",
            validationIssues: [],
            evidenceLinks: request.evidenceLinks ?? []
          }
        ]
      };
      return retainedWorkspace;
    });
    const services: AccountingConfigurationServices = {
      getConfiguration: vi.fn().mockResolvedValue(workspace),
      assessProductionReadiness: vi.fn().mockResolvedValue(productionReadiness),
      listMigrationRunArtifacts: vi.fn().mockResolvedValue({ fundProfileId: "fund-alpha", ledgerBookId: "book-primary", artifacts: productionReadiness.migrationRunArtifacts ?? [] }),
      createLedgerBook: vi.fn(),
      previewTemplate: vi.fn().mockResolvedValue({
        templateId: "template-trade-buy",
        displayName: "Trade buy settlement",
        isBalanced: true,
        totalDebits: 250000,
        totalCredits: 250000,
        lines: dryRunResult.generatedLines,
        validationIssues: []
      }),
      upsertRule,
      dryRunRule: vi.fn().mockResolvedValue(dryRunResult),
      buildJournalCandidate: vi.fn(),
      runRuleTests: vi.fn().mockResolvedValue({
        fundProfileId: "fund-alpha",
        ledgerBookId: "book-primary",
        executedAtUtc: "2026-06-30T12:05:00Z",
        actor: "browser-accounting-operator",
        totalCount: 1,
        passedCount: 1,
        failedCount: 0,
        results: [{
          testCaseId: "rule-test-rule-trade-buy",
          displayName: "Saved trade buy regression",
          passed: true,
          dryRunResult,
          assertionIssues: []
        }]
      }),
      saveRuleTestCase: vi.fn(async () => {
        retainedWorkspace = {
          ...retainedWorkspace,
          ruleTestCases: [
            ...(retainedWorkspace.ruleTestCases ?? []),
            {
              testCaseId: "rule-test-rule-trade-buy",
              displayName: "Trade buy posting retained dry-run regression",
              request: {
                fundProfileId: "fund-alpha",
                ledgerBookId: "book-primary",
                sourceEventType: "TradeExecuted",
                eventAmount: 250000,
                currency: "USD",
                effectiveDate: "2026-01-01",
                actor: "browser-accounting-operator",
                dimensions: {
                  fundId: "fund-alpha",
                  entityId: "entity-master",
                  strategyId: "strategy-long-only",
                  counterpartyId: "cp-001",
                  externalGlDimensions: {
                    class: "FundAlpha"
                  }
                },
                counterpartyId: "cp-001"
              },
              expectedRuleId: "rule-trade-buy",
              expectedRuleVersion: "v3",
              expectBalancedPosting: true,
              expectedIssueCodes: [],
              evidenceLinks: [
                "browser://accounting/rules-studio/dry-run/rule-trade-buy",
                "browser://accounting/rules-studio/test-case/rule-trade-buy"
              ]
            }
          ]
        };
        return retainedWorkspace;
      }),
      approveRulePromotion: vi.fn().mockResolvedValue(workspace),
      activate: vi.fn().mockResolvedValue(workspace)
    };

    const { result } = renderHook(() => useAccountingConfigurationViewModel(services));

    await waitFor(() => expect(result.current.rules).toHaveLength(1));
    expect(result.current.selectedRule).toMatchObject({
      id: "rule-trade-buy",
      title: "Trade buy posting",
      eventLabel: "TradeExecuted",
      effectiveLabel: "2026-01-01 -> 2026-12-31",
      priorityLabel: "Priority 10",
      promotionLabel: "Approved by cfo",
      statusLabel: "Generated postings"
    });
    expect(result.current.selectedRule?.scopeLabels).toEqual(expect.arrayContaining([
      "Fund: fund-alpha",
      "Entity: entity-master",
      "Counterparty: cp-001",
      "External class: FundAlpha"
    ]));
    expect(result.current.selectedRule?.conditionRows.join("\n")).toContain("event.kind Equals TradeExecuted");
    expect(result.current.selectedRule?.conditionRows.join("\n")).toContain("group-trade-source: Any (required)");
    expect(result.current.selectedRule?.conditionRows.join("\n")).toContain("event.source Equals Broker");
    expect(result.current.selectedRule?.formulaRows.join("\n")).toContain("formula-source: SourceAmount $250,000 USD");
    expect(result.current.selectedRule?.allocationRows.join("\n")).toContain("alloc-strategy: StrategyWeight weight 1 via formula-source");
    expect(result.current.selectedRule?.generatedPostingRows.join("\n")).toContain("Debit 1200.Investments $250,000 USD via formula-source");
    expect(result.current.selectedRule?.versionRows.join("\n")).toContain("v3 by controller on 2026-06-15");
    expect(result.current.metricRows).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "rules",
        value: "1",
        detail: "1 generated / 0 template mappings."
      }),
      expect.objectContaining({
        id: "rule-tests",
        value: "1",
        detail: "1 rule(s) covered; 0 current version gap(s)."
      })
    ]));
    expect(result.current.ledgerBookSummaryLabel).toBe("1 ledger book registered | selected Primary book.");
    expect(result.current.ledgerBookRows).toEqual([
      expect.objectContaining({
        id: "book-primary",
        title: "Primary book",
        statusLabel: "Selected",
        subtitle: "Gaap basis | USD",
        policyLabel: "policy-gaap/2026.06",
        scopeLabel: "fund-alpha / Entity entity-master",
        updatedLabel: "Updated 2026-06-30",
        tone: "success"
      })
    ]);
    expect(services.assessProductionReadiness).toHaveBeenCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      requiredLedgerBookScopes: null
    }));
    expect(result.current.productionReadiness).toMatchObject({
      statusLabel: "Review required",
      scoreLabel: "78/100",
      issueSummaryLabel: "1 warning requires review",
      externalGlLabel: "3 providers | 1 certified mapping | live posting disabled",
      ledgerBookRolloutLabel: "1 book | 1 open period | 0 rollout blockers"
    });
    expect(result.current.productionReadiness.components).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "RulesStudio",
        statusLabel: "Ready",
        tone: "success"
      }),
      expect.objectContaining({
        id: "TenantAdministration",
        statusLabel: "Review required",
        tone: "warning"
      })
    ]));
    expect(result.current.productionReadiness.blockerIssues).toEqual([
      expect.objectContaining({
        label: "Tenant Administration | Warning",
        message: "Production rollout still needs tenant setup controls.",
        tone: "warning"
      })
    ]);
    expect(result.current.selectedRule?.promotionReadiness).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "server-readiness",
        value: "Ready",
        detail: "0 critical, 0 warning, 1 saved test case(s).",
        tone: "success"
      }),
      expect.objectContaining({
        id: "promotion-approval",
        value: "Approved",
        tone: "success"
      }),
      expect.objectContaining({
        id: "saved-regression",
        value: "1 saved",
        tone: "success"
      }),
      expect.objectContaining({
        id: "latest-suite",
        value: "Not run",
        tone: "warning"
      }),
      expect.objectContaining({
        id: "activation-gate",
        value: "Ready",
        tone: "success"
      })
    ]));
    expect(result.current.canDryRun).toBe(true);
    expect(result.current.ruleTestCases).toEqual([
      expect.objectContaining({
        id: "rule-test-trade-buy-saved",
        title: "Saved trade buy regression",
        assertionLabel: "Expect rule-trade-buy version v3, balanced, no expected issue codes, no expected generated posting lines.",
        evidenceLabel: "1 evidence link",
        evidenceTone: "success"
      })
    ]);

    await act(async () => {
      await result.current.dryRunSelectedRule();
    });

    expect(services.dryRunRule).toHaveBeenCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      sourceEventType: "TradeExecuted",
      eventAmount: 250000,
      currency: "USD",
      effectiveDate: "2026-01-01",
      actor: "browser-accounting-operator",
      counterpartyId: "cp-001",
      dimensions: expect.objectContaining({
        fundId: "fund-alpha",
        strategyId: "strategy-long-only"
      })
    }));
    await waitFor(() => expect(result.current.dryRunPreview).not.toBeNull());
    expect(result.current.dryRunPreview).toMatchObject({
      title: "TradeExecuted dry run",
      balanceLabel: "Balanced $250,000 USD",
      selectedRuleLabel: "Selected rule rule-trade-buy"
    });
    expect(result.current.dryRunPreview?.matchRows.join("\n")).toContain("Trade buy posting matched at priority 10");
    expect(result.current.dryRunPreview?.generatedLineRows.join("\n")).toContain("Debit 1200.Investments $250,000 USD");
    expect(result.current.dryRunPreview?.generatedPostingRows.join("\n")).toContain("Credit 1000.Cash $250,000 USD via formula-source");

    await act(async () => {
      await result.current.applyDryRunEventPredicate();
    });

    expect(services.upsertRule).toHaveBeenLastCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      evidenceLinks: ["browser://accounting/rules-studio/event-predicate/rule-trade-buy"],
      rule: expect.objectContaining({
        ruleId: "rule-trade-buy",
        ruleVersion: "v3.event",
        requiresPromotionApproval: true,
        promotionApproval: null,
        conditions: expect.arrayContaining([
          expect.objectContaining({
            conditionId: "rule-trade-buy-source-event",
            field: "event.kind",
            operator: "Equals",
            value: "TradeExecuted",
            isRequired: true
          })
        ])
      })
    }));
    expect(result.current.applyEventPredicateStatusText).toBe("Applied event predicate to rule-trade-buy.");
    await waitFor(() => expect(result.current.selectedRule?.subtitle).toContain("v3.event"));

    await act(async () => {
      await result.current.applyDryRunEffectiveStart();
    });

    expect(services.upsertRule).toHaveBeenLastCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      evidenceLinks: ["browser://accounting/rules-studio/effective-start/rule-trade-buy"],
      rule: expect.objectContaining({
        ruleId: "rule-trade-buy",
        ruleVersion: "v3.event.effective",
        effectiveFrom: "2026-01-01",
        effectiveTo: "2026-12-31",
        requiresPromotionApproval: true,
        promotionApproval: null
      })
    }));
    expect(result.current.applyEffectiveStartStatusText).toBe("Applied effective start to rule-trade-buy.");
    await waitFor(() => expect(result.current.selectedRule?.subtitle).toContain("v3.event.effective"));

    await act(async () => {
      await result.current.captureDryRunGeneratedPostings();
    });

    expect(services.upsertRule).toHaveBeenLastCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      evidenceLinks: ["browser://accounting/rules-studio/generated-postings/rule-trade-buy"],
      rule: expect.objectContaining({
        ruleId: "rule-trade-buy",
        ruleVersion: "v3.event.effective.postings",
        requiresPromotionApproval: true,
        promotionApproval: null,
        generatedPostings: expect.arrayContaining([
          expect.objectContaining({
            lineId: "generated-cash",
            accountPath: "1000.Cash",
            side: "Credit",
            amountFormulaId: "formula-source",
            amount: 250000,
            dimensions: expect.objectContaining({
              fundId: "fund-alpha",
              counterpartyId: "cp-001"
            })
          })
        ])
      })
    }));
    expect(result.current.capturePostingsStatusText).toBe("Captured generated postings for rule-trade-buy.");
    await waitFor(() => expect(result.current.selectedRule?.subtitle).toContain("v3.event.effective.postings"));

    await act(async () => {
      await result.current.applyDryRunScope();
    });

    expect(services.upsertRule).toHaveBeenLastCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      evidenceLinks: ["browser://accounting/rules-studio/scope/rule-trade-buy"],
      rule: expect.objectContaining({
        ruleId: "rule-trade-buy",
        ruleVersion: "v3.event.effective.postings.scope",
        requiresPromotionApproval: true,
        promotionApproval: null,
        scope: expect.objectContaining({
          fundId: "fund-alpha",
          entityId: "entity-master",
          strategyId: "strategy-long-only",
          instrumentId: "AAPL",
          counterpartyId: "cp-001",
          externalGlDimensions: expect.objectContaining({
            class: "FundAlpha"
          })
        })
      })
    }));
    expect(result.current.applyScopeStatusText).toBe("Applied dry-run scope to rule-trade-buy.");
    await waitFor(() => expect(result.current.selectedRule?.subtitle).toContain("v3.event.effective.postings.scope"));

    await act(async () => {
      await result.current.saveDryRunAsRuleTest();
    });

    expect(services.saveRuleTestCase).toHaveBeenCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      testCase: expect.objectContaining({
        testCaseId: "rule-test-rule-trade-buy",
        expectedRuleId: "rule-trade-buy",
        expectedRuleVersion: "v3.event.effective.postings.scope",
        expectBalancedPosting: true,
        evidenceLinks: expect.arrayContaining([
          "browser://accounting/rules-studio/dry-run/rule-trade-buy",
          "browser://accounting/rules-studio/test-case/rule-trade-buy"
        ])
      })
    }));
    expect(result.current.ruleTestCases).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "rule-test-rule-trade-buy",
        title: "Trade buy posting retained dry-run regression",
        evidenceTone: "success"
      })
    ]));

    await act(async () => {
      await result.current.applyDryRunAmountThreshold();
    });

    expect(services.upsertRule).toHaveBeenLastCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      evidenceLinks: ["browser://accounting/rules-studio/threshold/rule-trade-buy"],
      rule: expect.objectContaining({
        ruleId: "rule-trade-buy",
        ruleVersion: "v3.event.effective.postings.scope.threshold",
        requiresPromotionApproval: true,
        promotionApproval: null,
        conditions: expect.arrayContaining([
          expect.objectContaining({
            conditionId: "rule-trade-buy-minimum-amount",
            field: "event.amount",
            operator: "AmountGreaterThanOrEqual",
            value: "250000",
            isRequired: true
          })
        ])
      })
    }));
    expect(result.current.applyThresholdStatusText).toBe("Applied amount threshold to rule-trade-buy.");
    expect(result.current.selectedRule?.conditionRows.join("\n")).toContain("event.amount AmountGreaterThanOrEqual 250000");

    await act(async () => {
      await result.current.applyDryRunFormulaAmount();
    });

    expect(services.upsertRule).toHaveBeenLastCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      evidenceLinks: ["browser://accounting/rules-studio/formula/rule-trade-buy"],
      rule: expect.objectContaining({
        ruleId: "rule-trade-buy",
        ruleVersion: "v3.event.effective.postings.scope.threshold.formula",
        requiresPromotionApproval: true,
        promotionApproval: null,
        formulas: expect.arrayContaining([
          expect.objectContaining({
            formulaId: "formula-source",
            value: 250000,
            currency: "USD",
            description: "Formula amount retained from dry-run 2026-01-01."
          })
        ])
      })
    }));
    expect(result.current.applyFormulaStatusText).toBe("Applied formula amount to rule-trade-buy.");
    expect(result.current.selectedRule?.formulaRows.join("\n")).toContain("formula-source: SourceAmount $250,000 USD - Formula amount retained from dry-run 2026-01-01.");

    await act(async () => {
      await result.current.applyDryRunAllocationTargets();
    });

    expect(services.upsertRule).toHaveBeenLastCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      evidenceLinks: ["browser://accounting/rules-studio/allocation/rule-trade-buy"],
      rule: expect.objectContaining({
        ruleId: "rule-trade-buy",
        ruleVersion: "v3.event.effective.postings.scope.threshold.formula.allocation",
        requiresPromotionApproval: true,
        promotionApproval: null,
        allocations: expect.arrayContaining([
          expect.objectContaining({
            allocationRuleId: "alloc-strategy",
            targetDimensions: expect.objectContaining({
              fundId: "fund-alpha",
              sleeveId: "sleeve-core",
              strategyId: "strategy-long-only"
            })
          })
        ])
      })
    }));
    expect(result.current.applyAllocationStatusText).toBe("Applied allocation targets to rule-trade-buy.");
    expect(result.current.selectedRule?.allocationRows.join("\n")).toContain("Fund: fund-alpha");

    await act(async () => {
      await result.current.raiseSelectedRulePriority();
    });

    expect(services.upsertRule).toHaveBeenLastCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      evidenceLinks: ["browser://accounting/rules-studio/priority/rule-trade-buy"],
      rule: expect.objectContaining({
        ruleId: "rule-trade-buy",
        ruleVersion: "v3.event.effective.postings.scope.threshold.formula.allocation.priority",
        priority: 11,
        requiresPromotionApproval: true,
        promotionApproval: null
      })
    }));
    expect(result.current.raisePriorityStatusText).toBe("Raised priority for rule-trade-buy.");
    expect(result.current.selectedRule?.priorityLabel).toBe("Priority 11");

    await act(async () => {
      await result.current.duplicateSelectedRule();
    });

    expect(services.upsertRule).toHaveBeenCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      evidenceLinks: ["browser://accounting/rules-studio/duplicate/rule-trade-buy"],
      rule: expect.objectContaining({
        displayName: "Trade buy posting draft",
        sourceEventType: "TradeExecuted",
        templateId: "template-trade-buy",
        ruleVersion: "v3.event.effective.postings.scope.threshold.formula.allocation.priority.draft",
        isArchived: false,
        priority: 12,
        requiresPromotionApproval: true,
        promotionApproval: null
      })
    }));
    const duplicateRequest = vi.mocked(services.upsertRule).mock.calls
      .map((call) => call[0])
      .find((request) => request.evidenceLinks?.includes("browser://accounting/rules-studio/duplicate/rule-trade-buy"));
    expect(duplicateRequest).toBeDefined();
    if (!duplicateRequest) {
      throw new Error("Duplicate rule request was not captured.");
    }
    expect(duplicateRequest.ledgerBookId).toBe("book-primary");
    expect(duplicateRequest.rule.ruleId).toMatch(/^rule-trade-buy-draft-\d+$/);
    expect(duplicateRequest.rule.scope).toMatchObject({
      fundId: "fund-alpha",
      entityId: "entity-master",
      externalGlDimensions: {
        class: "FundAlpha"
      }
    });
    expect(duplicateRequest.rule.generatedPostings?.[0].dimensions).toEqual(workspace.postingRules[0].generatedPostings?.[0].dimensions ?? null);
    expect(result.current.selectedRuleId).toBe(duplicateRequest.rule.ruleId);
    expect(result.current.duplicateRuleStatusText).toContain(duplicateRequest.rule.ruleId);

    await act(async () => {
      await result.current.runRuleTests();
    });

    expect(services.runRuleTests).toHaveBeenCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      testCases: null
    }));
    await waitFor(() => expect(result.current.ruleTestSuite).not.toBeNull());
    expect(result.current.ruleTestSuite).toMatchObject({
      title: "Accounting rule regression tests",
      summaryLabel: "1/1 passed",
      statusTone: "success"
    });
    expect(result.current.selectedRule?.promotionReadiness).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "latest-suite",
        value: "1/1 passed",
        tone: "success"
      }),
      expect.objectContaining({
        id: "activation-gate",
        value: "Blocked",
        detail: "Promotion approval is required before activation.",
        tone: "warning"
      })
    ]));
    expect(result.current.ruleTestSuite?.resultRows.join("\n")).toContain("Pass: Saved trade buy regression selected rule-trade-buy, balanced");

    await act(async () => {
      await result.current.archiveSelectedRule();
    });

    expect(services.upsertRule).toHaveBeenLastCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      evidenceLinks: [`browser://accounting/rules-studio/archive/${duplicateRequest.rule.ruleId}`],
      rule: expect.objectContaining({
        ruleId: duplicateRequest.rule.ruleId,
        isArchived: true,
        requiresPromotionApproval: true
      })
    }));
    expect(result.current.archiveRuleStatusText).toBe(`Archived posting rule ${duplicateRequest.rule.ruleId}.`);
    expect(result.current.rules.map((rule) => rule.id)).not.toContain(duplicateRequest.rule.ruleId);
    expect(result.current.selectedRuleId).toBe("rule-trade-buy");
  });

  it("blocks activation when promotion-gated rules are not approved or covered by saved tests", async () => {
    const workspace: AccountingConfigurationWorkspace = {
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      status: "Draft",
      configurationVersion: "v4",
      updatedAtUtc: "2026-06-30T12:00:00Z",
      ledgerBooks: [],
      chartOfAccounts: [
        { nodeId: "cash", path: "1000.Cash", accountName: "Cash", accountType: "Asset", parentPath: null, isArchived: false },
        { nodeId: "income", path: "4000.Interest", accountName: "Interest", accountType: "Revenue", parentPath: null, isArchived: false }
      ],
      journalTemplates: [{
        templateId: "template-interest",
        displayName: "Interest accrual",
        description: "Balanced interest accrual.",
        isArchived: false,
        version: "v1",
        lines: [
          { lineId: "debit-cash", accountPath: "1000.Cash", side: "Debit", amount: 100, currency: "USD", description: "Cash" },
          { lineId: "credit-income", accountPath: "4000.Interest", side: "Credit", amount: 100, currency: "USD", description: "Interest" }
        ]
      }],
      postingRules: [{
        ruleId: "rule-interest",
        displayName: "Interest accrual",
        sourceEventType: "InterestAccrual",
        templateId: "template-interest",
        ruleVersion: "v2",
        isArchived: false,
        priority: 10,
        requiresPromotionApproval: true
      }],
      validationIssues: [],
      auditTrail: [],
      ruleTestCases: []
    };
    const services: AccountingConfigurationServices = {
      getConfiguration: vi.fn().mockResolvedValue(workspace),
      assessProductionReadiness: vi.fn().mockResolvedValue(null),
      listMigrationRunArtifacts: vi.fn().mockResolvedValue({ fundProfileId: "fund-alpha", ledgerBookId: "book-primary", artifacts: [] }),
      createLedgerBook: vi.fn(),
      previewTemplate: vi.fn(),
      upsertRule: vi.fn(),
      dryRunRule: vi.fn(),
      buildJournalCandidate: vi.fn(),
      runRuleTests: vi.fn(),
      saveRuleTestCase: vi.fn(),
      approveRulePromotion: vi.fn().mockResolvedValue({
        ...workspace,
        postingRules: [{
          ...workspace.postingRules[0],
          promotionApproval: {
            approvalId: "approval-rule-interest",
            requestedBy: "browser-accounting-operator",
            requestedAtUtc: "2026-06-15T10:00:00Z",
            approvalState: "Approved",
            approvedBy: "browser-accounting-operator",
            approvedAtUtc: "2026-06-15T11:00:00Z",
            evidenceLinks: ["browser://accounting/rules-studio/promotion/rule-interest"]
          }
        }]
      }),
      activate: vi.fn()
    };

    const { result } = renderHook(() => useAccountingConfigurationViewModel(services));
    await waitFor(() => expect(result.current.rules).toHaveLength(1));
    expect(result.current.activateDisabledReason).toBe("Approve promotion for 1 required posting rule before activation.");
    expect(result.current.selectedRule?.promotionReadiness).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "promotion-approval",
        value: "Required",
        tone: "warning"
      }),
      expect.objectContaining({
        id: "activation-gate",
        value: "Blocked",
        detail: "Promotion approval is required before activation.",
        tone: "warning"
      })
    ]));

    await act(async () => {
      await result.current.approveRulePromotion();
    });

    expect(services.approveRulePromotion).toHaveBeenCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      ruleId: "rule-interest",
      ruleVersion: "v2",
      actor: "browser-accounting-operator",
      evidenceLinks: ["browser://accounting/rules-studio/promotion-review/rule-interest/v2"],
      requestedBy: "browser-accounting-operator",
      notes: "Approved Interest accrual v2 from Accounting Rules Studio."
    }));
    expect(result.current.approveRulePromotionStatusText).toBe("Approved promotion for Interest accrual.");

    const approvedServices: AccountingConfigurationServices = {
      ...services,
      getConfiguration: vi.fn().mockResolvedValue({
      ...workspace,
      postingRules: [{
        ...workspace.postingRules[0],
        promotionApproval: {
          approvalId: "approval-rule-interest",
          requestedBy: "controller",
          requestedAtUtc: "2026-06-15T10:00:00Z",
          approvalState: "Approved",
          approvedBy: "cfo",
          approvedAtUtc: "2026-06-15T11:00:00Z",
          evidenceLinks: ["evidence://rule-approval"]
        }
      }]
      })
    };

    const approved = renderHook(() => useAccountingConfigurationViewModel(approvedServices));
    await waitFor(() => expect(approved.result.current.rules).toHaveLength(1));
    expect(approved.result.current.activateDisabledReason).toBe("Save regression test cases for 1 promotion-gated posting rule before activation.");
    expect(approved.result.current.selectedRule?.promotionReadiness).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "promotion-approval",
        value: "Approved",
        tone: "success"
      }),
      expect.objectContaining({
        id: "saved-regression",
        value: "Missing",
        tone: "warning"
      }),
      expect.objectContaining({
        id: "activation-gate",
        value: "Blocked",
        detail: "A saved regression case is required before activation.",
        tone: "warning"
      })
    ]));
  });

  it("surfaces missing ledger-book setup as configuration setup readiness", async () => {
    const workspace: AccountingConfigurationWorkspace = {
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-missing",
      status: "Draft",
      configurationVersion: "v4",
      updatedAtUtc: "2026-06-30T12:00:00Z",
      ledgerBooks: [],
      chartOfAccounts: [
        { nodeId: "cash", path: "1000.Cash", accountName: "Cash", accountType: "Asset", parentPath: null, isArchived: false },
        { nodeId: "income", path: "4000.Interest", accountName: "Interest", accountType: "Revenue", parentPath: null, isArchived: false }
      ],
      journalTemplates: [{
        templateId: "template-interest",
        displayName: "Interest accrual",
        description: "Balanced interest accrual.",
        isArchived: false,
        version: "v1",
        lines: [
          { lineId: "debit-cash", accountPath: "1000.Cash", side: "Debit", amount: 100, currency: "USD", description: "Cash" },
          { lineId: "credit-income", accountPath: "4000.Interest", side: "Credit", amount: 100, currency: "USD", description: "Interest" }
        ]
      }],
      postingRules: [{
        ruleId: "rule-interest",
        displayName: "Interest accrual",
        sourceEventType: "InterestAccrual",
        templateId: "template-interest",
        ruleVersion: "v1",
        isArchived: false,
        priority: 10
      }],
      validationIssues: [{
        code: "configuration.ledger-book-missing",
        severity: "Critical",
        message: "Accounting configuration targets ledger book 'book-missing', but no matching ledger book setup was found.",
        targetId: "book-missing",
        suggestedAction: "Create or select the ledger book before activating book-scoped accounting configuration."
      }],
      auditTrail: [],
      ruleTestCases: [],
      ledgerBookSetupCandidate: {
        fundProfileId: "fund-alpha",
        fundStructureNodeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        fundStructureNodeKind: "Fund",
        displayName: "Alpha Fund primary book",
        baseCurrency: "USD",
        accountingBasis: "Primary",
        accountingPolicyId: "legacy-v1",
        accountingPolicyVersion: "legacy-v1",
        suggestedAction: "Create a ledger book using the registered fund-structure scope before activating book-scoped accounting configuration.",
        description: "Created from Accounting Configure setup readiness for requested ledger book book-missing.",
        sourceLedgerBookId: "book-template",
        requestedLedgerBookId: "book-missing"
      }
    };
    const createdBook = {
      ledgerBookId: "book-created",
      fundProfileId: "fund-alpha",
      fundStructureNodeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      fundStructureNodeKind: "Fund",
      displayName: "Alpha Fund primary book",
      baseCurrency: "USD",
      createdAt: "2026-06-30T12:00:00Z",
      updatedAt: "2026-06-30T12:00:00Z",
      description: "Created from Accounting Configure setup readiness for requested ledger book book-missing.",
      accountingBasis: "Primary" as const,
      accountingPolicyId: "legacy-v1",
      accountingPolicyVersion: "legacy-v1"
    };
    const updatedWorkspace: AccountingConfigurationWorkspace = {
      ...workspace,
      ledgerBookId: "book-created",
      ledgerBooks: [createdBook],
      validationIssues: [],
      ledgerBookSetupCandidate: null
    };
    const getConfiguration = vi.fn()
      .mockResolvedValueOnce(workspace)
      .mockResolvedValue(updatedWorkspace);
    const createLedgerBook = vi.fn().mockResolvedValue(createdBook);
    const services: AccountingConfigurationServices = {
      getConfiguration,
      assessProductionReadiness: vi.fn().mockResolvedValue(null),
      listMigrationRunArtifacts: vi.fn().mockResolvedValue({ fundProfileId: "fund-alpha", ledgerBookId: "book-missing", artifacts: [] }),
      createLedgerBook,
      previewTemplate: vi.fn(),
      upsertRule: vi.fn(),
      dryRunRule: vi.fn(),
      buildJournalCandidate: vi.fn(),
      runRuleTests: vi.fn(),
      saveRuleTestCase: vi.fn(),
      approveRulePromotion: vi.fn(),
      activate: vi.fn()
    };

    const { result } = renderHook(() => useAccountingConfigurationViewModel(services));

    await waitFor(() => expect(result.current.validationIssues).toHaveLength(1));
    expect(result.current.setupReadinessRows).toEqual([
      expect.objectContaining({
        id: "selected-ledger-book",
        value: "Missing",
        tone: "danger",
        detail: "Accounting configuration targets ledger book 'book-missing', but no matching ledger book setup was found."
      }),
      expect.objectContaining({
        id: "activation-readiness",
        value: "Blocked",
        tone: "danger",
        detail: "Create or select the ledger book before activating book-scoped accounting configuration."
      })
    ]);
    expect(result.current.canCreateLedgerBook).toBe(true);
    expect(result.current.createLedgerBookStatusText).toBe("Create a ledger book using the registered fund-structure scope before activating book-scoped accounting configuration.");

    await act(async () => {
      await result.current.createLedgerBookFromSetupCandidate();
    });

    expect(createLedgerBook).toHaveBeenCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      fundStructureNodeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      fundStructureNodeKind: "Fund",
      displayName: "Alpha Fund primary book",
      baseCurrency: "USD",
      accountingBasis: "Primary",
      accountingPolicyId: "legacy-v1",
      accountingPolicyVersion: "legacy-v1"
    }));
    await waitFor(() => expect(result.current.setupReadinessRows[0]).toEqual(expect.objectContaining({
      value: "Alpha Fund primary book",
      tone: "success"
    })));
    expect(result.current.createLedgerBookStatusText).toBe("Created Alpha Fund primary book.");
    expect(result.current.activateDisabledReason).toBeNull();
    expect(result.current.canActivate).toBe(true);
  });

  it("loads the Capital Account Workbench with investor evidence, allocation rules, lineage, and audit drill-through rows", async () => {
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
      auditDrillThroughCount: 2,
      netCapitalActivity: 125,
      investorAccounts: [{
        accountKey: "capital-account:fund-alpha:lp-1|investor:lp-1|USD",
        capitalAccountId: "capital-account:fund-alpha:lp-1",
        investorId: "investor:lp-1",
        currency: "USD",
        activityRoute: "/api/ledger/private-capital/capital-account-subledger?capitalAccountId=capital-account%3Afund-alpha%3Alp-1",
        readiness: "Ready",
        readinessLabel: "Ready",
        readinessReason: "Retained evidence and statement support are available.",
        nextAction: "Open statement",
        nextActionRoute: "/api/ledger/private-capital/report-output?reportOutputId=report-output-1",
        openingNetActivity: 0,
        endingNetActivity: 125,
        netCapitalActivity: 125,
        contributions: 125,
        distributions: 0,
        subscriptions: 0,
        redemptions: 0,
        managementFees: 0,
        fundEventCount: 1,
        postedFundEventCount: 1,
        approvalQueueCount: 0,
        publishedReportOutputCount: 1,
        evidenceLinkCount: 2,
        validationIssueCount: 0,
        evidenceCategorySummary: "2/2 allocation evidence categories ready.",
        evidenceLinks: ["/evidence/source", "/evidence/cash"],
        evidenceCategories: [],
        fundEventRecords: [manualJournalWorkbench.privateCapitalActivity!.fundEventRecords[0]],
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
          amount: 125,
          currency: "USD",
          effectiveDate: "2026-06-30",
          summary: "Cash evidence matched.",
          cashEvidenceLinkCount: 1,
          cashEvidenceLinks: ["/evidence/cash"],
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
        evidenceLinkCount: 2,
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
          amount: 125,
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
        reportLineProvenanceCount: 3,
        hasRestatementLineage: true,
        restatementStatus: "Restatement lineage retained.",
        restatementReasonCode: "capital-account-correction",
        restatementPriorVersionReportId: "prior-report",
        restatementApprover: "audit-partner",
        restatementChangedLineCount: 1,
        restatementEvidenceLinkCount: 1,
        reportOutputRoute: "/api/ledger/private-capital/report-output?reportOutputId=report-output-1",
        evidenceRoute: "/evidence/restatement",
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
        evidenceLinkCount: 2,
        evidenceLinks: ["/evidence/source", "/evidence/cash"],
        relatedIds: ["fund-event-1"]
      }],
      validationIssues: [],
      liveCapabilities: ["Investor-level capital account evidence", "Statement publication and restatement lineage"],
      plannedCapabilities: ["Full cap-table administration", "Broad LP portal self-service"]
    };
    const services: CapitalAccountWorkbenchServices = {
      getWorkbench: vi.fn().mockResolvedValue(workbench)
    };

    const { result } = renderHook(() => useCapitalAccountWorkbenchViewModel(
      true,
      "?capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1&currency=USD",
      services
    ));

    await waitFor(() => expect(result.current.statusLabel).toBe("Restated lineage"));
    expect(services.getWorkbench).toHaveBeenCalledWith(expect.objectContaining({
      capitalAccountId: "capital-account:fund-alpha:lp-1",
      investorId: "investor:lp-1",
      currency: "USD"
    }));
    expect(result.current.summaryCards).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "investor-accounts", value: "1" }),
      expect.objectContaining({ id: "statements", detail: "1 restatement lineage" })
    ]));
    expect(result.current.investorAccounts[0]).toMatchObject({
      title: "capital-account:fund-alpha:lp-1",
      netActivityLabel: "+$125.00 USD",
      paymentEvidenceLabel: "Settlement matched / Inflow / $125 USD / 1 cash evidence / settlement linked"
    });
    expect(result.current.fundEventCommandRows[0]).toMatchObject({
      title: "CapitalCall",
      readinessLabel: "Approval pending",
      readinessReasonLabel: "Submit the fund-event journal for approval before posting or stakeholder report output.",
      nextActionLabel: "Submit approval",
      commandCenterRouteLabel: "/api/ledger/private-capital/fund-event-command-center?fundProfileId=fund-alpha&fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630",
      evidenceRouteLabel: "/api/workstation/evidence/subjects/private-capital-fund-event/fund-event%3Afund-alpha%3Acapital-call%3A20260630/packet",
      ledgerImpactLabel: "1 ledger impact(s)",
      subledgerLabel: "1 subledger movement(s)",
      reportOutputLabel: "1 report output(s)"
    });
    expect(result.current.allocationRules[0]).toMatchObject({
      label: "Source support",
      statusLabel: "Satisfied",
      routeLabel: "/evidence/source",
      policyLabel: "source-support:projection:1.1.1.1",
      effectiveWindowLabel: "2026-06-30 -> 2026-06-30",
      formulaLabel: "fund_event.source_evidence_count > 0",
      approvalLabel: "Approved / approval-lp-1 / /accounting/approvals?approvalId=approval-lp-1",
      inputSummaryLabel: "1 input(s): fund-event fund-event:fund-alpha:capital-call +$125.00 USD",
      relatedFundEventLabel: "fund-event:fund-alpha:capital-call"
    });
    expect(result.current.statementLineage[0]).toMatchObject({
      title: "LP 1 Statement",
      statusLabel: "Restated",
      restatementLabel: "capital-account-correction / 1 changed line(s) / 1 evidence",
      changedLineRows: [
        expect.objectContaining({
          lineKey: "capital-account-ending",
          valueLabel: "100.00 -> 125.00",
          evidenceLabel: "1 changed-line evidence"
        })
      ]
    });
    expect(result.current.auditDrillThroughs[0]).toMatchObject({
      title: "LP 1 subledger",
      statusLabel: "Available"
    });
    expect(result.current.liveCapabilities).toContain("Investor-level capital account evidence");
    expect(result.current.plannedCapabilities).toContain("Broad LP portal self-service");
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
    act(() => result.current.updateLine("line-debit", { amount: 125 }));
    expect(result.current.totalDebitsLabel).toBe("$125");
    expect(result.current.totalCreditsLabel).toBe("$100");
    expect(result.current.balanceStatusLabel).toBe("Out by $25");
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

  it("derives a blocked controller close command center from workflow and provider signals", () => {
    const state = buildCloseCommandCenterViewState({
      data: accountingWorkspace,
      workflow: closeWorkflow,
      workflowLoading: false,
      workflowError: null,
      accountingSystemProviders: [accountingSystemProvider],
      accountingSystemImport: null,
      accountingSystemReconciliation,
      multiAssetCoverage
    });

    expect(state).toMatchObject({
      title: "CFO / Controller close command center",
      status: "blocked",
      statusLabel: "Blocked",
      statusTone: "danger",
      periodLabel: "2026-05",
      fundAccountLabel: "fund-alpha"
    });
    expect(state.metricRows).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "breaks", value: "2", tone: "warning" }),
      expect.objectContaining({ id: "source-files", value: "2", tone: "warning" }),
      expect.objectContaining({ id: "adjustments", value: "2", tone: "warning" }),
      expect.objectContaining({ id: "valuations", value: "1", tone: "warning" }),
      expect.objectContaining({ id: "providers", value: "2", tone: "warning" }),
      expect.objectContaining({ id: "report-pack", value: "Not ready", tone: "warning" }),
      expect.objectContaining({ id: "signoff", value: "0/1 approved", tone: "warning" })
    ]));
    expect(state.blockerRows).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "CLOSE_BLOCKED", tone: "danger" }),
      expect.objectContaining({ id: "SOURCE_FILE_MISSING", href: "/accounting/reconciliation" }),
      expect.objectContaining({ id: "evidence-source-records", detail: "Missing: custodian activity file" })
    ]));
    expect(state.actionRows.map((action) => action.href)).toEqual([
      "/accounting/reconciliation",
      "/accounting/approvals",
      "/reporting/evidence"
    ]);
  });

  it("marks the controller close command center ready only when all close signals clear", () => {
    const readyWorkflow: OperationsContinuityWorkflow = {
      ...closeWorkflow,
      status: "ReadyForClose",
      gates: [],
      nextActions: [],
      breakCases: [],
      approvals: [
        {
          ...closeWorkflow.approvals[0],
          status: "Approved",
          reviewer: "controller-reviewer",
          decidedAtUtc: "2026-06-01T04:00:00Z"
        }
      ],
      reportPackReadiness: {
        isReady: true,
        reportPackId: "report-pack-2026-05",
        blockingReason: null,
        evidenceLinks: []
      },
      closeChecklist: [
        {
          ...closeWorkflow.closeChecklist[0],
          status: "Done",
          evidencePointer: "source-file-evidence",
          acknowledgedAtUtc: "2026-06-01T03:00:00Z",
          acknowledgedBy: "controller"
        }
      ],
      closeReadiness: {
        isReadyToClose: true,
        severity: "Ready",
        score: 100,
        components: [],
        blockers: [],
        nextActions: []
      },
      closePackage: {
        closePackageId: "close-package-1",
        reportPackId: "report-pack-2026-05",
        retainedManifestId: "manifest-1",
        retainedManifestRoute: "/reporting/evidence/manifest-1",
        evidenceHash: "hash-1",
        publishedAtUtc: "2026-06-01T04:30:00Z",
        publishedBy: "controller-reviewer",
        signOffRationale: "Close evidence reviewed.",
        evidenceLinks: [],
        checklistControlApprovals: []
      },
      accountingRecordSummary: {
        ...closeWorkflow.accountingRecordSummary!,
        isAuditReady: true,
        completeCategoryCount: 2,
        evidenceCategories: closeWorkflow.accountingRecordSummary!.evidenceCategories.map((category) => ({
          ...category,
          isComplete: true,
          status: "Complete"
        }))
      },
      blockers: []
    };
    const readyData: AccountingWorkspaceResponse = {
      ...accountingWorkspace,
      breakQueue: [],
      reconciliationQueue: [],
      controlCenter: {
        ...accountingWorkspace.controlCenter!,
        closeReadiness: "Ready",
        alerts: [],
        slaBreachCount: 0
      }
    };
    const readyProvider: AccountingSystemProvider = {
      ...accountingSystemProvider,
      state: "Available",
      statusLabel: "Available"
    };
    const readyGl: AccountingSystemReconciliationSummary = {
      ...accountingSystemReconciliation,
      breakCount: 0,
      rows: accountingSystemReconciliation.rows.map((row) => ({ ...row, status: "Matched", variance: 0 }))
    };
    const readyCoverage: MultiAssetCoverageSummary = {
      ...multiAssetCoverage,
      assetClasses: multiAssetCoverage.assetClasses.map((assetClass) => ({
        ...assetClass,
        status: "Ready",
        statusLabel: "Ready",
        blockers: []
      }))
    };

    const state = buildCloseCommandCenterViewState({
      data: readyData,
      workflow: readyWorkflow,
      workflowLoading: false,
      workflowError: null,
      accountingSystemProviders: [readyProvider],
      accountingSystemImport: null,
      accountingSystemReconciliation: readyGl,
      multiAssetCoverage: readyCoverage
    });

    expect(state).toMatchObject({
      status: "ready",
      statusLabel: "Ready",
      statusTone: "success",
      summary: "The close is ready: breaks, source evidence, approvals, valuations, providers, report pack, and sign-off are clear."
    });
    expect(state.metricRows).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "breaks", value: "0", tone: "success" }),
      expect.objectContaining({ id: "source-files", value: "0", tone: "success" }),
      expect.objectContaining({ id: "adjustments", value: "0", tone: "success" }),
      expect.objectContaining({ id: "providers", value: "0", tone: "success" }),
      expect.objectContaining({ id: "report-pack", value: "report-pack-2026-05", tone: "success" }),
      expect.objectContaining({ id: "signoff", value: "Signed by controller-reviewer", tone: "success" })
    ]));
    expect(state.blockerRows).toEqual([]);
  });

  it("builds operational exception workbench state from reconciliation queues", () => {
    const breakRows = buildReconciliationBreakRows([
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
    ], null, "run-42:cash");

    const state = buildOperationalExceptionWorkbenchState({
      reconciliationQueue,
      breakRows
    });

    expect(state.title).toBe("Operational exception workbench");
    expect(state.metricRows.find((metric) => metric.id === "active-breaks")).toMatchObject({
      value: "1",
      tone: "warning"
    });
    expect(state.metricRows.find((metric) => metric.id === "comments")?.value).toBe("2");
    expect(state.metricRows.find((metric) => metric.id === "audit-evidence")?.value).toBe("3");
    expect(state.cases[0]).toMatchObject({
      id: "run-42:cash",
      ownerLabel: "Unassigned",
      routeHref: "/accounting/ledger"
    });
  });

  it("derives statement run rows and detail tabs from endpoint supplied counts", () => {
    const state = buildReconciliationStatementRunsViewState({
      statementRuns: [
        {
          runId: "statement-run-1",
          importId: "import-1",
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
      ],
      fallbackQueue: reconciliationQueue,
      selectedRunId: "statement-run-1",
      loading: false,
      error: null
    });

    expect(state).toMatchObject({
      title: "Statement runs",
      tableLabel: "Accounting statement runs",
      hasRows: true,
      loadingText: null,
      errorText: null
    });
    expect(state.rows[0]).toMatchObject({
      brokerCustodianLabel: "Northern Trust",
      accountLabel: "Fund A - Prime",
      periodLabel: "2026-04",
      statusLabel: "ReviewRequired",
      validationIssueCountLabel: "4",
      matchCountLabel: "24",
      breakCountLabel: "2",
      caseCountLabel: "1",
      importedAtLabel: "2026-05-01T00:04:00Z",
      unavailableReason: null
    });
    expect(state.tabs.map((tab) => tab.label)).toEqual([
      "Overview",
      "Validation",
      "Positions",
      "Cash",
      "Transactions",
      "Breaks & Cases",
      "Evidence"
    ]);
    expect(state.tabs.every((tab) => !tab.disabled)).toBe(true);
  });

  it("derives the compact reconciliation comparison from statement and cash-flow evidence", () => {
    const state = buildReconciliationComparisonViewState({
      statementRuns: [
        {
          runId: "statement-run-1",
          importId: "import-1",
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
      ],
      fallbackQueue: reconciliationQueue,
      selectedRunId: "statement-run-1",
      cashFlow: {
        totalCash: 120000,
        totalLedgerCash: 120500,
        netVariance: 500,
        totalFinancing: 1400,
        runsWithCashSignals: 4,
        runsWithCashVariance: 1,
        tone: "warning",
        summary: "Cash-flow coverage is available for 4 runs; 1 run needs variance review."
      }
    });

    expect(state).toMatchObject({
      title: "Cash reconciliation - broker statement vs. ledger",
      statementHeading: "Statement",
      ledgerHeading: "Ledger",
      matchedBadgeLabel: "24 matched",
      openBadgeLabel: "2 open",
      statementBalanceLabel: "$120,000",
      ledgerBalanceLabel: "$120,500",
      varianceLabel: "Out by $500",
      varianceTone: "warning"
    });
    expect(state.rows[0]).toMatchObject({
      statementTitle: "Northern Trust",
      statementValue: "$120,000",
      ledgerValue: "$120,500",
      statusTone: "warning"
    });
  });

  it("derives reconciliation detail queue row state and empty inspector copy", () => {
    const state = buildReconciliationQueuePanelViewState(reconciliationQueue, "run-57");

    expect(state).toMatchObject({
      title: "Reconciliation detail queue",
      overviewTitle: "Reconciliation queue",
      overviewCaption: "Read-only reconciliation queue summary. Open the reconciliation workstream to inspect selected run detail.",
      overviewActionHref: "/accounting/reconciliation",
      overviewActionLabel: "Open reconciliation",
      overviewActionAriaLabel: "Open Accounting reconciliation workstream",
      listLabel: "Reconciliation runs",
      detailPanelId: "reconciliation-run-detail-panel",
      hasRows: true
    });
    expect(state.rows[0]).toMatchObject({
      runId: "run-42",
      isSelected: false,
      isExpanded: false,
      controlsId: "reconciliation-run-detail-panel",
      reconciliationTone: "warning",
      openBreakLabel: "1 open",
      selectAriaLabel: "Inspect reconciliation run Paper Index Mean Reversion"
    });
    expect(state.rows[1]).toMatchObject({
      runId: "run-57",
      isSelected: true,
      isExpanded: true,
      reconciliationTone: "primary",
      openBreakLabel: "0 open"
    });

    const emptyState = buildReconciliationQueuePanelViewState([], null);
    expect(emptyState.hasRows).toBe(false);
    expect(emptyState.rows).toEqual([]);
    expect(emptyState.detailEmptyAriaLabel).toBe("No reconciliation run selected");
  });

  it("derives canonical Accounting and Reporting loading states", () => {
    expect(buildAccountingLoadingViewState("/accounting/reconciliation")).toMatchObject({
      role: "status",
      ariaBusy: true,
      ariaLive: "polite",
      titleId: "accounting-workspace-loading-title",
      detailId: "accounting-workspace-loading-detail",
      title: "Loading Accounting",
      detail: "Waiting for ledger, reconciliation, cash-flow, and Security Master summaries from the workstation bootstrap payload.",
      routeLabel: "/accounting/reconciliation",
      workstreamLabel: "Reconciliation",
      statusItemsLabel: "Accounting payloads loading"
    });
    expect(buildAccountingLoadingViewState("/accounting/reconciliation").statusItems.map((item) => item.id)).toEqual([
      "ledger-reconciliation",
      "approvals-exceptions",
      "security-reporting"
    ]);
    expect(buildAccountingLoadingViewState("/accounting/reconciliation").actions.map((action) => action.href)).toEqual([
      "/accounting/operations-continuity",
      "/accounting/entity-setup",
      "/data/providers",
      "/reporting/evidence"
    ]);

    expect(buildAccountingLoadingViewState("/reporting")).toMatchObject({
      titleId: "reporting-workspace-loading-title",
      detailId: "reporting-workspace-loading-detail",
      title: "Loading Reporting",
      detail: "Waiting for report-pack, governed export, and approval summaries from the workstation bootstrap payload.",
      workstreamLabel: "Reporting"
    });
  });

  it("derives cash-flow evidence rows, route context, and variance posture", () => {
    const cashFlow: AccountingCashFlowSummary = {
      totalCash: 120000,
      totalLedgerCash: 119750,
      netVariance: -250,
      totalFinancing: 1400,
      runsWithCashSignals: 4,
      runsWithCashVariance: 2,
      tone: "danger",
      summary: "Cash-flow coverage is available for 4 runs; 2 runs need variance review."
    };

    const state = buildAccountingCashFlowViewState(cashFlow, "/reporting", "reporting");

    expect(state).toMatchObject({
      title: cashFlow.summary,
      description: "Reporting packet context at /reporting reuses the shared accounting/reporting cash-flow summary payload.",
      statusLabel: "Variance review",
      statusTone: "danger",
      ariaLabel: "Cash-flow evidence for Reporting packet context at /reporting",
      statusAriaLabel: "Cash-flow status Variance review. Net variance -$250."
    });
    expect(state.rows).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "portfolio-cash", value: "$120,000", tone: "default" }),
      expect.objectContaining({ id: "net-variance", value: "-$250", tone: "danger", ariaLabel: "Net variance: -$250" }),
      expect.objectContaining({ id: "variance-runs", value: "2", tone: "danger", ariaLabel: "Runs with variance: 2" })
    ]));
    expect(state.statusAnnouncement).toBe("Variance review: Cash-flow coverage is available for 4 runs; 2 runs need variance review.");
  });

  it("derives pending cash-flow state when the bootstrap payload is unavailable", () => {
    const state = buildAccountingCashFlowViewState(null, "/accounting", "ledger");

    expect(state).toMatchObject({
      title: "Cash-flow evidence loading",
      statusLabel: "Pending",
      statusTone: "warning",
      rows: [],
      statusAnnouncement: "Cash-flow evidence is loading."
    });
  });

  it("derives calibration summary status presentation and KPI rows", () => {
    const summary: ReconciliationCalibrationSummary = {
      status: "ReviewRequired",
      summary: "Two routes need operator review before sign-off.",
      asOf: "2026-05-09T14:30:00Z",
      totalBreakCount: 8,
      activeBreakCount: 5,
      openBreakCount: 2,
      inReviewBreakCount: 3,
      resolvedBreakCount: 6,
      dismissedBreakCount: 0,
      criticalOpenBreakCount: 1,
      pendingSignoffCount: 3,
      signedOffCount: 4,
      missingCalibrationMetadataCount: 1,
      profiles: [
        {
          toleranceProfileId: "profile-cash",
          exceptionRoute: "cash",
          highestSeverity: "Critical",
          maxToleranceBand: 250,
          totalBreakCount: 8,
          openBreakCount: 2,
          inReviewBreakCount: 3,
          resolvedBreakCount: 6,
          dismissedBreakCount: 0,
          pendingSignoffCount: 3,
          signedOffCount: 4,
          lastUpdatedAt: "2026-05-09T14:00:00Z"
        }
      ]
    };

    const state = buildCalibrationSummaryViewState(summary, false, null);

    expect(state).toMatchObject({
      statusLabel: "Review required",
      statusTone: "warning",
      statusIcon: "alert",
      statusTextClassName: "text-warning",
      statusBannerClassName: "border-warning/30 bg-warning/5",
      profilesLabel: "1 tolerance profile",
      hasProfiles: true,
      tableAriaLabel: "Tolerance profile health by reconciliation route",
      selectedProfileId: "profile-cash",
      refreshCommand: {
        label: "Refresh calibration",
        ariaLabel: "Refresh calibration summary",
        disabled: false,
        disabledReason: null
      }
    });
    expect(state.metricRows).toEqual([
      expect.objectContaining({ id: "total", label: "Total breaks", value: 8, tone: "default", ariaLabel: "Total breaks: 8" }),
      expect.objectContaining({ id: "open", label: "Open", value: 2, tone: "warning", ariaLabel: "Open: 2" }),
      expect.objectContaining({ id: "critical-open", label: "Critical open", value: 1, tone: "warning" }),
      expect.objectContaining({ id: "pending-signoff", label: "Pending sign-off", value: 3, tone: "warning" }),
      expect.objectContaining({ id: "signed-off", label: "Signed off", value: 4, tone: "default" }),
      expect.objectContaining({ id: "missing-metadata", label: "Missing metadata", value: 1, tone: "warning" })
    ]);
    expect(state.profileRows[0].ariaLabel).toContain("profile-cash");
    expect(state.profileRows[0]).toMatchObject({
      maxToleranceBandLabel: "$250",
      totalBreakCount: 8,
      inReviewBreakCount: 3,
      signedOffCount: 4,
      selectAriaLabel: "Inspect tolerance profile profile-cash: Operator review required",
      detailPanelId: "calibration-profile-detail-panel",
      isSelected: true
    });
    expect(state.selectedProfile).toMatchObject({
      title: "Selected tolerance profile - profile-cash",
      statusLabel: "Operator review required",
      statusTone: "danger",
      ariaLabel: "Tolerance profile detail for profile-cash"
    });
    expect(state.selectedProfile?.fields).toEqual(expect.arrayContaining([
      { label: "Tolerance band", value: "$250" },
      { label: "Pending sign-off", value: "3" },
      { label: "Last updated", value: "2026-05-09" }
    ]));
  });

  it("selects requested calibration profile details and falls back to the first available row", () => {
    const summary: ReconciliationCalibrationSummary = {
      status: "Ready",
      summary: "Profiles calibrated.",
      asOf: "2026-05-09T14:30:00Z",
      totalBreakCount: 2,
      activeBreakCount: 0,
      openBreakCount: 0,
      inReviewBreakCount: 0,
      resolvedBreakCount: 2,
      dismissedBreakCount: 0,
      criticalOpenBreakCount: 0,
      pendingSignoffCount: 0,
      signedOffCount: 2,
      missingCalibrationMetadataCount: 0,
      profiles: [
        {
          toleranceProfileId: "profile-a",
          exceptionRoute: "cash",
          highestSeverity: "Info",
          maxToleranceBand: null,
          totalBreakCount: 1,
          openBreakCount: 0,
          inReviewBreakCount: 0,
          resolvedBreakCount: 1,
          dismissedBreakCount: 0,
          pendingSignoffCount: 0,
          signedOffCount: 1,
          lastUpdatedAt: "2026-05-09T14:00:00Z"
        },
        {
          toleranceProfileId: "profile-b",
          exceptionRoute: "settlement",
          highestSeverity: "Warning",
          maxToleranceBand: 125,
          totalBreakCount: 1,
          openBreakCount: 0,
          inReviewBreakCount: 0,
          resolvedBreakCount: 1,
          dismissedBreakCount: 0,
          pendingSignoffCount: 1,
          signedOffCount: 1,
          lastUpdatedAt: "2026-05-09T15:00:00Z"
        }
      ]
    };

    const selected = buildCalibrationSummaryViewState(summary, false, null, "profile-b");
    expect(selected.selectedProfileId).toBe("profile-b");
    expect(selected.profileRows.map((row) => [row.toleranceProfileId, row.isSelected])).toEqual([
      ["profile-a", false],
      ["profile-b", true]
    ]);
    expect(selected.selectedProfile).toMatchObject({
      title: "Selected tolerance profile - profile-b",
      statusLabel: "Pending sign-off",
      statusTone: "warning"
    });
    expect(selected.selectedProfile?.fields).toEqual(expect.arrayContaining([
      { label: "Tolerance band", value: "$125" }
    ]));

    const fallback = buildCalibrationSummaryViewState(summary, false, null, "missing-profile");
    expect(fallback.selectedProfileId).toBe("profile-a");
    expect(fallback.selectedProfile?.statusLabel).toBe("Within tolerance");
  });

  it("derives calibration refresh command states for loading and retry recovery", () => {
    expect(buildCalibrationSummaryViewState(null, true, null).refreshCommand).toEqual({
      label: "Refreshing...",
      ariaLabel: "Calibration summary refresh is already running",
      disabled: true,
      disabledReason: "Calibration summary refresh is already running."
    });
    expect(buildCalibrationSummaryViewState(null, false, "Calibration API offline").refreshCommand).toEqual({
      label: "Retry calibration summary",
      ariaLabel: "Retry calibration summary load",
      disabled: false,
      disabledReason: null
    });
  });

  it("retries calibration summary loads and ignores stale responses", async () => {
    let resolveFirst!: (summary: ReconciliationCalibrationSummary) => void;
    const firstLoad = new Promise<ReconciliationCalibrationSummary>((resolve) => {
      resolveFirst = resolve;
    });
    const retrySummary: ReconciliationCalibrationSummary = {
      status: "Ready",
      summary: "Retry summary loaded.",
      asOf: "2026-05-09T16:00:00Z",
      totalBreakCount: 1,
      activeBreakCount: 0,
      openBreakCount: 0,
      inReviewBreakCount: 0,
      resolvedBreakCount: 1,
      dismissedBreakCount: 0,
      criticalOpenBreakCount: 0,
      pendingSignoffCount: 0,
      signedOffCount: 1,
      missingCalibrationMetadataCount: 0,
      profiles: [
        {
          toleranceProfileId: "retry-profile",
          exceptionRoute: "cash",
          highestSeverity: "Info",
          maxToleranceBand: null,
          totalBreakCount: 1,
          openBreakCount: 0,
          inReviewBreakCount: 0,
          resolvedBreakCount: 1,
          dismissedBreakCount: 0,
          pendingSignoffCount: 0,
          signedOffCount: 1,
          lastUpdatedAt: "2026-05-09T16:00:00Z"
        }
      ]
    };
    const staleSummary: ReconciliationCalibrationSummary = {
      ...retrySummary,
      summary: "Stale first response.",
      profiles: [
        {
          ...retrySummary.profiles[0],
          toleranceProfileId: "stale-profile"
        }
      ]
    };
    const services: AccountingReconciliationServices = {
      getBreakQueue: vi.fn().mockResolvedValue([]),
      reviewBreak: vi.fn(),
      resolveBreak: vi.fn(),
      getTrialBalance: vi.fn().mockResolvedValue([]),
      getCalibrationSummary: vi.fn()
        .mockReturnValueOnce(firstLoad)
        .mockResolvedValueOnce(retrySummary),
      getStatementRuns: vi.fn().mockResolvedValue([]),
      getStatementRun: vi.fn(),
      previewTransactionLab: vi.fn()
    };
    const bootstrapData = {
      metrics: [],
      reconciliationQueue,
      breakQueue: [],
      cashFlow: null,
      reporting: null
    } as unknown as AccountingWorkspaceResponse;

    const { result } = renderHook(() => useAccountingReconciliationViewModel({
      ...bootstrapData
    }, "reconciliation", services));

    await waitFor(() => expect(result.current.calibrationView.refreshCommand.disabled).toBe(true));
    act(() => {
      result.current.calibrationView.refresh();
    });
    await waitFor(() => expect(result.current.calibrationView.selectedProfileId).toBe("retry-profile"));

    act(() => {
      resolveFirst(staleSummary);
    });

    await waitFor(() => expect(result.current.calibrationView.selectedProfileId).toBe("retry-profile"));
    expect(result.current.calibrationView.selectedProfile?.title).toContain("retry-profile");
  });

  it("wires Transaction Lab preview requests and renders shared response values", async () => {
    const preview: InvestmentAccountingTransactionLabPreview = {
      previewId: "txn-lab:run-42",
      kind: "BrokerReconciliation",
      fundAccountId: "fund-account-ops",
      symbol: "BOOKS",
      eventDate: "2026-06-02",
      currency: "USD",
      journalPreview: {
        journalPreviewId: "txn-lab:run-42",
        expectedEventId: "event-run-42",
        description: "Preview",
        eventDate: "2026-06-02",
        isBalanced: true,
        requiresOperatorApproval: true,
        idempotencyKey: "idempotency-run-42",
        lines: [
          { accountName: "Reconciliation Suspense", accountType: "Asset", symbol: "BOOKS", debit: 100, credit: 0 },
          { accountName: "Broker Statement Variance", accountType: "Liability", symbol: "BOOKS", debit: 0, credit: 100 }
        ]
      },
      ledgerImpact: {
        draftEntryCount: 1,
        netDebitEffect: 100,
        netCreditEffect: 100,
        netBalanceDelta: 0,
        hasValidationWarnings: false,
        validationFlags: []
      },
      trialBalanceImpact: [
        { accountName: "Reconciliation Suspense", accountType: "Asset", symbol: "BOOKS", balanceDelta: 100, explanation: "delta +" },
        { accountName: "Broker Statement Variance", accountType: "Liability", symbol: "BOOKS", balanceDelta: -100, explanation: "delta -" }
      ],
      reconciliationExpectation: {
        expectedState: "ReadyForReconciliation",
        expectedBreakType: "broker-statement-break",
        detail: "ready",
        evidenceIds: ["reconciliation-run:run-42", "statement-line:1"],
        brokerStatementId: "statement-run-42",
        reconciliationCaseId: "case-run-42"
      },
      evidenceIds: ["reconciliation-run:run-42", "statement-line:1"],
      sourceRunId: "run-42",
      sourceSessionId: null,
      booksBeforeBroker: {
        isBooksBeforeBrokerMode: true,
        canStageBrokerAction: true,
        expectedBrokerAction: "NoBrokerOrder-ReconciliationCase",
        brokerInstructionSummary: "Ready",
        requiredApprovals: ["operator-accounting-approval"],
        blockers: [],
        evidenceIds: ["reconciliation-run:run-42", "statement-line:1"]
      }
    };

    const services: AccountingReconciliationServices = {
      getBreakQueue: vi.fn().mockResolvedValue([]),
      reviewBreak: vi.fn(),
      resolveBreak: vi.fn(),
      getTrialBalance: vi.fn().mockResolvedValue([]),
      getCalibrationSummary: vi.fn().mockResolvedValue({
        status: "Ready",
        summary: "Ready",
        asOf: "2026-05-09T16:00:00Z",
        totalBreakCount: 0,
        activeBreakCount: 0,
        openBreakCount: 0,
        inReviewBreakCount: 0,
        resolvedBreakCount: 0,
        dismissedBreakCount: 0,
        criticalOpenBreakCount: 0,
        pendingSignoffCount: 0,
        signedOffCount: 0,
        missingCalibrationMetadataCount: 0,
        profiles: []
      }),
      getStatementRuns: vi.fn().mockResolvedValue([]),
      getStatementRun: vi.fn(),
      previewTransactionLab: vi.fn().mockResolvedValue(preview)
    };
    const bootstrapData = {
      metrics: [],
      reconciliationQueue,
      breakQueue: [],
      cashFlow: null,
      reporting: null
    } as unknown as AccountingWorkspaceResponse;

    const { result } = renderHook(() => useAccountingReconciliationViewModel({
      ...bootstrapData
    }, "reconciliation", services));

    await waitFor(() => expect(result.current.transactionLabView.canPreview).toBe(true));

    await act(async () => {
      await result.current.runTransactionLabPreview();
    });

    expect(services.previewTransactionLab).toHaveBeenCalledWith(expect.objectContaining({
      kind: "BrokerReconciliation",
      sourceRunId: "run-42",
      previewMode: "BooksBeforeBroker"
    }));
    expect(result.current.transactionLabView.requestSummaryLabel).toBe("Preview ready");
    expect(result.current.transactionLabView.journalLineCountLabel).toBe("2 lines");
    expect(result.current.transactionLabView.ledgerImpactLabel).toBe("$0");
    expect(result.current.transactionLabView.reconciliationLabel).toBe("ReadyForReconciliation");
    expect(result.current.transactionLabView.evidenceLabel).toBe("2 evidence items");
    expect(result.current.transactionLabView.impactRows).toEqual([
      expect.objectContaining({ label: "Reconciliation Suspense", value: "+$100.00", tone: "success" }),
      expect.objectContaining({ label: "Broker Statement Variance", value: "-$100.00", tone: "danger" })
    ]);
  });

  it("surfaces Transaction Lab preview failures from the shared endpoint", async () => {
    const services: AccountingReconciliationServices = {
      getBreakQueue: vi.fn().mockResolvedValue([]),
      reviewBreak: vi.fn(),
      resolveBreak: vi.fn(),
      getTrialBalance: vi.fn().mockResolvedValue([]),
      getCalibrationSummary: vi.fn().mockResolvedValue({
        status: "Ready",
        summary: "Ready",
        asOf: "2026-05-09T16:00:00Z",
        totalBreakCount: 0,
        activeBreakCount: 0,
        openBreakCount: 0,
        inReviewBreakCount: 0,
        resolvedBreakCount: 0,
        dismissedBreakCount: 0,
        criticalOpenBreakCount: 0,
        pendingSignoffCount: 0,
        signedOffCount: 0,
        missingCalibrationMetadataCount: 0,
        profiles: []
      }),
      getStatementRuns: vi.fn().mockResolvedValue([]),
      getStatementRun: vi.fn(),
      previewTransactionLab: vi.fn().mockRejectedValue(new Error("upstream unavailable"))
    };
    const bootstrapData = {
      metrics: [],
      reconciliationQueue,
      breakQueue: [],
      cashFlow: null,
      reporting: null
    } as unknown as AccountingWorkspaceResponse;

    const { result } = renderHook(() => useAccountingReconciliationViewModel({
      ...bootstrapData
    }, "reconciliation", services));

    await waitFor(() => expect(result.current.transactionLabView.canPreview).toBe(true));
    await act(async () => {
      await result.current.runTransactionLabPreview();
    });

    await waitFor(() => expect(result.current.transactionLabView.requestSummaryLabel).toBe("Request failed"));
    expect(result.current.transactionLabView.statusRole).toBe("alert");
    expect(result.current.transactionLabView.statusTone).toBe("danger");
    expect(result.current.transactionLabView.statusText).toContain("upstream unavailable");
    expect(result.current.transactionLabView.canPreview).toBe(true);
  });

  it("derives trial-balance table rows, labels, and status announcements", () => {
    const state = buildAccountingTrialBalanceViewState({
      runId: "run-42",
      rows: trialBalanceLines,
      loading: false,
      error: null
    });

    expect(state).toMatchObject({
      title: "Primary trial balance",
      description: "Primary basis ledger balances for run-42 grouped by account type. Values are basis per configured policy until accountant review.",
      tableLabel: "Primary trial balance lines for run-42",
      selectedBasis: "Primary",
      accountFilterLabel: "Filter by General Ledger account",
      accountFilterValue: "",
      filteredRowCountLabel: "2 GL account rows",
      state: "ready",
      hasRows: true,
      statusAnnouncement: "2 trial balance lines loaded for run-42."
    });
    expect(state.basisOptions).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "Primary", rowCount: 2, rowCountLabel: "2 rows", isSelected: true }),
      expect.objectContaining({ id: "Gaap", rowCount: 0, rowCountLabel: "0 rows", isSelected: false })
    ]));
    expect(state.rows[0]).toMatchObject({
      rowId: "Primary-Cash-Asset-acct-cash",
      accountLabel: "Cash",
      accountTypeLabel: "Asset",
      basisLabel: "Primary basis",
      policyLabel: "legacy-v1/legacy-v1",
      balanceLabel: "$120,500",
      balanceTone: "success",
      entryCountLabel: "12",
      ariaLabel: "Cash Asset. Primary basis. Policy legacy-v1/legacy-v1. Balance $120,500. 12 entries",
      selectAriaLabel: "Inspect trial-balance account Cash for Asset",
      detailPanelId: "trial-balance-account-detail",
      isExpanded: true
    });
    expect(state.selectedRowId).toBe("Primary-Cash-Asset-acct-cash");
    expect(state.selectedDetail).toMatchObject({
      eyebrow: "Trial-balance detail",
      title: "Cash",
      subtitle: "Asset · acct-cash",
      statusLabel: "Debit / asset",
      statusVariant: "success",
      ariaLabel: "Trial-balance detail for Cash",
      ledgerLinesTitle: "Ledger lines for selected account",
      supportingDocumentsTitle: "Supporting documentation"
    });
    expect(state.selectedDetail?.fields).toEqual(expect.arrayContaining([
      { label: "Journal entries", value: "je-cash-1" },
      { label: "Source events", value: "evt-cash-1" },
      { label: "Approvals", value: "approval-cash-1" }
    ]));
    expect(state.selectedDetail?.ledgerLines).toEqual([
      expect.objectContaining({
        journalEntryId: "je-cash-1",
        debitLabel: "$120,500",
        creditLabel: "$0",
        evidenceLabel: "Source evt-cash-1",
        evidenceHref: "/accounting/audit?sourceEventId=evt-cash-1",
        approvalHref: "/accounting/approvals?approvalId=approval-cash-1"
      })
    ]);
    expect(state.selectedDetail?.supportingDocuments).toEqual(expect.arrayContaining([
      expect.objectContaining({ label: "Run review packet", href: "/api/workstation/runs/run-42/review-packet" }),
      expect.objectContaining({ label: "Source event evt-cash-1", href: "/accounting/audit?sourceEventId=evt-cash-1" }),
      expect.objectContaining({ label: "Journal entry je-cash-1", href: "/accounting/ledger?journalEntryId=je-cash-1" }),
      expect.objectContaining({ label: "Approval approval-cash-1", href: "/accounting/approvals?approvalId=approval-cash-1" })
    ]));
    expect(state.rows[1]).toMatchObject({
      balanceLabel: "-$500",
      balanceTone: "danger",
      isExpanded: false
    });

    const selectedFinancing = buildAccountingTrialBalanceViewState({
      runId: "run-42",
      rows: trialBalanceLines,
      selectedRowId: "Primary-Financing payable-Liability-acct-financing",
      loading: false,
      error: null
    });
    expect(selectedFinancing.selectedDetail).toMatchObject({
      title: "Financing payable",
      statusLabel: "Credit / payable",
      statusVariant: "danger"
    });
    expect(selectedFinancing.selectedDetail?.fields).toEqual(expect.arrayContaining([
      { label: "Journal entries", value: "No journal entry references linked" },
      { label: "Source events", value: "No source events linked" },
      { label: "Approvals", value: "No approvals linked" }
    ]));
    expect(selectedFinancing.selectedDetail?.ledgerLines).toEqual([]);
    expect(selectedFinancing.selectedDetail?.ledgerLinesEmptyText).toBe("No ledger line support is attached to this account row yet.");
  });

  it("filters ledger account inquiry rows by General Ledger account text", () => {
    const state = buildAccountingTrialBalanceViewState({
      runId: "run-42",
      rows: trialBalanceLines,
      accountFilter: "financing",
      loading: false,
      error: null
    });

    expect(state.accountFilterValue).toBe("financing");
    expect(state.filteredRowCountLabel).toBe("1 of 2 GL account rows");
    expect(state.rows).toHaveLength(1);
    expect(state.rows[0]).toMatchObject({
      accountLabel: "Financing payable",
      rowId: "Primary-Financing payable-Liability-acct-financing",
      isExpanded: true
    });
    expect(state.selectedDetail?.title).toBe("Financing payable");

    const empty = buildAccountingTrialBalanceViewState({
      runId: "run-42",
      rows: trialBalanceLines,
      accountFilter: "management fee",
      loading: false,
      error: null
    });

    expect(empty.rows).toHaveLength(0);
    expect(empty.hasRows).toBe(false);
    expect(empty.emptyDetail).toContain("No Primary ledger accounts match \"management fee\"");
  });

  it("adds source-event and approval drill-through details to legacy and array trial-balance selections", () => {
    const legacyState = buildAccountingTrialBalanceViewState({
      runId: "run-42",
      rows: [
        {
          accountName: "Cash",
          accountType: "Asset",
          symbol: null,
          financialAccountId: "acct-cash",
          balance: 120500,
          entryCount: 12,
          security: null,
          sourceEventId: "legacy-source-event"
        }
      ],
      loading: false,
      error: null
    });

    expect(legacyState.selectedDetail?.fields).toEqual(expect.arrayContaining([
      { label: "Source events", value: "legacy-source-event" },
      { label: "Approvals", value: "No approvals linked" }
    ]));
    expect(legacyState.selectedDetail?.auditDrillThroughLabel).toBe("Open source event legacy-source-event");
    expect(legacyState.selectedDetail?.auditDrillThroughHref).toBe("/accounting/audit?sourceEventId=legacy-source-event");

    const arrayState = buildAccountingTrialBalanceViewState({
      runId: "run-42",
      rows: [
        {
          accountName: "Cash",
          accountType: "Asset",
          symbol: null,
          financialAccountId: "acct-cash",
          balance: 120500,
          entryCount: 12,
          security: null,
          sourceEventIds: ["evt-cash-1", "evt-cash-2"],
          approvalIds: ["approval-cash-1"]
        }
      ],
      loading: false,
      error: null
    });

    expect(arrayState.selectedDetail?.fields).toEqual(expect.arrayContaining([
      { label: "Source events", value: "evt-cash-1, evt-cash-2" },
      { label: "Approvals", value: "approval-cash-1" }
    ]));
    expect(arrayState.selectedDetail?.auditDrillThroughLabel).toBe("Open source event evt-cash-1");
    expect(arrayState.selectedDetail?.auditDrillThroughHref).toBe("/accounting/audit?sourceEventId=evt-cash-1");
    expect(arrayState.selectedDetail?.approvalDrillThroughHref).toBe("/accounting/approvals?approvalId=approval-cash-1");
  });

  it("filters trial-balance rows by basis and builds a basis bridge", () => {
    const state = buildAccountingTrialBalanceViewState({
      runId: "run-42",
      selectedBasis: "Gaap",
      rows: [
        {
          accountName: "Cash",
          accountType: "Asset",
          symbol: null,
          financialAccountId: "acct-cash",
          balance: 120500,
          entryCount: 12,
          security: null,
          accountingBasis: "Primary",
          accountingPolicyId: "legacy-v1",
          accountingPolicyVersion: "legacy-v1",
          ruleId: "direct-lending.daily-accrual",
          sourceEventId: "event-42"
        },
        {
          accountName: "Cash",
          accountType: "Asset",
          symbol: null,
          financialAccountId: "acct-cash",
          balance: 119500,
          entryCount: 10,
          security: null,
          accountingBasis: "Gaap",
          accountingPolicyId: "gaap-default-v1",
          accountingPolicyVersion: "v1",
          ruleId: "direct-lending.daily-accrual",
          sourceEventId: "event-42"
        },
        {
          accountName: "Accrued interest receivable",
          accountType: "Asset",
          symbol: null,
          financialAccountId: "acct-interest",
          balance: 1000,
          entryCount: 1,
          security: null,
          accountingBasis: "Gaap",
          accountingPolicyId: "gaap-default-v1",
          accountingPolicyVersion: "v1",
          ruleId: "direct-lending.daily-accrual",
          sourceEventId: "event-42"
        }
      ],
      loading: false,
      error: null
    });

    expect(state.selectedBasis).toBe("Gaap");
    expect(state.rows).toHaveLength(2);
    expect(state.rows[0]).toMatchObject({
      basisLabel: "GAAP basis",
      basisTone: "success",
      policyLabel: "gaap-default-v1/v1"
    });
    expect(state.basisOptions).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "Primary", rowCount: 1, isSelected: false }),
      expect.objectContaining({ id: "Gaap", rowCount: 2, isSelected: true })
    ]));
    expect(state.basisBridge).toMatchObject({
      title: "Basis bridge",
      fromBasis: "Primary",
      toBasis: "Gaap",
      hasRows: true
    });
    expect(state.basisBridge.rows).toEqual(expect.arrayContaining([
      expect.objectContaining({
        accountLabel: "Cash",
        primaryBalanceLabel: "$120,500",
        comparisonBalanceLabel: "$119,500",
        varianceLabel: "-$1,000",
        varianceTone: "danger",
        sourceLabel: "Source event-42 / Rule direct-lending.daily-accrual"
      })
    ]));
  });

  it("derives trial-balance loading, empty, and error states", () => {
    expect(buildAccountingTrialBalanceViewState({
      runId: "run-42",
      rows: [],
      loading: true,
      error: null
    })).toMatchObject({
      state: "loading",
      loadingText: "Loading trial balance for run-42.",
      statusAnnouncement: "Loading trial balance for run-42."
    });

    expect(buildAccountingTrialBalanceViewState({
      runId: "run-42",
      rows: [],
      loading: false,
      error: null
    })).toMatchObject({
      state: "empty",
      emptyTitle: "No trial balance lines",
      statusAnnouncement: "No trial balance lines returned for run-42."
    });

    expect(buildAccountingTrialBalanceViewState({
      runId: "run-42",
      rows: trialBalanceLines,
      loading: false,
      error: "Ledger unavailable."
    })).toMatchObject({
      state: "error",
      errorText: "Ledger unavailable.",
      statusAnnouncement: "Trial balance failed for run-42: Ledger unavailable."
    });
  });

  it("derives search status, result count, and live announcement copy", () => {
    expect(buildSecuritySearchState({
      query: "",
      searching: false,
      results: null,
      searchError: null,
      identityLoading: false,
      identityError: null
    }).searchStatusText).toBe("Enter a ticker, ISIN, CUSIP, FIGI, or display name.");

    const searching = buildSecuritySearchState({
      query: " aapl ",
      searching: true,
      results: null,
      searchError: null,
      identityLoading: false,
      identityError: null
    });

    expect(searching.searchStatusText).toBe('Searching Security Master for "aapl"...');
    expect(searching.statusAnnouncement).toBe("Searching Security Master for aapl.");

    const queued = buildSecuritySearchState({
      query: "AAPL",
      searching: false,
      results: null,
      searchError: null,
      identityLoading: false,
      identityError: null
    });

    expect(queued.searchStatusText).toBe('Security Master search queued for "AAPL".');
    expect(queued.statusAnnouncement).toBe("Security Master search queued for AAPL.");

    const complete = buildSecuritySearchState({
      query: "AAPL",
      searching: false,
      results: [securityResult],
      searchError: null,
      identityLoading: false,
      identityError: null
    });

    expect(complete.hasResults).toBe(true);
    expect(complete.resultCount).toBe(1);
    expect(complete.resultsTableLabel).toBe("Security search results");
    expect(complete.resultColumns.map((column) => column.label)).toEqual(["Name", "Asset Class", "Primary ID", "Currency", "Status"]);
    expect(complete.resultRows[0]).toMatchObject({
      rowId: "security-result-sec-1",
      isSelected: false,
      detailPanelId: "security-master-identity-detail",
      isExpanded: false,
      selectAriaLabel: "Open identity drill-in for Apple Inc.",
      primaryIdentifierLabel: "Ticker: AAPL",
      statusTone: "success",
      ariaLabel: "Apple Inc., Equity, primary identifier Ticker: AAPL, currency USD, status Active."
    });
    expect(complete.searchStatusText).toBe('1 securities found for "AAPL".');
    expect(complete.statusAnnouncement).toBe("1 securities found for AAPL.");
  });

  it("derives selected Security Master search result rows", () => {
    const rows = buildSecuritySearchResultRows([securityResult], "sec-1");

    expect(rows[0]).toMatchObject({
      rowId: "security-result-sec-1",
      isSelected: true,
      detailPanelId: "security-master-identity-detail",
      isExpanded: true,
      selectAriaLabel: "Open identity drill-in for Apple Inc.",
      primaryIdentifierLabel: "Ticker: AAPL",
      statusTone: "success"
    });
    expect(rows[0].ariaLabel).toContain("selected");
    expect(buildSecuritySearchResultRows(null, null)).toEqual([]);
  });

  it("derives selectable corporate-action rows with a detail panel", () => {
    const state = buildCorporateActionsViewState("sec-1", corporateActions, "ca-split-1", false, null);

    expect(state).toMatchObject({
      tableLabel: "Corporate actions for sec-1",
      detailPanelId: "corporate-action-detail-panel",
      selectedRowId: "ca-split-1",
      hasRows: true,
      errorText: null,
      errorDetails: [],
      loadingText: null
    });
    expect(state.rows[1]).toMatchObject({
      rowId: "ca-split-1",
      eventTypeLabel: "Stock split",
      amountLabel: "4:1 split",
      selectAriaLabel: "Inspect corporate action Stock split for sec-1",
      detailPanelId: "corporate-action-detail-panel",
      isExpanded: true
    });
    expect(state.selectedDetail).toMatchObject({
      id: "corporate-action-detail-panel",
      title: "Stock split",
      ariaLabel: "Corporate action detail for Stock split on sec-1",
      statusLabel: "Pay date unavailable"
    });
    expect(state.selectedDetail?.fields).toEqual(expect.arrayContaining([
      { label: "Amount or ratio", value: "4:1 split", tone: "default" },
      { label: "Pay date", value: "—", tone: "warning" }
    ]));
  });

  it("keeps corporate-action loading, empty, and error states in the view model", () => {
    expect(buildCorporateActionsViewState("sec-1", null, null, true, null)).toMatchObject({
      loadingText: "Loading corporate actions...",
      statusAnnouncement: "Loading corporate actions for sec-1."
    });

    expect(buildCorporateActionsViewState("sec-1", [], null, false, null)).toMatchObject({
      hasRows: false,
      emptyText: "No corporate actions recorded for sec-1.",
      selectedDetail: null,
      detailEmptyAriaLabel: "No corporate action selected"
    });

    expect(buildCorporateActionsViewState("sec-1", [], null, false, "Corporate API offline")).toMatchObject({
      errorText: "Corporate API offline",
      errorDetails: [],
      statusAnnouncement: "Corporate actions error: Corporate API offline"
    });
  });

  it("preserves structured legacy-governance api-errors in trial balance, calibration, and corporate actions views", () => {
    const apiError = new MeridianApiError({
      path: "/api/workstation/governance/trial-balance",
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
    });

    const displayError = describeApiError(apiError, "Trial balance failed to load.");

    const trialBalanceState = buildAccountingTrialBalanceViewState({
      runId: "run-42",
      rows: [],
      loading: false,
      error: displayError
    });
    expect(trialBalanceState).toMatchObject({
      state: "error",
      errorText: "Fund account is required."
    });
    expect(trialBalanceState.errorDetails).toEqual([
      "Endpoint returned 422 for /api/workstation/governance/trial-balance.",
      "Validation failed",
      "Fund account: Select a fund account before loading accounting evidence."
    ]);

    const calibrationState = buildCalibrationSummaryViewState(null, false, displayError);
    expect(calibrationState.errorText).toBe("Fund account is required.");
    expect(calibrationState.errorDetails).toEqual(trialBalanceState.errorDetails);

    const corporateActionsState = buildCorporateActionsViewState("sec-1", [], null, false, displayError);
    expect(corporateActionsState.errorText).toBe("Fund account is required.");
    expect(corporateActionsState.errorDetails).toEqual(trialBalanceState.errorDetails);
  });

  it("derives cash-flow and factor schedule rows with selected detail state", () => {
    const rows = buildSecurityScheduleRows(cashFlowSchedules, "sched-1-paydown");
    const state = buildSecuritySchedulesViewState({
      securityId: "sec-1",
      displayName: "Apple Inc.",
      assetClass: "Fixed Income",
      schedules: cashFlowSchedules,
      selectedRowId: "sched-1-paydown"
    });

    expect(rows[1]).toMatchObject({
      rowId: "sched-1-paydown",
      eventTypeLabel: "Paydown",
      paymentDateLabel: "2026-11-15",
      expectedAmountLabel: "126,250 USD",
      actualAmountLabel: "124,900 USD",
      varianceLabel: "-1,350 USD",
      factorLabel: "1.000000 -> 0.900000",
      postingStatusLabel: "Variance review",
      postingStatusTone: "danger",
      selectAriaLabel: "Inspect schedule event Paydown for sec-1 on 2026-11-15",
      detailPanelId: "security-schedule-detail-panel",
      isExpanded: true
    });
    expect(state).toMatchObject({
      title: "Cash-flow and factor schedules",
      tableLabel: "Cash-flow and factor schedules for sec-1",
      selectedRowId: "sched-1-paydown",
      hasRows: true,
      statusAnnouncement: "2 cash-flow schedule events loaded for sec-1."
    });
    expect(state.toolbarItems).toEqual(expect.arrayContaining([
      { id: "events", label: "Events", value: "2", active: true },
      { id: "variance", label: "Variance", value: "1" }
    ]));
    expect(state.selectedDetail).toMatchObject({
      id: "security-schedule-detail-panel",
      title: "Paydown",
      ariaLabel: "Cash-flow schedule detail for Paydown on sec-1",
      statusLabel: "Variance review",
      statusTone: "danger"
    });
    expect(state.selectedDetail?.fields).toEqual(expect.arrayContaining([
      { label: "Expected", value: "126,250 USD" },
      { label: "Actual", value: "124,900 USD", tone: "default" },
      { label: "Variance", value: "-1,350 USD", tone: "danger" },
      { label: "Factor", value: "1.000000 -> 0.900000" }
    ]));
  });

  it("keeps cash-flow schedule empty states and fixture resolution deterministic", () => {
    expect(resolveSecurityScheduleEvents("sec-dev-004")).toHaveLength(3);
    expect(resolveSecurityScheduleEvents("unknown-security")).toEqual([]);

    const state = buildSecuritySchedulesViewState({
      securityId: "unknown-security",
      displayName: "Unknown security",
      assetClass: "Unclassified",
      schedules: [],
      selectedRowId: null
    });

    expect(state).toMatchObject({
      hasRows: false,
      selectedDetail: null,
      emptyText: "No cash-flow or factor schedule rows are available for unknown-security.",
      detailEmptyAriaLabel: "No cash-flow schedule event selected",
      statusAnnouncement: ""
    });
  });

  it("maps trust snapshot scheduleBook into operator schedule rows", () => {
    const mapped = mapScheduleBookToCashFlowScheduleEvents("sec-1", securityTrustSnapshot);
    const state = buildSecuritySchedulesViewState({
      securityId: "sec-1",
      displayName: "Apple Inc.",
      assetClass: "Fixed Income",
      schedules: mapped,
      selectedRowId: "sched-1-paydown",
      factorHistoryCount: securityTrustSnapshot.scheduleBook?.factorHistory.length ?? 0,
      provenanceCount: securityTrustSnapshot.scheduleBook?.provenanceHistory.length ?? 0,
      sourceSummary: securityTrustSnapshot.scheduleBook?.sourceSummary ?? null
    });

    expect(mapped).toHaveLength(2);
    expect(mapped[1]).toMatchObject({
      eventId: "sched-1-paydown",
      paymentDate: "2026-11-15",
      expectedAmount: 126250,
      actualAmount: 124900,
      postingStatus: "Variance",
      auditReference: "golden-edm · EDM-123",
      note: "Expected-versus-actual variance."
    });
    expect(state.description).toContain("Schedule source EDM-123 is current.");
    expect(state.toolbarItems).toEqual(expect.arrayContaining([
      { id: "factor", label: "Factor rows", value: "2" },
      { id: "sources", label: "Sources", value: "1" }
    ]));
  });

  it("derives open-lot read-model rows, detail, and empty/error states", () => {
    const readModel = securityTrustSnapshot.openLotReadModel ?? null;
    const rows = buildSecurityOpenLotRows(readModel, "lot-1");
    const state = buildSecurityOpenLotReadModelViewState({
      securityId: "sec-1",
      readModel,
      selectedRowId: "lot-1"
    });

    expect(rows[0]).toMatchObject({
      rowId: "lot-1",
      tradeDateLabel: "2026-04-20",
      settleDateLabel: "2026-04-22",
      quantityLabel: "95,000",
      faceLabel: "95,000",
      factorAdjustedLabel: "85,500",
      costBasisLabel: "99,000 USD",
      unrealizedPnlLabel: "+1,250 USD",
      scopeLabel: "Fund Alpha - Main",
      statusTone: "success",
      isExpanded: true
    });
    expect(state).toMatchObject({
      title: "Open lot read model",
      tableLabel: "Open lot read model for sec-1",
      selectedRowId: "lot-1",
      hasRows: true,
      statusAnnouncement: "1 open lot loaded for sec-1."
    });
    expect(state.selectedDetail).toMatchObject({
      title: "lot-1",
      statusLabel: "Open",
      ariaLabel: "Open lot detail for lot-1 on AAPL"
    });
    expect(state.selectedDetail?.fields).toEqual(expect.arrayContaining([
      { label: "Factor-adjusted exposure", value: "85,500", tone: "success" },
      { label: "Source", value: "ledger · LOT-1" }
    ]));

    expect(buildSecurityOpenLotReadModelViewState({
      securityId: "sec-1",
      readModel: null,
      selectedRowId: null,
      loading: true
    })).toMatchObject({
      loadingText: "Loading open lot read model...",
      statusAnnouncement: "Loading open lot read model for sec-1."
    });
    expect(buildSecurityOpenLotReadModelViewState({
      securityId: "sec-1",
      readModel: null,
      selectedRowId: null,
      error: "Trust snapshot offline"
    })).toMatchObject({
      errorText: "Trust snapshot offline",
      errorDetails: [],
      statusAnnouncement: "Open lot read model error: Trust snapshot offline"
    });
  });

  it("projects Security Master instrument passport evidence and provider confidence rows", async () => {
    const view = buildInstrumentPassportViewState({
      securityId: "sec-1",
      passport: instrumentPassport
    });

    expect(view).toMatchObject({
      title: "Instrument passport",
      statusLabel: "Trusted",
      statusBadgeVariant: "success",
      providerTableLabel: "Provider confidence for sec-1",
      providerEmptyText: "No provider confidence rows are available for sec-1."
    });
    expect(view.fields).toEqual(expect.arrayContaining([
      { label: "Identifiers", value: "Primary identifiers are aligned." },
      { label: "Provider confidence", value: "1 active / 2 total" },
      { label: "Pricing", value: "Ready: Trading parameters are active." },
      { label: "Usage", value: "Used by accounting and trading workflows." }
    ]));
    expect(view.providerRows).toEqual(expect.arrayContaining([
      expect.objectContaining({
        providerLabel: "Bloomberg / blp-reference",
        symbolLabel: "Ticker: AAPL US Equity",
        confidenceLabel: "87%",
        freshnessLabel: "5 min",
        statusLabel: "Primary",
        statusTone: "success"
      }),
      expect.objectContaining({
        providerLabel: "Reuters / refinitiv",
        symbolLabel: "RIC: AAPL.O",
        confidenceLabel: "87%",
        freshnessLabel: "-",
        statusLabel: "Inactive",
        statusTone: "warning"
      })
    ]));
  });

  it("projects multi-asset reference data coverage rows and selected endpoint detail", () => {
    const view = buildReferenceDataWorkbenchViewState({
      securityId: "sec-1",
      coverage: {
        requestedAtUtc: "2026-05-10T12:00:00Z",
        endpoints: [
          {
            id: "bond-reference",
            family: "Bonds",
            label: "Bond reference",
            method: "GET",
            path: "/api/reference-data/bonds/sec-1",
            requestLabel: "GET bond reference for sec-1",
            probe: true,
            status: "Ready",
            statusCode: 200,
            durationMs: 9,
            responseCount: 1,
            responseSummary: "1 fields returned.",
            responsePreview: "{\n  \"couponRate\": 5.25\n}",
            errorSummary: null,
            errorDetails: []
          },
          {
            id: "option-chain-import",
            family: "Options",
            label: "Option chain import",
            method: "POST",
            path: "/api/reference-data/options/chains/import",
            requestLabel: "POST option chain import endpoint catalogued; not invoked by this read-only workbench.",
            probe: false,
            mutation: true,
            status: "Deferred",
            statusCode: null,
            durationMs: null,
            responseCount: null,
            responseSummary: "POST option chain import endpoint catalogued; not invoked by this read-only workbench.",
            responsePreview: null,
            errorSummary: null,
            errorDetails: []
          }
        ]
      },
      selectedRowId: "reference-data-bond-reference"
    });

    expect(view.metrics).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "routes", value: "2" }),
      expect.objectContaining({ id: "ready", value: "1", tone: "success" }),
      expect.objectContaining({ id: "deferred", value: "1", tone: "warning" })
    ]));
    expect(view.rows).toEqual(expect.arrayContaining([
      expect.objectContaining({
        rowId: "reference-data-bond-reference",
        statusLabel: "Ready",
        statusBadgeVariant: "success",
        countLabel: "1 record",
        latencyLabel: "9 ms"
      }),
      expect.objectContaining({
        rowId: "reference-data-option-chain-import",
        statusLabel: "Deferred",
        statusBadgeVariant: "outline"
      })
    ]));
    expect(view.selectedDetail).toMatchObject({
      title: "Bond reference",
      subtitle: "GET /api/reference-data/bonds/sec-1",
      responsePreview: "{\n  \"couponRate\": 5.25\n}"
    });
  });

  it("loads Security Master instrument passport drill-in without blocking trust evidence", async () => {
    const getInstrumentPassport = vi.fn().mockResolvedValue(instrumentPassport);
    const services = createSecurityMasterServices();
    const drillInServices = createSecurityMasterDrillInServices({ getInstrumentPassport });

    const { result } = renderHook(() => useSecurityMasterViewModel(true, services, drillInServices, 0));

    act(() => {
      void result.current.selectSecurity("sec-1");
    });

    await waitFor(() => expect(getInstrumentPassport).toHaveBeenCalledWith("sec-1"));
    await waitFor(() => expect(result.current.instrumentPassportView.providerRows).toHaveLength(2));

    expect(result.current.trustSnapshot).toBe(securityTrustSnapshot);
    expect(result.current.instrumentPassportErrorText).toBeNull();
    expect(result.current.instrumentPassportView.fields).toEqual(expect.arrayContaining([
      { label: "Provider confidence", value: "1 active / 2 total" }
    ]));
  });

  it("surfaces Security Master instrument passport failures while preserving other drill-in evidence", async () => {
    const getInstrumentPassport = vi.fn().mockRejectedValue(new MeridianApiError({
      path: "/api/workstation/security-master/securities/sec-1/passport",
      status: 503,
      detail: "Passport provider unavailable."
    }));
    const services = createSecurityMasterServices();
    const drillInServices = createSecurityMasterDrillInServices({ getInstrumentPassport });

    const { result } = renderHook(() => useSecurityMasterViewModel(true, services, drillInServices, 0));

    act(() => {
      void result.current.selectSecurity("sec-1");
    });

    await waitFor(() => expect(result.current.instrumentPassportErrorText).toBe("Passport provider unavailable."));

    expect(result.current.trustSnapshot).toBe(securityTrustSnapshot);
    expect(result.current.tradingParameters).toBe(tradingParameters);
    expect(result.current.instrumentPassport).toBeNull();
    expect(result.current.instrumentPassportView).toMatchObject({
      errorText: "Passport provider unavailable.",
      errorDetails: ["Endpoint returned 503 for /api/workstation/security-master/securities/sec-1/passport."]
    });
  });

  it("ignores stale Security Master identity responses after a newer selection settles", async () => {
    const staleIdentity = deferred<SecurityIdentityDrillIn>();
    const latestIdentity = deferred<SecurityIdentityDrillIn>();
    const services = createSecurityMasterServices({
      getIdentity: vi.fn()
        .mockReturnValueOnce(staleIdentity.promise)
        .mockReturnValueOnce(latestIdentity.promise)
    });
    const drillInServices = createSecurityMasterDrillInServices();
    const latestDetail: SecurityIdentityDrillIn = {
      ...securityIdentity,
      securityId: "sec-2",
      displayName: "Microsoft Corp."
    };

    const { result } = renderHook(() => useSecurityMasterViewModel(true, services, drillInServices, 0));

    act(() => {
      void result.current.selectSecurity("sec-1");
    });
    act(() => {
      void result.current.selectSecurity("sec-2");
    });
    await act(async () => {
      latestIdentity.resolve(latestDetail);
      await latestIdentity.promise;
    });

    await waitFor(() => expect(result.current.identity?.securityId).toBe("sec-2"));

    await act(async () => {
      staleIdentity.resolve(securityIdentity);
      await staleIdentity.promise;
    });

    expect(result.current.identity?.securityId).toBe("sec-2");
    expect(result.current.identityLoading).toBe(false);
    expect(result.current.identityErrorText).toBeNull();
  });

  it("clears pending Security Master drill-in state when the workstream becomes inactive", async () => {
    const identity = deferred<SecurityIdentityDrillIn>();
    const corporateActions = deferred<CorporateAction[]>();
    const parameters = deferred<TradingParameters>();
    const trustSnapshot = deferred<SecurityMasterTrustSnapshot>();
    const passport = deferred<InstrumentPassport>();
    const services = createSecurityMasterServices({
      getIdentity: vi.fn().mockReturnValue(identity.promise)
    });
    const drillInServices = createSecurityMasterDrillInServices({
      getCorporateActions: vi.fn().mockReturnValue(corporateActions.promise),
      getInstrumentPassport: vi.fn().mockReturnValue(passport.promise),
      getTradingParameters: vi.fn().mockReturnValue(parameters.promise),
      getTrustSnapshot: vi.fn().mockReturnValue(trustSnapshot.promise)
    });

    const { result, rerender } = renderHook(
      ({ active }) => useSecurityMasterViewModel(active, services, drillInServices, 0),
      { initialProps: { active: true } }
    );

    act(() => {
      void result.current.selectSecurity("sec-1");
    });
    await waitFor(() => expect(result.current.identityLoading).toBe(true));

    rerender({ active: false });

    await waitFor(() => expect(result.current.identityLoading).toBe(false));
    expect(result.current.selectedSecurityId).toBeNull();
    expect(result.current.identity).toBeNull();
    expect(result.current.corporateActionsLoading).toBe(false);
    expect(result.current.tradingParametersLoading).toBe(false);
    expect(result.current.trustSnapshotLoading).toBe(false);
    expect(result.current.instrumentPassportLoading).toBe(false);

    await act(async () => {
      identity.resolve(securityIdentity);
      corporateActions.resolve([]);
      parameters.resolve(tradingParameters);
      trustSnapshot.resolve(securityTrustSnapshot);
      passport.resolve(instrumentPassport);
      await Promise.all([identity.promise, corporateActions.promise, parameters.promise, trustSnapshot.promise, passport.promise]);
    });

    expect(result.current.identity).toBeNull();
    expect(result.current.selectedSecurityId).toBeNull();
    expect(result.current.trustSnapshot).toBeNull();
    expect(result.current.instrumentPassport).toBeNull();
  });

  it("surfaces search failures and counts open conflicts for badges", () => {
    const failed = buildSecuritySearchState({
      query: "AAPL",
      searching: false,
      results: [],
      searchError: "Provider offline",
      identityLoading: false,
      identityError: null
    });

    expect(failed.searchErrorText).toBe("Security search failed: Provider offline");
    expect(failed.statusAnnouncement).toBe("Security search failed: Provider offline");
    expect(countOpenSecurityConflicts(conflicts)).toBe(1);
    expect(countOpenSecurityConflicts(null)).toBe(0);
    expect(buildSecurityConflictRefreshCommand(false, null)).toEqual({
      label: "Refresh conflicts",
      ariaLabel: "Refresh Security Master identifier conflicts",
      disabled: false,
      disabledReason: null,
      busy: false,
      busyLabel: null,
      feedbackId: "security-conflict-refresh-feedback",
      feedbackText: null
    });
    expect(buildSecurityConflictRefreshCommand(true, null)).toMatchObject({
      label: "Refreshing...",
      disabled: true,
      disabledReason: "Identifier conflicts are already loading.",
      busy: true,
      busyLabel: "Refreshing..."
    });
    expect(buildSecurityConflictRefreshCommand(false, "Provider offline")).toMatchObject({
      label: "Retry conflicts",
      ariaLabel: "Retry loading Security Master identifier conflicts"
    });
    expect(buildSecurityConflictRefreshCommand(false, null, "conflict-1")).toMatchObject({
      label: "Refresh conflicts",
      ariaLabel: "Refresh disabled while identifier conflict conflict-1 is resolving",
      disabled: true,
      disabledReason: "Wait until identifier conflict conflict-1 finishes resolving before refreshing the conflict queue.",
      busy: false,
      feedbackId: "security-conflict-refresh-feedback",
      feedbackText: "Wait until identifier conflict conflict-1 finishes resolving before refreshing the conflict queue."
    });
  });

  it("preserves structured Security Master search errors with operator details", () => {
    const failed = buildSecuritySearchState({
      query: "AAPL",
      searching: false,
      results: [],
      searchError: describeApiError(new MeridianApiError({
        path: "/api/security-master/search",
        status: 503,
        title: "Provider unavailable",
        detail: "Search feed is offline."
      }), "Security search failed."),
      identityLoading: false,
      identityError: null
    });

    expect(failed.searchErrorText).toBe("Security search failed: Search feed is offline.");
    expect(failed.searchErrorDetails).toEqual([
      "Endpoint returned 503 for /api/security-master/search.",
      "Provider unavailable"
    ]);
    expect(failed.statusAnnouncement).toBe("Security search failed: Search feed is offline.");
  });

  it("derives Security Master master-detail page summary from selected state", () => {
    const state = buildSecurityMasterPageViewState({
      query: "AAPL",
      results: [securityResult],
      selectedSecurityId: "sec-1",
      selectedDisplayName: "Apple Inc.",
      selectedAssetClass: "Equity",
      selectedStatus: "Active",
      identity: securityIdentity,
      identityLoading: false,
      conflicts,
      conflictsLoading: false,
      corporateActions,
      securitySchedules: cashFlowSchedules,
      openLotReadModel: securityTrustSnapshot.openLotReadModel ?? null,
      tradingParameters
    });

    expect(state).toMatchObject({
      ariaLabel: "Security Master command deck",
      title: "Security Master command deck",
      detailTitle: "Security detail page",
      detailSubtitle: "sec-1 · Equity",
      detailStatusLabel: "Active",
      detailStatusBadgeVariant: "success"
    });
    expect(state.metrics).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "results", value: "1", tone: "success" }),
      expect.objectContaining({ id: "selected", value: "Apple Inc.", detail: "Security ID sec-1" }),
      expect.objectContaining({ id: "conflicts", value: "1", tone: "warning" })
    ]));
    expect(state.detailSections).toEqual(expect.arrayContaining([
      { id: "overview", label: "Overview", value: "1 identifier", active: true },
      { id: "schedules", label: "Schedules", value: "2 cash-flow events" },
      { id: "lots", label: "Open lots", value: "1 lot" },
      { id: "controls", label: "Controls", value: "Trading set" },
      { id: "audit", label: "Audit", value: "1 conflict" }
    ]));
  });

  it("retries Security Master identifier conflicts through view-model command state", async () => {
    const retryConflicts = deferred<SecurityMasterConflict[]>();
    const services = createSecurityMasterServices({
      getConflicts: vi.fn()
        .mockRejectedValueOnce(new MeridianApiError({
          path: "/api/workstation/security-master/conflicts",
          status: 503,
          detail: "Conflict API offline"
        }))
        .mockReturnValueOnce(retryConflicts.promise)
    });
    const drillInServices = createSecurityMasterDrillInServices();

    const { result } = renderHook(() => useSecurityMasterViewModel(true, services, drillInServices, 0));

    await waitFor(() => expect(result.current.conflictsErrorText).toBe("Conflict API offline"));
    expect(result.current.conflictsErrorDetails).toEqual([
      "Endpoint returned 503 for /api/workstation/security-master/conflicts."
    ]);
    expect(result.current.conflictRefreshCommand).toMatchObject({
      label: "Retry conflicts",
      disabled: false
    });

    let retry!: Promise<void>;
    act(() => {
      retry = result.current.refreshConflicts();
    });

    await waitFor(() => expect(result.current.conflictRefreshCommand).toMatchObject({
      label: "Refreshing...",
      disabled: true,
      busy: true
    }));

    await act(async () => {
      retryConflicts.resolve(conflicts);
      await retry;
    });

    expect(result.current.conflictsErrorText).toBeNull();
    expect(result.current.conflictRows).toHaveLength(2);
    expect(result.current.conflictRefreshCommand).toMatchObject({
      label: "Refresh conflicts",
      disabled: false
    });
  });

  it("derives Security Master identity drill-in rows and accessible table labels", () => {
    const state = buildSecurityIdentityDrillInState(securityIdentity);

    expect(state).toMatchObject({
      panelId: "security-master-identity-detail",
      title: "Identity drill-in · Apple Inc.",
      subtitle: "sec-1 · v3 · Equity",
      description: "1 identifier · 1 alias · effective 2024-01-01 -> active",
      ariaLabel: "Security identity detail for Apple Inc.",
      statusLabel: "Active",
      statusBadgeVariant: "success",
      identifiersTableLabel: "Identifiers for Apple Inc.",
      aliasesTableLabel: "Aliases for Apple Inc."
    });
    expect(state?.summaryFields).toEqual(expect.arrayContaining([
      { label: "Security ID", value: "sec-1" },
      { label: "Effective", value: "2024-01-01 -> active" }
    ]));
    expect(state?.identifiers[0]).toMatchObject({
      rowId: "identifier-ticker-aapl",
      providerLabel: "Bloomberg",
      primaryLabel: "Primary",
      primaryBadgeVariant: "success",
      validRangeLabel: "2024-01-01 -> active",
      ariaLabel: "Ticker AAPL, Primary, provider Bloomberg, valid 2024-01-01 -> active"
    });
    expect(state?.aliases[0]).toMatchObject({
      rowId: "alias-alias-1",
      providerLabel: "—",
      enabledLabel: "Enabled",
      enabledBadgeVariant: "success",
      validRangeLabel: "2025-01-01 -> active",
      createdLabel: "2025-01-01",
      reasonText: "Market data source mapping",
      ariaLabel: "ProviderSymbol AAPL.OQ, Enabled, scope Collector, provider —, valid 2025-01-01 -> active"
    });
  });

  it("derives provider-specific conflict actions and row accessibility copy", () => {
    const rows = buildSecurityConflictRows(conflicts, "conflict-1");

    expect(rows[0]).toMatchObject({
      conflictId: "conflict-1",
      statusTone: "warning",
      isOpen: true,
      isResolving: true,
      providerASummary: "Bloomberg -> security sec-1",
      providerBSummary: "Refinitiv -> security sec-2",
      detectedLabel: "Detected 2026-01-01",
      resolutionStatusText: "Resolving identifier conflict conflict-1."
    });
    expect(rows[0].ariaLabel).toContain("Identifier conflict conflict-1 on identifiers.CUSIP: Open.");
    expect(rows[0].actions).toEqual([
      expect.objectContaining({
        resolution: "AcceptA",
        label: "Use Bloomberg",
        disabled: true,
        disabledReason: "Resolution is already in progress for identifier conflict conflict-1.",
        ariaLabel: "Resolve identifier conflict conflict-1 on identifiers.CUSIP with Bloomberg value sec-1. Disabled: Resolution is already in progress for identifier conflict conflict-1."
      }),
      expect.objectContaining({
        resolution: "AcceptB",
        label: "Use Refinitiv",
        disabled: true,
        disabledReason: "Resolution is already in progress for identifier conflict conflict-1.",
        ariaLabel: "Resolve identifier conflict conflict-1 on identifiers.CUSIP with Refinitiv value sec-2. Disabled: Resolution is already in progress for identifier conflict conflict-1."
      }),
      expect.objectContaining({
        resolution: "Dismiss",
        label: "Dismiss conflict",
        disabled: true,
        disabledReason: "Resolution is already in progress for identifier conflict conflict-1.",
        ariaLabel: "Dismiss identifier conflict conflict-1 on identifiers.CUSIP. Disabled: Resolution is already in progress for identifier conflict conflict-1."
      })
    ]);
    expect(rows[1]).toMatchObject({
      conflictId: "conflict-2",
      statusTone: "neutral",
      isOpen: false,
      actions: []
    });
  });

  it("derives reconciliation break action state and live announcements", () => {
    const rows = buildReconciliationBreakRows(breakQueue, { breakId: "run-42:cash", command: "assign" });

    expect(rows[0]).toMatchObject({
      breakId: "run-42:cash",
      actionBusy: true,
      varianceLabel: "+$500.00",
      varianceTone: "success",
      statusBadgeVariant: "danger",
      ownerLabel: "Unassigned",
      rowSelectAriaLabel: "Inspect reconciliation break run-42:cash",
      assignLabel: "Assigning...",
      canAssign: false,
      canResolve: false,
      canDismiss: false
    });
    expect(rows[1]).toMatchObject({
      breakId: "run-57:fees",
      resolveLabel: "Resolve",
      canAssign: false,
      canResolve: false,
      canDismiss: false
    });

    const state = buildReconciliationBreakQueueState({
      breakQueue,
      selectedBreakId: "run-57:fees",
      loading: false,
      loadError: null,
      action: { breakId: "run-42:cash", command: "assign" },
      actionError: null
    });

    expect(state.hasBreaks).toBe(true);
    expect(state.tableLabel).toBe("Reconciliation break queue");
    expect(state.selectedBreakId).toBe("run-57:fees");
    expect(state.selectedDetail).toMatchObject({
      id: "reconciliation-break-detail-panel",
      title: "Intraday Vol Carry - FeeMismatch",
      statusLabel: "Resolved",
      statusBadgeVariant: "success",
      analysisText: "Provider fees and Meridian ledger fees now match after operator review.",
      recommendedActionText: "Attach the provider activity evidence before close sign-off.",
      routingActionLabel: "Open routing target",
      routingActionHref: "/accounting/ledger",
      routingActionAriaLabel: "Open routing target for reconciliation break run-57:fees"
    });
    expect(state.selectedDetail?.fields).toEqual(expect.arrayContaining([
      { label: "Detected", value: "Jan 2, 00:00 UTC" },
      { label: "Updated", value: "Jan 2, 00:00 UTC" },
      { label: "Exception route", value: "fund-ops-review" },
      { label: "Tolerance profile", value: "fee-variance-ops" },
      { label: "Tolerance band", value: "$100" },
      {
        label: "Required sign-off",
        value: "Decision captured; sign-off: Pending Signoff by Fund operations lead. Close approval remains blocked."
      },
      { label: "Decision note", value: "Reviewed in accounting panel." },
      { label: "Explanation summary", value: "Provider fees and Meridian ledger fees now match after operator review." },
      { label: "Source systems", value: "Provider activity, Meridian ledger" },
      { label: "Probable cause", value: "The provider posted fees after the first reconciliation pass." },
      { label: "Ledger impact", value: "No remaining ledger adjustment is required." },
      { label: "Suggested next action", value: "Attach the provider activity evidence before close sign-off." },
      { label: "Explanation evidence", value: "/accounting/evidence/provider-fees" }
    ]));
    expect(state.rows[1]).toMatchObject({
      isSelected: true,
      isExpanded: true,
      detailPanelId: "reconciliation-break-detail-panel"
    });
    expect(state.statusAnnouncement).toBe("Assigning reconciliation break run-42:cash.");
  });

  it("derives reconciliation empty and failure copy", () => {
    const empty = buildReconciliationBreakQueueState({
      breakQueue: [],
      loading: false,
      loadError: null,
      action: null,
      actionError: null
    });

    expect(empty.hasBreaks).toBe(false);
    expect(empty.selectedDetail).toBeNull();
    expect(empty.detailEmptyAriaLabel).toBe("No reconciliation break selected");
    expect(empty.emptyText).toBe("No reconciliation breaks in the current queue.");
    expect(empty.statusAnnouncement).toBe("No reconciliation breaks in the current queue.");

    const failed = buildReconciliationBreakQueueState({
      breakQueue,
      loading: false,
      loadError: "Provider offline",
      action: null,
      actionError: "Review endpoint rejected"
    });

    expect(failed.errorText).toBe("Reconciliation break queue failed: Provider offline");
    expect(failed.actionErrorText).toBe("Break action failed: Review endpoint rejected");
    expect(failed.statusAnnouncement).toBe("Break action failed: Review endpoint rejected");
  });

  it("derives reconciliation resolve dialog labels and validation state", () => {
    const blankResolve = buildReconciliationResolveDialogState("run-42:cash", "Resolved", "  ");

    expect(blankResolve).toMatchObject({
      breakId: "run-42:cash",
      status: "Resolved",
      inputId: "rationale-run-42:cash",
      helpId: "rationale-help-run-42:cash",
      formAriaLabel: "Resolve reconciliation break run-42:cash",
      label: "Resolve rationale",
      submitLabel: "Confirm resolve",
      submitAriaLabel: "Confirm resolve for reconciliation break run-42:cash",
      cancelLabel: "Cancel",
      cancelAriaLabel: "Cancel resolve for reconciliation break run-42:cash",
      isSubmitDisabled: true
    });

    const dismiss = buildReconciliationResolveDialogState("run-42:cash", "Dismissed", "Reviewed duplicate break");

    expect(dismiss).toMatchObject({
      label: "Dismiss rationale",
      placeholder: "Describe why this break is being dismissed...",
      submitLabel: "Confirm dismiss",
      isSubmitDisabled: false
    });
  });

  it("keeps reconciliation narratives in the view model", () => {
    expect(buildReconciliationNarrative(reconciliationQueue[0])).toContain("Open reconciliation breaks remain");
    expect(buildReconciliationNarrative({ ...reconciliationQueue[0], reconciliationStatus: "Balanced" })).toContain("currently balanced");
    expect(buildReconciliationNarrative({ ...reconciliationQueue[0], reconciliationStatus: "NotStarted" })).toContain("Accounting review work");
    expect(buildReconciliationNarrative({ ...reconciliationQueue[0], reconciliationStatus: "NotStarted" })).not.toContain("governance review work");
  });

  it("derives reconciliation detail presentation state", () => {
    expect(buildReconciliationDetailViewState(reconciliationQueue[0])).toMatchObject({
      eyebrow: "Reconciliation detail",
      title: "Paper Index Mean Reversion",
      description: "run-42 is currently BreaksOpen.",
      ariaLabel: "Reconciliation detail for Paper Index Mean Reversion",
      narrativeLabel: "Reconciliation narrative for Paper Index Mean Reversion",
      fields: [
        { label: "Mode", value: "PAPER", tone: "default", ariaLabel: "Mode: PAPER" },
        { label: "Run status", value: "Running", tone: "default", ariaLabel: "Run status: Running" },
        { label: "Break count", value: "2", tone: "default", ariaLabel: "Break count: 2" },
        { label: "Open breaks", value: "1", tone: "warning", ariaLabel: "Open breaks: 1" },
        { label: "Last updated", value: "3m ago", tone: "default", ariaLabel: "Last updated: 3m ago" }
      ]
    });
  });

  it("derives reconciliation detail actions from the selected run", () => {
    expect(buildReconciliationDetailActions(reconciliationQueue[0])).toEqual({
      breakChecklistTargetId: "reconciliation-break-queue",
      breakChecklistHref: "#reconciliation-break-queue",
      breakChecklistLabel: "Open break checklist",
      breakChecklistAriaLabel: "Open break checklist for Paper Index Mean Reversion; 1 open break",
      evidencePacketHref: "/reporting/evidence?subjectKind=reconciliation-review&subjectId=run-42",
      evidencePacketLabel: "Evidence packet",
      evidencePacketAriaLabel: "Open reconciliation evidence packet for Paper Index Mean Reversion",
      auditPacketHref: "/api/workstation/runs/run-42/review-packet",
      auditPacketLabel: "Review audit packet",
      auditPacketAriaLabel: "Review audit packet for Paper Index Mean Reversion"
    });
  });

  it("derives reporting profile selector rows and detail state", () => {
    const state = buildAccountingReportingViewState({
      reporting: {
        profileCount: 2,
        recommendedProfiles: ["board"],
        reportPackTargets: ["board", "audit"],
        summary: "2 export/reporting profiles are available for Accounting and Reporting workflows.",
        profiles: [
          {
            id: "excel",
            name: "Excel",
            targetTool: "Excel",
            format: "Xlsx",
            description: "Board-ready workbook export.",
            loaderScript: false,
            dataDictionary: true
          },
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
      },
      selectedProfileId: "board"
    });

    expect(state.countLabel).toBe("2 profiles");
    expect(state.targetSummary).toBe("Board, Audit");
    expect(state.rows[1]).toMatchObject({
      id: "board",
      isSelected: true,
      formatLabel: "MARKDOWN",
      targetLabel: "Target - Board",
      recommendationLabel: "Recommended for current packet flow",
      selectAriaLabel: "Inspect reporting profile Board packet for Board Markdown"
    });
    expect(state.rows[1].badges.map((badge) => badge.label)).toEqual(["Recommended", "Dictionary missing", "Loader script"]);
    expect(state.selectedProfile?.fields).toEqual(expect.arrayContaining([
      expect.objectContaining({ label: "Data dictionary", value: "Missing", tone: "warning" }),
      expect.objectContaining({ label: "Loader script", value: "Available", tone: "success" })
    ]));
    expect(state.selectedExportProfileId).toBe("board");
    expect(state.exportCanRun).toBe(true);
    expect(state.exportAriaLabel).toBe("Run reporting export for Board packet");
    expect(state.exportDisabledReason).toBeNull();
    expect(state.backendLinks).toEqual([
      {
        id: "preview",
        label: "Preview report payload",
        href: "/api/export/preview",
        ariaLabel: "Open GET /api/export/preview for Preview report payload"
      },
      {
        id: "formats",
        label: "List export formats",
        href: "/api/export/formats",
        ariaLabel: "Open GET /api/export/formats for List export formats"
      }
    ]);
  });

  it("surfaces reporting profile empty state from the view model", () => {
    const state = buildAccountingReportingViewState({
      reporting: {
        profileCount: 0,
        recommendedProfiles: [],
        reportPackTargets: [],
        profiles: [],
        summary: "No profiles loaded."
      },
      selectedProfileId: null
    });

    expect(state.hasRows).toBe(false);
    expect(state.emptyText).toBe("No reporting profiles available. Sync report-pack metadata before export review.");
    expect(state.statusDetail).toBe("No reporting profiles are configured for packet generation.");
    expect(state.nextAction).toBe("Sync reporting profile metadata before packet generation.");
    expect(state.exportCanRun).toBe(false);
    expect(state.exportAriaLabel).toBe("Run reporting export unavailable until a reporting profile is loaded");
    expect(state.exportDisabledReason).toBe("Load or select a reporting profile before running an export.");
  });

  it("surfaces reporting export busy state from the view model", () => {
    const state = buildAccountingReportingViewState({
      reporting: {
        profileCount: 1,
        recommendedProfiles: ["excel"],
        reportPackTargets: ["board"],
        summary: "1 profile loaded.",
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
        ]
      },
      selectedProfileId: "excel",
      exportBusy: true
    });

    expect(state.exportCanRun).toBe(false);
    expect(state.exportButtonLabel).toBe("Export running...");
    expect(state.exportAriaLabel).toBe("Excel reporting export is already running");
    expect(state.exportDisabledReason).toBe("Excel reporting export is already running.");
  });

  it("formats reporting export command results for success and failure states", () => {
    expect(formatReportingExportResult({
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
    })).toEqual({
      text: "Export export-1 completed with 2 file(s), 12 record(s), and 2 KB. Output artifacts/exports/export-1.",
      tone: "success",
      role: "status"
    });

    expect(formatReportingExportResult({
      jobId: null,
      success: false,
      status: "failed",
      profileId: "excel",
      symbols: null,
      filesGenerated: 0,
      totalRecords: 0,
      totalBytes: 0,
      outputDirectory: null,
      durationSeconds: 0,
      error: "Exporter unavailable",
      warnings: null,
      files: [],
      timestamp: "2026-01-01T00:00:00Z"
    })).toEqual({
      text: "Export excel failed: Exporter unavailable",
      tone: "danger",
      role: "alert"
    });
  });
});
