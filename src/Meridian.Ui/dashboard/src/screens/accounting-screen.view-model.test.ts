import { act, renderHook, waitFor } from "@testing-library/react";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it, vi } from "vitest";
import { ApiError as MeridianApiError, describeApiError } from "@/lib/api-errors";
import {
  buildCalibrationSummaryViewState,
  buildCorporateActionsViewState,
  buildAccountingCashFlowViewState,
  buildAccountingLoadingViewState,
  buildAccountingReportingViewState,
  buildAccountingLedgerJournalEvidenceViewState,
  buildAccountingTrialBalanceViewState,
  buildSecurityScheduleRows,
  buildSecuritySchedulesViewState,
  buildSecurityOpenLotReadModelViewState,
  buildSecurityOpenLotRows,
  mapScheduleBookToCashFlowScheduleEvents,
  formatReportingExportResult,
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
  resolveSelectedReconciliation,
  useCapitalAccountWorkbenchViewModel,
  useAccountingConfigurationViewModel,
  useAccountingReconciliationViewModel,
  useSecurityMasterViewModel
} from "@/screens/accounting-screen.view-model";
import {
  accountingTaskModeLauncherLinks,
  buildAccountingTaskMode,
  resolveAccountingWorkstream
} from "@/screens/accounting-screen.task-mode-view-model";
import type {
  AccountingConfigurationServices,
  AccountingReconciliationServices,
  CapitalAccountWorkbenchServices,
  SecurityCashFlowScheduleEvent,
  SecurityMasterDrillInServices,
  SecurityMasterServices
} from "@/screens/accounting-screen.view-model";
import type {
  CorporateAction,
  AccountingCashFlowSummary,
  AccountingWorkspaceResponse,
  AccountingConfigurationWorkspace,
  AccountingProductionReadiness,
  AccountingTenantAdministrationProfile,
  CapitalAccountWorkbench,
  RuleDryRunResult,
  ManualJournalEntryDraft,
  ManualJournalEntryWorkbench,
  LedgerJournalLine,
  LedgerTrialBalanceLine,
  ReconciliationCalibrationSummary,
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
  ],
  referenceDataWorkbench: {
    status: "Ready",
    summary: "Multi-asset reference-data workbench is ready for downstream FINOPS use.",
    sections: [
      {
        sectionId: "provider-evidence",
        title: "Provider evidence",
        status: "Ready",
        summary: "1 active provider evidence row retained on the passport.",
        evidenceCount: 2,
        blockingIssueCount: 0
      },
      {
        sectionId: "identifier-confidence",
        title: "Identifier confidence",
        status: "Ready",
        summary: "Primary identifiers are aligned.",
        evidenceCount: 3,
        blockingIssueCount: 0
      },
      {
        sectionId: "operations-handoff",
        title: "Operations handoff",
        status: "Ready",
        summary: "Used by accounting and trading workflows.",
        evidenceCount: 1,
        blockingIssueCount: 0
      }
    ],
    operationsHandoffs: [
      {
        handoffId: "handoff-1",
        target: "FINOPS",
        title: "Retain Security Master context",
        detail: "Continue close review from the selected passport.",
        status: "Available",
        isEnabled: true
      }
    ]
  },
  operationsWorkbench: {
    status: "Ready",
    summary: "Security Master operations workbench is ready for downstream portfolio, accounting, reconciliation, close, and reporting use.",
    panels: [
      {
        panelId: "identity",
        title: "Identity",
        status: "Ready",
        summary: "Identity panel has 3 retained evidence item(s) and no blocking issue.",
        items: [
          {
            itemId: "primary-identifier",
            label: "Ticker",
            value: "AAPL",
            status: "Ready",
            detail: "Primary identifiers are aligned.",
            evidenceCount: 1,
            blockingIssueCount: 0
          }
        ]
      },
      {
        panelId: "provider-evidence",
        title: "Provider evidence",
        status: "Ready",
        summary: "Provider evidence panel has retained source records and no blocking issue.",
        items: [
          {
            itemId: "source-record-1",
            label: "golden-edm",
            value: "EconomicDefinition: AAPL",
            status: "Ready",
            detail: "Source record edm-aapl; as of 2026-05-28 00:00 UTC; updated by steward; primary source.",
            evidenceCount: 1,
            blockingIssueCount: 0,
            route: "/workstation/accounting/security-master#source-1"
          }
        ]
      },
      {
        panelId: "terms",
        title: "Terms",
        status: "Ready",
        summary: "Terms panel has retained economics, schedule, and obligation evidence.",
        items: [
          {
            itemId: "economics",
            label: "Economics",
            value: "Trading parameters are active.",
            status: "Ready",
            detail: "2 corporate action obligation event(s) retained on the passport.",
            evidenceCount: 3,
            blockingIssueCount: 0
          }
        ]
      }
    ],
    readiness: [
      {
        readinessId: "ledger",
        label: "Ledger-ready",
        status: "Ready",
        isReady: true,
        summary: "1 ledger line",
        evidenceCount: 2,
        blockingIssueCount: 0,
        nextAction: "No blocker.",
        route: "/workstation/accounting/ledger"
      },
      {
        readinessId: "close",
        label: "Close-ready",
        status: "Ready",
        isReady: true,
        summary: "Used by accounting and trading workflows.",
        evidenceCount: 3,
        blockingIssueCount: 0,
        nextAction: "No blocker.",
        route: "/workstation/accounting/security-master"
      }
    ],
    handoffs: [
      {
        handoffId: "handoff-1",
        target: "FINOPS",
        title: "Retain Security Master context",
        detail: "Continue close review from the selected passport.",
        status: "Available",
        isEnabled: true,
        owner: "Security Master steward",
        blockerReason: "Continue close review from the selected passport.",
        impactedOutputs: ["Ledger", "Close"],
        linkedCases: [],
        route: "/workstation/accounting/security-master"
      }
    ]
  },
  operatingModel: {
    securityId: "sec-1",
    clientId: null,
    accountId: "acct-1",
    fundProfileId: "fund-alpha",
    retrievedAtUtc: "2026-06-03T12:00:00Z",
    status: "Ready",
    summary: "Security Master operating model has applicable entitlement, source, control, and approval evidence for the selected scope.",
    stages: [
      {
        stageId: "reconcile",
        title: "Reconcile",
        status: "Ready",
        summary: "1 most-specific entitlement record applies to the selected Security Master scope.",
        evidenceCount: 1,
        blockingIssueCount: 0
      }
    ],
    entitlementApplicability: [
      {
        entitlementId: "ent-1",
        vendorName: "LSEG/Refinitiv",
        dataType: "Pricing",
        scope: "FundProfile",
        clientId: null,
        accountId: null,
        fundProfileId: "fund-alpha",
        securityId: null,
        isApplicable: true,
        isMostSpecific: true,
        status: "Active",
        requiresDirectClientContract: true,
        contractReference: "LSEG-FUND-ALPHA",
        summary: "LSEG/Refinitiv Pricing entitlement applies at FundProfile scope with Active status."
      }
    ],
    operatorMetadata: [
      {
        metadataId: "entitlement-ent-1",
        vendorName: "LSEG/Refinitiv",
        dataType: "Pricing",
        sourceCategory: "Fund pricing feed",
        expectedRefreshCadence: "Daily close",
        defaultMaxDaysStale: 1,
        requiresDirectClientContract: true,
        operatorMetadata: "Fund Alpha direct-client pricing metadata.",
        summary: "Fund Alpha direct-client pricing metadata."
      }
    ],
    manualChangeApproval: {
      policyKey: "operations-continuity.security-master-override",
      gate: "SecurityMaster",
      route: "/api/workstation/operations-continuity/security-master/overrides/approve",
      requiredPermission: "AdminMaintenance or ModifySecurityMaster",
      requiredDistinctApprovals: 1,
      requiresIndependentReviewer: true,
      evidenceRequirement: "Override id, policy reference, rationale, expiration date, and linked evidence.",
      status: "Ready",
      manualChangeCount: 1,
      unapprovedManualChangeCount: 0,
      summary: "1 manual change event reuses the operations approval policy."
    },
    controls: []
  }
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

