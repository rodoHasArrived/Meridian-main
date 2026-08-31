import { act, renderHook, waitFor } from "@testing-library/react";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it, vi } from "vitest";
import { ApiError as MeridianApiError, describeApiError } from "@/lib/api-errors";
import { hasDevelopmentFixtureUsage, resetDevelopmentFixtureUsage } from "@/lib/api";
import type { ReferenceDataEndpointProbeResult, ReferenceDataEndpointProbeStatus } from "@/lib/api";
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
  useAccountingReconciliationViewModel,
  useSecurityMasterViewModel
} from "@/screens/accounting-screen.view-model";
import {
  buildAccountingTaskMode,
  resolveAccountingWorkstream
} from "@/screens/accounting-screen.task-mode-view-model";
import type {
  AccountingReconciliationServices,
  SecurityCashFlowScheduleEvent,
  SecurityMasterDrillInServices,
  SecurityMasterServices
} from "@/screens/accounting-screen.view-model";
import type {
  CorporateAction,
  AccountingCashFlowSummary,
  AccountingWorkspaceResponse,
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
    rightsPerShare: null,
    recordDate: null,
    lifecycleState: null,
    supersedesCorpActId: null,
    redemptionPricePercentOfPar: null,
    payload: null,
    payloadSchemaVersion: 1
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
    rightsPerShare: null,
    recordDate: null,
    lifecycleState: null,
    supersedesCorpActId: null,
    redemptionPricePercentOfPar: null,
    payload: null,
    payloadSchemaVersion: 1
  }
];

const referenceDataCoverage = {
  requestedAtUtc: "2026-05-10T12:00:00Z",
  endpoints: []
};

function createReferenceDataEndpoint(
  index: number,
  status: ReferenceDataEndpointProbeStatus
): ReferenceDataEndpointProbeResult {
  return {
    id: `reference-route-${index}`,
    family: "Equities",
    label: `Reference route ${index}`,
    method: status === "Deferred" ? "POST" : "GET",
    path: `/api/reference-data/routes/${index}`,
    requestLabel: `Reference route ${index}`,
    probe: status !== "Deferred",
    mutation: status === "Deferred",
    status,
    statusCode: status === "Ready" ? 200 : null,
    durationMs: status === "Ready" ? 12 : null,
    responseCount: status === "Ready" ? 1 : null,
    responseSummary: `Reference route ${index} returned ${status}.`,
    responsePreview: null,
    errorSummary: status === "Missing" ? "Reference data is missing." : null,
    errorDetails: []
  };
}
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
  classificationProfile: {
    instrumentType: "Equity",
    displayName: "Equity",
    securityMasterAssetClass: "Equity",
    assetFamily: "CommonEquity",
    subType: "CommonShare",
    defaultProviderSecurityType: "STK",
    isTradeable: true,
    isReferenceOnly: false,
    isDerivative: false,
    requiresUnderlying: false,
    producesCashFlows: true,
    requiresLotTracking: true,
    settlementModel: "Cash security settlement with dividend and corporate-action events.",
    compatibleSecurityMasterAssetClasses: ["Equity", "MoneyMarketFund"],
    preferredIdentifierKinds: ["Ticker", "ISIN", "CUSIP", "FIGI"],
    requiredEconomicTerms: ["issuer", "share class", "currency", "primary exchange"],
    providerCapabilities: ["Quote", "Trade", "Position", "CorporateAction"],
    lifecycleEvents: ["Purchase", "Sale", "Dividend", "Split", "Merger", "Delisting"],
    validationRules: ["primary identifier present", "listing venue known"],
    ledgerBehaviorHints: ["security position", "dividend income", "realized and unrealized P&L", "tax-lot tracking"],
    riskModelHints: ["equity exposure", "issuer concentration"],
    summary: "Equity maps to Security Master Equity for provider routing and operations checks. Provider route STK; 5 required term(s); 6 lifecycle event(s)."
  },
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