const trialBalanceLines: LedgerTrialBalanceLine[] = [
  {
    accountName: "Cash",
    accountType: "Asset",
    symbol: null,
    financialAccountId: "acct-cash",
    balance: 120500,
    entryCount: 12,
    security: null,
    dimensions: {
      fundId: "fund-alpha",
      entityId: "entity-alpha",
      sleeveId: "sleeve-credit",
      costCenterId: "ops-close",
      externalGlDimensions: {
        class: "private-fund",
        department: "finance"
      }
    },
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

const journalLines: LedgerJournalLine[] = [
  {
    journalEntryId: "journal-cash-1",
    timestamp: "2026-06-30T14:30:00Z",
    description: "Cash close journal",
    totalDebits: 120500,
    totalCredits: 120500,
    lineCount: 2,
    dimensions: {
      fundId: "fund-alpha",
      entityId: "entity-alpha",
      sleeveId: "sleeve-credit",
      costCenterId: "ops-close",
      externalGlDimensions: {
        class: "private-fund"
      }
    }
  },
  {
    journalEntryId: "journal-unscoped",
    timestamp: "2026-06-30T15:00:00Z",
    description: "Legacy unscoped journal",
    totalDebits: 500,
    totalCredits: 500,
    lineCount: 2
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
  it("keeps Accounting task-mode routing outside the overloaded view model", () => {
    // NB: read relative to the dashboard project root (process.cwd()). The Vitest runner executes
    // from src/Meridian.Ui/dashboard, and import.meta.url is not a file:// URL under Vitest, so
    // `new URL(..., import.meta.url)` throws ERR_INVALID_URL_SCHEME.
    const viewModelSource = readFileSync(resolve(process.cwd(), "src/screens/accounting-screen.view-model.ts"), "utf8");
    const taskModeSource = readFileSync(resolve(process.cwd(), "src/screens/accounting-screen.task-mode-view-model.ts"), "utf8");

    expect(taskModeSource).toContain("const accountingTaskModeDefinitions");
    expect(taskModeSource).toContain("export const accountingTaskModeLauncherLinks");
    expect(taskModeSource).toContain("export function resolveAccountingWorkstream");
    expect(taskModeSource).toContain("export function buildAccountingTaskMode");
    expect(taskModeSource).toContain("export function accountingWorkstreamHref");
    expect(viewModelSource).not.toContain("const accountingTaskModeDefinitions");
    expect(viewModelSource).not.toContain("function normalizeAccountingTaskModePath");
    expect(viewModelSource).not.toContain("function buildAccountingTaskModeViewModel");
    expect(accountingTaskModeLauncherLinks.map((mode) => [mode.id, mode.href])).toEqual([
      ["reconciliation-casework", "/accounting/reconciliation"],
      ["ledger-explorer", "/accounting/ledger"],
      ["journal-entry", "/accounting/journal-entries"],
      ["capital-accounts", "/accounting/capital-accounts"],
      ["delivery-evidence", "/reporting/evidence"],
      ["governance", "/accounting/configure"]
    ]);
  });

  it("derives the accounting workstream and selected reconciliation run", () => {
    expect(resolveAccountingWorkstream("/accounting/security-master")).toBe("security-master");
    expect(resolveAccountingWorkstream("/accounting/reconciliation")).toBe("reconciliation");
    expect(resolveAccountingWorkstream("/accounting/exceptions")).toBe("exceptions");
    expect(resolveAccountingWorkstream("/accounting/approvals")).toBe("approvals");
    expect(resolveAccountingWorkstream("/accounting/capital-accounts")).toBe("capital-accounts");
    expect(resolveAccountingWorkstream("/accounting")).toBe("ledger");
    expect(resolveAccountingWorkstream("/accounting/ledger")).toBe("ledger");
    expect(resolveAccountingWorkstream("/reporting")).toBe("ledger");
    expect(resolveAccountingWorkstream("/accounting/reporting")).toBe("reporting");
    expect(resolveAccountingWorkstream("/governance/security-master")).toBe("security-master");
    expect(resolveAccountingWorkstream("/governance/reconciliation")).toBe("reconciliation");
    expect(resolveAccountingWorkstream("/governance")).toBe("ledger");

    expect(buildAccountingTaskMode("/accounting")).toMatchObject({
      id: "close-cockpit",
      label: "Close Cockpit",
      href: "/accounting",
      workstream: "ledger"
    });
    expect(buildAccountingTaskMode("/accounting/ledger")).toMatchObject({
      id: "ledger-explorer",
      label: "Ledger Explorer",
      href: "/accounting/ledger",
      workstream: "ledger"
    });
    expect(buildAccountingTaskMode("/accounting/reconciliation")).toMatchObject({
      id: "reconciliation-casework",
      label: "Reconciliation Casework",
      href: "/accounting/reconciliation",
      workstream: "reconciliation"
    });
    expect(buildAccountingTaskMode("/accounting/journal-entries")).toMatchObject({
      id: "journal-entry",
      label: "Journal Entry",
      href: "/accounting/journal-entries",
      workstream: "journal-entries"
    });
    expect(buildAccountingTaskMode("/accounting/configure")).toMatchObject({
      id: "governance",
      label: "Governance",
      href: "/accounting/configure",
      workstream: "configure"
    });

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
      ledgerBookWorkflows: {
        ledgerBookId: "book-primary",
        postingRulesLedgerBookNativeCertified: true,
        journalLifecycleLedgerBookNativeCertified: true,
        closeReportingLedgerBookNativeCertified: false,
        closePlanConfigurationLedgerBookNativeCertified: false,
        externalGlLedgerBookNativeCertified: false,
        reconciliationLedgerBookNativeCertified: false,
        directLendingLedgerBookNativeCertified: false,
        strategyLedgerReadLedgerBookNativeCertified: false,
        evidenceReferences: ["evidence://ledger-book/book-primary/workflow-certification"],
        completedControlCount: 4,
        requiredControlCount: 10,
        hasLedgerBookScope: true,
        hasRetainedEvidence: true,
        hasLedgerBookScopedEvidence: true
      },
      dimensionalReporting: {
        ledgerBookId: "book-primary",
        periodReportDimensionQueriesCertified: true,
        crossPeriodReportDimensionQueriesCertified: false,
        journalQueryDimensionFiltersCertified: true,
        externalExportDimensionMappingCertified: false,
        ledgerLineDimensionsPersistedCertified: false,
        trialBalanceDimensionFiltersCertified: false,
        reportPackageDimensionProvenanceCertified: false,
        evidenceReferences: ["evidence://ledger-book/book-primary/dimensions/reporting"],
        completedControlCount: 4,
        requiredControlCount: 9,
        hasLedgerBookScope: true,
        hasRetainedEvidence: true,
        hasLedgerBookScopedEvidence: true
      },
      tenantAdministration: {
        tenantId: "tenant-alpha",
        companyId: "company-alpha",
        tenantScopeConfigured: true,
        adminRoleProfileConfigured: true,
        scopedAccessPoliciesConfigured: true,
        reportingGroupsConfigured: false,
        accountingAdminSurfaceConfigured: false,
        browserAccountingAdminSurfaceConfigured: false,
        wpfAccountingAdminSurfaceConfigured: false,
        chartAdministrationStudioConfigured: false,
        ruleTestPromotionStudioConfigured: false,
        closeSetupStudioConfigured: false,
        providerMappingStudioConfigured: false,
        tenantCompanyReportGroupSetupStudioConfigured: false,
        auditReviewToolingConfigured: false,
        bulkImportExportSafeguardsConfigured: false,
        performanceValidationConfigured: false,
        disasterRecoveryRunbookConfigured: false,
        ledgerBookAdministrationStudioConfigured: false,
        postingRuleAuthoringStudioConfigured: false,
        approvalQueueStudioConfigured: false,
        dimensionMappingStudioConfigured: false,
        implementationSandboxConfigured: false,
        evidenceReferences: ["evidence://tenant-admin/gap"],
        completedControlCount: 5,
        requiredControlCount: 23,
        hasTenantScope: true,
        hasCompanyScope: true,
        hasRetainedEvidence: true
      },
      productionGaps: [
        {
          code: "multi-ledger-native-workflows",
          label: "Configurable multi-ledger accounting",
          status: "ReviewRequired",
          highestSeverity: "Warning",
          summary: "Ledger-book-native certification still needs end-to-end workflow evidence.",
          requiredAction: "Retain ledger-book-native workflow evidence for posting, JE lifecycle, reconciliation, close, and reporting.",
          areas: ["LedgerBooks", "PostingRules", "JournalLifecycle", "CloseReporting"],
          blockingIssueCodes: ["workflow.evidence.missing", "journal.lifecycle.missing"],
          issues: [
            {
              code: "workflow.evidence.missing",
              area: "LedgerBooks",
              severity: "Warning",
              message: "Ledger-book workflow evidence is missing for the selected book.",
              suggestedAction: "Retain selected-book workflow evidence before production rollout.",
              evidenceReferences: []
            }
          ],
          routes: ["/accounting/configure", "/accounting/journal-entries"]
        },
        {
          code: "enterprise-accounting-configuration-studio",
          label: "Enterprise accounting configuration studio",
          status: "ReviewRequired",
          highestSeverity: "Warning",
          summary: "Operator setup controls still need enterprise admin-studio coverage.",
          requiredAction: "Complete retained chart, rules, approval, tenant, and dimension setup controls.",
          areas: ["RulesStudio", "TenantAdministration"],
          blockingIssueCodes: ["tenant-admin.operator-surface-required"],
          routes: ["/accounting/configure", "/settings"]
        },
        {
          code: "external-gl-guarded-integration",
          label: "External GL guarded integration",
          status: "ReviewRequired",
          highestSeverity: "Info",
          summary: "External GL remains import-first with guarded export artifacts.",
          requiredAction: "Retain mapping, reconciliation, and export-package evidence while live posting remains disabled.",
          areas: ["ExternalGl"],
          blockingIssueCodes: ["external-gl.live-posting-disabled"],
          routes: ["/accounting/external-gl"]
        },
        {
          code: "dimensional-ledger-reporting",
          label: "Dimensional ledger and reporting",
          status: "ReviewRequired",
          highestSeverity: "Warning",
          summary: "Dimensional ledger/query/report/export controls need full certification.",
          requiredAction: "Certify ledger-line dimensions, trial-balance filters, report provenance, and export mappings.",
          areas: ["DimensionalAccounting", "ExternalGl", "CloseReporting"],
          blockingIssueCodes: ["dimensions.external-gl-missing"],
          routes: ["/accounting/ledger", "/reporting"]
        },
        {
          code: "production-controls-hardening",
          label: "Production controls and rollout hardening",
          status: "ReviewRequired",
          highestSeverity: "Warning",
          summary: "Migration, performance, disaster recovery, and bulk safeguard controls need completion.",
          requiredAction: "Retain certified migration runs, performance proof, disaster-recovery runbooks, and bulk import/export safeguards.",
          areas: ["MigrationRollout", "TenantAdministration", "CloseReporting"],
          blockingIssueCodes: ["migration.close-reporting-evidence-not-certified"],
          routes: ["/accounting/configure", "/settings", "/accounting/close"]
        }
      ],
      migrationRolloutPlan: [
        {
          kind: "LedgerBookScope",
          code: "ledger-book-scope",
          label: "Ledger-book migration scope",
          certified: true,
          status: "Ready",
          scopeLabel: "tenant tenant-alpha | company company-alpha | fund fund-alpha | book book-primary",
          requiredAction: "Ledger-book scope migration is retained.",
          latestRunId: "migration-run-ledger-book-scope-book-primary",
          latestRunStatus: "Certified",
          migratedRecordCount: 24,
          issueCount: 0,
          evidenceReferences: ["evidence://migration/ledger-book-scope/book-primary"],
          blockingIssueCodes: []
        },
        {
          kind: "HistoricalJournalBackfill",
          code: "historical-journal-backfill",
          label: "Historical journal backfill",
          certified: false,
          status: "Blocked",
          scopeLabel: "tenant tenant-alpha | company company-alpha | fund fund-alpha | book book-primary",
          requiredAction: "Run and retain historical journal backfill evidence before certifying ledger-book-native accounting.",
          latestRunId: null,
          latestRunStatus: null,
          migratedRecordCount: 0,
          issueCount: 0,
          evidenceReferences: [],
          blockingIssueCodes: ["migration.historical-journal-backfill-not-certified"]
        }
      ],
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
    let retainedTenantAdministrationProfile: AccountingTenantAdministrationProfile = {
      tenantId: "tenant-alpha",
      companyId: "company-alpha",
      tenantScopeConfigured: true,
      adminRoleProfileConfigured: true,
      scopedAccessPoliciesConfigured: true,
      reportingGroupsConfigured: true,
      accountingAdminSurfaceConfigured: false,
      browserAccountingAdminSurfaceConfigured: false,
      wpfAccountingAdminSurfaceConfigured: false,
      chartAdministrationStudioConfigured: false,
      ruleTestPromotionStudioConfigured: false,
      closeSetupStudioConfigured: false,
      providerMappingStudioConfigured: false,
      tenantCompanyReportGroupSetupStudioConfigured: false,
      auditReviewToolingConfigured: false,
      bulkImportExportSafeguardsConfigured: false,
      performanceValidationConfigured: false,
      disasterRecoveryRunbookConfigured: false,
      ledgerBookAdministrationStudioConfigured: false,
      postingRuleAuthoringStudioConfigured: false,
      approvalQueueStudioConfigured: false,
      dimensionMappingStudioConfigured: false,
      implementationSandboxConfigured: false,
      updatedAtUtc: "2026-06-30T11:55:00Z",
      updatedBy: "controller",
      evidenceReferences: ["evidence://tenant-admin/setup"],
      correlationId: "tenant-admin-existing"
    };
    let retainedProductionCertificationProfile = {
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      tenantId: "tenant-alpha",
      companyId: "company-alpha",
      postingRulesLedgerBookNativeCertified: true,
      journalLifecycleLedgerBookNativeCertified: true,
      closeReportingLedgerBookNativeCertified: false,
      closePlanConfigurationLedgerBookNativeCertified: false,
      externalGlLedgerBookNativeCertified: false,
      periodReportDimensionQueriesCertified: true,
      crossPeriodReportDimensionQueriesCertified: false,
      journalQueryDimensionFiltersCertified: true,
      externalExportDimensionMappingCertified: false,
      ledgerLineDimensionsPersistedCertified: false,
      trialBalanceDimensionFiltersCertified: false,
      reportPackageDimensionProvenanceCertified: false,
      updatedAtUtc: "2026-06-30T11:50:00Z",
      updatedBy: "controller",
      evidenceReferences: ["evidence://ledger-book/book-primary/workflow-certification"],
      correlationId: "production-certification-existing"
    };
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
      listMigrationWorkerPlans: vi.fn().mockResolvedValue({
        fundProfileId: "fund-alpha",
        ledgerBookId: "book-primary",
        kind: null,
        plans: [{
          planId: "worker-plan-historical-book-primary",
          kind: "HistoricalJournalBackfill",
          fundProfileId: "fund-alpha",
          ledgerBookId: "book-primary",
          sourceRecordCount: 275,
          migratedRecordCount: 275,
          evidenceReferences: ["evidence://migration-worker-plan/historical/book-primary"],
          tenantId: "tenant-alpha",
          companyId: "company-alpha",
          summary: "Historical journal worker plan retained for primary book."
        }]
      }),
      listExternalGlMappingProfiles: vi.fn().mockResolvedValue([]),
      upsertExternalGlMappingProfile: vi.fn(async (request) => request.profile),
      getProductionCertificationProfile: vi.fn(async () => retainedProductionCertificationProfile),
      upsertProductionCertificationProfile: vi.fn(async (request) => {
        retainedProductionCertificationProfile = request.profile;
        return retainedProductionCertificationProfile;
      }),
      getTenantAdministrationProfile: vi.fn(async () => retainedTenantAdministrationProfile),
      upsertTenantAdministrationProfile: vi.fn(async (request) => {
        retainedTenantAdministrationProfile = request.profile;
        return retainedTenantAdministrationProfile;
      }),
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
      upsertChartNode: vi.fn(async (request) => {
        retainedWorkspace = {
          ...retainedWorkspace,
          chartOfAccounts: [
            ...retainedWorkspace.chartOfAccounts.filter((node) => node.nodeId !== request.node.nodeId),
            request.node
          ],
          auditTrail: [
            ...retainedWorkspace.auditTrail,
            {
              auditEventId: `audit-chart-upsert-${retainedWorkspace.auditTrail.length + 1}`,
              action: "chart.upsert",
              actor: request.actor,
              fundProfileId: request.fundProfileId,
              ledgerBookId: request.ledgerBookId ?? null,
              correlationId: request.correlationId ?? null,
              recordedAtUtc: "2026-06-30T12:08:00Z",
              beforeHash: "before-chart-upsert",
              afterHash: "after-chart-upsert",
              validationIssues: [],
              evidenceLinks: request.evidenceLinks ?? []
            }
          ]
        };
        return retainedWorkspace;
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

    act(() => {
      result.current.chartAccountEditor.updateDraft({
        nodeId: "coa-management-fees",
        path: "Expenses:Management Fees",
        accountName: "Management Fees",
        accountType: "Expense",
        parentPath: "Expenses",
        financialAccountId: "gl-6100-management-fees",
        evidenceText: "evidence://chart/management-fees"
      });
    });
    expect(result.current.chartAccountEditor.canSave).toBe(true);

    act(() => {
      result.current.chartAccountEditor.updateDraft({ path: "   " });
    });
    expect(result.current.chartAccountEditor.canSave).toBe(false);
    expect(result.current.chartAccountEditor.saveDisabledReason).toBe("Account path is required.");

    await act(async () => {
      await result.current.chartAccountEditor.save();
    });

    expect(services.upsertChartNode).not.toHaveBeenCalled();
    expect(result.current.chartAccountEditor.statusText).toBe("Chart account is missing required fields.");

    act(() => {
      result.current.chartAccountEditor.updateDraft({ path: "Expenses:Management Fees" });
    });
    expect(result.current.chartAccountEditor.canSave).toBe(true);

    await act(async () => {
      await result.current.chartAccountEditor.save();
    });

    expect(services.upsertChartNode).toHaveBeenCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      evidenceLinks: ["evidence://chart/management-fees"],
      node: expect.objectContaining({
        nodeId: "coa-management-fees",
        path: "Expenses:Management Fees",
        accountName: "Management Fees",
        accountType: "Expense",
        parentPath: "Expenses",
        financialAccountId: "gl-6100-management-fees",
        isArchived: false
      })
    }));
    expect(result.current.metricRows.find((row) => row.id === "chart")?.value).toBe("3");
    expect(result.current.chartAccountEditor.statusText).toBe("Saved chart account Expenses:Management Fees.");
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
      ledgerBookRolloutLabel: "1 book | 1 open period | 0 rollout blockers | 4/10 workflow controls",
      dimensionalReportingLabel: "4/9 ledger/query/report/export dimension controls | ledger book book-primary",
      dimensionalReportingEvidenceLabel: "1 retained dimensional evidence reference",
      tenantAdministrationLabel: "5/23 admin controls | tenant tenant-alpha | company company-alpha",
      tenantAdministrationEvidenceLabel: "1 retained setup evidence reference",
      migrationWorkerPlanSummaryLabel: "1/1 retained worker plan reconciled"
    });
    expect(result.current.productionReadiness.migrationWorkerPlanRows).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "worker-plan-historical-book-primary",
        kindLabel: "Historical journal backfill",
        countLabel: "275 source records -> 275 migrated records",
        evidenceLabel: "1 evidence reference",
        tone: "success"
      })
    ]));
    expect(services.listMigrationWorkerPlans).toHaveBeenCalledWith({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary"
    });
    expect(result.current.productionReadiness.productionGapRows).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "multi-ledger-native-workflows",
        label: "Configurable multi-ledger accounting",
        statusLabel: "Review required",
        severityLabel: "Warning",
        areaLabel: "Ledger Books, Posting Rules, Journal Lifecycle, Close Reporting",
        blockingIssueLabel: "workflow.evidence.missing, journal.lifecycle.missing",
        issueDetailLabel: "workflow.evidence.missing: Ledger-book workflow evidence is missing for the selected book. -> Retain selected-book workflow evidence before production rollout.",
        routeLabel: "/accounting/configure, /accounting/journal-entries",
        tone: "warning"
      }),
      expect.objectContaining({
        id: "external-gl-guarded-integration",
        label: "External GL guarded integration",
        severityLabel: "Info",
        areaLabel: "External GL",
        tone: "warning"
      }),
      expect.objectContaining({
        id: "production-controls-hardening",
        label: "Production controls and rollout hardening",
        blockingIssueLabel: "migration.close-reporting-evidence-not-certified",
        routeLabel: "/accounting/configure, /settings, /accounting/close"
      })
    ]));
    expect(result.current.productionReadiness.tenantAdministrationControls).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "tenant-scope", statusLabel: "Ready", tone: "success" }),
      expect.objectContaining({ id: "reporting-groups", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "operator-surface", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "browser-admin-studio", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "wpf-admin-studio", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "chart-administration-studio", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "rule-test-promotion-studio", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "provider-mapping-studio", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "audit-review-tooling", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "bulk-import-export-safeguards", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "performance-validation", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "disaster-recovery-runbook", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "ledger-book-administration-studio", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "posting-rule-authoring-studio", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "approval-queue-studio", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "dimension-mapping-studio", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "implementation-sandbox", statusLabel: "Missing", tone: "danger" })
    ]));
    expect(result.current.productionReadiness.migrationPlanRows).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "ledger-book-scope",
        statusLabel: "Ready",
        certificationLabel: "Certified",
        latestRunLabel: "migration-run-ledger-book-scope-book-primary | Certified",
        metricsLabel: "24 records | 0 issues",
        tone: "success"
      }),
      expect.objectContaining({
        id: "historical-journal-backfill",
        statusLabel: "Blocked",
        certificationLabel: "Not certified",
        latestRunLabel: "No retained run",
        blockingIssueLabel: "migration.historical-journal-backfill-not-certified",
        tone: "danger"
      })
    ]));
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
    expect(result.current.productionCertificationProfile.scopeLabel).toBe("Tenant tenant-alpha | company company-alpha | fund fund-alpha | ledger book book-primary");
    expect(result.current.productionCertificationProfile.controls).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "posting-rules-book", checked: true }),
      expect.objectContaining({ id: "close-reporting-book", checked: false }),
      expect.objectContaining({ id: "close-plan-configuration-book", checked: false }),
      expect.objectContaining({ id: "reconciliation-book", checked: false }),
      expect.objectContaining({ id: "direct-lending-book", checked: false }),
      expect.objectContaining({ id: "strategy-ledger-reads-book", checked: false }),
      expect.objectContaining({ id: "ledger-line-dimensions", checked: false }),
      expect.objectContaining({ id: "trial-balance-dimensions", checked: false }),
      expect.objectContaining({ id: "report-package-dimensions", checked: false }),
      expect.objectContaining({ id: "cross-period-dimensions", checked: false })
    ]));
    expect(result.current.productionCertificationProfile.canSave).toBe(true);

    act(() => {
      result.current.productionCertificationProfile.updateControl("close-reporting-book", true);
      result.current.productionCertificationProfile.updateControl("close-plan-configuration-book", true);
      result.current.productionCertificationProfile.updateControl("cross-period-dimensions", true);
      result.current.productionCertificationProfile.updateEvidence("   ");
    });

    expect(result.current.productionCertificationProfile.canSave).toBe(false);
    expect(result.current.productionCertificationProfile.saveDisabledReason).toBe("Retained evidence is required before saving production certification controls.");

    await act(async () => {
      await result.current.productionCertificationProfile.save();
    });

    expect(services.upsertProductionCertificationProfile).not.toHaveBeenCalled();
    expect(result.current.productionCertificationProfile.statusText).toBe("Retained evidence is required before saving production certification controls.");

    act(() => {
      result.current.productionCertificationProfile.updateEvidence("evidence://ledger-book/book-primary/workflow-certification\nevidence://ledger-book/book-primary/close-reporting");
    });

    await act(async () => {
      await result.current.productionCertificationProfile.save();
    });

    expect(services.upsertProductionCertificationProfile).toHaveBeenCalledWith(expect.objectContaining({
      actor: "browser-accounting-operator",
      profile: expect.objectContaining({
        fundProfileId: "fund-alpha",
        ledgerBookId: "book-primary",
        closeReportingLedgerBookNativeCertified: true,
        closePlanConfigurationLedgerBookNativeCertified: true,
        crossPeriodReportDimensionQueriesCertified: true,
        evidenceReferences: expect.arrayContaining([
          "evidence://ledger-book/book-primary/workflow-certification",
          "evidence://ledger-book/book-primary/close-reporting",
          "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/posting-candidate",
          "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/journal-lifecycle",
          "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/close-reporting",
          "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/close-plan-configuration",
          "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/dimensions/period-report/dimension-scope/canonical-production",
          "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/dimensions/cross-period/dimension-scope/canonical-production",
          "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/dimensions/journal-query/dimension-scope/canonical-production"
        ]),
        workflowCertificationArtifacts: expect.arrayContaining([
          expect.objectContaining({
            status: "Certified",
            tenantId: "tenant-alpha",
            companyId: "company-alpha",
            fundProfileId: "fund-alpha",
            ledgerBookId: "book-primary",
            sourceService: "browser-accounting-configure",
            lanes: expect.arrayContaining([
              expect.objectContaining({ kind: "PostingRules", status: "Passed" }),
              expect.objectContaining({ kind: "JournalLifecycle", status: "Passed" }),
              expect.objectContaining({ kind: "CloseReporting", status: "Passed" }),
              expect.objectContaining({ kind: "ClosePlanConfiguration", status: "Passed" })
            ])
          })
        ]),
        dimensionalCertificationArtifacts: expect.arrayContaining([
          expect.objectContaining({
            status: "Certified",
            dimensionScopeEvidenceKey: "canonical-production",
            sourceService: "browser-accounting-configure",
            lanes: expect.arrayContaining([
              expect.objectContaining({ kind: "PeriodReports", status: "Passed" }),
              expect.objectContaining({ kind: "CrossPeriodReports", status: "Passed" }),
              expect.objectContaining({ kind: "JournalFilters", status: "Passed" })
            ])
          })
        ]),
        tenantAdminCertificationArtifacts: expect.arrayContaining([
          expect.objectContaining({
            status: "Certified",
            tenantId: "tenant-alpha",
            companyId: "company-alpha",
            fundProfileId: "fund-alpha",
            ledgerBookId: "book-primary",
            sourceService: "browser-accounting-configure",
            lanes: expect.arrayContaining([
              expect.objectContaining({ kind: "TenantScope", status: "Passed" }),
              expect.objectContaining({ kind: "AdminRoleProfile", status: "Passed" }),
              expect.objectContaining({ kind: "ScopedAccessPolicies", status: "Passed" })
            ])
          })
        ])
      }),
      evidenceLinks: expect.arrayContaining([
        "evidence://ledger-book/book-primary/workflow-certification",
        "evidence://ledger-book/book-primary/close-reporting",
        "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/posting-candidate",
        "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/journal-lifecycle",
        "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/close-reporting",
        "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/close-plan-configuration",
        "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/dimensions/period-report/dimension-scope/canonical-production",
        "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/dimensions/cross-period/dimension-scope/canonical-production",
        "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/dimensions/journal-query/dimension-scope/canonical-production"
      ])
    }));
    expect(result.current.productionCertificationProfile.statusText).toBe("Production certification profile saved; readiness refreshed from retained book and dimension controls.");
    expect(result.current.tenantAdministrationProfile.scopeLabel).toBe("Tenant tenant-alpha | company company-alpha");
    expect(result.current.tenantAdministrationProfile.controls).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "operator-surface", checked: false }),
      expect.objectContaining({ id: "chart-administration-studio", checked: false }),
      expect.objectContaining({ id: "rule-test-promotion-studio", checked: false }),
      expect.objectContaining({ id: "close-setup-studio", checked: false }),
      expect.objectContaining({ id: "provider-mapping-studio", checked: false }),
      expect.objectContaining({ id: "tenant-company-report-group-studio", checked: false }),
      expect.objectContaining({ id: "audit-review-tooling", checked: false }),
      expect.objectContaining({ id: "bulk-import-export-safeguards", checked: false }),
      expect.objectContaining({ id: "performance-validation", checked: false }),
      expect.objectContaining({ id: "disaster-recovery-runbook", checked: false }),
      expect.objectContaining({ id: "ledger-book-administration-studio", checked: false }),
      expect.objectContaining({ id: "posting-rule-authoring-studio", checked: false }),
      expect.objectContaining({ id: "approval-queue-studio", checked: false }),
      expect.objectContaining({ id: "dimension-mapping-studio", checked: false }),
      expect.objectContaining({ id: "implementation-sandbox", checked: false })
    ]));
    expect(result.current.tenantAdministrationProfile.canSave).toBe(true);
    expect(result.current.tenantAdministrationProfile.canRetainSandboxProof).toBe(true);

    act(() => {
      result.current.tenantAdministrationProfile.updateEvidence("   ");
    });
    expect(result.current.tenantAdministrationProfile.canSave).toBe(false);
    expect(result.current.tenantAdministrationProfile.saveDisabledReason)
      .toBe("Retained setup evidence is required before saving tenant administration controls.");

    await act(async () => {
      await result.current.tenantAdministrationProfile.save();
    });

    expect(services.upsertTenantAdministrationProfile).not.toHaveBeenCalled();
    expect(result.current.tenantAdministrationProfile.statusText)
      .toBe("Retained setup evidence is required before saving tenant administration controls.");

    act(() => {
      result.current.tenantAdministrationProfile.updateEvidence("evidence://tenant-admin/setup");
      result.current.tenantAdministrationProfile.updateControl("approval-queue-studio", true);
      result.current.tenantAdministrationProfile.updateControl("dimension-mapping-studio", true);
      result.current.tenantAdministrationProfile.updateApprovalQueueSetup({ queueId: "" });
    });
    expect(result.current.tenantAdministrationProfile.canSave).toBe(false);
    expect(result.current.tenantAdministrationProfile.saveDisabledReason)
      .toBe("Complete approval queue id, workflow kind, approval role/count, segregation policy, and evidence requirement before saving approval queue setup.");
    expect(result.current.tenantAdministrationProfile.canRetainSandboxProof).toBe(false);
    expect(result.current.tenantAdministrationProfile.sandboxDisabledReason)
      .toBe("Complete approval queue id, workflow kind, approval role/count, segregation policy, and evidence requirement before retaining implementation sandbox proof.");

    await act(async () => {
      await result.current.tenantAdministrationProfile.save();
    });

    expect(services.upsertTenantAdministrationProfile).not.toHaveBeenCalled();
    expect(result.current.tenantAdministrationProfile.statusText)
      .toBe("Complete approval queue id, workflow kind, approval role/count, segregation policy, and evidence requirement before saving approval queue setup.");

    act(() => {
      result.current.tenantAdministrationProfile.updateApprovalQueueSetup({
        queueId: "sandbox-configuration-approval",
        displayName: "Sandbox configuration approval",
        workflowKind: "ConfigurationPromotion",
        requiredApprovalRole: "Controller",
        requiredApprovalCount: "2",
        segregationPolicy: "Preparer cannot approve own sandbox configuration proof.",
        evidenceRequirement: "sandbox-proof;configuration-approval;segregation-review"
      });
      result.current.tenantAdministrationProfile.updateDimensionMappingSetup({ mappingId: "" });
    });
    expect(result.current.tenantAdministrationProfile.canSave).toBe(false);
    expect(result.current.tenantAdministrationProfile.saveDisabledReason)
      .toBe("Complete dimension mapping id, display name, provider id, Meridian dimensions, provider dimensions, and evidence requirement before saving dimension mapping setup.");
    expect(result.current.tenantAdministrationProfile.canRetainSandboxProof).toBe(false);
    expect(result.current.tenantAdministrationProfile.sandboxDisabledReason)
      .toBe("Complete dimension mapping id, display name, provider id, Meridian dimensions, provider dimensions, and evidence requirement before retaining implementation sandbox proof.");

    await act(async () => {
      await result.current.tenantAdministrationProfile.save();
    });

    expect(services.upsertTenantAdministrationProfile).not.toHaveBeenCalled();
    expect(result.current.tenantAdministrationProfile.statusText)
      .toBe("Complete dimension mapping id, display name, provider id, Meridian dimensions, provider dimensions, and evidence requirement before saving dimension mapping setup.");

    act(() => {
      result.current.tenantAdministrationProfile.updateDimensionMappingSetup({
        mappingId: "sandbox-qbo-dimension-map",
        displayName: "Sandbox QuickBooks dimensions",
        providerId: "quickbooks-fixture",
        meridianDimensionsText: "fundId=fund-alpha\nbookId=book-primary\ncostCenterId=sandbox-accounting",
        providerDimensionsText: "Class=fund-alpha\nBook=book-primary\nDepartment=sandbox-accounting",
        evidenceRequirement: "sandbox-proof;dimension-mapping;controller-approval"
      });
    });
    expect(result.current.tenantAdministrationProfile.canSave).toBe(true);
    expect(result.current.tenantAdministrationProfile.canRetainSandboxProof).toBe(true);

    await act(async () => {
      await result.current.tenantAdministrationProfile.retainSandboxProof();
    });

    expect(services.upsertTenantAdministrationProfile).toHaveBeenCalledWith(expect.objectContaining({
      actor: "browser-accounting-operator",
      profile: expect.objectContaining({
        tenantId: "tenant-alpha",
        companyId: "company-alpha",
        approvalQueueStudioConfigured: true,
        approvalQueueConfigurations: [
          expect.objectContaining({
            queueId: "sandbox-configuration-approval",
            displayName: "Sandbox configuration approval",
            workflowKind: "ConfigurationPromotion",
            requiredApprovalRole: "Controller",
            requiredApprovalCount: 2,
            segregationPolicy: "Preparer cannot approve own sandbox configuration proof.",
            evidenceRequirement: "sandbox-proof;configuration-approval;segregation-review"
          })
        ],
        dimensionMappingStudioConfigured: true,
        dimensionMappingConfigurations: [
          expect.objectContaining({
            mappingId: "sandbox-qbo-dimension-map",
            displayName: "Sandbox QuickBooks dimensions",
            providerId: "quickbooks-fixture",
            meridianDimensions: expect.objectContaining({
              fundId: "fund-alpha",
              bookId: "book-primary",
              costCenterId: "sandbox-accounting"
            }),
            providerDimensions: expect.objectContaining({
              externalGlDimensions: expect.objectContaining({
                Class: "fund-alpha",
                Book: "book-primary",
                Department: "sandbox-accounting"
              })
            }),
            evidenceRequirement: "sandbox-proof;dimension-mapping;controller-approval"
          })
        ],
        implementationSandboxConfigured: true,
        evidenceReferences: expect.arrayContaining([
          "evidence://tenant-admin/tenant-alpha/company-alpha/implementation-sandbox/ledgerBookId=book-primary",
          "evidence://tenant-admin/tenant-alpha/company-alpha/sandbox-validation/ledgerBookId=book-primary",
          "evidence://tenant-admin/tenant-alpha/company-alpha/fixture-validation/ledgerBookId=book-primary",
          "evidence://tenant-admin/tenant-alpha/company-alpha/implementation-fixture/ledgerBookId=book-primary"
        ])
      }),
      evidenceLinks: expect.arrayContaining([
        "evidence://tenant-admin/tenant-alpha/company-alpha/implementation-sandbox/ledgerBookId=book-primary",
        "evidence://tenant-admin/tenant-alpha/company-alpha/sandbox-validation/ledgerBookId=book-primary",
        "evidence://tenant-admin/tenant-alpha/company-alpha/fixture-validation/ledgerBookId=book-primary",
        "evidence://tenant-admin/tenant-alpha/company-alpha/implementation-fixture/ledgerBookId=book-primary"
      ])
    }));
    expect(result.current.tenantAdministrationProfile.sandboxStatusText)
      .toBe("Implementation sandbox proof retained; readiness refreshed from validation and ledger-book evidence.");

    act(() => {
      result.current.tenantAdministrationProfile.updateControl("operator-surface", true);
      result.current.tenantAdministrationProfile.updateControl("chart-administration-studio", true);
      result.current.tenantAdministrationProfile.updateControl("rule-test-promotion-studio", true);
      result.current.tenantAdministrationProfile.updateControl("close-setup-studio", true);
      result.current.tenantAdministrationProfile.updateControl("provider-mapping-studio", true);
      result.current.tenantAdministrationProfile.updateControl("tenant-company-report-group-studio", true);
      result.current.tenantAdministrationProfile.updateControl("audit-review-tooling", true);
      result.current.tenantAdministrationProfile.updateControl("bulk-import-export-safeguards", true);
      result.current.tenantAdministrationProfile.updateControl("performance-validation", true);
      result.current.tenantAdministrationProfile.updateControl("disaster-recovery-runbook", true);
      result.current.tenantAdministrationProfile.updateControl("ledger-book-administration-studio", true);
      result.current.tenantAdministrationProfile.updateControl("posting-rule-authoring-studio", true);
      result.current.tenantAdministrationProfile.updateControl("approval-queue-studio", true);
      result.current.tenantAdministrationProfile.updateControl("dimension-mapping-studio", true);
      result.current.tenantAdministrationProfile.updateControl("implementation-sandbox", true);
      result.current.tenantAdministrationProfile.updateApprovalQueueSetup({
        queueId: "configuration-promotion-queue",
        displayName: "Configuration promotion queue",
        workflowKind: "ConfigurationPromotion",
        requiredApprovalRole: "Controller",
        requiredApprovalCount: "2",
        segregationPolicy: "Preparer cannot approve own configuration change.",
        evidenceRequirement: "approval-queue;configuration-approval;segregation-review"
      });
      result.current.tenantAdministrationProfile.updateDimensionMappingSetup({
        mappingId: "qbo-fund-alpha-dimension-map",
        displayName: "QuickBooks fund alpha dimensions",
        providerId: "quickbooks-fixture",
        meridianDimensionsText: "fundId=fund-alpha\nbookId=book-primary\ncostCenterId=fund-accounting",
        providerDimensionsText: "Class=fund-alpha\nBook=book-primary\nDepartment=fund-accounting",
        evidenceRequirement: "dimension-mapping;provider-segment-review;controller-approval"
      });
      result.current.tenantAdministrationProfile.updateEvidence("evidence://tenant-admin/setup\nevidence://tenant-admin/operator-surface\nevidence://tenant-admin/chart-administration\nevidence://tenant-admin/rules-studio\nevidence://tenant-admin/close-setup\nevidence://tenant-admin/provider-mapping\nevidence://tenant-admin/tenant-company-report-group\nevidence://tenant-admin/audit-review\nevidence://tenant-admin/bulk-import-export\nevidence://tenant-admin/performance-validation\nevidence://tenant-admin/disaster-recovery\nevidence://tenant-admin/ledger-book-administration\nevidence://tenant-admin/posting-rule-authoring\nevidence://tenant-admin/approval-queue\nevidence://tenant-admin/dimension-mapping\nevidence://tenant-admin/implementation-sandbox");
    });

    await act(async () => {
      await result.current.tenantAdministrationProfile.save();
    });

    expect(services.upsertTenantAdministrationProfile).toHaveBeenCalledWith(expect.objectContaining({
      actor: "browser-accounting-operator",
      profile: expect.objectContaining({
        tenantId: "tenant-alpha",
        companyId: "company-alpha",
        accountingAdminSurfaceConfigured: true,
        browserAccountingAdminSurfaceConfigured: true,
        wpfAccountingAdminSurfaceConfigured: false,
        chartAdministrationStudioConfigured: true,
        ruleTestPromotionStudioConfigured: true,
        closeSetupStudioConfigured: true,
        providerMappingStudioConfigured: true,
        tenantCompanyReportGroupSetupStudioConfigured: true,
        auditReviewToolingConfigured: true,
        bulkImportExportSafeguardsConfigured: true,
        performanceValidationConfigured: true,
        disasterRecoveryRunbookConfigured: true,
        ledgerBookAdministrationStudioConfigured: true,
        postingRuleAuthoringStudioConfigured: true,
        approvalQueueStudioConfigured: true,
        approvalQueueConfigurations: [
          expect.objectContaining({
            queueId: "configuration-promotion-queue",
            displayName: "Configuration promotion queue",
            workflowKind: "ConfigurationPromotion",
            requiredApprovalRole: "Controller",
            requiredApprovalCount: 2,
            segregationPolicy: "Preparer cannot approve own configuration change.",
            evidenceRequirement: "approval-queue;configuration-approval;segregation-review"
          })
        ],
        dimensionMappingStudioConfigured: true,
        dimensionMappingConfigurations: [
          expect.objectContaining({
            mappingId: "qbo-fund-alpha-dimension-map",
            displayName: "QuickBooks fund alpha dimensions",
            providerId: "quickbooks-fixture",
            meridianDimensions: expect.objectContaining({
              fundId: "fund-alpha",
              bookId: "book-primary",
              costCenterId: "fund-accounting"
            }),
            providerDimensions: expect.objectContaining({
              externalGlDimensions: expect.objectContaining({
                Class: "fund-alpha",
                Book: "book-primary",
                Department: "fund-accounting"
              })
            }),
            evidenceRequirement: "dimension-mapping;provider-segment-review;controller-approval"
          })
        ],
        implementationSandboxConfigured: true,
        evidenceReferences: expect.arrayContaining([
          "evidence://tenant-admin/setup",
          "evidence://tenant-admin/operator-surface",
          "evidence://tenant-admin/chart-administration",
          "evidence://tenant-admin/rules-studio",
          "evidence://tenant-admin/close-setup",
          "evidence://tenant-admin/provider-mapping",
          "evidence://tenant-admin/tenant-company-report-group",
          "evidence://tenant-admin/audit-review",
          "evidence://tenant-admin/bulk-import-export",
          "evidence://tenant-admin/performance-validation",
          "evidence://tenant-admin/disaster-recovery",
          "evidence://tenant-admin/ledger-book-administration",
          "evidence://tenant-admin/posting-rule-authoring",
          "evidence://tenant-admin/approval-queue",
          "evidence://tenant-admin/dimension-mapping",
          "evidence://tenant-admin/implementation-sandbox",
          "evidence://tenant-admin/tenant-alpha/company-alpha/ledger-book-administration/ledgerBookId=book-primary",
          "evidence://tenant-admin/tenant-alpha/company-alpha/implementation-sandbox/ledgerBookId=book-primary",
          "evidence://tenant-admin/tenant-alpha/company-alpha/sandbox-validation/ledgerBookId=book-primary",
          "evidence://tenant-admin/tenant-alpha/company-alpha/fixture-validation/ledgerBookId=book-primary",
          "evidence://tenant-admin/tenant-alpha/company-alpha/implementation-fixture/ledgerBookId=book-primary"
        ])
      }),
      evidenceLinks: expect.arrayContaining([
        "evidence://tenant-admin/setup",
        "evidence://tenant-admin/operator-surface",
        "evidence://tenant-admin/chart-administration",
        "evidence://tenant-admin/rules-studio",
        "evidence://tenant-admin/close-setup",
        "evidence://tenant-admin/provider-mapping",
        "evidence://tenant-admin/tenant-company-report-group",
        "evidence://tenant-admin/audit-review",
        "evidence://tenant-admin/bulk-import-export",
        "evidence://tenant-admin/performance-validation",
        "evidence://tenant-admin/disaster-recovery",
        "evidence://tenant-admin/ledger-book-administration",
        "evidence://tenant-admin/posting-rule-authoring",
        "evidence://tenant-admin/approval-queue",
        "evidence://tenant-admin/dimension-mapping",
        "evidence://tenant-admin/implementation-sandbox",
        "evidence://tenant-admin/tenant-alpha/company-alpha/ledger-book-administration/ledgerBookId=book-primary",
        "evidence://tenant-admin/tenant-alpha/company-alpha/implementation-sandbox/ledgerBookId=book-primary",
        "evidence://tenant-admin/tenant-alpha/company-alpha/sandbox-validation/ledgerBookId=book-primary",
        "evidence://tenant-admin/tenant-alpha/company-alpha/fixture-validation/ledgerBookId=book-primary",
        "evidence://tenant-admin/tenant-alpha/company-alpha/implementation-fixture/ledgerBookId=book-primary"
      ])
    }));
    expect(result.current.tenantAdministrationProfile.statusText).toBe("Tenant administration setup profile saved; production readiness refreshed from retained controls.");
    expect(result.current.externalGlMappingProfile.scopeLabel).toBe("Fund fund-alpha | ledger book book-primary");
    expect(result.current.externalGlMappingProfile.canSave).toBe(true);

    act(() => {
      result.current.externalGlMappingProfile.updateProviderId("quickbooks-fixture");
      result.current.externalGlMappingProfile.updateProfileId("qbo-fund-alpha-book-primary");
      result.current.externalGlMappingProfile.updateDisplayName("Fund Alpha QuickBooks mapping");
      result.current.externalGlMappingProfile.updateMeridianDimensions("fundId=fund-alpha\nbookId=book-primary\ncustomerId=investor-alpha\nProject=direct-lending");
      result.current.externalGlMappingProfile.updateExternalDimensions("bookId=Book:book-primary\ncustomerId=qbo-customer-alpha\nProject=qbo-project-credit");
      result.current.externalGlMappingProfile.updateEvidence("approval:external-gl-mapping:qbo-fund-alpha-book-primary");
      result.current.externalGlMappingProfile.updateCertified(true);
      result.current.externalGlMappingProfile.updateAccountMappings("   ");
    });
    expect(result.current.externalGlMappingProfile.canSave).toBe(false);
    expect(result.current.externalGlMappingProfile.saveDisabledReason).toBe("At least one account mapping is required.");

    await act(async () => {
      await result.current.externalGlMappingProfile.save();
    });

    expect(services.upsertExternalGlMappingProfile).not.toHaveBeenCalled();
    expect(result.current.externalGlMappingProfile.statusText)
      .toBe("Provider, profile id, display name, account mappings, and retained evidence are required before saving an external GL mapping profile.");

    act(() => {
      result.current.externalGlMappingProfile.updateAccountMappings("Assets:Cash:Operating=qbo-1000\nIncome:Investment Income=qbo-4000");
    });
    expect(result.current.externalGlMappingProfile.canSave).toBe(true);

    await act(async () => {
      await result.current.externalGlMappingProfile.save();
    });

    expect(services.upsertExternalGlMappingProfile).toHaveBeenCalledWith(expect.objectContaining({
      actor: "browser-accounting-operator",
      providerId: "quickbooks-fixture",
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      tenantId: "tenant-alpha",
      companyId: "company-alpha",
      actionOrigin: "HumanOperator",
      profile: expect.objectContaining({
        profileId: "qbo-fund-alpha-book-primary",
        providerId: "quickbooks-fixture",
        displayName: "Fund Alpha QuickBooks mapping",
        certificationState: "Certified",
        accountMappings: expect.objectContaining({
          "Assets:Cash:Operating": "qbo-1000",
          "Income:Investment Income": "qbo-4000"
        }),
        dimensionMappings: [
          expect.objectContaining({
            certificationState: "Certified",
            meridianDimensions: expect.objectContaining({
              fundId: "fund-alpha",
              bookId: "book-primary",
              customerId: "investor-alpha",
              externalGlDimensions: expect.objectContaining({
                Project: "direct-lending"
              })
            }),
            externalDimensions: expect.objectContaining({
              bookId: "Book:book-primary",
              customerId: "qbo-customer-alpha",
              externalGlDimensions: expect.objectContaining({
                Project: "qbo-project-credit"
              })
            })
          })
        ]
      }),
      evidenceLinks: expect.arrayContaining([
        "approval:external-gl-mapping:qbo-fund-alpha-book-primary",
        "evidence://external-gl/mapping-certification/provider/quickbooks-fixture/fund/fund-alpha/profile/qbo-fund-alpha-book-primary",
        "evidence://ledger-book/book-primary/external-gl/mapping-certification/qbo-fund-alpha-book-primary"
      ])
    }));
    expect(result.current.externalGlMappingProfile.statusText).toBe("External GL mapping profile qbo-fund-alpha-book-primary saved as Certified; readiness refreshed from retained provider mapping.");
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
      listMigrationWorkerPlans: vi.fn().mockResolvedValue({ fundProfileId: "fund-alpha", ledgerBookId: "book-primary", kind: null, plans: [] }),
      listExternalGlMappingProfiles: vi.fn().mockResolvedValue([]),
      upsertExternalGlMappingProfile: vi.fn(),
      getProductionCertificationProfile: vi.fn(),
      upsertProductionCertificationProfile: vi.fn(),
      getTenantAdministrationProfile: vi.fn(),
      upsertTenantAdministrationProfile: vi.fn(),
      createLedgerBook: vi.fn(),
      previewTemplate: vi.fn(),
      upsertChartNode: vi.fn().mockResolvedValue(workspace),
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

    await act(async () => {
      await result.current.activate();
    });

    expect(services.activate).not.toHaveBeenCalled();
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
      approveRulePromotion: vi.fn(),
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

    await act(async () => {
      await approved.result.current.approveRulePromotion();
    });

    expect(approvedServices.approveRulePromotion).not.toHaveBeenCalled();
    expect(approved.result.current.approveRulePromotionStatusText).toBe("Selected posting rule already has an approved promotion.");
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

    const noRuleServices: AccountingConfigurationServices = {
      ...services,
      getConfiguration: vi.fn().mockResolvedValue({
        ...workspace,
        postingRules: [],
        rulesStudio: {
          summary: {
            activeRules: 0,
            templateMappingRules: 0,
            generatedPostingRules: 0,
            rulesRequiringPromotionApproval: 0,
            rulesWithApprovedPromotion: 0,
            pendingPromotionApprovalRules: 0,
            savedTestCaseCount: 0,
            rulesWithSavedRegressionTests: 0,
            rulesMissingCurrentVersionRegressionTests: 0
          },
          rules: [],
          promotionQueue: []
        }
      }),
      upsertRule: vi.fn(),
      approveRulePromotion: vi.fn()
    };

    const noRules = renderHook(() => useAccountingConfigurationViewModel(noRuleServices));
    await waitFor(() => expect(noRules.result.current.loading).toBe(false));
    expect(noRules.result.current.rules).toHaveLength(0);

    await act(async () => {
      await noRules.result.current.duplicateSelectedRule();
      await noRules.result.current.archiveSelectedRule();
      await noRules.result.current.approveRulePromotion();
    });

    expect(noRuleServices.upsertRule).not.toHaveBeenCalled();
    expect(noRuleServices.approveRulePromotion).not.toHaveBeenCalled();
    expect(noRules.result.current.duplicateRuleStatusText).toBe("Select an active posting rule before drafting a copy.");
    expect(noRules.result.current.archiveRuleStatusText).toBe("Select an active posting rule before archiving.");
    expect(noRules.result.current.approveRulePromotionStatusText).toBe("Select an active posting rule before approving promotion.");
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
      listMigrationWorkerPlans: vi.fn().mockResolvedValue({ fundProfileId: "fund-alpha", ledgerBookId: "book-missing", kind: null, plans: [] }),
      listExternalGlMappingProfiles: vi.fn().mockResolvedValue([]),
      upsertExternalGlMappingProfile: vi.fn(),
      getProductionCertificationProfile: vi.fn(),
      upsertProductionCertificationProfile: vi.fn(),
      getTenantAdministrationProfile: vi.fn(),
      upsertTenantAdministrationProfile: vi.fn(),
      createLedgerBook,
      previewTemplate: vi.fn(),
      upsertChartNode: vi.fn().mockResolvedValue(workspace),
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

  it("derives canonical Accounting and Reporting loading states", () => {
    expect(buildAccountingLoadingViewState("/accounting/reconciliation")).toMatchObject({
      role: "status",
      ariaBusy: true,
      ariaLive: "polite",
      titleId: "accounting-workspace-loading-title",
      detailId: "accounting-workspace-loading-detail",
      title: "Loading Accounting",
      detail: "Waiting for ledger, reconciliation, cash-flow, and Security Master summaries from workspace data.",
      routeLabel: "/accounting/reconciliation",
      workstreamLabel: "Reconciliation Casework",
      statusItemsLabel: "Accounting workspace data loading"
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
      detail: "Waiting for report-pack, governed export, and approval summaries from workspace data.",
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
      description: "Reporting packet context at /reporting reuses the shared accounting/reporting cash-flow summary data.",
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

  it("derives pending cash-flow state when workspace data is unavailable", () => {
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
      rowId: "Primary-Cash-Asset-acct-cash-Fund: fund-alpha / Entity: entity-alpha / Sleeve: sleeve-credit +3",
      accountLabel: "Cash",
      accountTypeLabel: "Asset",
      basisLabel: "Primary basis",
      policyLabel: "legacy-v1/legacy-v1",
      dimensionLabel: "Fund: fund-alpha / Entity: entity-alpha / Sleeve: sleeve-credit +3",
      dimensionDetailLabel: "Fund: fund-alpha | Entity: entity-alpha | Sleeve: sleeve-credit | Cost center: ops-close | External class: private-fund | External department: finance",
      balanceLabel: "$120,500",
      balanceTone: "success",
      entryCountLabel: "12",
      ariaLabel: "Cash Asset. Primary basis. Policy legacy-v1/legacy-v1. Dimensions Fund: fund-alpha / Entity: entity-alpha / Sleeve: sleeve-credit +3. Balance $120,500. 12 entries",
      selectAriaLabel: "Inspect trial-balance account Cash for Asset",
      detailPanelId: "trial-balance-account-detail",
      isExpanded: true
    });
    expect(state.selectedRowId).toBe("Primary-Cash-Asset-acct-cash-Fund: fund-alpha / Entity: entity-alpha / Sleeve: sleeve-credit +3");
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
      { label: "Dimensions", value: "Fund: fund-alpha | Entity: entity-alpha | Sleeve: sleeve-credit | Cost center: ops-close | External class: private-fund | External department: finance" },
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
      { label: "Dimensions", value: "No fund, entity, sleeve, strategy, investor, capital-account, instrument, tax-lot, cost-center, counterparty, or external GL dimensions are attached." },
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

  it("filters ledger account inquiry rows by retained dimensional scope", () => {
    const state = buildAccountingTrialBalanceViewState({
      runId: "run-42",
      rows: trialBalanceLines,
      accountFilter: "private-fund",
      loading: false,
      error: null
    });

    expect(state.filteredRowCountLabel).toBe("1 of 2 GL account rows");
    expect(state.rows).toHaveLength(1);
    expect(state.rows[0]).toMatchObject({
      accountLabel: "Cash",
      dimensionLabel: "Fund: fund-alpha / Entity: entity-alpha / Sleeve: sleeve-credit +3"
    });
    expect(state.selectedDetail?.fields).toEqual(expect.arrayContaining([
      { label: "Dimensions", value: "Fund: fund-alpha | Entity: entity-alpha | Sleeve: sleeve-credit | Cost center: ops-close | External class: private-fund | External department: finance" }
    ]));
  });

  it("derives ledger journal evidence rows with retained dimensional scope", () => {
    const state = buildAccountingLedgerJournalEvidenceViewState({
      runId: "run-42",
      rows: journalLines
    });

    expect(state).toMatchObject({
      title: "Journal evidence dimensions",
      filteredRowCountLabel: "2 GL account rows",
      hasRows: true
    });
    expect(state.rows[0]).toMatchObject({
      rowId: "journal-cash-1",
      timestampLabel: "Jun 30, 14:30 UTC",
      amountLabel: "$120,500 debit / $120,500 credit",
      lineCountLabel: "2 lines",
      dimensionLabel: "Fund: fund-alpha / Entity: entity-alpha / Sleeve: sleeve-credit +2",
      dimensionDetailLabel: "Fund: fund-alpha | Entity: entity-alpha | Sleeve: sleeve-credit | Cost center: ops-close | External class: private-fund",
      ariaLabel: "Journal journal-cash-1. Cash close journal. $120,500 debit / $120,500 credit. 2 lines. Dimensions Fund: fund-alpha / Entity: entity-alpha / Sleeve: sleeve-credit +2"
    });
    expect(state.rows[1]).toMatchObject({
      rowId: "journal-unscoped",
      dimensionLabel: "No dimensions",
      dimensionDetailLabel: "No fund, entity, sleeve, strategy, investor, capital-account, instrument, tax-lot, cost-center, counterparty, or external GL dimensions are attached."
    });

    const filtered = buildAccountingLedgerJournalEvidenceViewState({
      runId: "run-42",
      rows: journalLines,
      dimensionFilter: "private-fund"
    });

    expect(filtered.filteredRowCountLabel).toBe("1 of 2 GL account rows");
    expect(filtered.rows).toHaveLength(1);
    expect(filtered.rows[0].journalEntryId).toBe("journal-cash-1");
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
      "Meridian service returned 422. Open diagnostics for technical details.",
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
      { label: "Usage", value: "Used by accounting and trading workflows." },
      {
        label: "Reference-data workbench",
        value: "Ready: Multi-asset reference-data workbench is ready for downstream FINOPS use.",
        tone: "success"
      },
      {
        label: "Operating model",
        value: "Ready: Security Master operating model has applicable entitlement, source, control, and approval evidence for the selected scope.",
        tone: "success"
      },
      {
        label: "Reconcile",
        value: "Ready: 1 most-specific entitlement record applies to the selected Security Master scope. Evidence 1; blockers 0.",
        tone: "success"
      },
      { label: "Entitlement applicability", value: "1 most-specific applicable entitlement(s).", tone: "success" },
      {
        label: "Manual-change approval",
        value: "Ready: operations-continuity.security-master-override via SecurityMaster; 1 manual change event reuses the operations approval policy.",
        tone: "success"
      },
      {
        label: "Provider evidence",
        value: "Ready: 1 active provider evidence row retained on the passport. Evidence 2; blockers 0.",
        tone: "success"
      },
      { label: "Operations handoff", value: "1 enabled / 1 total handoff(s).", tone: "success" },
      {
        label: "Operations workbench",
        value: "Ready: Security Master operations workbench is ready for downstream portfolio, accounting, reconciliation, close, and reporting use.",
        tone: "success"
      }
    ]));
    expect(view.operationsReadiness).toEqual(expect.arrayContaining([
      expect.objectContaining({
        readinessId: "ledger",
        label: "Ledger-ready",
        statusBadgeVariant: "success",
        evidenceLabel: "2 evidence",
        blockerLabel: "0 blockers",
        route: "/accounting/ledger"
      })
    ]));
    expect(view.operationsPanels).toEqual(expect.arrayContaining([
      expect.objectContaining({
        panelId: "identity",
        statusBadgeVariant: "success",
        items: expect.arrayContaining([
          expect.objectContaining({
            itemId: "primary-identifier",
            evidenceLabel: "1 evidence",
            blockerLabel: "0 blockers"
          })
        ])
      }),
      expect.objectContaining({
        panelId: "provider-evidence",
        statusBadgeVariant: "success",
        items: expect.arrayContaining([
          expect.objectContaining({
            itemId: "source-record-1",
            route: "/accounting/security-master#source-1",
            evidenceLabel: "1 evidence",
            blockerLabel: "0 blockers"
          })
        ])
      })
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
        accessLabel: "Read-only",
        displaySummary: "1 fields returned.",
        statusLabel: "Ready",
        statusBadgeVariant: "success",
        countLabel: "1 record",
        latencyLabel: "9 ms"
      }),
      expect.objectContaining({
        rowId: "reference-data-option-chain-import",
        accessLabel: "Write-capable",
        displaySummary: "option chain import source catalogued; not invoked by this read-only workbench.",
        statusLabel: "Deferred",
        statusBadgeVariant: "outline"
      })
    ]));
    expect(view.selectedDetail).toMatchObject({
      title: "Bond reference",
      subtitle: "Reference data source: Bonds",
      description: "1 fields returned.",
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
      errorDetails: ["Meridian service returned 503. Open diagnostics for technical details."]
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
      "Meridian service returned 503. Open diagnostics for technical details.",
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
      "Meridian service returned 503. Open diagnostics for technical details."
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
        label: "Preview report",
        href: "/api/export/preview",
        ariaLabel: "Open Preview report service reference"
      },
      {
        id: "formats",
        label: "List export formats",
        href: "/api/export/formats",
        ariaLabel: "Open List export formats service reference"
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