describe("accounting-screen view model", () => {
  it("keeps Accounting task-mode routing outside the overloaded view model", () => {
    // NB: read relative to the dashboard project root (process.cwd()). The Vitest runner executes
    // from src/Meridian.Ui/dashboard, and import.meta.url is not a file:// URL under Vitest, so
    // `new URL(..., import.meta.url)` throws ERR_INVALID_URL_SCHEME.
    const viewModelSource = readFileSync(resolve(process.cwd(), "src/screens/accounting-screen.view-model.ts"), "utf8");
    const taskModeSource = readFileSync(resolve(process.cwd(), "src/screens/accounting-screen.task-mode-view-model.ts"), "utf8");

    expect(taskModeSource).toContain("const accountingTaskModeDefinitions");
    expect(taskModeSource).toContain("export function resolveAccountingWorkstream");
    expect(taskModeSource).toContain("export function buildAccountingTaskMode");
    expect(taskModeSource).toContain("export function accountingWorkstreamHref");
    expect(viewModelSource).not.toContain("const accountingTaskModeDefinitions");
    expect(viewModelSource).not.toContain("function normalizeAccountingTaskModePath");
    expect(viewModelSource).not.toContain("function buildAccountingTaskModeViewModel");
    expect(taskModeSource).not.toContain("accountingTaskModeLauncherLinks");
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
      id: "configure",
      label: "Configure",
      href: "/accounting/configure",
      workstream: "configure"
    });

    expect(resolveSelectedReconciliation(reconciliationQueue, "run-57")?.runId).toBe("run-57");
    expect(resolveSelectedReconciliation(reconciliationQueue, null)?.runId).toBe("run-42");
    expect(resolveSelectedReconciliation([], null)).toBeNull();
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

  it("retains ordered statement runs when a manual refresh fails", async () => {
    const retainedRuns = [
      {
        runId: "older-run",
        importId: "older-import",
        startedAtUtc: "2026-05-01T00:00:00Z",
        completedAtUtc: "2026-05-01T00:03:00Z",
        importedAtUtc: "2026-05-01T00:04:00Z",
        positionMatches: 1,
        cashMatches: 0,
        transactionMatches: 0,
        openExceptionCount: 0
      },
      {
        runId: "newer-run",
        importId: "newer-import",
        startedAtUtc: "2026-05-02T00:00:00Z",
        completedAtUtc: "2026-05-02T00:03:00Z",
        importedAtUtc: "2026-05-02T00:04:00Z",
        positionMatches: 1,
        cashMatches: 0,
        transactionMatches: 0,
        openExceptionCount: 0
      }
    ];
    const getStatementRuns = vi.fn()
      .mockResolvedValueOnce(retainedRuns)
      .mockRejectedValueOnce(new Error("statement store unavailable"));
    const services: AccountingReconciliationServices = {
      getBreakQueue: vi.fn().mockResolvedValue([]),
      reviewBreak: vi.fn(),
      resolveBreak: vi.fn(),
      getTrialBalance: vi.fn().mockResolvedValue([]),
      getCalibrationSummary: vi.fn().mockResolvedValue(null),
      getStatementRuns,
      getStatementRun: vi.fn(),
      previewTransactionLab: vi.fn()
    };
    const bootstrapData = {
      metrics: [],
      reconciliationQueue: [],
      breakQueue: [],
      cashFlow: null,
      reporting: null
    } as unknown as AccountingWorkspaceResponse;
    const { result } = renderHook(() => useAccountingReconciliationViewModel(
      bootstrapData,
      "reconciliation",
      services
    ));

    await waitFor(() => expect(result.current.statementRunsView.rows).toHaveLength(2));
    expect(result.current.statementRunsView.rows.map((row) => row.runId)).toEqual(["newer-run", "older-run"]);

    await act(async () => {
      await result.current.refreshStatementRuns();
    });

    expect(result.current.statementRunsView.rows.map((row) => row.runId)).toEqual(["newer-run", "older-run"]);
    expect(result.current.statementRunsView.errorText).toContain("statement store unavailable");
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
        expectedState: "ProjectedForReconciliation",
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
    expect(result.current.transactionLabView.requestSummaryLabel).toBe("Projection ready");
    expect(result.current.transactionLabView.statusText).toContain("Expected accounting projection");
    expect(result.current.transactionLabView.statusText).toContain("no journal has been posted");
    expect(result.current.transactionLabView.journalLineCountLabel).toBe("2 lines");
    expect(result.current.transactionLabView.ledgerImpactLabel).toBe("$0");
    expect(result.current.transactionLabView.reconciliationLabel).toBe("ProjectedForReconciliation");
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

  it("gives two rows differing only in a late dimension distinct identities", () => {
    // The summary shows the first three dimensions and a "+N" count, so these two rows read
    // identically. Keying on it produced duplicate React keys and made selecting the second row
    // resolve the first row's detail and evidence.
    const scoped = (customerId: string) => ({
      accountName: "Cash",
      accountType: "Asset",
      symbol: null,
      financialAccountId: "acct-cash",
      balance: 100,
      entryCount: 1,
      security: null,
      accountingBasis: "Primary" as const,
      dimensions: {
        organizationId: "org-1",
        fundId: "fund-alpha",
        entityId: "entity-alpha",
        customerId
      }
    });

    const state = buildAccountingTrialBalanceViewState({
      runId: "run-42",
      rows: [scoped("cust-1"), scoped("cust-2")],
      loading: false,
      error: null
    });

    expect(state.rows).toHaveLength(2);
    expect(state.rows[0].dimensionLabel).toBe(state.rows[1].dimensionLabel);
    expect(state.rows[0].rowId).not.toBe(state.rows[1].rowId);
  });

  it("computes the balance control from the whole basis, not the filtered rows", () => {
    // The control answers "does this book tie", which is a property of the book. Summing only the
    // rows an account search left visible declared the book out of balance by the value of
    // everything filtered out, and told the operator to resolve a variance that does not exist.
    const unfiltered = buildAccountingTrialBalanceViewState({
      runId: "run-42",
      rows: trialBalanceLines,
      loading: false,
      error: null
    });

    const filtered = buildAccountingTrialBalanceViewState({
      runId: "run-42",
      rows: trialBalanceLines,
      accountFilter: "Cash",
      loading: false,
      error: null
    });

    expect(filtered.rows.length).toBeLessThan(unfiltered.rows.length);
    expect(filtered.basisVariance).toBe(unfiltered.basisVariance);
    expect(filtered.isBasisOutOfBalance).toBe(unfiltered.isBasisOutOfBalance);
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
      description: "Primary basis ledger balances for the selected ledger run grouped by account type. Values are basis per configured policy until accountant review.",
      tableLabel: "Primary trial balance lines for the selected ledger run",
      selectedBasis: "Primary",
      accountFilterLabel: "Filter by General Ledger account",
      accountFilterValue: "",
      filteredRowCountLabel: "2 GL account rows",
      state: "ready",
      hasRows: true,
      statusAnnouncement: "2 trial balance lines loaded for the selected ledger run."
    });
    expect(state.basisOptions).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "Primary", rowCount: 2, rowCountLabel: "2 rows", isSelected: true }),
      expect.objectContaining({ id: "Gaap", rowCount: 0, rowCountLabel: "0 rows", isSelected: false })
    ]));
    // Identity is the full dimension set, not the truncated summary: two rows differing only in a
    // dimension past the third shared the summary string, so React saw duplicate keys and
    // selecting the second resolved the first row's detail.
    expect(state.rows[0]).toMatchObject({
      rowId: "Primary-Cash-Asset-acct-cash-Fund: fund-alpha | Entity: entity-alpha | Sleeve: sleeve-credit | Cost center: ops-close | External class: private-fund | External department: finance",
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
    expect(state.selectedRowId).toBe("Primary-Cash-Asset-acct-cash-Fund: fund-alpha | Entity: entity-alpha | Sleeve: sleeve-credit | Cost center: ops-close | External class: private-fund | External department: finance");
    expect(state.selectedDetail).toMatchObject({
      eyebrow: "Trial-balance detail",
      title: "Cash",
      subtitle: "Asset · Primary basis",
      statusLabel: "Debit / asset",
      statusVariant: "success",
      ariaLabel: "Trial-balance detail for Cash",
      ledgerLinesTitle: "Ledger lines for selected account",
      supportingDocumentsTitle: "Supporting documentation"
    });
    expect(state.selectedDetail?.description).not.toContain("run-42");
    expect(state.selectedDetail?.fields).toContainEqual({ label: "Run", value: "the selected ledger run" });
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
        evidenceLabel: "Source evidence",
        evidenceHref: "/accounting/audit?sourceEventId=evt-cash-1",
        approvalHref: "/accounting/approvals?approvalId=approval-cash-1"
      })
    ]);
    expect(state.selectedDetail?.supportingDocuments).toEqual(expect.arrayContaining([
      expect.objectContaining({ label: "Run review packet", href: "/api/workstation/runs/run-42/review-packet" }),
      expect.objectContaining({ label: "Source event evidence", href: "/accounting/audit?sourceEventId=evt-cash-1" }),
      // The journal-entry detail screen, not the ledger explorer: the explorer never read a
      // journalEntryId, so this link used to drop the entry it named.
      expect.objectContaining({
        label: "Journal entry evidence",
        href: "/accounting/journal-entries/detail?journalEntryId=je-cash-1&runId=run-42"
      }),
      expect.objectContaining({ label: "Approval evidence", href: "/accounting/approvals?approvalId=approval-cash-1" })
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
      { label: "Dimensions", value: "No ledger dimensions are attached to this row." },
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
      description: "Retained journal rows for the selected ledger run with canonical dimensional scope preserved for ledger evidence review.",
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
      dimensionDetailLabel: "No ledger dimensions are attached to this row."
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
    expect(legacyState.selectedDetail?.auditDrillThroughLabel).toBe("Open source evidence");
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
    expect(arrayState.selectedDetail?.auditDrillThroughLabel).toBe("Open source evidence");
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
      loadingText: "Loading trial balance for the selected ledger run.",
      statusAnnouncement: "Loading trial balance for the selected ledger run."
    });

    expect(buildAccountingTrialBalanceViewState({
      runId: "run-42",
      rows: [],
      loading: false,
      error: null
    })).toMatchObject({
      state: "empty",
      emptyTitle: "No trial balance lines",
      statusAnnouncement: "No trial balance lines returned for the selected ledger run."
    });

    expect(buildAccountingTrialBalanceViewState({
      runId: "run-42",
      rows: trialBalanceLines,
      loading: false,
      error: "Ledger unavailable."
    })).toMatchObject({
      state: "error",
      errorText: "Ledger unavailable.",
      statusAnnouncement: "Trial balance failed for the selected ledger run: Ledger unavailable."
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

  it("retains canonical lifecycle, amendment, redemption, and typed-payload fields in corporate-action detail", () => {
    const amended = {
      ...corporateActions[0],
      recordDate: "2026-05-02T00:00:00Z",
      lifecycleState: "Confirmed",
      supersedesCorpActId: "ca-div-original",
      redemptionPricePercentOfPar: 101.25,
      payload: { treatmentHint: "source-assertion", optionCode: "CASH" },
      payloadSchemaVersion: 2
    };

    const state = buildCorporateActionsViewState("sec-1", [amended], amended.corpActId, false, null);

    expect(state.selectedDetail?.statusLabel).toBe("Confirmed");
    expect(state.selectedDetail?.fields).toEqual(expect.arrayContaining([
      { label: "Record date", value: "May 2, 2026", tone: "default" },
      { label: "Lifecycle", value: "Confirmed", tone: "default" },
      { label: "Supersedes", value: "ca-div-original" },
      { label: "Redemption price (% par)", value: "101.25" },
      { label: "Payload schema", value: "2", tone: "default" },
      { label: "Typed payload", value: "{\"treatmentHint\":\"source-assertion\",\"optionCode\":\"CASH\"}", tone: "default" }
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
      paymentDateLabel: "Nov 15, 2026",
      expectedAmountLabel: "126,250 USD",
      actualAmountLabel: "124,900 USD",
      varianceLabel: "-1,350 USD",
      factorLabel: "1.000000 -> 0.900000",
      postingStatusLabel: "Variance review",
      postingStatusTone: "danger",
      selectAriaLabel: "Inspect schedule event Paydown for sec-1 on Nov 15, 2026",
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

  it("resolves DEV-only schedule fixtures and marks demo-data usage for the banner", () => {
    resetDevelopmentFixtureUsage();

    expect(resolveSecurityScheduleEvents("unknown-security")).toEqual([]);
    expect(hasDevelopmentFixtureUsage()).toBe(false);

    expect(resolveSecurityScheduleEvents("sec-dev-004")).toHaveLength(3);
    expect(hasDevelopmentFixtureUsage()).toBe(true);

    resetDevelopmentFixtureUsage();
  });

  it("returns an empty schedule in production instead of fabricated fixture rows", () => {
    vi.stubEnv("DEV", false);
    resetDevelopmentFixtureUsage();
    try {
      expect(resolveSecurityScheduleEvents("sec-dev-004")).toEqual([]);
      expect(hasDevelopmentFixtureUsage()).toBe(false);
    } finally {
      vi.unstubAllEnvs();
      resetDevelopmentFixtureUsage();
    }
  });

  it("keeps cash-flow schedule empty states and fixture resolution deterministic", () => {
    expect(resolveSecurityScheduleEvents("sec-dev-004")).toHaveLength(3);
    expect(resolveSecurityScheduleEvents("unknown-security")).toEqual([]);
    resetDevelopmentFixtureUsage();

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
      tradeDateLabel: "Apr 20, 2026",
      settleDateLabel: "Apr 22, 2026",
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
      { label: "Instrument type", value: "Equity (Equity)", tone: "success" },
      { label: "Provider routing", value: "STK: Quote, Trade, Position, CorporateAction" },
      { label: "Lifecycle profile", value: "Purchase, Sale, Dividend, Split, +2 more" },
      { label: "Ledger behavior", value: "security position, dividend income, realized and unrealized P&L, tax-lot tracking" },
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

  it("keeps a partial instrument passport response reviewable instead of crashing", () => {
    const view = buildInstrumentPassportViewState({
      securityId: "sec-dev-001",
      passport: {
        securityId: "sec-dev-001",
        identity: {
          displayName: "Apple Inc.",
          assetClass: "Equity"
        }
      } as InstrumentPassport
    });

    expect(view).toMatchObject({
      title: "Instrument passport",
      statusLabel: "Unknown",
      statusBadgeVariant: "outline"
    });
    expect(view.fields).toEqual(expect.arrayContaining([
      { label: "Display name", value: "Apple Inc." },
      { label: "Trust", value: "Trust posture is unavailable.", tone: "default" },
      { label: "Identifiers", value: "Identifier summary is unavailable." },
      { label: "Usage", value: "Downstream usage is unavailable." }
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
      { label: "Instrument type", value: "Equity (Equity)", tone: "success" },
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
      detailSubtitle: "Equity · Active",
      detailStatusLabel: "Active",
      detailStatusBadgeVariant: "success"
    });
    expect(state.metrics).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "selected", value: "Apple Inc.", detail: "Equity · Active.", tone: "success" }),
      expect.objectContaining({ id: "conflicts", value: "1", tone: "warning" }),
      expect.objectContaining({ id: "reference", value: "Pending", tone: "default" }),
      expect.objectContaining({ id: "passport", value: "Controls set", tone: "success" })
    ]));
    expect(state.detailSections).toEqual(expect.arrayContaining([
      { id: "overview", label: "Overview", value: "1 identifier", active: true },
      { id: "schedules", label: "Schedules", value: "2 cash-flow events" },
      { id: "lots", label: "Open lots", value: "1 lot" },
      { id: "controls", label: "Controls", value: "Trading set" },
      { id: "audit", label: "Audit", value: "1 conflict" }
    ]));
  });

  it("derives a review posture from a partial passport without dereferencing missing trust evidence", () => {
    const state = buildSecurityMasterPageViewState({
      query: "",
      results: null,
      selectedSecurityId: "sec-dev-001",
      selectedDisplayName: "Apple Inc.",
      selectedAssetClass: "Equity",
      selectedStatus: "Active",
      identity: null,
      identityLoading: false,
      conflicts: [],
      conflictsLoading: false,
      corporateActions: [],
      instrumentPassport: {
        securityId: "sec-dev-001",
        identity: { displayName: "Apple Inc.", assetClass: "Equity" }
      } as InstrumentPassport,
      securitySchedules: [],
      openLotReadModel: null,
      tradingParameters: null
    });

    expect(state.metrics).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "passport",
        value: "Passport evidence incomplete",
        tone: "warning"
      })
    ]));
  });

  it("does not close Security Master coverage when a trusted passport returns no operations checks", () => {
    const state = buildSecurityMasterPageViewState({
      query: "AAPL",
      results: [securityResult],
      selectedSecurityId: "sec-1",
      selectedDisplayName: "Apple Inc.",
      selectedAssetClass: "Equity",
      selectedStatus: "Active",
      identity: securityIdentity,
      identityLoading: false,
      conflicts: [],
      conflictsLoading: false,
      corporateActions: [],
      instrumentPassport: {
        ...instrumentPassport,
        operationsWorkbench: {
          ...instrumentPassport.operationsWorkbench!,
          readiness: []
        }
      },
      referenceDataCoverage: {
        requestedAtUtc: "2026-05-10T12:00:00Z",
        endpoints: [createReferenceDataEndpoint(0, "Ready")]
      },
      securitySchedules: [],
      openLotReadModel: null,
      tradingParameters
    });

    expect(state.coveragePosture).toEqual({
      label: "Review required",
      detail: "passport evidence is incomplete.",
      tone: "warning"
    });
  });

  it("fails Security Master coverage closed and reconciles every reference route bucket", () => {
    const missingRoutes = Array.from({ length: 41 }, (_, index) => createReferenceDataEndpoint(index, "Missing"));
    const deferredRoutes = Array.from({ length: 2 }, (_, index) => createReferenceDataEndpoint(41 + index, "Deferred"));
    const coverage = {
      requestedAtUtc: "2026-05-10T12:00:00Z",
      endpoints: [...missingRoutes, ...deferredRoutes]
    };
    const partialPassport = {
      securityId: "sec-dev-001",
      identity: { displayName: "Apple Inc.", assetClass: "Equity" }
    } as InstrumentPassport;

    const state = buildSecurityMasterPageViewState({
      query: "AAPL",
      results: [securityResult],
      selectedSecurityId: "sec-dev-001",
      selectedDisplayName: "Apple Inc.",
      selectedAssetClass: "Equity",
      selectedStatus: "Active",
      identity: securityIdentity,
      identityLoading: false,
      conflicts,
      conflictsLoading: false,
      corporateActions: [],
      instrumentPassport: partialPassport,
      referenceDataCoverage: coverage,
      securitySchedules: [],
      openLotReadModel: null,
      tradingParameters
    });

    expect(state.coveragePosture).toEqual({
      label: "Review required",
      detail: "1 open conflict; 41 routes need review; 2 routes are deferred or blocked; passport evidence is incomplete.",
      tone: "warning"
    });
    expect(state.metrics).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "reference",
        value: "43 routes",
        detail: "0 ready · 41 need review · 2 deferred or blocked · 43 total.",
        tone: "warning"
      })
    ]));

    const workbench = buildReferenceDataWorkbenchViewState({
      securityId: "sec-dev-001",
      coverage
    });
    const routeValue = Number(workbench.metrics.find((metric) => metric.id === "routes")?.value);
    const readyValue = Number(workbench.metrics.find((metric) => metric.id === "ready")?.value);
    const reviewValue = Number(workbench.metrics.find((metric) => metric.id === "review")?.value);
    const deferredOrBlockedValue = Number(workbench.metrics.find((metric) => metric.id === "deferred")?.value);

    expect({ routeValue, readyValue, reviewValue, deferredOrBlockedValue }).toEqual({
      routeValue: 43,
      readyValue: 0,
      reviewValue: 41,
      deferredOrBlockedValue: 2
    });
    expect(readyValue + reviewValue + deferredOrBlockedValue).toBe(routeValue);
    expect(workbench.metrics.find((metric) => metric.id === "deferred")).toMatchObject({
      label: "Deferred / blocked",
      detail: "2 write-capable sources intentionally deferred."
    });
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
      subtitle: "Equity · Active",
      description: "1 identifier · 1 alias · effective Jan 1, 2024 – active",
      ariaLabel: "Security identity detail for Apple Inc.",
      statusLabel: "Active",
      statusBadgeVariant: "success",
      identifiersTableLabel: "Identifiers for Apple Inc.",
      aliasesTableLabel: "Aliases for Apple Inc."
    });
    expect(state?.summaryFields).toEqual(expect.arrayContaining([
      { label: "Security ID", value: "sec-1" },
      { label: "Effective", value: "Jan 1, 2024 – active" }
    ]));
    expect(state?.identifiers[0]).toMatchObject({
      rowId: "identifier-ticker-aapl",
      providerLabel: "Bloomberg",
      primaryLabel: "Primary",
      primaryBadgeVariant: "success",
      validRangeLabel: "Jan 1, 2024 – active",
      ariaLabel: "Ticker AAPL, Primary, provider Bloomberg, valid Jan 1, 2024 – active"
    });
    expect(state?.aliases[0]).toMatchObject({
      rowId: "alias-alias-1",
      providerLabel: "—",
      enabledLabel: "Enabled",
      enabledBadgeVariant: "success",
      validRangeLabel: "Jan 1, 2025 – active",
      createdLabel: "Jan 1, 2025",
      reasonText: "Market data source mapping",
      ariaLabel: "ProviderSymbol AAPL.OQ, Enabled, scope Collector, provider —, valid Jan 1, 2025 – active"
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
      detectedLabel: "Detected Jan 1, 2026",
      fieldLabel: "CUSIP identifier",
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
