import { fixtureTradingRisk } from "./dev-fixtures.trading-risk";
import type {
  CoveredCallChainPreview,
  CoveredCallRunResult,
  CoveredCallRunSummary
} from "@/lib/covered-call";
import type {
  BrokerageConnectionStatus,
  BrokerageHouseholdPortfolio,
  ClosePeriodPlan,
  ExternalGlExportPackage,
  ExternalGlMappingProfile,
  AccountingProductionReadiness,
  AccountingSystemImportDetail,
  AccountingSystemProvider,
  AccountingSystemProviderMappingRequirement,
  AccountingSystemReconciliationSummary,
  CorporateAction,
  DataWorkspaceResponse,
  EvidenceCompleteness,
  EvidencePacket,
  EvidencePacketExportResponse,
  EvidenceSubject,
  EvidenceVaultDocumentEntry,
  EvidenceVaultIdentity,
  EvidenceVaultRequestListEntry,
  ExecutionAuditEntry,
  ExecutionControlSnapshot,
  FeatureCapabilitySettingsResponse,
  FinancialRecordExplorerDto,
  AccountingReportPackageBundle,
  AccountingConfigurationWorkspace,
  AccountingWorkspaceResponse,
  OperatorInbox,
  OperatorWorkflowHomeSummary,
  OperationsApprovalPolicyMatrix,
  OperationsCloseCalendar,
  OperationsContinuityWorkflow,
  OperationsContinuityWorkflowSummary,
  PrivateCapitalCloseCockpit,
  LedgerTrialBalanceLine,
  LedgerMappingWorkbench,
  OperatorOverridesDto,
  PaperSessionDetail,
  PaperSessionReplayVerification,
  PaperSessionSummary,
  PortfolioWorkspaceResponse,
  ProviderConnectionRow,
  ProviderIntegrationConnectionMonitor,
  ProviderIntegrationPromotionReadinessPreview,
  ProviderIntegrationQuarantineReview,
  ProviderIntegrationReconciliationHandoffHistory,
  ProviderIntegrationStagingIdentityResolutionPreview,
  ProviderIntegrationStagingReview,
  ProviderIntegrationSyncPlan,
  ProviderIntegrationSyncRunEvidence,
  ProviderIntegrationSyncRunHistory,
  ProviderReadinessSummary,
  ProviderRoutingBinding,
  ProviderRoutingConnection,
  ProviderRoutingTrustSnapshot,
  PromotionEvaluationResult,
  PromotionRecord,
  QuantParametersResponse,
  QuantTemplatesResponse,
  ReconciliationCalibrationSummary,
  RiskRuleConfig,
  RiskRuleStatus,
  RuleDryRunResult,
  StatementConnectorDescriptor,
  StatementMappingProfile,
  StatementRunSummary,
  StrategyBriefingResponse,
  StrategyWorkspaceResponse,
  ReplayFileRecord,
  SecurityAssetProfileDefinition,
  SecurityIdentityDrillIn,
  SecurityMasterConflict,
  SecurityMasterEntry,
  SessionInfo,
  RolePermissionCatalog,
  StrategyDesignDocument,
  StrategyDesignDraftSummary,
  StrategyDesignFieldCatalogItem,
  StrategyDesignTemplate,
  SystemOverviewResponse,
  TradingOperatorReadiness,
  TradingParameters,
  TradingWorkspaceResponse,
  UserAccessAssignment,
  WorkflowAction,
  WorkflowLibrary,
  WorkflowPresetLibrary
} from "../types";
import {
  ACCOUNTING_SYSTEM_API_ENDPOINTS,
  AUTH_API_ENDPOINTS,
  COVERED_CALL_API_ENDPOINTS,
  EXECUTION_API_ENDPOINTS,
  FUND_STRUCTURE_API_ENDPOINTS,
  PORTFOLIO_API_ENDPOINTS,
  PROVIDER_API_ENDPOINTS,
  PROVIDER_ROUTING_API_ENDPOINTS,
  PROMOTION_API_ENDPOINTS,
  QUANT_API_ENDPOINTS,
  RECONCILIATION_API_ENDPOINTS,
  REPLAY_API_ENDPOINTS,
  RISK_API_ENDPOINTS,
  SECURITY_MASTER_API_ENDPOINTS,
  STATEMENT_CONNECTOR_API_ENDPOINTS,
  STRATEGY_DESIGNER_API_ENDPOINTS,
  WORKSTATION_API_ENDPOINTS,
  brokerageConnectionStatusEndpoint,
  riskRuleConfigEndpoint,
  workstationFinancialRecordExplorerEndpoint
} from "./workstation-endpoints";
import {
  apiRoutePattern,
  cloneFixture,
  readDecodedPathSegment,
  readFixtureSearchParams,
  resolveFixtureFromMaps,
  type DynamicFixturePattern
} from "./dev-fixtures/fixture-resolver";
import { marketDataFixturePatterns, marketDataFixtureRoutes } from "./dev-fixtures/market-data-fixtures";

const fixtureSession: SessionInfo = {
  displayName: "Ops Desk",
  role: "Operator",
  environment: "paper",
  activeWorkspace: "trading",
  commandCount: 7
};

const fixtureSystemOverview: SystemOverviewResponse = {
  systemStatus: "Degraded",
  providersOnline: 3,
  providersTotal: 4,
  activeRuns: 2,
  openPositions: 5,
  activeBackfills: 1,
  symbolsMonitored: 128,
  storageHealth: "Warning",
  lastHeartbeatUtc: "2026-04-28T18:15:00Z",
  metrics: [
    { id: "providers", label: "Providers Online", value: "3 / 4", delta: "1 degraded", tone: "warning" },
    { id: "runs", label: "Active Runs", value: "2", delta: "+1", tone: "default" },
    { id: "positions", label: "Open Positions", value: "5", delta: "+2", tone: "default" },
    { id: "backfills", label: "Active Backfills", value: "1", delta: "review", tone: "warning" }
  ],
  recentEvents: []
};

const fixtureStrategyWorkspace: StrategyWorkspaceResponse = {
  metrics: [
    { id: "runs", label: "Runs", value: "24", delta: "+8%", tone: "success" },
    { id: "queued", label: "Queued", value: "3", delta: "0%", tone: "default" },
    { id: "review", label: "Needs Review", value: "2", delta: "-1%", tone: "warning" },
    { id: "promotions", label: "Promotions", value: "5", delta: "+2", tone: "default" }
  ],
  runs: [
    {
      id: "run-dev-1",
      strategyName: "Mean Reversion FX",
      engine: "Meridian Native",
      mode: "paper",
      status: "Running",
      dataset: "FX Majors",
      window: "90d",
      pnl: "+4.2%",
      sharpe: "1.41",
      lastUpdated: "2m ago",
      notes: "Primary paper candidate for development preview."
    },
    {
      id: "run-dev-2",
      strategyName: "Index Momentum",
      engine: "Lean",
      mode: "backtest",
      status: "Completed",
      dataset: "US Equities",
      window: "180d",
      pnl: "+1.9%",
      sharpe: "0.91",
      lastUpdated: "5m ago",
      notes: "Completed backtest run available for compare and diff review."
    }
  ]
};

const fixtureStrategyBriefing: StrategyBriefingResponse = {
  workspace: {
    totalRuns: 2,
    activeRuns: 1,
    promotionCandidates: 1,
    positivePnlRuns: 2,
    latestRunId: "run-dev-1",
    latestStrategyName: "Mean Reversion FX",
    hasLedgerCoverage: true,
    hasPortfolioCoverage: true,
    summary: "Strategy briefing is populated from development fixtures."
  },
  insightFeed: {
    feedId: "strategy-market-briefing",
    title: "Pinned Insights",
    summary: "Development Strategy briefing with pinned run context.",
    generatedAt: "2026-04-28T18:15:00Z",
    widgets: [
      {
        widgetId: "insight-run-dev-1",
        title: "Mean Reversion FX",
        subtitle: "Paper · Running",
        headline: "+4.2%",
        tone: "success",
        summary: "Primary paper candidate for development preview.",
        runId: "run-dev-1",
        drillInRoute: "/api/workstation/runs/run-dev-1/equity-curve"
      }
    ]
  },
  watchlists: [],
  recentRuns: [
    {
      runId: "run-dev-1",
      strategyName: "Mean Reversion FX",
      mode: 1,
      status: 1,
      dataset: "FX Majors",
      windowLabel: "90d",
      returnLabel: "+4.2%",
      sharpeLabel: "1.41",
      lastUpdatedLabel: "2m ago",
      notes: "Primary paper candidate for development preview.",
      promotionState: 2,
      netPnl: 4200,
      totalReturn: 0.042,
      finalEquity: 104200,
      drillIn: {
        equityCurve: "/api/workstation/runs/run-dev-1/equity-curve",
        fills: "/api/workstation/runs/run-dev-1/fills",
        attribution: "/api/workstation/runs/run-dev-1/attribution",
        ledger: "/api/workstation/runs/run-dev-1/ledger",
        cashFlows: "/api/portfolio/run-dev-1/cash-flows",
        continuity: "/api/workstation/runs/run-dev-1/continuity"
      }
    }
  ],
  savedComparisons: [],
  alerts: [],
  whatChanged: []
};

const fixtureTradingReadiness: TradingOperatorReadiness = {
  asOf: "2026-04-28T18:15:00Z",
  overallStatus: "ReviewRequired",
  readyForPaperOperation: false,
  activeSession: {
    sessionId: "paper-dev-42",
    strategyId: "strat-mean-reversion",
    strategyName: "Mean Reversion FX",
    isActive: true,
    initialCash: 100000,
    createdAt: "2026-04-28T17:30:00Z",
    closedAt: null,
    symbolCount: 4,
    orderCount: 3,
    positionCount: 2,
    portfolioValue: 101240
  },
  sessions: [],
  replay: {
    sessionId: "paper-dev-42",
    replaySource: "fixtures/paper-dev-42.jsonl",
    isConsistent: true,
    comparedFillCount: 9,
    comparedOrderCount: 3,
    comparedLedgerEntryCount: 11,
    verifiedAt: "2026-04-28T18:12:00Z",
    lastPersistedFillAt: "2026-04-28T18:10:00Z",
    lastPersistedOrderUpdateAt: "2026-04-28T18:10:30Z",
    verificationAuditId: "audit-replay-dev-42",
    mismatchReasons: []
  },
  controls: {
    circuitBreakerOpen: false,
    circuitBreakerReason: null,
    circuitBreakerChangedBy: null,
    circuitBreakerChangedAt: null,
    manualOverrideCount: 1,
    symbolLimitCount: 3,
    defaultMaxPositionSize: 50000
  },
  promotion: {
    state: "ReviewRequired",
    reason: "Promotion checklist still needs portfolio and ledger continuity review.",
    requiresReview: true,
    sourceRunId: "run-dev-1",
    targetRunId: null,
    suggestedNextMode: "paper",
    auditReference: "audit-promo-dev-1",
    approvalStatus: "pending",
    manualOverrideId: null,
    approvedBy: null,
    approvalChecklist: ["DK1 trust packet", "Replay consistency", "Portfolio continuity", "Ledger continuity"]
  },
  trustGate: {
    gateId: "dk1-provider-trust",
    status: "signed",
    readyForOperatorReview: true,
    operatorSignoffRequired: true,
    operatorSignoffStatus: "signed",
    generatedAt: "2026-04-27T21:00:00Z",
    packetPath: "artifacts/provider-validation/_automation/2026-04-27/dk1-pilot-parity-packet.json",
    sourceSummary: "wave1-validation-summary.json",
    requiredSampleCount: 4,
    readySampleCount: 4,
    validatedEvidenceDocumentCount: 4,
    requiredOwners: ["Data", "Provider Reliability", "Trading"],
    blockers: [],
    detail: "Signed DK1 parity packet is available for readiness projection.",
    operatorSignoff: {
      status: "signed",
      requiredBeforeDk1Exit: true,
      requiredOwners: ["Data", "Provider Reliability", "Trading"],
      signedOwners: ["Data", "Provider Reliability", "Trading"],
      missingOwners: [],
      completedAt: "2026-04-27T22:10:00Z",
      sourcePath: "artifacts/provider-validation/_automation/2026-04-27/dk1-operator-signoff.json"
    }
  },
  brokerageSync: {
    fundAccountId: "53bf0251-17f6-4fb7-8dbe-6fb4966e2749",
    providerId: "alpaca",
    externalAccountId: "PA-DEMO",
    health: "Stale",
    isLinked: true,
    isStale: true,
    lastAttemptedSyncAt: "2026-04-28T18:00:00Z",
    lastSuccessfulSyncAt: "2026-04-28T16:00:00Z",
    lastError: null,
    positionCount: 2,
    openOrderCount: 1,
    fillCount: 9,
    cashTransactionCount: 3,
    securityMissingCount: 0,
    warnings: ["Brokerage sync is older than the active paper session."]
  },
  acceptanceGates: [
    {
      gateId: "paper-session",
      label: "Paper session",
      status: "Ready",
      detail: "Active paper session is present.",
      sessionId: "paper-dev-42",
      runId: "run-dev-1",
      auditReference: "audit-replay-dev-42"
    },
    {
      gateId: "brokerage-sync",
      label: "Brokerage sync",
      status: "ReviewRequired",
      detail: "Refresh brokerage sync before treating paper operation as ready.",
      sessionId: "paper-dev-42",
      runId: "run-dev-1",
      auditReference: null
    },
    {
      gateId: "promotion-checklist",
      label: "Promotion checklist",
      status: "ReviewRequired",
      detail: "Portfolio and ledger continuity checklist items are not complete.",
      sessionId: "paper-dev-42",
      runId: "run-dev-1",
      auditReference: "audit-promo-dev-1"
    }
  ],
  workItems: [
    {
      workItemId: "promotion-review-run-dev-1",
      kind: "PromotionReview",
      label: "Promotion checklist incomplete",
      detail: "Portfolio and ledger continuity review must be finished before paper-operation readiness is accepted.",
      tone: "Warning",
      createdAt: "2026-04-28T18:15:00Z",
      runId: "run-dev-1",
      fundAccountId: "53bf0251-17f6-4fb7-8dbe-6fb4966e2749",
      auditReference: "audit-promo-dev-1",
      workspace: "Trading",
      targetRoute: "/trading/readiness",
      targetPageTag: "TradingReadinessConsole"
    },
    {
      workItemId: "brokerage-sync-stale-53bf0251",
      kind: "BrokerageSync",
      label: "Brokerage sync stale",
      detail: "Refresh brokerage account sync so position and cash evidence matches the active paper session.",
      tone: "Warning",
      createdAt: "2026-04-28T18:15:00Z",
      runId: null,
      fundAccountId: "53bf0251-17f6-4fb7-8dbe-6fb4966e2749",
      auditReference: null,
      workspace: "Trading",
      targetRoute: "/trading/readiness",
      targetPageTag: "TradingReadinessConsole"
    }
  ],
  warnings: ["Brokerage sync is older than the active paper session."]
};

const fixturePaperSessionSummaries: PaperSessionSummary[] = [
  {
    sessionId: "paper-dev-42",
    strategyId: "strat-mean-reversion",
    strategyName: "Mean Reversion FX",
    initialCash: 100000,
    createdAt: "2026-04-28T17:30:00Z",
    closedAt: null,
    isActive: true
  }
];

const fixturePaperSessionPortfolio = {
  cash: 98210.5,
  portfolioValue: 101240,
  unrealisedPnl: 1240,
  realisedPnl: 320.75,
  positions: [
    {
      symbol: "AAPL",
      quantity: 100,
      averageCostBasis: 176.6,
      currentPrice: 188.4,
      marketValue: 18840,
      unrealisedPnl: 1180,
      realisedPnl: 0
    },
    {
      symbol: "MSFT",
      quantity: 16,
      averageCostBasis: 418,
      currentPrice: 421.7,
      marketValue: 6747.2,
      unrealisedPnl: 59.2,
      realisedPnl: 320.75
    }
  ],
  asOf: "2026-04-28T18:14:30Z"
};

const fixturePaperSessionDetail: PaperSessionDetail = {
  summary: fixturePaperSessionSummaries[0]!,
  symbols: ["AAPL", "MSFT", "NVDA"],
  portfolio: fixturePaperSessionPortfolio,
  orderHistory: [
    {
      orderId: "PO-0",
      symbol: "NVDA",
      side: "Sell",
      type: "Market",
      quantity: 10,
      filledQuantity: 10,
      averageFillPrice: 948.2,
      status: "Filled",
      createdAt: "2026-04-28T18:03:00Z",
      updatedAt: "2026-04-28T18:03:10Z"
    },
    {
      orderId: "PO-1",
      symbol: "MSFT",
      side: "Buy",
      type: "Limit",
      quantity: 20,
      filledQuantity: 0,
      averageFillPrice: null,
      status: "Working",
      createdAt: "2026-04-28T18:09:00Z",
      updatedAt: "2026-04-28T18:09:00Z"
    }
  ]
};

const fixturePaperSessionReplayVerification: PaperSessionReplayVerification = {
  summary: fixturePaperSessionSummaries[0]!,
  symbols: fixturePaperSessionDetail.symbols,
  replaySource: "fixtures/paper-dev-42.jsonl",
  isConsistent: true,
  mismatchReasons: [],
  currentPortfolio: fixturePaperSessionPortfolio,
  replayPortfolio: fixturePaperSessionPortfolio,
  verifiedAt: "2026-04-28T18:12:00Z",
  comparedFillCount: 9,
  comparedOrderCount: 3,
  comparedLedgerEntryCount: 11,
  lastPersistedFillAt: "2026-04-28T18:10:00Z",
  lastPersistedOrderUpdateAt: "2026-04-28T18:10:30Z",
  verificationAuditId: "audit-replay-dev-42"
};

const fixtureExecutionAudit: ExecutionAuditEntry[] = [
  {
    auditId: "audit-replay-dev-42",
    category: "PaperSession",
    action: "ReplayPaperSession",
    outcome: "Completed",
    occurredAt: "2026-04-28T18:12:00Z",
    actor: "fixture-operator",
    brokerName: null,
    orderId: null,
    runId: "run-dev-1",
    symbol: null,
    correlationId: "fixture-readiness",
    message: "Replay matched the recorded paper session state.",
    metadata: { sessionId: "paper-dev-42" }
  }
];

const fixtureExecutionControls: ExecutionControlSnapshot = {
  circuitBreaker: {
    isOpen: false,
    reason: null,
    changedBy: "fixture-operator",
    changedAt: "2026-04-28T17:45:00Z"
  },
  defaultMaxPositionSize: 50000,
  symbolPositionLimits: {
    AAPL: 25000,
    MSFT: 20000,
    NVDA: 15000
  },
  manualOverrides: [
    {
      overrideId: "override-fixture-1",
      kind: "BypassOrderControls",
      reason: "Fixture drill for paper-cockpit acceptance review.",
      createdBy: "fixture-operator",
      createdAt: "2026-04-28T17:58:00Z",
      expiresAt: null,
      symbol: "MSFT",
      strategyId: "strat-mean-reversion",
      runId: "run-dev-1"
    }
  ],
  asOf: "2026-04-28T18:15:00Z"
};

const fixtureReplayFiles = {
  files: [
    {
      path: "fixtures/paper-dev-42.jsonl",
      name: "paper-dev-42.jsonl",
      symbol: "AAPL",
      eventType: "trades",
      sizeBytes: 18432,
      isCompressed: false,
      lastModified: "2026-04-28T18:10:00Z"
    } satisfies ReplayFileRecord
  ],
  total: 1,
  timestamp: "2026-04-28T18:15:00Z"
};

const fixturePromotionHistory: PromotionRecord[] = [
  {
    promotionId: "promo-dev-1",
    strategyId: "strat-mean-reversion",
    strategyName: "Mean Reversion FX",
    sourceRunType: "backtest",
    targetRunType: "paper",
    runId: "run-dev-1",
    sourceRunId: "run-dev-1",
    targetRunId: "paper-dev-42",
    decision: "Approved",
    approvedBy: "fixture-operator",
    approvalReason: "Replay, DK1 trust, and risk controls are available for review.",
    reviewNotes: "Fixture promotion history keeps the no-host Trading cockpit populated.",
    auditReference: "audit-promo-dev-1",
    manualOverrideId: null,
    qualifyingSharpe: 1.41,
    qualifyingMaxDrawdownPercent: 5,
    qualifyingTotalReturn: 4.2,
    promotedAt: "2026-04-28T18:05:00Z"
  }
];

const fixturePromotionEvaluations: Record<string, PromotionEvaluationResult> = {
  "run-dev-2": {
    runId: "run-dev-2",
    strategyId: "run-dev-2",
    strategyName: "Index Momentum",
    sourceMode: "backtest",
    targetMode: "paper",
    isEligible: true,
    sharpeRatio: 1.25,
    maxDrawdownPercent: -0.04,
    totalReturn: 0.08,
    reason: "Promotion gates passed.",
    found: true,
    ready: true
  }
};

const fixtureTradingWorkspace: TradingWorkspaceResponse = {
  metrics: [
    { id: "pnl", label: "Net P&L", value: "+$3,100", delta: "+2.1%", tone: "success" },
    { id: "orders", label: "Open Orders", value: "4", delta: "+1", tone: "default" },
    { id: "fills", label: "Fills", value: "13", delta: "+3", tone: "success" },
    { id: "risk", label: "Risk", value: "Observe", delta: "0%", tone: "warning" }
  ],
  positions: [
    {
      symbol: "AAPL",
      side: "Long",
      quantity: "100",
      averagePrice: "188.10",
      markPrice: "189.00",
      dayPnl: "+$90",
      unrealizedPnl: "+$90",
      exposure: "$18,900"
    }
  ],
  openOrders: [
    {
      orderId: "PO-1",
      symbol: "MSFT",
      side: "Buy",
      type: "Limit",
      quantity: "20",
      limitPrice: "414.20",
      status: "Working",
      submittedAt: "09:42:00 ET"
    }
  ],
  fills: [
    {
      fillId: "FL-1",
      orderId: "PO-0",
      symbol: "NVDA",
      side: "Sell",
      quantity: "10",
      price: "948.20",
      venue: "NASDAQ",
      timestamp: "09:40:10 ET"
    }
  ],
  risk: fixtureTradingRisk,
  brokerage: {
    provider: "Interactive Brokers",
    account: "DU1009034",
    environment: "paper",
    connection: "Connected",
    lastHeartbeat: "2s ago",
    orderIngress: "healthy",
    fillFeed: "healthy",
    notes: "Adapter wiring preview from local development fixtures."
  },
  readiness: fixtureTradingReadiness
};

const fixtureDataWorkspace: DataWorkspaceResponse = {
  metrics: [
    { id: "providers", label: "Providers Healthy", value: "4", delta: "0", tone: "success" },
    { id: "backfills", label: "Backfills Running", value: "2", delta: "+1", tone: "default" },
    { id: "exports", label: "Exports Ready", value: "3", delta: "+1", tone: "success" },
    { id: "review", label: "Needs Review", value: "1", delta: "+1", tone: "warning" }
  ],
  providers: [
    {
      providerId: "polygon",
      displayName: "Polygon.io",
      provider: "Polygon",
      status: "Healthy",
      capability: "Streaming equities",
      latency: "18ms p50",
      note: "Realtime subscriptions are stable.",
      trustScore: "0.96",
      signalSource: "wave1-validation-summary",
      reasonCode: "provider-ready",
      recommendedAction: "Keep provider in the active trust set.",
      gateImpact: "Supports DK1"
    },
    {
      providerId: "databento",
      displayName: "Databento",
      provider: "Databento",
      status: "Warning",
      capability: "Backfill bars",
      latency: "42ms p50",
      note: "One options-chain backfill is waiting on operator review.",
      trustScore: "0.82",
      signalSource: "backfill-monitor",
      reasonCode: "review-backfill",
      recommendedAction: "Review queued options-chain backfill before report-pack use.",
      gateImpact: "Review required"
    }
  ],
  backfills: [
    {
      jobId: "BF-1042",
      scope: "US equities / 30d",
      provider: "Databento",
      status: "Running",
      progress: "62%",
      updatedAt: "2m ago"
    },
    {
      jobId: "BF-1044",
      scope: "Options chains / 7d",
      provider: "Databento",
      status: "Review",
      progress: "95%",
      updatedAt: "5m ago"
    }
  ],
  exports: [
    {
      exportId: "EX-2201",
      profile: "python-pandas",
      target: "strategy pack",
      status: "Ready",
      rows: "124k",
      updatedAt: "4m ago"
    }
  ]
};

const fixtureProviderConnections: ProviderConnectionRow[] = [
  {
    providerId: "polygon",
    displayName: "Polygon.io",
    capability: "Data",
    credentialState: "Verified",
    credentialSource: "LocalEncryptedStore",
    verificationState: "Verified",
    health: "Healthy",
    fallbackActive: false,
    lastVerifiedAt: "2026-06-02T16:40:00Z",
    lastSuccessfulAt: "2026-06-02T16:45:00Z",
    lastFailureAt: null,
    lastError: null,
    maskedKeyPreview: "pk_live_****7F3A",
    environment: "paper",
    externalAccountId: null,
    affectedWorkflows: ["Import", "Validate", "Backfill"],
    recommendedAction: "No provider readiness action required.",
    actionHref: "/settings#provider-polygon"
  },
  {
    providerId: "databento",
    displayName: "Databento",
    capability: "Data",
    credentialState: "Configured",
    credentialSource: "ExternalVaultReference",
    verificationState: "Stale",
    health: "Degraded",
    fallbackActive: true,
    lastVerifiedAt: "2026-06-01T15:15:00Z",
    lastSuccessfulAt: "2026-06-02T14:05:00Z",
    lastFailureAt: "2026-06-02T15:48:00Z",
    lastError: "Backfill latency exceeded the validation threshold.",
    maskedKeyPreview: "db_live_****91AC",
    environment: "paper",
    externalAccountId: null,
    affectedWorkflows: ["Acquire Data", "Validate Data", "Publish Data"],
    recommendedAction: "Review degradation evidence and fallback routing before accepting downstream readiness.",
    actionHref: "/settings#provider-databento"
  },
  {
    providerId: "plaid",
    displayName: "Plaid",
    capability: "Data",
    credentialState: "Missing",
    credentialSource: "None",
    verificationState: "NotVerified",
    health: "Warning",
    fallbackActive: false,
    lastVerifiedAt: null,
    lastSuccessfulAt: null,
    lastFailureAt: null,
    lastError: null,
    maskedKeyPreview: null,
    environment: "sandbox",
    externalAccountId: null,
    affectedWorkflows: ["Accounting evidence", "Brokerage sync"],
    recommendedAction: "Connect Plaid sandbox credentials before bank-account evidence can be retained.",
    actionHref: "/data/providers"
  }
];

const fixtureProviderReadiness: ProviderReadinessSummary = {
  asOf: "2026-06-02T16:50:00Z",
  status: "Blocked",
  totalProviders: 4,
  readyProviders: 1,
  reviewProviders: 1,
  degradedProviders: 1,
  blockedProviders: 1,
  summary: "1 provider blocks dependent workflows.",
  recommendedAction: "Repair Plaid credentials before routing accounting evidence workflows.",
  providers: [
    {
      providerId: "plaid",
      displayName: "Plaid",
      capability: "Data",
      status: "Blocked",
      credentialState: "Missing",
      credentialSource: "None",
      verificationState: "NotVerified",
      connectionHealth: "Warning",
      isEnabled: true,
      isConnected: false,
      fallbackActive: false,
      degradationScore: null,
      lastVerifiedAt: null,
      lastSuccessfulAt: null,
      lastFailureAt: null,
      lastError: "Sandbox client credentials have not been configured.",
      maskedKeyPreview: null,
      environment: "sandbox",
      externalAccountId: null,
      affectedWorkflows: ["Accounting evidence", "Brokerage sync"],
      recommendedAction: "Connect Plaid sandbox credentials before bank-account evidence can be retained.",
      actionHref: "/data/providers",
      evidence: [
        {
          kind: "Credential",
          label: "Credential",
          status: "Blocked",
          detail: "Required Plaid client fields are missing."
        },
        {
          kind: "Plaid",
          label: "Linked Plaid evidence",
          status: "Review",
          detail: "No linked Plaid items retained yet."
        }
      ],
      recoveryActions: [
        {
          actionId: "provider.plaid.open-setup",
          label: "Open setup",
          target: "/data/providers",
          requiresMutation: false
        }
      ]
    },
    {
      providerId: "databento",
      displayName: "Databento",
      capability: "Data",
      status: "Degraded",
      credentialState: "Configured",
      credentialSource: "ExternalVaultReference",
      verificationState: "Stale",
      connectionHealth: "Degraded",
      isEnabled: true,
      isConnected: true,
      fallbackActive: true,
      degradationScore: 0.74,
      lastVerifiedAt: "2026-06-01T15:15:00Z",
      lastSuccessfulAt: "2026-06-02T14:05:00Z",
      lastFailureAt: "2026-06-02T15:48:00Z",
      lastError: "Backfill latency exceeded the validation threshold.",
      maskedKeyPreview: "db_live_****91AC",
      environment: "paper",
      externalAccountId: null,
      affectedWorkflows: ["Acquire Data", "Validate Data", "Publish Data"],
      recommendedAction: "Review degradation evidence and fallback routing before accepting downstream readiness.",
      actionHref: "/settings#provider-databento",
      evidence: [
        {
          kind: "Credential",
          label: "Credential",
          status: "Review",
          detail: "Configured from external vault reference; verification is stale.",
          observedAt: "2026-06-01T15:15:00Z",
          route: "/settings#provider-databento"
        },
        {
          kind: "Degradation",
          label: "Degradation",
          status: "Degraded",
          detail: "Composite degradation score 74%."
        },
        {
          kind: "Routing",
          label: "Fallback route",
          status: "Degraded",
          detail: "Fallback routing is active for historical bars."
        }
      ],
      recoveryActions: [
        {
          actionId: "provider.databento.verify",
          label: "Verify credentials",
          target: "/api/providers/databento/verify",
          requiresMutation: true
        },
        {
          actionId: "provider.databento.diagnostics",
          label: "Open diagnostics",
          target: "/api/health/providers/databento/diagnostics",
          requiresMutation: false
        }
      ]
    },
    {
      providerId: "polygon",
      displayName: "Polygon.io",
      capability: "Data",
      status: "Ready",
      credentialState: "Verified",
      credentialSource: "LocalEncryptedStore",
      verificationState: "Verified",
      connectionHealth: "Healthy",
      isEnabled: true,
      isConnected: true,
      fallbackActive: false,
      degradationScore: 0.08,
      lastVerifiedAt: "2026-06-02T16:40:00Z",
      lastSuccessfulAt: "2026-06-02T16:45:00Z",
      lastFailureAt: null,
      lastError: null,
      maskedKeyPreview: "pk_live_****7F3A",
      environment: "paper",
      externalAccountId: null,
      affectedWorkflows: ["Import", "Validate", "Backfill"],
      recommendedAction: "No provider readiness action required.",
      actionHref: "/settings#provider-polygon",
      evidence: [
        {
          kind: "Credential",
          label: "Credential",
          status: "Ready",
          detail: "Verified from encrypted local store.",
          observedAt: "2026-06-02T16:40:00Z"
        },
        {
          kind: "Connection",
          label: "Connection",
          status: "Ready",
          detail: "Connected with 12 active subscriptions and 18 ms average latency.",
          observedAt: "2026-06-02T16:45:00Z"
        }
      ],
      recoveryActions: [
        {
          actionId: "provider.polygon.open-setup",
          label: "Open setup",
          target: "/settings#provider-polygon",
          requiresMutation: false
        },
        {
          actionId: "provider.polygon.verify",
          label: "Verify credentials",
          target: "/api/providers/polygon/verify",
          requiresMutation: true
        }
      ]
    },
    {
      providerId: "yahoo",
      displayName: "Yahoo Finance",
      capability: "Data",
      status: "Review",
      credentialState: "NotRequired",
      credentialSource: "NotRequired",
      verificationState: "NotRequired",
      connectionHealth: "Warning",
      isEnabled: false,
      isConnected: false,
      fallbackActive: false,
      degradationScore: null,
      lastVerifiedAt: null,
      lastSuccessfulAt: null,
      lastFailureAt: null,
      lastError: null,
      maskedKeyPreview: null,
      environment: null,
      externalAccountId: null,
      affectedWorkflows: ["Backfill fallback"],
      recommendedAction: "Review provider setup before routing dependent workflows.",
      actionHref: "/settings#provider-yahoo",
      evidence: [
        {
          kind: "Credential",
          label: "Credential",
          status: "Ready",
          detail: "No credentials are required for this provider."
        },
        {
          kind: "Connection",
          label: "Connection",
          status: "Review",
          detail: "Provider is not enabled for fallback routing."
        }
      ],
      recoveryActions: [
        {
          actionId: "provider.yahoo.open-setup",
          label: "Open setup",
          target: "/settings#provider-yahoo",
          requiresMutation: false
        }
      ]
    }
  ]
};

const fixtureAccountingWorkspace: AccountingWorkspaceResponse = {
  metrics: [
    { id: "breaks", label: "Open Breaks", value: "2", delta: "+1", tone: "warning" },
    { id: "drift", label: "Timing Drift", value: "1", delta: "0%", tone: "warning" },
    { id: "coverage", label: "Security Gaps", value: "0", delta: "0%", tone: "success" },
    { id: "audit", label: "Audit Ready", value: "4", delta: "+2", tone: "success" }
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
      resolutionNote: null
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
    fundProfileId: "default-fund",
    selectedFundProfileId: "default-fund",
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
    reportPackDistributions: [
      {
        distributionId: "board-reporting-committee",
        recipient: "Board reporting committee",
        recipientRole: "Board",
        channel: "Board portal",
        state: "Pending approval",
        pendingItems: 1,
        pendingSummary: "1 report pack still needs approval before Board reporting committee delivery.",
        owner: "fund-controller",
        dueAtUtc: "2026-05-03T20:00:00Z",
        lastSentAtUtc: null,
        route: "/reporting/report-packs?recipient=board"
      }
    ],
    pnlSlices: [
      {
        sliceId: "pnl:daily",
        period: "Daily",
        label: "Daily P&L",
        currency: "USD",
        startDate: "2026-05-03",
        endDate: "2026-05-03",
        realizedPnl: 3200,
        unrealizedPnl: 1200,
        totalPnl: 4400,
        priorTotalPnl: 2800,
        pnlChange: 1600,
        sourceCount: 2,
        asOf: "2026-05-03T20:00:00Z",
        route: "/api/workstation/reporting?pnlSlice=daily",
        readinessSummary: "2 source-backed run(s) in the daily window; compared with 1 prior-period run(s).",
        tags: ["pnl", "daily", "source-backed"],
        versionStamp: "pnl-slice:20260503200000:daily:sources-2:prior-1"
      },
      {
        sliceId: "pnl:weekly",
        period: "Weekly",
        label: "Weekly P&L",
        currency: "USD",
        startDate: "2026-04-27",
        endDate: "2026-05-03",
        realizedPnl: 5200,
        unrealizedPnl: 4300,
        totalPnl: 9500,
        priorTotalPnl: 6100,
        pnlChange: 3400,
        sourceCount: 5,
        asOf: "2026-05-03T20:00:00Z",
        route: "/api/workstation/reporting?pnlSlice=weekly",
        readinessSummary: "5 source-backed run(s) in the weekly window; compared with 3 prior-period run(s).",
        tags: ["pnl", "weekly", "source-backed"],
        versionStamp: "pnl-slice:20260503200000:weekly:sources-5:prior-3"
      },
      {
        sliceId: "pnl:monthly",
        period: "Monthly",
        label: "Monthly P&L",
        currency: "USD",
        startDate: "2026-05-01",
        endDate: "2026-05-03",
        realizedPnl: 5200,
        unrealizedPnl: 4300,
        totalPnl: 9500,
        priorTotalPnl: 7800,
        pnlChange: 1700,
        sourceCount: 5,
        asOf: "2026-05-03T20:00:00Z",
        route: "/api/workstation/reporting?pnlSlice=monthly",
        readinessSummary: "5 source-backed run(s) in the monthly window; compared with 8 prior-period run(s).",
        tags: ["pnl", "monthly", "source-backed"],
        versionStamp: "pnl-slice:20260503200000:monthly:sources-5:prior-8"
      },
      {
        sliceId: "pnl:yearly",
        period: "Yearly",
        label: "Yearly P&L",
        currency: "USD",
        startDate: "2026-01-01",
        endDate: "2026-05-03",
        realizedPnl: 5200,
        unrealizedPnl: 4300,
        totalPnl: 9500,
        priorTotalPnl: 0,
        pnlChange: 9500,
        sourceCount: 5,
        asOf: "2026-05-03T20:00:00Z",
        route: "/api/workstation/reporting?pnlSlice=yearly",
        readinessSummary: "5 source-backed run(s) in the yearly window; no prior-period source run is available for comparison.",
        tags: ["pnl", "yearly", "source-backed"],
        versionStamp: "pnl-slice:20260503200000:yearly:sources-5:prior-0"
      }
    ],
    analyticsRows: [
      {
        analyticsId: "analytics:topwinner:security:bdc-a",
        kind: "TopWinner",
        scope: "Security",
        rank: 1,
        label: "BDC Alpha",
        symbol: "BDC-A",
        classification: "Equity",
        currency: "USD",
        realizedPnl: 2800,
        unrealizedPnl: 1900,
        totalPnl: 4700,
        contributionPercent: 49.4737,
        heatMapIntensity: 49.4737,
        sourceCount: 2,
        asOf: "2026-05-03T20:00:00Z",
        route: "/api/workstation/reporting?analyticsId=analytics%3Atopwinner%3Asecurity%3Abdc-a",
        readinessSummary: "Top-N winner from 2 source-backed run(s); contributes 49.47% of portfolio P&L.",
        tags: ["analytics", "topwinner", "security", "equity"],
        versionStamp: "analytics:20260503200000:topwinner:security:sources-2"
      },
      {
        analyticsId: "analytics:toplaggard:security:hedge-overlay",
        kind: "TopLaggard",
        scope: "Security",
        rank: 1,
        label: "Hedge Overlay",
        symbol: "HEDGE",
        classification: "Derivative",
        currency: "USD",
        realizedPnl: -900,
        unrealizedPnl: -350,
        totalPnl: -1250,
        contributionPercent: -13.1579,
        heatMapIntensity: 13.1579,
        sourceCount: 1,
        asOf: "2026-05-03T20:00:00Z",
        route: "/api/workstation/reporting?analyticsId=analytics%3Atoplaggard%3Asecurity%3Ahedge-overlay",
        readinessSummary: "Top-N laggard from 1 source-backed run(s); contributes -13.16% of portfolio P&L.",
        tags: ["analytics", "toplaggard", "security", "derivative"],
        versionStamp: "analytics:20260503200000:toplaggard:security:sources-1"
      },
      {
        analyticsId: "analytics:contribution:strategy:paper-income",
        kind: "Contribution",
        scope: "Strategy",
        rank: 1,
        label: "Paper Income",
        symbol: null,
        classification: "Strategy",
        currency: "USD",
        realizedPnl: 5200,
        unrealizedPnl: 4300,
        totalPnl: 9500,
        contributionPercent: 100,
        heatMapIntensity: 100,
        sourceCount: 5,
        asOf: "2026-05-03T20:00:00Z",
        route: "/api/workstation/reporting?analyticsId=analytics%3Acontribution%3Astrategy%3Apaper-income",
        readinessSummary: "5 source-backed run(s); contribution is 100% of portfolio P&L with 100% heat-map intensity.",
        tags: ["analytics", "contribution", "strategy", "strategy"],
        versionStamp: "analytics:20260503200000:contribution:strategy:sources-5"
      }
    ],
    crossFundConsolidations: [
      {
        consolidationId: "cross-fund:company",
        label: "Company-wide consolidation",
        scope: "Company",
        currency: "USD",
        isReady: true,
        fundCount: 2,
        entityCount: 1,
        accountCount: 3,
        runCount: 2,
        grossExposure: 425000,
        netExposure: 398000,
        longMarketValue: 425000,
        shortMarketValue: -27000,
        totalCash: 120000,
        pendingSettlement: 1400,
        totalPnl: 9500,
        shadowNav: 518000,
        shadowNavVariance: 120000,
        sourceCount: 5,
        asOf: "2026-05-03T20:00:00Z",
        route: "/api/workstation/reporting?consolidationId=cross-fund%3Acompany",
        readinessSummary: "5 source record(s) across 2 fund(s), 1 entity row(s), 3 account(s), and 2 run(s).",
        tags: ["company", "cross-fund", "consolidated"],
        versionStamp: "cross-fund:20260503200000:funds-2:entities-1:sources-5"
      },
      {
        consolidationId: "cross-fund:fund:demo-fund",
        label: "Demo Income Fund",
        scope: "Fund",
        currency: "USD",
        isReady: true,
        fundCount: 1,
        entityCount: 1,
        accountCount: 2,
        runCount: 1,
        grossExposure: 310000,
        netExposure: 301000,
        longMarketValue: 310000,
        shortMarketValue: -9000,
        totalCash: 82500,
        pendingSettlement: 900,
        totalPnl: 6200,
        shadowNav: 383500,
        shadowNavVariance: 82500,
        sourceCount: 3,
        asOf: "2026-05-03T20:00:00Z",
        route: "/api/workstation/reporting?consolidationId=cross-fund%3Afund%3Ademo-fund",
        readinessSummary: "3 source record(s) across 1 fund(s), 1 entity row(s), 2 account(s), and 1 run(s).",
        tags: ["fund", "cross-fund", "consolidated"],
        versionStamp: "cross-fund:20260503200000:funds-1:entities-1:sources-3"
      }
    ],
    structuredExports: [
      {
        exportId: "investment-topn-contribution-analytics",
        label: "Top-N contribution analytics",
        purpose: "InvestmentDecision",
        format: "Csv",
        dataset: "portfolio-topn-contribution-analytics",
        consumer: "Investment and risk decision workflows",
        schemaVersion: 1,
        rowCount: 3,
        fieldCount: 18,
        sourceCount: 8,
        currency: "USD",
        asOf: "2026-05-03T20:00:00Z",
        isReady: true,
        retainedPath: "exports/reporting/default-fund/20260503200000/investment-topn-contribution-analytics.csv",
        route: "/api/workstation/reporting/structured-exports/investment-topn-contribution-analytics",
        dataDictionaryRoute: "/api/workstation/reporting",
        validationSummary: "Exports source-backed Top-N winners, laggards, and contribution rows with P&L percentages and heat-map intensities. 3 row(s), 18 field(s), and 8 source record(s) are ready.",
        evidenceRoute: "/api/fund-structure/report-packs",
        versionStamp: "structured-export:20260503200000:rows-3:sources-8:schema-1",
        tags: ["investment", "top-n", "contribution", "analytics"]
      }
    ],
    schedules: [
      {
        scheduleId: "sched-monthly-board-pack",
        templateId: "monthly-board-pack",
        cronExpression: "0 8 1 * *",
        nextAsOfDate: "2026-06-01",
        dueAtUtc: "2026-06-01T08:00:00Z",
        maxRetries: 2,
        requestedBy: "fund-controller",
        state: "Active",
        createdAtUtc: "2026-05-01T08:00:00Z",
        updatedAtUtc: "2026-05-28T12:00:00Z",
        lastRunAtUtc: "2026-05-01T08:05:00Z",
        lastRunId: "sched-monthly-board-pack-20260501",
        runCount: 1,
        description: "Monthly board packet with portal and email-link delivery.",
        deliveryTargets: [
          {
            distributionId: "board-reporting-committee",
            formats: ["Pdf", "Xlsx", "Csv"],
            deliveryMode: "SecurePortal",
            note: "Board portal delivery."
          },
          {
            distributionId: "investor-relations",
            formats: ["Pdf", "Csv"],
            deliveryMode: "EmailLink",
            note: "Investor email-link delivery."
          }
        ]
      }
    ],
    scheduleDeliveryPlans: [
      {
        planId: "schedule-delivery:sched-monthly-board-pack:board-reporting-committee",
        scheduleId: "sched-monthly-board-pack",
        templateId: "monthly-board-pack",
        distributionId: "board-reporting-committee",
        recipient: "Board reporting committee",
        recipientRole: "Board",
        channel: "Board portal",
        deliveryMode: "SecurePortal",
        formats: ["Pdf", "Xlsx", "Csv"],
        isReady: true,
        readinessSummary: "Will deliver Pdf/Xlsx/Csv by SecurePortal to Board reporting committee when schedule 'sched-monthly-board-pack' runs.",
        route: "/reporting/report-packs?recipient=board",
        dueAtUtc: "2026-06-01T08:00:00Z",
        nextAsOfDate: "2026-06-01",
        owner: "fund-controller",
        note: "Board portal delivery.",
        lastDeliveryAttemptId: null,
        lastDeliveryState: null,
        lastDeliveryAtUtc: null,
        lastDeliveryPackageRoute: null,
        lastDeliverySecureLink: null,
        versionStamp: "schedule-delivery-plan:sched-monthly-board-pack:board-reporting-committee:20260528120000:formats-3"
      },
      {
        planId: "schedule-delivery:sched-monthly-board-pack:investor-relations",
        scheduleId: "sched-monthly-board-pack",
        templateId: "monthly-board-pack",
        distributionId: "investor-relations",
        recipient: "Investor relations",
        recipientRole: "Investor communications",
        channel: "Investor portal",
        deliveryMode: "EmailLink",
        formats: ["Pdf", "Csv"],
        isReady: true,
        readinessSummary: "Will deliver Pdf/Csv by EmailLink to Investor relations when schedule 'sched-monthly-board-pack' runs.",
        route: "/reporting/report-packs?recipient=investor-relations",
        dueAtUtc: "2026-06-01T08:00:00Z",
        nextAsOfDate: "2026-06-01",
        owner: "investor-relations",
        note: "Investor email-link delivery.",
        lastDeliveryAttemptId: null,
        lastDeliveryState: null,
        lastDeliveryAtUtc: null,
        lastDeliveryPackageRoute: null,
        lastDeliverySecureLink: null,
        versionStamp: "schedule-delivery-plan:sched-monthly-board-pack:investor-relations:20260528120000:formats-2"
      }
    ],
    brandingThemes: [
      {
        themeId: "meridianstandard",
        name: "Meridian Standard",
        firmName: "Meridian",
        primaryColor: "#195E63",
        accentColor: "#2F9C95",
        textColor: "#102A2D",
        backgroundColor: "#FFFFFF",
        logoUri: null,
        footerText: "Generated by Meridian Reporting",
        disclaimer: "For authorized recipients only.",
        isBuiltIn: true
      },
      {
        themeId: "lpcustomtheme",
        name: "LP Custom Theme",
        firmName: "Northstar Capital",
        primaryColor: "#101828",
        accentColor: "#AA5500",
        textColor: "#111827",
        backgroundColor: "#FFFFFF",
        logoUri: "https://example.test/northstar.png",
        footerText: "Northstar Capital confidential.",
        disclaimer: "Prepared for authorized allocator review.",
        isBuiltIn: false
      }
    ],
    summary: "4 export/reporting profiles are available for Accounting and Reporting workflows.",
    templates: [
      {
        templateId: "investor-monthly-statement",
        family: "InvestorStatement",
        name: "Investor Monthly Statement",
        version: "1.0.0",
        sections: ["cover", "performance", "positions", "flows"],
        lifecycleStatus: "Approved",
        isBuiltIn: true,
        isLatestApproved: true,
        approvalSummary: "Built-in approved template for InvestorStatement.",
        authoringRoute: "/api/fund-structure/reporting/templates/investor-monthly-statement/versions/1",
        reportWriterGrids: [
          {
            gridId: "sector-pivot",
            title: "Sector Pivot",
            kind: "Pivot",
            dimensionCount: 2,
            metricCount: 2,
            formulaCount: 1,
            rowFields: ["sector"],
            columnFields: ["strategy"],
            metrics: [
              { name: "marketValue", sourceField: "marketValue", function: "Sum", label: "Market value" },
              { name: "pnl", sourceField: "pnl", function: "Sum", label: "P&L" }
            ],
            formulas: [
              { name: "returnPct", expression: "{pnl} / {marketValue} * 100", label: "Return %" }
            ],
            topN: null,
            sortBy: "pnl",
            sortDescending: true
          }
        ]
      },
      {
        templateId: "investor-monthly-statement",
        family: "InvestorStatement",
        name: "Investor Monthly Statement Draft",
        version: "2",
        sections: ["cover", "performance", "positions", "flows", "fees"],
        lifecycleStatus: "InReview",
        isBuiltIn: false,
        isLatestApproved: false,
        approvalSummary: "Custom v2 revision is waiting for controller approval.",
        authoringRoute: "/api/fund-structure/reporting/templates/investor-monthly-statement/versions/2"
      }
    ],
    workflowRecords: [
      {
        reportId: "report-restated-demo",
        fundProfileId: "demo-fund",
        fundAccountId: "demo-account",
        period: "2026-05",
        templateId: { name: "monthly-board-pack", version: 1 },
        state: "Restated",
        version: 2,
        createdAt: "2026-05-27T10:00:00Z",
        createdBy: "demo.reporter",
        updatedAt: "2026-05-28T12:00:00Z",
        auditTrail: [
          {
            at: "2026-05-28T12:00:00Z",
            actor: "demo.approver",
            action: "restated",
            fromState: "Published",
            toState: "Restated",
            note: "pricing-correction"
          }
        ],
        restatement: {
          reasonCode: "pricing-correction",
          approver: "fund-controller",
          priorVersionReportId: "report-published-demo",
          changedLines: [
            {
              lineKey: "nav.total",
              previousValue: "1250000",
              currentValue: "1249500",
              evidenceLinks: [
                {
                  evidenceId: "pricing-evidence-1",
                  label: "Pricing override",
                  route: "/reporting/evidence?subject=pricing-evidence-1",
                  source: "pricing",
                  capturedAtUtc: "2026-05-28T11:59:00Z"
                }
              ]
            }
          ],
          evidenceLinks: null
        },
        lineProvenance: [],
        publication: {
          manifestId: "manifest-restated-demo",
          retainedManifestPath: "vault/report-packs/manifest-restated-demo.json",
          evidenceHash: "sha256:restated-demo",
          signedOffBy: "demo.publisher",
          signedOffAt: "2026-05-28T15:20:00Z",
          evidenceLinks: [
            {
              evidenceId: "publication-evidence-demo",
              label: "Publication manifest",
              route: "/reporting/manifests/manifest-restated-demo",
              source: "reporting",
              capturedAtUtc: "2026-05-28T15:20:00Z"
            }
          ]
        }
      }
    ]
  }
};

const fixtureOperatorInbox: OperatorInbox = {
  asOf: "2026-04-28T18:15:00Z",
  criticalCount: 0,
  warningCount: 3,
  reviewCount: 3,
  summary: "3 operator review items need attention before paper-operation readiness is accepted.",
  items: [
    ...fixtureTradingReadiness.workItems,
    {
      workItemId: "reconciliation-break-run-42-cash",
      kind: "ReconciliationBreak",
      label: "Reconciliation break open",
      detail: "Cash variance over tolerance remains open on Paper Index Mean Reversion.",
      tone: "Warning",
      createdAt: "2026-04-28T18:15:00Z",
      runId: "run-42",
      fundAccountId: null,
      auditReference: null,
      workspace: "Accounting",
      targetRoute: "/accounting/reconciliation",
      targetPageTag: "FundReconciliation"
    }
  ]
};

const fixtureAlpacaConnection: BrokerageConnectionStatus = {
  providerId: "alpaca",
  displayName: "Alpaca paper",
  state: "Connected",
  isConfigured: true,
  isConnected: true,
  authorizationUrl: null,
  connectedAt: "2026-05-07T11:50:00Z",
  expiresAt: null,
  lastError: null,
  warnings: [],
  scopes: ["trading:account", "brokerage-sync:read"],
  environment: "paper",
  externalAccountId: "PA-DEMO",
  verifiedAt: "2026-05-07T11:50:00Z",
  maskedKeyId: "********DEMO"
};

const fixtureAlpacaPortfolio: BrokerageHouseholdPortfolio = {
  providerId: "alpaca",
  asOf: "2026-05-07T12:00:00Z",
  totalCash: 87500,
  totalEquity: 312400,
  totalBuyingPower: 87500,
  currency: "USD",
  warnings: [],
  accounts: [
    {
      fundAccountId: "rh-fund-roth",
      providerId: "alpaca",
      externalAccountId: "alpaca-roth",
      displayName: "Alpaca Roth IRA",
      accountKind: "RothIra",
      health: "Healthy",
      cash: 18500,
      equity: 104200,
      buyingPower: 18500,
      currency: "USD",
      syncedAt: "2026-05-07T12:00:00Z",
      positionCount: 2,
      cashTransactionCount: 4,
      warnings: []
    },
    {
      fundAccountId: "rh-fund-traditional",
      providerId: "alpaca",
      externalAccountId: "alpaca-traditional",
      displayName: "Alpaca Traditional IRA",
      accountKind: "TraditionalIra",
      health: "Healthy",
      cash: 24000,
      equity: 98200,
      buyingPower: 24000,
      currency: "USD",
      syncedAt: "2026-05-07T12:00:00Z",
      positionCount: 1,
      cashTransactionCount: 2,
      warnings: []
    },
    {
      fundAccountId: "rh-fund-taxable",
      providerId: "alpaca",
      externalAccountId: "alpaca-taxable",
      displayName: "Alpaca Brokerage",
      accountKind: "TaxableBrokerage",
      health: "Stale",
      cash: 45000,
      equity: 110000,
      buyingPower: 45000,
      currency: "USD",
      syncedAt: "2026-05-07T10:30:00Z",
      positionCount: 2,
      cashTransactionCount: 5,
      warnings: ["Brokerage sync is stale."]
    }
  ],
  positions: [
    {
      fundAccountId: "rh-fund-roth",
      providerId: "alpaca",
      externalAccountId: "alpaca-roth",
      accountKind: "RothIra",
      symbol: "AAPL",
      quantity: 38,
      averageEntryPrice: 142.2,
      marketPrice: 188.4,
      marketValue: 7159.2,
      unrealizedPnl: 1755.6,
      assetClass: "equity",
      security: null,
      description: "Apple Inc.",
      positionId: "rh-roth-aapl",
      currency: "USD"
    },
    {
      fundAccountId: "rh-fund-traditional",
      providerId: "alpaca",
      externalAccountId: "alpaca-traditional",
      accountKind: "TraditionalIra",
      symbol: "VTI",
      quantity: 120,
      averageEntryPrice: 205,
      marketPrice: 254.5,
      marketValue: 30540,
      unrealizedPnl: 5940,
      assetClass: "etf",
      security: null,
      description: "Vanguard Total Stock Market ETF",
      positionId: "rh-traditional-vti",
      currency: "USD"
    },
    {
      fundAccountId: "rh-fund-taxable",
      providerId: "alpaca",
      externalAccountId: "alpaca-taxable",
      accountKind: "TaxableBrokerage",
      symbol: "MSFT",
      quantity: 16,
      averageEntryPrice: 312,
      marketPrice: 421.7,
      marketValue: 6747.2,
      unrealizedPnl: 1755.2,
      assetClass: "equity",
      security: null,
      description: "Microsoft Corporation",
      positionId: "rh-taxable-msft",
      currency: "USD"
    }
  ]
};

const fixtureTradingWorkflowActions: WorkflowAction[] = [
  {
    actionId: "workflow.trading.review-paper-candidate",
    label: "Review Candidate for Paper",
    detail: "Continue the Strategy to Trading handoff.",
    targetPageTag: "TradingShell",
    tone: "Primary",
    workItemKind: "PromotionReview",
    routePrefixes: [WORKSTATION_API_ENDPOINTS.tradingReadiness],
    routeContains: [],
    aliases: []
  },
  {
    actionId: "workflow.trading.review-execution-controls",
    label: "Review Execution Controls",
    detail: "Inspect control evidence and operator override posture.",
    targetPageTag: "RunRisk",
    tone: "Warning",
    workItemKind: "ExecutionControl",
    routePrefixes: [EXECUTION_API_ENDPOINTS.controls],
    routeContains: [],
    aliases: []
  }
];

const fixtureDataWorkflowActions: WorkflowAction[] = [
  {
    actionId: "workflow.data.open-provider-health",
    label: "Open Provider Health",
    detail: "Inspect provider posture and reconnect degraded feeds.",
    targetPageTag: "ProviderHealth",
    tone: "Warning",
    workItemKind: null,
    routePrefixes: [],
    routeContains: [],
    aliases: []
  },
  {
    actionId: "workflow.data.review-security-master",
    label: "Review Security Master",
    detail: "Review reference-data coverage and symbol lifecycle issues.",
    targetPageTag: "SecurityMaster",
    tone: "Warning",
    workItemKind: "SecurityMasterCoverage",
    routePrefixes: [SECURITY_MASTER_API_ENDPOINTS.workstationSecurities],
    routeContains: [],
    aliases: []
  }
];

const fixtureWorkflowLibrary: WorkflowLibrary = {
  generatedAt: "2026-04-28T18:15:00Z",
  workflows: [
    {
      workflowId: "paper-trading-readiness",
      title: "Paper Trading Readiness",
      summary: "Review context, replay, controls, and cockpit readiness before live escalation.",
      workspaceId: "trading",
      workspaceTitle: "Trading",
      entryPageTag: "TradingShell",
      tone: "Warning",
      actions: fixtureTradingWorkflowActions,
      evidenceTags: ["readiness gates", "replay verification", "control evidence", "operator work items"],
      marketPatternTags: ["paper trading", "live readiness gate", "execution controls"]
    },
    {
      workflowId: "data-provider-recovery",
      title: "Data Provider Recovery",
      summary: "Review provider health, failed backfills, security coverage, and data quality.",
      workspaceId: "data",
      workspaceTitle: "Data",
      entryPageTag: "DataShell",
      tone: "Warning",
      actions: fixtureDataWorkflowActions,
      evidenceTags: ["provider metrics", "backfill status", "security coverage", "data quality"],
      marketPatternTags: ["provider dashboard", "data quality queue", "coverage workbench"]
    }
  ],
  actions: [...fixtureTradingWorkflowActions, ...fixtureDataWorkflowActions]
};

const fixtureWorkflowPresetLibrary: WorkflowPresetLibrary = {
  generatedAt: "2026-04-28T18:15:00Z",
  presets: [
    {
      presetId: "daily-paper-readiness",
      name: "Daily paper readiness",
      description: "Review session, replay, brokerage sync, and execution controls before the desk opens.",
      workflowId: "paper-trading-readiness",
      workflowTitle: "Paper Trading Readiness",
      actionId: "workflow.trading.review-paper-candidate",
      actionLabel: "Review Candidate for Paper",
      workspaceId: "trading",
      workspaceTitle: "Trading",
      targetPageTag: "TradingShell",
      tags: ["paper", "readiness"],
      isPinned: true,
      createdAt: "2026-04-28T17:00:00Z",
      updatedAt: "2026-04-28T18:15:00Z",
      lastUsedAt: null
    }
  ]
};

const fixtureWorkflowSummary: OperatorWorkflowHomeSummary = {
  generatedAt: "2026-04-28T18:15:00Z",
  hasOperatingContext: false,
  operatingContextLabel: "No-host fixture workspace",
  fundDisplayName: "Demo Fund",
  assuranceScore: null,
  workspaces: [
    {
      workspaceId: "trading",
      workspaceTitle: "Trading",
      statusLabel: "Review required",
      statusDetail: "Paper-readiness controls are populated from fixture replay and risk evidence.",
      statusTone: "Warning",
      nextAction: {
        label: "Review paper readiness",
        detail: "Inspect session, replay, and execution-control evidence before escalation.",
        targetPageTag: "TradingShell",
        tone: "Warning"
      },
      primaryBlocker: {
        code: "FIXTURE_MODE",
        label: "Fixture-only preview",
        detail: "No live operating context is attached to this workstation preview.",
        tone: "Info",
        isBlocking: false
      },
      evidence: [
        { label: "Replay", value: "Fixture", tone: "Info" },
        { label: "Controls", value: "Seeded", tone: "Warning" }
      ]
    },
    {
      workspaceId: "data",
      workspaceTitle: "Data",
      statusLabel: "Provider review",
      statusDetail: "Provider routing and security-master coverage use no-host fixture payloads.",
      statusTone: "Warning",
      nextAction: {
        label: "Review provider routes",
        detail: "Confirm paper and reference-data routes before using live data.",
        targetPageTag: "ProviderHealth",
        tone: "Warning"
      },
      primaryBlocker: {
        code: "NO_HOST_ROUTING",
        label: "No host connected",
        detail: "Provider trust snapshots are demo-only until the Meridian API host is available.",
        tone: "Info",
        isBlocking: false
      },
      evidence: [
        { label: "Routes", value: "2", tone: "Info" },
        { label: "Trust", value: "Demo", tone: "Warning" }
      ]
    },
    {
      workspaceId: "settings",
      workspaceTitle: "Settings",
      statusLabel: "Demo controls",
      statusDetail: "Runtime capabilities and provider setup controls are seeded for first-run review.",
      statusTone: "Info",
      nextAction: {
        label: "Inspect runtime controls",
        detail: "Review capability toggles and provider-routing fixture state.",
        targetPageTag: "SettingsShell",
        tone: "Info"
      },
      primaryBlocker: {
        code: "NONE",
        label: "No blocking setup item",
        detail: "Settings controls are available for no-host product review.",
        tone: "Success",
        isBlocking: false
      },
      evidence: [
        { label: "Capabilities", value: "Fixture", tone: "Info" },
        { label: "Providers", value: "Seeded", tone: "Info" }
      ]
    }
  ]
};

const fixtureFeatureCapabilities: FeatureCapabilitySettingsResponse = {
  capabilities: [
    {
      capabilityKey: "desktop.settings.provider-connection-center-inline-management",
      displayName: "Provider connection center",
      description: "Inline provider setup and routing controls for the Settings workspace.",
      isEnabled: true,
      defaultEnabled: true,
      isPermanent: false,
      isOverridden: false,
      canToggle: true,
      disabledReason: null
    },
    {
      capabilityKey: "desktop.data.security-master",
      displayName: "Security master governance",
      description: "Reference-data governance and asset profile controls for operator review.",
      isEnabled: true,
      defaultEnabled: true,
      isPermanent: false,
      isOverridden: false,
      canToggle: true,
      disabledReason: null
    },
    {
      capabilityKey: "browser.no-host-fixtures",
      displayName: "No-host fixture preview",
      description: "Keeps browser workstation demos visibly labeled when the Meridian API host is unavailable.",
      isEnabled: true,
      defaultEnabled: true,
      isPermanent: true,
      isOverridden: false,
      canToggle: false,
      disabledReason: "Fixture preview capability is required when no API host is reachable."
    }
  ]
};

const fixtureProviderRoutingConnections: ProviderRoutingConnection[] = [
  {
    connectionId: "provider-alpaca-paper",
    providerFamilyId: "alpaca",
    displayName: "Alpaca paper route",
    connectionType: "DataVendor",
    connectionMode: "Paper",
    enabled: true,
    credentialReference: "fixture://provider/alpaca-paper",
    institutionId: null,
    externalAccountId: null,
    scope: null,
    tags: ["paper", "market-data", "fixture"],
    description: "No-host fixture route for paper-market data review.",
    productionReady: false
  },
  {
    connectionId: "provider-reference",
    providerFamilyId: "polygon",
    displayName: "Reference data route",
    connectionType: "DataVendor",
    connectionMode: "ReadOnly",
    enabled: true,
    credentialReference: "fixture://provider/reference-data",
    institutionId: null,
    externalAccountId: null,
    scope: null,
    tags: ["reference", "security-master", "fixture"],
    description: "No-host fixture route for security-master and reference-data coverage.",
    productionReady: false
  }
];

const fixtureProviderRoutingBindings: ProviderRoutingBinding[] = [
  {
    bindingId: "provider-alpaca-paper-RealtimeMarketData",
    capability: "RealtimeMarketData",
    connectionId: "provider-alpaca-paper",
    target: null,
    priority: 100,
    enabled: true,
    failoverConnectionIds: ["provider-reference"],
    safetyModeOverride: "PaperOnly",
    notes: "Fixture route used only for no-host browser workstation review."
  },
  {
    bindingId: "provider-reference-ReferenceData",
    capability: "ReferenceData",
    connectionId: "provider-reference",
    target: null,
    priority: 110,
    enabled: true,
    failoverConnectionIds: [],
    safetyModeOverride: "ReadOnly",
    notes: "Fixture reference-data path for first-run security-master review."
  }
];

const fixtureProviderRoutingTrustSnapshots: ProviderRoutingTrustSnapshot[] = [
  {
    connectionId: "provider-alpaca-paper",
    providerFamilyId: "alpaca",
    score: 84,
    isHealthy: true,
    healthStatus: "Healthy",
    isProductionReady: false,
    isCertificationFresh: false,
    signals: ["fixture-mode", "paper-only"],
    decision: null
  },
  {
    connectionId: "provider-reference",
    providerFamilyId: "polygon",
    score: 91,
    isHealthy: true,
    healthStatus: "Healthy",
    isProductionReady: false,
    isCertificationFresh: false,
    signals: ["fixture-mode", "reference-data"],
    decision: null
  }
];

const fixtureAccessAssignments: UserAccessAssignment[] = [];

function buildFixtureProviderIntegrationSyncRun(connectionId: string): ProviderIntegrationSyncRunEvidence {
  return {
    syncRunId: `${connectionId}-fixture-sync-1`,
    capability: "Positions",
    endpointKey: "positions",
    startedAt: "2026-06-16T12:30:00Z",
    completedAt: "2026-06-16T12:31:00Z",
    status: "Quarantined",
    recordsReceived: 12,
    recordsAccepted: 10,
    recordsQuarantined: 2,
    durableStagingRecordCount: 10,
    durableQuarantinedRecordCount: 2,
    criticalIssueCount: 1,
    warningIssueCount: 1,
    rawPayloadId: `${connectionId}-fixture-raw-1`,
    issues: [
      {
        code: "SCHEMA_REQUIRED",
        severity: "Critical",
        message: "CUSIP is required before staging promotion.",
        targetField: "cusip",
        suggestedFix: "Add provider mapping."
      }
    ]
  };
}

function buildFixtureProviderIntegrationMonitor(connectionId: string): ProviderIntegrationConnectionMonitor {
  const syncRun = buildFixtureProviderIntegrationSyncRun(connectionId);
  return {
    connectionId,
    manifestId: `${connectionId}-fixture-manifest`,
    providerId: connectionId,
    displayName: `${connectionId} fixture integration`,
    connectionName: "Fixture provider runtime route",
    environment: "paper",
    state: "Active",
    enabledCapabilities: ["Positions"],
    lastSyncRun: syncRun,
    recentSyncRuns: [syncRun],
    recentRecordsReceived: syncRun.recordsReceived,
    recentRecordsAccepted: syncRun.recordsAccepted,
    recentRecordsQuarantined: syncRun.recordsQuarantined,
    durableStagingRecordCount: syncRun.durableStagingRecordCount,
    durableQuarantinedRecordCount: syncRun.durableQuarantinedRecordCount,
    hasCriticalIssues: true
  };
}

function buildFixtureProviderIntegrationSyncHistory(connectionId: string): ProviderIntegrationSyncRunHistory {
  const syncRun = buildFixtureProviderIntegrationSyncRun(connectionId);
  return {
    connectionId,
    syncRuns: [syncRun],
    totalSyncRuns: 1,
    returnedSyncRuns: 1,
    latestStartedAt: syncRun.startedAt
  };
}

function buildFixtureProviderIntegrationSyncPlan(connectionId: string, path: string): ProviderIntegrationSyncPlan {
  const params = readFixtureSearchParams(path);
  return {
    connectionId,
    manifestId: `${connectionId}-fixture-manifest`,
    providerId: connectionId,
    connectionName: "Fixture provider runtime route",
    connectionState: "Active",
    evaluatedAt: params.get("evaluatedAt") ?? "2026-06-16T12:35:00Z",
    dueCount: 1,
    blockedCount: 0,
    items: [
      {
        capability: "Positions",
        endpointKey: "positions",
        scheduleMode: "incremental",
        frequency: "daily",
        timezone: "America/New_York",
        lastSuccessfulSyncAt: "2026-06-16T12:30:00Z",
        nextEligibleSyncAt: "2026-06-17T12:30:00Z",
        isDue: true,
        isBlocked: false,
        reason: "Fixture provider position sync is due for review.",
        issues: []
      }
    ]
  };
}

function buildFixtureProviderIntegrationStaging(connectionId: string): ProviderIntegrationStagingReview {
  const syncRunId = `${connectionId}-fixture-sync-1`;
  return {
    connectionId,
    syncRunIds: [syncRunId],
    records: [
      {
        stagingRecordId: `${connectionId}-fixture-stage-1`,
        syncRunId,
        connectionId,
        capability: "Positions",
        rawPayloadId: `${connectionId}-fixture-raw-1`,
        sourceRecordId: `${connectionId}-position-1`,
        dedupeKey: `Positions:${connectionId}-position-1`,
        mappedRecord: { providerAccountId: "fixture-account", quantity: 10 },
        validationWarnings: [],
        status: "Validated",
        createdAt: "2026-06-16T12:31:00Z"
      }
    ],
    capabilitySummaries: [{ capability: "Positions", recordCount: 1, warningCount: 0 }],
    warningGroups: [],
    totalStagedRecords: 1,
    readyForReconciliationCount: 1,
    warningRecordCount: 0
  };
}

function buildFixtureProviderIntegrationIdentity(connectionId: string): ProviderIntegrationStagingIdentityResolutionPreview {
  return {
    connectionId,
    syncRunIds: [`${connectionId}-fixture-sync-1`],
    rows: [],
    totalRows: 1,
    accountReviewRequiredCount: 0,
    missingAccountIdentifierCount: 0,
    securityResolvedCount: 1,
    securityReviewRequiredCount: 0,
    missingSecurityIdentifierCount: 0
  };
}

function buildFixtureProviderIntegrationPromotion(connectionId: string): ProviderIntegrationPromotionReadinessPreview {
  const syncRunId = `${connectionId}-fixture-sync-1`;
  return {
    connectionId,
    syncRunIds: [syncRunId],
    rows: [
      {
        stagingRecordId: `${connectionId}-fixture-stage-1`,
        syncRunId,
        capability: "Positions",
        promotionTarget: "reconciliation-staging",
        status: "ReadyForReconciliation",
        providerAccountId: "fixture-account",
        internalAccountId: "fixture-internal-account",
        internalSecurityId: "fixture-security-1",
        securityDisplayName: "Fixture Treasury 2031",
        securityRoute: "/data/security-master/fixture-security-1",
        issues: []
      }
    ],
    totalRows: 1,
    readyForReconciliationCount: 1,
    reviewRequiredCount: 0,
    blockedCount: 0
  };
}

function buildFixtureProviderIntegrationHandoffs(connectionId: string): ProviderIntegrationReconciliationHandoffHistory {
  const syncRunId = `${connectionId}-fixture-sync-1`;
  return {
    connectionId,
    records: [
      {
        handoffId: `${connectionId}-fixture-handoff-1`,
        connectionId,
        syncRunId,
        stagingRecordId: `${connectionId}-fixture-stage-1`,
        capability: "Positions",
        promotionTarget: "reconciliation-staging",
        requestedBy: "fixture-operator",
        requestedAt: "2026-06-16T12:40:00Z",
        approvalEvidenceId: `${connectionId}-fixture-approval-1`,
        note: "Fixture handoff retained after identity review.",
        providerAccountId: "fixture-account",
        internalAccountId: "fixture-internal-account",
        internalSecurityId: "fixture-security-1",
        securityRoute: "/data/security-master/fixture-security-1",
        issues: []
      }
    ],
    totalRecords: 1,
    handoffCount: 1,
    lastRequestedAt: "2026-06-16T12:40:00Z"
  };
}

function buildFixtureProviderIntegrationQuarantine(connectionId: string): ProviderIntegrationQuarantineReview {
  const syncRunId = `${connectionId}-fixture-sync-1`;
  return {
    connectionId,
    syncRunIds: [syncRunId],
    records: [
      {
        quarantineRecordId: `${connectionId}-fixture-quarantine-1`,
        syncRunId,
        connectionId,
        capability: "Positions",
        rawRecord: { accountNumber: "fixture-account", cusip: null },
        mappedRecord: { providerAccountId: "fixture-account" },
        validationErrors: [
          {
            code: "SCHEMA_REQUIRED",
            severity: "Critical",
            message: "CUSIP is required before staging promotion.",
            targetField: "cusip",
            suggestedFix: "Add provider mapping."
          }
        ],
        status: "Quarantined",
        createdAt: "2026-06-16T12:31:00Z"
      }
    ],
    issueGroups: [
      {
        issueCode: "SCHEMA_REQUIRED",
        severity: "Critical",
        targetField: "cusip",
        message: "CUSIP is required before staging promotion.",
        suggestedFix: "Add provider mapping.",
        recordCount: 1
      }
    ],
    decisions: [],
    totalQuarantinedRecords: 1,
    criticalIssueCount: 1,
    warningIssueCount: 0,
    pendingReviewRecordCount: 1,
    decisionedRecordCount: 0,
    replayRequestedRecordCount: 0,
    ignoredRecordCount: 0,
    cashPositionCandidateCount: 0
  };
}

const fixtureSecurityAssetProfiles: SecurityAssetProfileDefinition[] = [
  {
    profileId: "fixture-public-equity",
    version: 1,
    name: "Listed common equity",
    category: "Equity",
    subType: "CommonStock",
    status: "Approved",
    fields: [
      {
        key: "primaryTicker",
        label: "Primary ticker",
        fieldType: "Text",
        isRequired: true,
        allowedValues: [],
        description: "Primary exchange ticker used by paper-market data fixtures.",
        minValue: null,
        maxValue: null,
        isProjected: true,
        isSearchable: true
      }
    ],
    identifierPreferences: [
      {
        kind: "Ticker",
        isRequiredForClose: true,
        reason: "Ticker coverage is required for fixture trade and position review."
      }
    ],
    lifecycleStates: ["Active", "Inactive", "Retired"],
    accountingImpactHints: ["LedgerClassification", "Valuation"],
    dateOrderRules: [],
    effectiveFrom: "2026-01-01",
    effectiveTo: null,
    approvedBy: "fixture-operator",
    approvedAtUtc: "2026-04-28T18:15:00Z",
    changeReason: "No-host browser workstation fixture profile."
  }
];

const fixtureRiskRules: RiskRuleStatus[] = [
  {
    ruleName: "DrawdownCircuitBreaker",
    state: "Observe",
    summary: "Fixture drawdown control is in observe mode for paper review.",
    isBreached: false,
    threshold: "8.00%",
    currentValue: "-1.20%",
    asOf: "2026-04-28T18:15:00Z",
    recentViolations: []
  },
  {
    ruleName: "PositionLimit",
    state: "Healthy",
    summary: "Fixture position limits are within configured paper thresholds.",
    isBreached: false,
    threshold: "500 shares",
    currentValue: "250 shares",
    asOf: "2026-04-28T18:15:00Z",
    recentViolations: []
  }
];

const fixtureDrawdownRiskRuleConfig: RiskRuleConfig = {
  ruleName: "DrawdownCircuitBreaker",
  defaultMaxPositionSize: 500,
  symbolPositionLimits: {
    AAPL: 250,
    MSFT: 200
  },
  maxDrawdownPercent: 8,
  maxOrdersPerMinute: 12
};

const fixtureCalibrationSummary: ReconciliationCalibrationSummary = {
  asOf: "2026-04-28T18:15:00Z",
  status: "ReviewRequired",
  summary: "2 open breaks remain across 2 tolerance profiles. 1 critical break requires immediate review before sign-off.",
  totalBreakCount: 3,
  activeBreakCount: 2,
  openBreakCount: 2,
  inReviewBreakCount: 0,
  resolvedBreakCount: 1,
  dismissedBreakCount: 0,
  criticalOpenBreakCount: 1,
  pendingSignoffCount: 1,
  signedOffCount: 2,
  missingCalibrationMetadataCount: 0,
  breakCountTrend: 1,
  autoMatchRate: 0.86,
  t0ClosureRate: 0.67,
  breakCountAlertThreshold: 25,
  autoMatchRateAlertThreshold: 0.85,
  t0ClosureRateAlertThreshold: 0.9,
  breakCountAlertTriggered: false,
  autoMatchRateAlertTriggered: false,
  t0ClosureRateAlertTriggered: true,
  profiles: [
    {
      toleranceProfileId: "tp-cash-variance",
      exceptionRoute: "ops.gov",
      highestSeverity: "Critical",
      maxToleranceBand: 250,
      totalBreakCount: 2,
      openBreakCount: 1,
      inReviewBreakCount: 0,
      resolvedBreakCount: 1,
      dismissedBreakCount: 0,
      pendingSignoffCount: 1,
      signedOffCount: 1,
      lastUpdatedAt: "2026-04-28T18:10:00Z"
    },
    {
      toleranceProfileId: "tp-timing-drift",
      exceptionRoute: "ops.gov",
      highestSeverity: "Warning",
      maxToleranceBand: null,
      totalBreakCount: 1,
      openBreakCount: 1,
      inReviewBreakCount: 0,
      resolvedBreakCount: 0,
      dismissedBreakCount: 0,
      pendingSignoffCount: 0,
      signedOffCount: 1,
      lastUpdatedAt: "2026-04-28T18:05:00Z"
    }
  ]
};

const fixtureCorporateActions: CorporateAction[] = [
  {
    corpActId: "ca-aapl-div-2026-02",
    securityId: "sec-dev-001",
    eventType: "Dividend",
    exDate: "2026-02-07",
    payDate: "2026-02-13",
    dividendPerShare: 0.25,
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
    corpActId: "ca-aapl-split-2020-08",
    securityId: "sec-dev-001",
    eventType: "StockSplit",
    exDate: "2020-08-31",
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

const fixtureSecurityMasterEntries: SecurityMasterEntry[] = [
  {
    securityId: "sec-dev-001",
    displayName: "Apple Inc.",
    status: "Active",
    classification: {
      assetClass: "Equity",
      subType: "Common Stock",
      primaryIdentifierKind: "Ticker",
      primaryIdentifierValue: "AAPL",
      matchedIdentifierKind: "Ticker",
      matchedIdentifierValue: "AAPL",
      matchedProvider: "Bloomberg"
    },
    economicDefinition: {
      currency: "USD",
      version: 3,
      effectiveFrom: "2024-01-01",
      effectiveTo: null,
      subType: "Common Stock",
      assetFamily: "Equities",
      issuerType: "Corporate"
    }
  },
  {
    securityId: "sec-dev-002",
    displayName: "PG&E Corporation",
    status: "Active",
    classification: {
      assetClass: "Equity",
      subType: "Common Stock",
      primaryIdentifierKind: "Ticker",
      primaryIdentifierValue: "PCG",
      matchedIdentifierKind: "Ticker",
      matchedIdentifierValue: "PCG",
      matchedProvider: "Interactive Brokers"
    },
    economicDefinition: {
      currency: "USD",
      version: 2,
      effectiveFrom: "2023-07-01",
      effectiveTo: null,
      subType: "Common Stock",
      assetFamily: "Utilities",
      issuerType: "Corporate"
    }
  },
  {
    securityId: "sec-dev-003",
    displayName: "Microsoft Corporation",
    status: "Active",
    classification: {
      assetClass: "Equity",
      subType: "Common Stock",
      primaryIdentifierKind: "Ticker",
      primaryIdentifierValue: "MSFT",
      matchedIdentifierKind: "Ticker",
      matchedIdentifierValue: "MSFT",
      matchedProvider: "Nasdaq"
    },
    economicDefinition: {
      currency: "USD",
      version: 4,
      effectiveFrom: "2024-01-01",
      effectiveTo: null,
      subType: "Common Stock",
      assetFamily: "Software",
      issuerType: "Corporate"
    }
  },
  {
    securityId: "sec-dev-004",
    displayName: "Meridian 5.875% 2031 Corporate Bond",
    status: "Active",
    classification: {
      assetClass: "Fixed Income",
      subType: "Corporate Bond",
      primaryIdentifierKind: "CUSIP",
      primaryIdentifierValue: "589999AB4",
      matchedIdentifierKind: "ISIN",
      matchedIdentifierValue: "US589999AB47",
      matchedProvider: "Reference fixture"
    },
    economicDefinition: {
      currency: "USD",
      version: 1,
      effectiveFrom: "2025-12-15",
      effectiveTo: null,
      subType: "Corporate Bond",
      assetFamily: "Credit",
      issuerType: "Corporate"
    }
  }
];

const fixtureSecurityIdentities: Record<string, SecurityIdentityDrillIn> = {
  "sec-dev-001": {
    securityId: "sec-dev-001",
    displayName: "Apple Inc.",
    assetClass: "Equity",
    status: "Active",
    version: 3,
    effectiveFrom: "2024-01-01",
    effectiveTo: null,
    identifiers: [
      {
        kind: "Ticker",
        value: "AAPL",
        isPrimary: true,
        validFrom: "2024-01-01",
        validTo: null,
        provider: "Bloomberg"
      },
      {
        kind: "ISIN",
        value: "US0378331005",
        isPrimary: false,
        validFrom: "2024-01-01",
        validTo: null,
        provider: "Refinitiv"
      }
    ],
    aliases: [
      {
        aliasId: "alias-dev-001",
        securityId: "sec-dev-001",
        aliasKind: "ProviderSymbol",
        aliasValue: "AAPL.OQ",
        provider: "Nasdaq",
        scope: "Collector",
        reason: "Market data source mapping",
        createdBy: "dashboard-dev",
        createdAt: "2026-04-28T18:15:00Z",
        validFrom: "2024-01-01",
        validTo: null,
        isEnabled: true
      }
    ]
  },
  "sec-dev-002": {
    securityId: "sec-dev-002",
    displayName: "PG&E Corporation",
    assetClass: "Equity",
    status: "Active",
    version: 2,
    effectiveFrom: "2023-07-01",
    effectiveTo: null,
    identifiers: [
      {
        kind: "Ticker",
        value: "PCG",
        isPrimary: true,
        validFrom: "2023-07-01",
        validTo: null,
        provider: "Interactive Brokers"
      },
      {
        kind: "CUSIP",
        value: "69331C108",
        isPrimary: false,
        validFrom: "2023-07-01",
        validTo: null,
        provider: "Bloomberg"
      }
    ],
    aliases: [
      {
        aliasId: "alias-dev-002",
        securityId: "sec-dev-002",
        aliasKind: "ProviderSymbol",
        aliasValue: "PCG.N",
        provider: "NYSE",
        scope: "Collector",
        reason: "Primary venue symbol",
        createdBy: "dashboard-dev",
        createdAt: "2026-04-28T18:15:00Z",
        validFrom: "2023-07-01",
        validTo: null,
        isEnabled: true
      }
    ]
  },
  "sec-dev-003": {
    securityId: "sec-dev-003",
    displayName: "Microsoft Corporation",
    assetClass: "Equity",
    status: "Active",
    version: 4,
    effectiveFrom: "2024-01-01",
    effectiveTo: null,
    identifiers: [
      {
        kind: "Ticker",
        value: "MSFT",
        isPrimary: true,
        validFrom: "2024-01-01",
        validTo: null,
        provider: "Nasdaq"
      }
    ],
    aliases: []
  },
  "sec-dev-004": {
    securityId: "sec-dev-004",
    displayName: "Meridian 5.875% 2031 Corporate Bond",
    assetClass: "Fixed Income",
    status: "Active",
    version: 1,
    effectiveFrom: "2025-12-15",
    effectiveTo: null,
    identifiers: [
      {
        kind: "CUSIP",
        value: "589999AB4",
        isPrimary: true,
        validFrom: "2025-12-15",
        validTo: null,
        provider: "Reference fixture"
      },
      {
        kind: "ISIN",
        value: "US589999AB47",
        isPrimary: false,
        validFrom: "2025-12-15",
        validTo: null,
        provider: "Reference fixture"
      }
    ],
    aliases: [
      {
        aliasId: "alias-dev-004",
        securityId: "sec-dev-004",
        aliasKind: "ProviderSymbol",
        aliasValue: "MERIDIAN 5.875 12/31",
        provider: "Bloomberg",
        scope: "Operations",
        reason: "Cash-flow/factor schedule fixture",
        createdBy: "dashboard-dev",
        createdAt: "2026-05-14T16:00:00Z",
        validFrom: "2025-12-15",
        validTo: null,
        isEnabled: true
      }
    ]
  }
};

const fixturePortfolioWorkspace: PortfolioWorkspaceResponse = {
  metrics: [
    { id: "portfolio-equity", label: "Portfolio equity", value: "$312,400", delta: "+1.2%", tone: "success" },
    { id: "portfolio-cash", label: "Cash", value: "$87,500", delta: "28% reserve", tone: "default" },
    { id: "portfolio-exposure", label: "Exposure", value: "$54,347", delta: "3 positions", tone: "default" },
    { id: "portfolio-sync", label: "Brokerage sync", value: "Stale", delta: "review", tone: "warning" }
  ],
  positions: fixtureTradingWorkspace.positions,
  risk: fixtureTradingWorkspace.risk,
  brokerage: fixtureTradingWorkspace.brokerage,
  runs: [
    {
      runId: "portfolio-run-dev-1",
      strategyName: "Mean Reversion FX",
      engine: "Meridian Native",
      mode: "paper",
      status: "Running",
      pnl: "+4.2%",
      sharpe: "1.41",
      dataset: "FX Majors",
      window: "90d",
      lastUpdated: "2m ago",
      notes: "Primary paper candidate reflected in the portfolio fixture.",
      promotionState: "ReviewRequired"
    }
  ],
  cashFlow: fixtureAccountingWorkspace.cashFlow
};

const fixturePortfolioFinancialRecordExplorerRows: FinancialRecordExplorerDto["rows"] = [
  {
    recordId: "portfolio:portfolio-run-dev-1:AAPL",
    recordType: "portfolio-position",
    label: "AAPL",
    source: "Development fixture portfolio",
    status: "Long",
    tone: "Success",
    cells: [
      { columnId: "symbol", displayValue: "AAPL", rawValue: "AAPL", tone: "Success", linkHref: "" },
      { columnId: "quantity", displayValue: "100", rawValue: "100", tone: "Default", linkHref: "" },
      { columnId: "averageCost", displayValue: "$176.60", rawValue: "176.6", tone: "Default", linkHref: "" },
      { columnId: "marketValue", displayValue: "$18,840.00", rawValue: "18840", tone: "Default", linkHref: "" },
      { columnId: "unrealizedPnl", displayValue: "+$1,180.00", rawValue: "1180", tone: "Success", linkHref: "" },
      { columnId: "realizedPnl", displayValue: "$0.00", rawValue: "0", tone: "Default", linkHref: "" }
    ],
    detail: {
      recordId: "portfolio:portfolio-run-dev-1:AAPL",
      recordType: "Portfolio position",
      title: "AAPL",
      subtitle: "Long - portfolio-run-dev-1",
      description: "Demo retained position record used by the no-host Portfolio Explorer preview.",
      tone: "Success",
      fields: [
        { label: "Quantity", value: "100", detail: "Retained fixture quantity.", tone: "Default" },
        { label: "Market value", value: "$18,840.00", detail: "Fixture market value.", tone: "Default" },
        { label: "Unrealized PnL", value: "+$1,180.00", detail: "Fixture unrealized gain.", tone: "Success" }
      ],
      proofActions: [
        {
          actionId: "open-source",
          label: "Open source record",
          description: "Open the no-host portfolio fixture source.",
          href: WORKSTATION_API_ENDPOINTS.portfolio,
          isEnabled: true,
          disabledReason: "",
          tone: "Info"
        }
      ],
      usedIn: [
        {
          relationshipId: "portfolio-run-dev-1",
          label: "Portfolio run",
          description: "Used by the fixture portfolio run projection.",
          href: WORKSTATION_API_ENDPOINTS.portfolio,
          tone: "Info"
        }
      ],
      impacts: [
        {
          relationshipId: "portfolio-market-value",
          label: "Portfolio market value",
          description: "Contributes to aggregate market value.",
          href: WORKSTATION_API_ENDPOINTS.portfolio,
          tone: "Success"
        }
      ],
      fullRecordHref: `${WORKSTATION_API_ENDPOINTS.portfolio}?recordId=portfolio%3Aportfolio-run-dev-1%3AAAPL`
    }
  },
  {
    recordId: "portfolio:portfolio-run-dev-1:MSFT",
    recordType: "portfolio-position",
    label: "MSFT",
    source: "Development fixture portfolio",
    status: "Long",
    tone: "Success",
    cells: [
      { columnId: "symbol", displayValue: "MSFT", rawValue: "MSFT", tone: "Success", linkHref: "" },
      { columnId: "quantity", displayValue: "16", rawValue: "16", tone: "Default", linkHref: "" },
      { columnId: "averageCost", displayValue: "$418.00", rawValue: "418", tone: "Default", linkHref: "" },
      { columnId: "marketValue", displayValue: "$6,747.20", rawValue: "6747.2", tone: "Default", linkHref: "" },
      { columnId: "unrealizedPnl", displayValue: "+$59.20", rawValue: "59.2", tone: "Success", linkHref: "" },
      { columnId: "realizedPnl", displayValue: "+$320.75", rawValue: "320.75", tone: "Success", linkHref: "" }
    ],
    detail: {
      recordId: "portfolio:portfolio-run-dev-1:MSFT",
      recordType: "Portfolio position",
      title: "MSFT",
      subtitle: "Long - portfolio-run-dev-1",
      description: "Demo retained position record used by the no-host Portfolio Explorer preview.",
      tone: "Success",
      fields: [
        { label: "Quantity", value: "16", detail: "Retained fixture quantity.", tone: "Default" },
        { label: "Market value", value: "$6,747.20", detail: "Fixture market value.", tone: "Default" },
        { label: "Unrealized PnL", value: "+$59.20", detail: "Fixture unrealized gain.", tone: "Success" }
      ],
      proofActions: [
        {
          actionId: "open-source",
          label: "Open source record",
          description: "Open the no-host portfolio fixture source.",
          href: WORKSTATION_API_ENDPOINTS.portfolio,
          isEnabled: true,
          disabledReason: "",
          tone: "Info"
        }
      ],
      usedIn: [
        {
          relationshipId: "portfolio-run-dev-1",
          label: "Portfolio run",
          description: "Used by the fixture portfolio run projection.",
          href: WORKSTATION_API_ENDPOINTS.portfolio,
          tone: "Info"
        }
      ],
      impacts: [
        {
          relationshipId: "portfolio-market-value",
          label: "Portfolio market value",
          description: "Contributes to aggregate market value.",
          href: WORKSTATION_API_ENDPOINTS.portfolio,
          tone: "Success"
        }
      ],
      fullRecordHref: `${WORKSTATION_API_ENDPOINTS.portfolio}?recordId=portfolio%3Aportfolio-run-dev-1%3AMSFT`
    }
  }
];

const fixturePortfolioFinancialRecordExplorer: FinancialRecordExplorerDto = {
  explorerId: "portfolio",
  title: "Portfolio Explorer",
  description: "Explore retained account and aggregate position records from no-host development fixtures.",
  sourceState: "Demo data: source-backed portfolio projection from development fixture run portfolio-run-dev-1.",
  isBlocked: false,
  blockedReason: "",
  scopeItems: [
    { label: "Workstream", value: "Portfolio", tone: "Info" },
    { label: "Source", value: "Development fixture portfolio", tone: "Default" },
    { label: "Run", value: "portfolio-run-dev-1", tone: "Info" },
    { label: "As of", value: fixturePaperSessionPortfolio.asOf, tone: "Default" }
  ],
  savedViews: [
    {
      viewId: "system-portfolio-dev-default",
      label: "Open positions + run evidence",
      description: "Default no-host portfolio explorer fixture view.",
      isSystem: true,
      isActive: true,
      filters: [],
      searchText: ""
    }
  ],
  summaryItems: [
    {
      label: "Positions",
      value: `${fixturePortfolioFinancialRecordExplorerRows.length}`,
      detail: "Retained fixture position rows.",
      tone: "Success"
    },
    {
      label: "Market value",
      value: "$25,587",
      detail: "Aggregate market value from fixture paper-session positions.",
      tone: "Default"
    },
    {
      label: "Unrealized PnL",
      value: "+$1,239",
      detail: "Fixture unrealized gain retained for portfolio review.",
      tone: "Success"
    }
  ],
  filters: [
    { filterId: "run", label: "Run", value: "portfolio-run-dev-1", operator: "equals", tone: "Info" },
    { filterId: "source", label: "Source", value: "development fixture", operator: "equals", tone: "Default" }
  ],
  columns: [
    { columnId: "symbol", header: "Symbol", cellKind: "text", width: 120, isRightAligned: false },
    { columnId: "quantity", header: "Quantity", cellKind: "number", width: 120, isRightAligned: true },
    { columnId: "averageCost", header: "Average cost", cellKind: "currency", width: 140, isRightAligned: true },
    { columnId: "marketValue", header: "Market value", cellKind: "currency", width: 150, isRightAligned: true },
    { columnId: "unrealizedPnl", header: "Unrealized PnL", cellKind: "currency", width: 150, isRightAligned: true },
    { columnId: "realizedPnl", header: "Realized PnL", cellKind: "currency", width: 140, isRightAligned: true }
  ],
  rows: fixturePortfolioFinancialRecordExplorerRows,
  selectedRecord: fixturePortfolioFinancialRecordExplorerRows[0]!.detail,
  proofActions: [
    {
      actionId: "open-evidence",
      label: "Open evidence packet",
      description: "Open retained evidence for the fixture portfolio run.",
      href: "/reporting/evidence?subjectKind=strategy-run&subjectId=portfolio-run-dev-1",
      isEnabled: true,
      disabledReason: "",
      tone: "Info"
    }
  ],
  recordGraph: {
    nodes: [
      {
        nodeId: "portfolio-run-dev-1",
        label: "portfolio-run-dev-1",
        nodeType: "run",
        tone: "Info",
        href: WORKSTATION_API_ENDPOINTS.portfolio
      },
      {
        nodeId: "portfolio:portfolio-run-dev-1:AAPL",
        label: "AAPL",
        nodeType: "portfolio-position",
        tone: "Success",
        href: `${WORKSTATION_API_ENDPOINTS.portfolio}?recordId=portfolio%3Aportfolio-run-dev-1%3AAAPL`
      },
      {
        nodeId: "portfolio:portfolio-run-dev-1:MSFT",
        label: "MSFT",
        nodeType: "portfolio-position",
        tone: "Success",
        href: `${WORKSTATION_API_ENDPOINTS.portfolio}?recordId=portfolio%3Aportfolio-run-dev-1%3AMSFT`
      }
    ],
    edges: [
      {
        sourceNodeId: "portfolio-run-dev-1",
        targetNodeId: "portfolio:portfolio-run-dev-1:AAPL",
        label: "projects",
        tone: "Info"
      },
      {
        sourceNodeId: "portfolio-run-dev-1",
        targetNodeId: "portfolio:portfolio-run-dev-1:MSFT",
        label: "projects",
        tone: "Info"
      }
    ]
  }
};

const fixtureAccountingConfiguration: AccountingConfigurationWorkspace = {
  fundProfileId: "default-fund",
  ledgerBookId: "ledger-book-default",
  status: "Active",
  configurationVersion: "fixture-accounting-config-v1",
  updatedAtUtc: "2026-05-03T20:00:00Z",
  ledgerBooks: [
    {
      ledgerBookId: "ledger-book-default",
      fundProfileId: "default-fund",
      fundStructureNodeId: "fund-node-default",
      fundStructureNodeKind: "Fund",
      displayName: "Default fund primary book",
      baseCurrency: "USD",
      createdAt: "2026-05-01T14:00:00Z",
      updatedAt: "2026-05-03T20:00:00Z",
      description: "No-host fixture ledger book for Accounting configuration preview.",
      accountingBasis: "Primary",
      accountingPolicyId: "policy-default-primary",
      accountingPolicyVersion: "2026.05"
    }
  ],
  chartOfAccounts: [
    {
      nodeId: "coa-cash",
      path: "1000",
      accountName: "Cash",
      accountType: "Asset",
      parentPath: null,
      symbol: null,
      financialAccountId: "acct-cash",
      isArchived: false
    },
    {
      nodeId: "coa-investments",
      path: "1200",
      accountName: "Investments",
      accountType: "Asset",
      parentPath: null,
      symbol: "AAPL",
      financialAccountId: "acct-investments",
      isArchived: false
    },
    {
      nodeId: "coa-pnl",
      path: "4000",
      accountName: "Realized P&L",
      accountType: "Income",
      parentPath: null,
      symbol: null,
      financialAccountId: "acct-realized-pnl",
      isArchived: false
    }
  ],
  journalTemplates: [
    {
      templateId: "journal-template-paper-fill",
      displayName: "Paper fill settlement",
      description: "Fixture posting template for paper fill cash and investment movement.",
      lines: [
        {
          lineId: "paper-fill-debit-investments",
          accountPath: "1200",
          side: "Debit",
          amount: 100,
          currency: "USD",
          description: "Increase investment position."
        },
        {
          lineId: "paper-fill-credit-cash",
          accountPath: "1000",
          side: "Credit",
          amount: 100,
          currency: "USD",
          description: "Reduce cash for settled buy."
        }
      ],
      isArchived: false,
      version: "1"
    }
  ],
  postingRules: [
    {
      ruleId: "posting-rule-paper-fill",
      displayName: "Paper fill posting",
      sourceEventType: "PaperFill",
      templateId: "journal-template-paper-fill",
      ruleVersion: "1",
      isArchived: false,
      description: "Routes paper execution fills into the default fixture ledger book.",
      effectiveFrom: "2026-01-01",
      effectiveTo: null,
      priority: 50,
      scope: {
        fundId: "default-fund",
        entityId: "fund-entity-main",
        strategyId: "paper-index-mean-reversion",
        instrumentId: "2c0f364f-6020-4675-a7e2-27448950c5af",
        counterpartyId: "paper-broker",
        externalGlDimensions: {
          Class: "DefaultFund",
          Location: "Main"
        }
      },
      conditions: [
        {
          conditionId: "condition-event-kind",
          field: "sourceEventType",
          operator: "Equals",
          value: "PaperFill",
          secondValue: null,
          isRequired: true,
          description: "Only execution fill events enter this rule."
        },
        {
          conditionId: "condition-notional-threshold",
          field: "eventAmount",
          operator: "AmountGreaterThanOrEqual",
          value: "1",
          secondValue: null,
          isRequired: true,
          description: "Zero-value fills remain blocked before journal generation."
        },
        {
          conditionId: "condition-counterparty",
          field: "counterpartyId",
          operator: "Equals",
          value: "paper-broker",
          secondValue: null,
          isRequired: false,
          description: "Counterparty scope keeps paper fills out of live broker posting."
        }
      ],
      formulas: [
        {
          formulaId: "formula-source-notional",
          kind: "SourceAmount",
          value: 100,
          currency: "USD",
          description: "Use the event notional supplied by the fill."
        },
        {
          formulaId: "formula-fee-allocation",
          kind: "PercentageOfSourceAmount",
          value: 0.0025,
          currency: "USD",
          description: "Reserve an audit-visible fee allocation calculation."
        }
      ],
      allocations: [
        {
          allocationRuleId: "allocation-default-strategy",
          basis: "StrategyWeight",
          weight: 1,
          formulaId: "formula-source-notional",
          targetDimensions: {
            fundId: "default-fund",
            strategyId: "paper-index-mean-reversion",
            externalGlDimensions: {
              Class: "DefaultFund"
            }
          },
          description: "Allocate the full paper fill to the strategy sleeve."
        }
      ],
      generatedPostings: [
        {
          lineId: "generated-investment-debit",
          accountPath: "1200",
          side: "Debit",
          amountFormulaId: "formula-source-notional",
          amount: 100,
          currency: "USD",
          dimensions: {
            fundId: "default-fund",
            strategyId: "paper-index-mean-reversion",
            instrumentId: "2c0f364f-6020-4675-a7e2-27448950c5af",
            externalGlDimensions: {
              Class: "DefaultFund"
            }
          },
          description: "Generated debit to investment asset."
        },
        {
          lineId: "generated-cash-credit",
          accountPath: "1000",
          side: "Credit",
          amountFormulaId: "formula-source-notional",
          amount: 100,
          currency: "USD",
          dimensions: {
            fundId: "default-fund",
            counterpartyId: "paper-broker",
            externalGlDimensions: {
              Location: "Main"
            }
          },
          description: "Generated credit to operating cash."
        }
      ],
      versions: [
        {
          version: "1",
          createdAtUtc: "2026-05-03T19:30:00Z",
          createdBy: "fixture-controller",
          changeSummary: "Initial paper-fill posting rule with generated balanced lines.",
          promotionApproval: {
            approvalId: "rule-promotion-paper-fill-v1",
            requestedBy: "fixture-controller",
            requestedAtUtc: "2026-05-03T19:35:00Z",
            approvalState: "Approved",
            approvedBy: "fixture-reviewer",
            approvedAtUtc: "2026-05-03T19:50:00Z",
            notes: "Approved for no-host accounting fixture.",
            evidenceLinks: ["evidence:accounting-rule:paper-fill:v1"]
          },
          evidenceLinks: ["evidence:accounting-rule:paper-fill:v1"]
        }
      ],
      promotionApproval: {
        approvalId: "rule-promotion-paper-fill-v1",
        requestedBy: "fixture-controller",
        requestedAtUtc: "2026-05-03T19:35:00Z",
        approvalState: "Approved",
        approvedBy: "fixture-reviewer",
        approvedAtUtc: "2026-05-03T19:50:00Z",
        notes: "Approved for no-host accounting fixture.",
        evidenceLinks: ["evidence:accounting-rule:paper-fill:v1"]
      },
      requiresPromotionApproval: true
    }
  ],
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
    rules: [
      {
        ruleId: "posting-rule-paper-fill",
        displayName: "Paper fill posting",
        sourceEventType: "PaperFill",
        ruleVersion: "1",
        priority: 50,
        effectiveFrom: "2026-01-01",
        effectiveTo: null,
        templateId: "journal-template-paper-fill",
        isArchived: false,
        usesGeneratedPostings: true,
        conditionCount: 3,
        conditionGroupCount: 0,
        formulaCount: 2,
        allocationCount: 1,
        generatedPostingLineCount: 2,
        versionCount: 1,
        savedTestCaseCount: 1,
        savedTestEvidenceLinkCount: 1,
        requiresPromotionApproval: true,
        isPromotionApproved: true,
        promotionApprovalState: "Approved",
        promotionApprovalId: "rule-promotion-paper-fill-v1",
        criticalIssueCount: 0,
        warningIssueCount: 0,
        canDryRun: true,
        canRequestPromotion: false,
        canActivate: true
      }
    ],
    promotionQueue: []
  },
  auditTrail: [
    {
      auditEventId: "accounting-config-fixture-audit-1",
      recordedAtUtc: "2026-05-03T20:00:00Z",
      actor: "fixture-controller",
      action: "Activate accounting configuration",
      fundProfileId: "default-fund",
      ledgerBookId: "ledger-book-default",
      correlationId: "fixture-accounting-config",
      beforeHash: "fixture-before-accounting-config",
      afterHash: "fixture-after-accounting-config",
      validationIssues: [],
      evidenceLinks: ["/reporting/evidence?subjectKind=accounting-record&subjectId=accounting-record-2026-05"],
      companyId: null,
      reportGroupPrincipalIds: ["fund-controller"]
    }
  ]
};

const fixtureAccountingRuleDryRun: RuleDryRunResult = {
  fundProfileId: fixtureAccountingConfiguration.fundProfileId,
  ledgerBookId: fixtureAccountingConfiguration.ledgerBookId,
  sourceEventType: "PaperFill",
  effectiveDate: "2026-01-01",
  eventAmount: 100,
  currency: "USD",
  isPostingBalanced: true,
  selectedRuleId: "posting-rule-paper-fill",
  ruleMatches: [
    {
      ruleId: "posting-rule-paper-fill",
      displayName: "Paper fill posting",
      ruleVersion: "1",
      priority: 50,
      isMatched: true,
      explanations: [
        "Source event type matched PaperFill.",
        "Effective date falls inside the open-ended rule range.",
        "Generated debit and credit lines balance before posting."
      ],
      validationIssues: []
    }
  ],
  generatedLines: [
    {
      accountPath: "1200",
      accountName: "Investments",
      side: "Debit",
      amount: 100,
      currency: "USD",
      description: "Generated debit to investment asset."
    },
    {
      accountPath: "1000",
      accountName: "Cash",
      side: "Credit",
      amount: 100,
      currency: "USD",
      description: "Generated credit to operating cash."
    }
  ],
  generatedPostingLines: fixtureAccountingConfiguration.postingRules[0].generatedPostings ?? [],
  validationIssues: []
};

const fixtureStatementRuns: StatementRunSummary[] = [
  {
    runId: "stmt-run-42",
    importId: "statement-import-42",
    startedAtUtc: "2026-05-03T19:45:00Z",
    completedAtUtc: "2026-05-03T19:48:00Z",
    positionMatches: 3,
    cashMatches: 2,
    transactionMatches: 7,
    openExceptionCount: 1,
    brokerCustodian: "Interactive Brokers",
    account: "DU1009034",
    period: "2026-05-03",
    status: "ReviewRequired",
    validationIssueCount: 1,
    matchCount: 12,
    breakCount: 1,
    caseCount: 1,
    importedAtUtc: "2026-05-03T19:44:00Z"
  }
];

// Mirrors the registered connector descriptors in
// Meridian.FinancialOperations/Reconciliation/Connectors (csv-mapped, ofx, ib-flex,
// alpaca-activity) so the statement-import screen demos the real connector catalog.
const fixtureStatementConnectors: StatementConnectorDescriptor[] = [
  {
    connectorId: "csv-mapped",
    displayName: "Custodian/Broker CSV (mapping profile)",
    fileExtensions: [".csv", ".txt"],
    supportsFileImport: true,
    supportsRemoteFetch: false,
    requiresMappingProfile: true,
    defaultProfileId: "canonical-csv-v1"
  },
  {
    connectorId: "ofx",
    displayName: "OFX / QFX statement",
    fileExtensions: [".ofx", ".qfx"],
    supportsFileImport: true,
    supportsRemoteFetch: false,
    requiresMappingProfile: true,
    defaultProfileId: "ofx-bank-v1"
  },
  {
    connectorId: "ib-flex",
    displayName: "Interactive Brokers Flex Report (XML)",
    fileExtensions: [".xml"],
    supportsFileImport: true,
    supportsRemoteFetch: false,
    requiresMappingProfile: false,
    defaultProfileId: "ib-flex-v1"
  },
  {
    connectorId: "alpaca-activity",
    displayName: "Alpaca account activity",
    fileExtensions: [".json"],
    supportsFileImport: true,
    supportsRemoteFetch: true,
    requiresMappingProfile: false,
    defaultProfileId: "alpaca-activity-v1"
  }
];

const fixtureStatementMappingProfiles: StatementMappingProfile[] = [
  {
    schemaVersion: 1,
    profileId: "canonical-csv-v1",
    displayName: "Canonical CSV (v1)",
    format: "csv",
    csv: { delimiter: ",", quote: "\"", hasHeader: true },
    culture: null,
    dateFormats: null,
    fields: [
      { canonicalField: "Account", sourceColumn: "Account", aliases: null, required: true },
      { canonicalField: "SecurityIdentifier", sourceColumn: "Symbol", aliases: ["Ticker"], required: false },
      { canonicalField: "ActivityType", sourceColumn: "ActivityType", aliases: ["Type"], required: true },
      { canonicalField: "Quantity", sourceColumn: "Quantity", aliases: null, required: false },
      { canonicalField: "Price", sourceColumn: "Price", aliases: null, required: false },
      { canonicalField: "CashAmount", sourceColumn: "Amount", aliases: ["CashAmount"], required: false },
      { canonicalField: "TradeDate", sourceColumn: "TradeDate", aliases: ["Date"], required: true }
    ],
    activityCodes: [
      { sourceCode: "BUY", canonicalActivityType: "Buy" },
      { sourceCode: "SELL", canonicalActivityType: "Sell" },
      { sourceCode: "DIV", canonicalActivityType: "Dividend" }
    ],
    lastAcceptedFingerprint: null,
    isBuiltIn: true,
    notes: "Demo fixture mirroring the built-in canonical CSV mapping profile."
  }
];

function buildFixtureProviderMappingRequirements(providerId: string): AccountingSystemProviderMappingRequirement[] {
  const normalized = providerId.toLowerCase();
  const accountEvidenceKind = normalized.includes("xero")
    ? "XeroAccount"
    : normalized.includes("netsuite")
      ? "NetSuiteAccount"
      : "QuickBooksAccount";
  const journalEvidenceKind = normalized.includes("xero")
    ? "XeroManualJournal"
    : normalized.includes("netsuite")
      ? "NetSuiteJournalEntry"
      : "QuickBooksJournalEntry";
  const trialBalanceEvidenceKind = normalized.includes("xero")
    ? "XeroTrialBalance"
    : normalized.includes("netsuite")
      ? "NetSuiteTrialBalance"
      : "QuickBooksTrialBalance";
  const dimensionVocabulary = normalized.includes("xero")
    ? "Xero tracking categories"
    : normalized.includes("netsuite")
      ? "NetSuite segments, departments, classes, and subsidiaries"
      : "QuickBooks classes, locations, and departments";

  return [
    {
      requirementId: `${normalized}:account-mapping`,
      label: "Account mapping",
      requiredEvidenceKind: accountEvidenceKind,
      requiredAction: "Map every reconciled Meridian GL account to a certified external GL account before guarded export review.",
      requiredForGuardedExport: true
    },
    {
      requirementId: `${normalized}:journal-lineage`,
      label: "Journal lineage",
      requiredEvidenceKind: journalEvidenceKind,
      requiredAction: "Retain provider journal evidence and Meridian ledger-entry lineage for the exact fund, book, and export period.",
      requiredForGuardedExport: true
    },
    {
      requirementId: `${normalized}:trial-balance-tie-out`,
      label: "Trial-balance tie-out",
      requiredEvidenceKind: trialBalanceEvidenceKind,
      requiredAction: "Reconcile provider trial-balance rows against Meridian-owned ledger totals before certification.",
      requiredForGuardedExport: true
    },
    {
      requirementId: `${normalized}:dimension-mapping`,
      label: "Dimension mapping",
      requiredEvidenceKind: `${accountEvidenceKind}:Dimensions`,
      requiredAction: `Certify canonical Meridian dimensions against ${dimensionVocabulary} before generated export lines can be review-ready.`,
      requiredForGuardedExport: true
    }
  ];
}

const fixtureAccountingSystemProviders: AccountingSystemProvider[] = [
  {
    providerId: "quickbooks-fixture",
    displayName: "QuickBooks Fixture",
    state: "Available",
    requiresCredentials: false,
    supportsChartOfAccounts: true,
    supportsJournalEntries: true,
    supportsTrialBalance: true,
    supportsPosting: false,
    statusLabel: "Ready for fixture import",
    statusDetail: "Read-only external GL import and reconciliation compare provider evidence against Meridian-owned ledger truth.",
    evidenceKinds: ["QuickBooksAccount", "QuickBooksJournalEntry", "QuickBooksTrialBalance"],
    mappingRequirements: buildFixtureProviderMappingRequirements("quickbooks-fixture")
  },
  {
    providerId: "quickbooks",
    displayName: "QuickBooks Online",
    state: "Disabled",
    requiresCredentials: true,
    supportsChartOfAccounts: true,
    supportsJournalEntries: true,
    supportsTrialBalance: true,
    supportsPosting: false,
    statusLabel: "Local config required",
    statusDetail: "Add QuickBooks client ID, client secret, refresh token, and company realm ID before importing read-only GL evidence.",
    evidenceKinds: ["QuickBooksAccount", "QuickBooksJournalEntry", "QuickBooksTrialBalance"],
    mappingRequirements: buildFixtureProviderMappingRequirements("quickbooks"),
    connection: {
      providerId: "quickbooks",
      environment: "sandbox",
      companyId: null,
      companyName: null,
      hasLocalConfig: false,
      hasRefreshToken: false,
      lastConnectedAtUtc: null,
      statusLabel: "Local config required",
      statusDetail: "Add QuickBooks client ID, client secret, refresh token, and company realm ID before importing read-only GL evidence.",
      missingFields: ["ClientId", "ClientSecret", "RefreshToken", "RealmId"]
    }
  },
  {
    providerId: "xero",
    displayName: "Xero",
    state: "Planned",
    requiresCredentials: true,
    supportsChartOfAccounts: true,
    supportsJournalEntries: true,
    supportsTrialBalance: true,
    supportsPosting: false,
    statusLabel: "Import adapter not registered",
    statusDetail: "Xero chart, journal, and trial-balance import mapping is planned; live posting remains disabled until a separately approved adapter exists.",
    evidenceKinds: ["XeroAccount", "XeroManualJournal", "XeroTrialBalance"],
    mappingRequirements: buildFixtureProviderMappingRequirements("xero")
  },
  {
    providerId: "netsuite",
    displayName: "NetSuite",
    state: "Planned",
    requiresCredentials: true,
    supportsChartOfAccounts: true,
    supportsJournalEntries: true,
    supportsTrialBalance: true,
    supportsPosting: false,
    statusLabel: "Import adapter not registered",
    statusDetail: "NetSuite chart, journal, and trial-balance import mapping is planned; live posting remains disabled until a separately approved adapter exists.",
    evidenceKinds: ["NetSuiteAccount", "NetSuiteJournalEntry", "NetSuiteTrialBalance"],
    mappingRequirements: buildFixtureProviderMappingRequirements("netsuite")
  }
];

const fixtureAccountingSystemImport: AccountingSystemImportDetail = {
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
    chartAccountCount: 4,
    journalEntryCount: 2,
    trialBalanceLineCount: 4,
    evidenceReferences: ["quickbooks-fixture:chart-of-accounts", "quickbooks-fixture:journal", "quickbooks-fixture:trial-balance"],
    warnings: ["Meridian remains the source of all ledger truth; external posting/export is disabled for the contract-first evidence lane."]
  },
  chartAccounts: [
    { externalAccountId: "qbo-1000", accountCode: "Assets:Cash:Operating", displayName: "Operating Cash", accountType: "Asset", currency: "USD", isActive: true, parentExternalAccountId: null, evidenceRef: "quickbooks-fixture:account:qbo-1000" },
    { externalAccountId: "qbo-1500", accountCode: "Assets:Investments:Public", displayName: "Public Investments", accountType: "Asset", currency: "USD", isActive: true, parentExternalAccountId: null, evidenceRef: "quickbooks-fixture:account:qbo-1500" },
    { externalAccountId: "qbo-4000", accountCode: "Income:Investment", displayName: "Investment Income", accountType: "Income", currency: "USD", isActive: true, parentExternalAccountId: null, evidenceRef: "quickbooks-fixture:account:qbo-4000" },
    { externalAccountId: "qbo-6100", accountCode: "Expenses:Trading", displayName: "Trading Expenses", accountType: "Expense", currency: "USD", isActive: true, parentExternalAccountId: null, evidenceRef: "quickbooks-fixture:account:qbo-6100" }
  ],
  journalEntries: [
    {
      externalJournalEntryId: "qbo-je-100",
      accountingDate: "2026-01-05",
      description: "Fixture capital contribution",
      currency: "USD",
      totalDebits: 250000,
      totalCredits: 250000,
      evidenceRef: "quickbooks-fixture:journal:qbo-je-100",
      lines: [
        { externalLineId: "qbo-je-100-1", externalAccountId: "qbo-1000", accountCode: "Assets:Cash:Operating", description: "Capital contribution received", debit: 250000, credit: 0, currency: "USD", evidenceRef: "quickbooks-fixture:journal:qbo-je-100:1" },
        { externalLineId: "qbo-je-100-2", externalAccountId: "qbo-4000", accountCode: "Income:Investment", description: "Capital contribution offset", debit: 0, credit: 250000, currency: "USD", evidenceRef: "quickbooks-fixture:journal:qbo-je-100:2" }
      ]
    }
  ],
  trialBalance: [
    { externalAccountId: "qbo-1000", accountCode: "Assets:Cash:Operating", accountName: "Operating Cash", accountType: "Asset", debit: 248750, credit: 0, currency: "USD", asOfDate: "2026-01-31", evidenceRef: "quickbooks-fixture:trial-balance:qbo-1000" },
    { externalAccountId: "qbo-4000", accountCode: "Income:Investment", accountName: "Investment Income", accountType: "Income", debit: 0, credit: 250000, currency: "USD", asOfDate: "2026-01-31", evidenceRef: "quickbooks-fixture:trial-balance:qbo-4000" },
    { externalAccountId: "qbo-6100", accountCode: "Expenses:Trading", accountName: "Trading Expenses", accountType: "Expense", debit: 1250, credit: 0, currency: "USD", asOfDate: "2026-01-31", evidenceRef: "quickbooks-fixture:trial-balance:qbo-6100" }
  ]
};

const fixtureAccountingSystemReconciliation: AccountingSystemReconciliationSummary = {
  reconciliationId: "gl-recon-qbo-fixture-20260131",
  importId: fixtureAccountingSystemImport.summary.importId,
  providerId: "quickbooks-fixture",
  fundProfileId: "default-fund",
  periodStart: "2026-01-01",
  periodEnd: "2026-01-31",
  generatedAtUtc: "2026-02-01T00:05:00Z",
  matchedCount: 0,
  breakCount: 3,
  totalExternalDebits: 250000,
  totalExternalCredits: 250000,
  totalMeridianDebits: 0,
  totalMeridianCredits: 0,
  postingEnabled: false,
  postingDisabledReason: "Meridian is the source of all ledger truth; external GL posting/export is disabled until an approved adapter publishes Meridian-owned ledger entries.",
  evidenceReferences: fixtureAccountingSystemImport.summary.evidenceReferences,
  evidencePackages: [
    {
      packageId: "gl-external-evidence:qbo-fixture-20260131",
      label: "External GL import evidence",
      status: "Ready",
      evidenceReferenceCount: fixtureAccountingSystemImport.summary.evidenceReferences.length,
      evidenceReferences: fixtureAccountingSystemImport.summary.evidenceReferences,
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
      status: "Missing",
      evidenceReferenceCount: fixtureAccountingSystemImport.summary.evidenceReferences.length,
      evidenceReferences: fixtureAccountingSystemImport.summary.evidenceReferences,
      requiredActions: ["Load Meridian ledger journal evidence.", "Resolve GL reconciliation breaks before approving close evidence."]
    }
  ],
  rows: fixtureAccountingSystemImport.trialBalance.map((row) => ({
    rowId: `gl-recon-${row.externalAccountId}`,
    accountCode: row.accountCode,
    accountName: row.accountName,
    currency: row.currency,
    status: "MissingMeridian",
    externalDebit: row.debit,
    externalCredit: row.credit,
    meridianDebit: 0,
    meridianCredit: 0,
    variance: row.debit - row.credit,
    detail: "External GL evidence is absent from Meridian-owned ledger truth and requires review before close.",
    evidenceRef: row.evidenceRef,
    externalEvidenceReferences: row.evidenceRef ? [row.evidenceRef] : [],
    meridianEvidenceReferences: [],
    evidenceReferences: row.evidenceRef ? [row.evidenceRef] : []
  }))
};

const fixtureAccountingSystemMappingProfiles: ExternalGlMappingProfile[] = [
  {
    profileId: "qbo-default-fund-certified",
    providerId: "quickbooks-fixture",
    displayName: "Default fund QBO mapping",
    updatedAtUtc: "2026-02-01T00:08:00Z",
    certificationState: "Certified",
    accountMappings: {
      "Assets:Cash:Operating": "qbo-1000",
      "Income:Investment": "qbo-4000",
      "Expenses:Trading": "qbo-6100"
    },
    dimensionMappings: [
      {
        profileId: "qbo-default-fund-dimensions",
        displayName: "Default fund dimensions",
        providerId: "quickbooks-fixture",
        certificationState: "Certified",
        meridianDimensions: {
          fundId: "default-fund",
          entityId: "fund-entity-main",
          externalGlDimensions: {}
        },
        externalDimensions: {
          fundId: "Class:DefaultFund",
          entityId: "Location:Main",
          externalGlDimensions: {
            Class: "DefaultFund",
            Location: "Main"
          }
        },
        validationIssues: []
      }
    ]
  }
];

const fixtureAccountingSystemExportPackage: ExternalGlExportPackage = {
  exportPackageId: "external-gl-export-quickbooks-fixture-default-fund-20260131",
  providerId: "quickbooks-fixture",
  fundProfileId: "default-fund",
  ledgerBookId: null,
  periodStart: "2026-01-01",
  periodEnd: "2026-01-31",
  createdAtUtc: "2026-02-01T00:10:00Z",
  createdBy: "fixture-operator",
  postingEnabled: false,
  postingDisabledReason: "Guarded export package only; live external GL posting remains disabled until a separately approved adapter and release gate publish Meridian-owned ledger entries.",
  journalEntryIds: [],
  evidenceLinks: [
    "external-gl-mapping-profile:qbo-default-fund-certified",
    "external-gl-reconciliation:gl-recon-qbo-fixture-20260131"
  ],
  certification: {
    certificationId: "external-gl-export-cert-quickbooks-fixture-default-fund-20260201001000",
    state: "Draft",
    actor: "fixture-operator",
    recordedAtUtc: "2026-02-01T00:10:00Z",
    summary: "Export package is retained as a guarded review artifact and cannot be certified until validation issues are resolved.",
    evidenceLinks: [
      "external-gl-mapping-profile:qbo-default-fund-certified",
      "external-gl-reconciliation:gl-recon-qbo-fixture-20260131"
    ]
  },
  validationIssues: [
    {
      code: "UnresolvedExternalGlBreaks",
      severity: "Critical",
      message: "3 external GL reconciliation break(s) remain unresolved.",
      targetId: "gl-recon-qbo-fixture-20260131",
      suggestedAction: "Resolve or approve GL tie-out breaks with retained evidence before export certification."
    },
    {
      code: "LiveExternalPostingDisabled",
      severity: "Info",
      message: "Live external GL posting is disabled; this operation only creates a guarded export artifact.",
      targetId: "quickbooks-fixture",
      suggestedAction: "Review, approve, and reconcile the export artifact outside Meridian until a later live-posting adapter is explicitly approved."
    }
  ]
};

const fixtureAccountingProductionReadiness: AccountingProductionReadiness = {
  generatedAtUtc: "2026-02-01T00:15:00Z",
  fundProfileId: "default-fund",
  ledgerBookId: null,
  status: "ReviewRequired",
  score: 70,
  criticalIssueCount: 0,
  warningIssueCount: 4,
  externalGlProviderCount: fixtureAccountingSystemProviders.length,
  certifiedExternalGlMappingProfileCount: fixtureAccountingSystemMappingProfiles.filter((profile) => profile.certificationState === "Certified").length,
  externalGlLivePostingEnabled: false,
  migrationRunArtifacts: [
    {
      runId: "migration-run-ledger-book-scope-default-fund",
      kind: "LedgerBookScope",
      status: "Certified",
      startedAtUtc: "2026-02-01T00:00:00Z",
      completedAtUtc: "2026-02-01T00:05:00Z",
      actor: "controller",
      migratedRecordCount: 24,
      issueCount: 0,
      evidenceReferences: ["fixture:migration:ledger-book-run"],
      fundProfileId: "default-fund",
      ledgerBookId: null,
      summary: "Fixture ledger-book scope migration retained and certified."
    },
    {
      runId: "migration-run-dimensions-default-fund",
      kind: "DimensionalBackfill",
      status: "Certified",
      startedAtUtc: "2026-02-01T00:05:00Z",
      completedAtUtc: "2026-02-01T00:12:00Z",
      actor: "controller",
      migratedRecordCount: 48,
      issueCount: 0,
      evidenceReferences: ["fixture:migration:dimensions-run"],
      fundProfileId: "default-fund",
      ledgerBookId: null,
      summary: "Fixture dimensional backfill retained and certified."
    }
  ],
  migrationRolloutPlan: [
    {
      kind: "LedgerBookScope",
      code: "ledger-book-scope",
      label: "Ledger-book migration scope",
      certified: true,
      status: "Ready",
      scopeLabel: "tenant fixture-tenant | company fixture-company | fund default-fund | book missing",
      requiredAction: "Ledger-book scoping and historical fund-level compatibility paths are retained.",
      latestRunId: "migration-run-ledger-book-scope-default-fund",
      latestRunStatus: "Certified",
      migratedRecordCount: 24,
      issueCount: 0,
      evidenceReferences: ["fixture:migration:ledger-book-run"],
      blockingIssueCodes: []
    },
    {
      kind: "HistoricalJournalBackfill",
      code: "historical-journal-backfill",
      label: "Historical journal backfill",
      certified: false,
      status: "Blocked",
      scopeLabel: "tenant fixture-tenant | company fixture-company | fund default-fund | book missing",
      requiredAction: "Run and retain historical journal backfill evidence before certifying ledger-book-native accounting.",
      latestRunId: null,
      latestRunStatus: null,
      migratedRecordCount: 0,
      issueCount: 0,
      evidenceReferences: [],
      blockingIssueCodes: ["migration.historical-journal-backfill-not-certified"]
    }
  ],
  ledgerBookRollout: {
    generatedAtUtc: "2026-02-01T00:15:00Z",
    fundProfileId: "default-fund",
    fundStructureNodeId: "fund-default",
    fundStructureNodeKind: "Fund",
    accountingBasis: "Gaap",
    books: [],
    issues: [
      {
        code: "ledger-books.period-open",
        severity: "Warning",
        message: "Fixture ledger book rollout still has open periods for close-review evidence.",
        scope: "default-fund",
        ledgerBookId: null,
        fundStructureNodeId: "fund-default",
        accountingBasis: "Gaap"
      }
    ],
    isReady: false,
    criticalIssueCount: 0,
    warningIssueCount: 1,
    bookCount: 1,
    openPeriodCount: 1
  },
  rulesStudioSummary: fixtureAccountingConfiguration.rulesStudio?.summary ?? null,
  ledgerBookWorkflows: {
    ledgerBookId: null,
    postingRulesLedgerBookNativeCertified: false,
    journalLifecycleLedgerBookNativeCertified: false,
    closeReportingLedgerBookNativeCertified: false,
    externalGlLedgerBookNativeCertified: false,
    reconciliationLedgerBookNativeCertified: false,
    directLendingLedgerBookNativeCertified: false,
    strategyLedgerReadLedgerBookNativeCertified: false,
    evidenceReferences: [],
    completedControlCount: 0,
    requiredControlCount: 9,
    hasLedgerBookScope: false,
    hasRetainedEvidence: false,
    hasLedgerBookScopedEvidence: false
  },
  dimensionalReporting: {
    ledgerBookId: null,
    periodReportDimensionQueriesCertified: true,
    crossPeriodReportDimensionQueriesCertified: false,
    journalQueryDimensionFiltersCertified: true,
    externalExportDimensionMappingCertified: false,
    ledgerLineDimensionsPersistedCertified: false,
    trialBalanceDimensionFiltersCertified: false,
    reportPackageDimensionProvenanceCertified: false,
    evidenceReferences: ["fixture:dimensions:default-fund"],
    completedControlCount: 3,
    requiredControlCount: 9,
    hasLedgerBookScope: false,
    hasRetainedEvidence: true,
    hasLedgerBookScopedEvidence: false
  },
  tenantAdministration: {
    tenantId: "fixture-tenant",
    companyId: "fixture-company",
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
    evidenceReferences: ["fixture:tenant-admin:gap"],
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
      summary: "Ledger books and scoped workflow controls exist, but fixture certification still shows fund-level and open-period gaps.",
      requiredAction: "Retain ledger-book-native workflow evidence for posting, JE lifecycle, reconciliation, close/reporting, direct lending, and strategy ledger reads.",
      areas: ["LedgerBooks", "PostingRules", "JournalLifecycle", "CloseReporting"],
      blockingIssueCodes: ["workflow.ledger-book-scope-missing", "workflow.close-reporting-not-certified"],
      issues: [
        {
          code: "workflow.ledger-book-scope-missing",
          area: "LedgerBooks",
          severity: "Warning",
          message: "Ledger-book workflow certification still needs retained selected-book evidence.",
          suggestedAction: "Attach selected-book workflow evidence before production rollout.",
          evidenceReferences: []
        }
      ],
      routes: ["/accounting/configure", "/accounting/journal-entries", "/accounting/close"]
    },
    {
      code: "enterprise-accounting-configuration-studio",
      label: "Enterprise accounting configuration studio",
      status: "ReviewRequired",
      highestSeverity: "Warning",
      summary: "Rules Studio and ledger-book administration are visible, but tenant setup and admin-studio coverage remain incomplete.",
      requiredAction: "Complete retained tenant/company/reporting-group setup, chart administration, rule promotion, approval queues, and implementation sandbox controls.",
      areas: ["RulesStudio", "TenantAdministration"],
      blockingIssueCodes: ["tenant-admin.operator-surface-required"],
      routes: ["/accounting/configure", "/settings"]
    },
    {
      code: "external-gl-guarded-integration",
      label: "External GL guarded integration",
      status: "ReviewRequired",
      highestSeverity: "Info",
      summary: "QuickBooks import, certified mapping, reconciliation, and guarded export artifacts exist while live posting stays disabled by policy.",
      requiredAction: "Keep external GL import-first, retain export-package evidence, and expand Xero/NetSuite fixtures before considering a separate live-posting gate.",
      areas: ["ExternalGl"],
      blockingIssueCodes: ["external-gl.live-posting-disabled"],
      routes: ["/accounting/external-gl"]
    },
    {
      code: "dimensional-ledger-reporting",
      label: "Dimensional ledger and reporting",
      status: "ReviewRequired",
      highestSeverity: "Warning",
      summary: "Canonical dimensions flow through key accounting DTOs and fixtures, but full ledger-line, trial-balance, report, and export certification is not complete.",
      requiredAction: "Certify ledger-line dimension persistence, trial-balance filters, report-package provenance, and external export dimension mappings.",
      areas: ["DimensionalAccounting", "ExternalGl", "CloseReporting"],
      blockingIssueCodes: ["dimensions.external-gl-missing"],
      routes: ["/accounting/ledger", "/reporting", "/accounting/external-gl"]
    },
    {
      code: "production-controls-hardening",
      label: "Production controls and rollout hardening",
      status: "ReviewRequired",
      highestSeverity: "Warning",
      summary: "Migration artifacts and tenant controls are retained, but broad migration, performance, disaster recovery, and bulk import/export safeguards still need completion.",
      requiredAction: "Retain certified migration runs, performance validation, disaster-recovery runbooks, bulk import/export safeguards, and close/reporting evidence migration.",
      areas: ["MigrationRollout", "TenantAdministration", "CloseReporting"],
      blockingIssueCodes: ["migration.close-reporting-evidence-not-certified"],
      routes: ["/accounting/configure", "/settings", "/accounting/close"]
    }
  ],
  components: [
    {
      area: "LedgerBooks",
      label: "Ledger books",
      status: "ReviewRequired",
      score: 70,
      summary: "Ledger-book setup exists in fixture mode, but open-period posture still needs close review.",
      issues: [],
      evidenceReferences: ["fixture:ledger-books:default-fund"],
      route: "/accounting/configure"
    },
    {
      area: "RulesStudio",
      label: "Rules Studio",
      status: "Ready",
      score: 90,
      summary: "Rules Studio fixture contains effective-dated generated posting rules, saved tests, and promotion evidence.",
      issues: [],
      evidenceReferences: ["fixture:accounting-rules-studio"],
      route: "/accounting/configure"
    },
    {
      area: "PostingRules",
      label: "Posting rules",
      status: "Ready",
      score: 88,
      summary: "Generated multi-line posting definitions are retained for the active fixture rule.",
      issues: [],
      evidenceReferences: ["fixture:posting-rule:trade-buy"],
      route: "/accounting/configure"
    },
    {
      area: "JournalLifecycle",
      label: "Journal lifecycle",
      status: "Ready",
      score: 85,
      summary: "Manual journal lifecycle controls are registered and remain approval-gated.",
      issues: [],
      evidenceReferences: ["fixture:manual-journal:lifecycle"],
      route: "/accounting/journal-entries"
    },
    {
      area: "DimensionalAccounting",
      label: "Dimensional accounting",
      status: "ReviewRequired",
      score: 65,
      summary: "Key dimensions flow through fixture rules, but line-level external GL coverage still requires review.",
      issues: [
        {
          code: "dimensions.external-gl-missing",
          area: "DimensionalAccounting",
          severity: "Warning",
          message: "Generated posting lines do not fully prove external-GL dimensions.",
          suggestedAction: "Map generated postings to external GL dimensions before production export certification.",
          evidenceReferences: ["fixture:posting-rule:trade-buy"]
        }
      ],
      evidenceReferences: ["fixture:dimensions:default-fund"],
      route: "/accounting/ledger"
    },
    {
      area: "ExternalGl",
      label: "External GL",
      status: "ReviewRequired",
      score: 72,
      summary: "QuickBooks fixture import and certified mapping exist; live external posting remains disabled by policy.",
      issues: [
        {
          code: "external-gl.live-posting-disabled",
          area: "ExternalGl",
          severity: "Info",
          message: "Live external GL posting remains disabled by product policy.",
          suggestedAction: "Use import, reconciliation, and guarded export artifacts until a separately approved live-posting adapter exists.",
          evidenceReferences: ["fixture:external-gl:quickbooks"]
        }
      ],
      evidenceReferences: ["fixture:external-gl:quickbooks"],
      route: "/accounting/external-gl"
    },
    {
      area: "CloseReporting",
      label: "Close and reporting",
      status: "Ready",
      score: 82,
      summary: "Close plan and report package fixtures expose sign-off, lock, certification, and restatement posture.",
      issues: [],
      evidenceReferences: ["fixture:close-reporting:default-fund"],
      route: "/accounting/close"
    },
    {
      area: "MigrationRollout",
      label: "Migration rollout",
      status: "ReviewRequired",
      score: 75,
      summary: "Fixture rollout retains ledger-book and dimensional backfill evidence, but close/report evidence migration still needs certification.",
      issues: [
        {
          code: "migration.close-reporting-evidence-not-certified",
          area: "MigrationRollout",
          severity: "Warning",
          message: "Close and reporting evidence migration has not been certified.",
          suggestedAction: "Retain close checklist, report package, certification, and restatement evidence migration proof before production close.",
          evidenceReferences: ["fixture:migration:ledger-book", "fixture:migration:dimensions"]
        }
      ],
      evidenceReferences: ["fixture:migration:ledger-book", "fixture:migration:dimensions"],
      route: "/accounting/configure"
    },
    {
      area: "TenantAdministration",
      label: "Tenant administration",
      status: "ReviewRequired",
      score: 50,
      summary: "Production rollout still needs a complete tenant/company/report-group setup operator surface.",
      issues: [
        {
          code: "tenant-admin.operator-surface-required",
          area: "TenantAdministration",
          severity: "Warning",
          message: "Production rollout still needs a full tenant/company/report-group setup operator surface over shared controls.",
          suggestedAction: "Bind browser and WPF admin setup screens to this shared readiness contract instead of local setup heuristics.",
          evidenceReferences: ["fixture:tenant-admin:gap"]
        }
      ],
      evidenceReferences: ["fixture:tenant-admin:gap"],
      route: "/settings"
    }
  ],
  issues: [
    {
      code: "dimensions.external-gl-missing",
      area: "DimensionalAccounting",
      severity: "Warning",
      message: "Generated posting lines do not fully prove external-GL dimensions.",
      suggestedAction: "Map generated postings to external GL dimensions before production export certification.",
      evidenceReferences: ["fixture:posting-rule:trade-buy"]
    },
    {
      code: "migration.close-reporting-evidence-not-certified",
      area: "MigrationRollout",
      severity: "Warning",
      message: "Close and reporting evidence migration has not been certified.",
      suggestedAction: "Retain close checklist, report package, certification, and restatement evidence migration proof before production close.",
      evidenceReferences: ["fixture:migration:ledger-book", "fixture:migration:dimensions"]
    },
    {
      code: "tenant-admin.operator-surface-required",
      area: "TenantAdministration",
      severity: "Warning",
      message: "Production rollout still needs a full tenant/company/report-group setup operator surface over shared controls.",
      suggestedAction: "Bind browser and WPF admin setup screens to this shared readiness contract instead of local setup heuristics.",
      evidenceReferences: ["fixture:tenant-admin:gap"]
    },
    {
      code: "external-gl.live-posting-disabled",
      area: "ExternalGl",
      severity: "Info",
      message: "Live external GL posting remains disabled by product policy.",
      suggestedAction: "Use import, reconciliation, and guarded export artifacts until a separately approved live-posting adapter exists.",
      evidenceReferences: ["fixture:external-gl:quickbooks"]
    }
  ]
};

const fixtureLedgerClosePeriodPlan: ClosePeriodPlan = {
  closePlanId: "close-plan-fixture-202601",
  fundProfileId: "default-fund",
  ledgerBookId: null,
  periodId: "2026-01",
  periodStart: "2026-01-01",
  periodEnd: "2026-01-31",
  closeDueDate: "2026-02-05",
  isPeriodLocked: false,
  materialityPolicy: {
    policyId: "materiality-2026-01",
    amountThreshold: 10000,
    percentThreshold: 0.01,
    currency: "USD",
    reviewRole: "Controller",
    requiresLateAdjustmentApproval: true
  },
  tasks: [
    {
      taskId: "close-gate-brokeringest",
      displayName: "Receive external activity",
      status: "ReadyForSignOff",
      owner: "Accounting ops",
      dueDate: "2026-02-02",
      dependencies: [],
      signOffs: [
        {
          signOffId: "close-gate-brokeringest:approval-1",
          role: "Reviewer",
          actor: "controller",
          approvalState: "Approved",
          signedAtUtc: "2026-02-02T18:00:00Z",
          evidenceLinks: ["ops-close-evidence:broker-ingest"]
        }
      ],
      evidenceLinks: ["ops-close-evidence:broker-ingest"],
      blockerReason: null
    },
    {
      taskId: "close-gate-reconciliation",
      displayName: "Resolve reconciliation breaks",
      status: "WaitingOnDependency",
      owner: "Controller",
      dueDate: "2026-02-04",
      dependencies: [
        {
          dependencyId: "dependency-close-gate-reconciliation",
          dependsOnTaskId: "close-gate-brokeringest",
          reason: "Close checklist tasks must be completed in workflow order."
        }
      ],
      signOffs: [],
      evidenceLinks: [],
      blockerReason: "Unresolved GL tie-out breaks remain open."
    }
  ],
  lateAdjustments: [
    {
      requestId: "late-adjustment-fixture-1",
      journalEntryId: "11111111-1111-1111-1111-111111111111",
      requestedBy: "accounting-ops",
      requestedAtUtc: "2026-02-04T20:15:00Z",
      amount: 15000,
      currency: "USD",
      reason: "Controller late adjustment review for material accrual.",
      approvalState: "Submitted",
      materialityPolicy: {
        policyId: "materiality-2026-01",
        amountThreshold: 10000,
        percentThreshold: 0.01,
        currency: "USD",
        reviewRole: "Controller",
        requiresLateAdjustmentApproval: true
      },
      evidenceLinks: ["late-adjustment:evidence:fixture-1"]
    }
  ],
  validationIssues: [
    {
      code: "LateAdjustmentRequiresApproval",
      severity: "Warning",
      message: "Late adjustment 'late-adjustment-fixture-1' exceeds the materiality policy and requires Controller approval.",
      targetId: "late-adjustment-fixture-1",
      suggestedAction: "Approve or reject the late adjustment before final close certification."
    }
  ]
};

const fixtureAccountingReportPackage: AccountingReportPackageBundle = {
  financialStatements: {
    packageId: "accounting-report-package-default-fund-2026-01",
    fundProfileId: "default-fund",
    ledgerBookId: null,
    periodId: "2026-01",
    certificationState: "ReadyForReview",
    statementIds: ["balance-sheet", "income-statement", "trial-balance", "statement-of-changes-in-capital"],
    evidenceLinks: ["evidence:report-package:2026-01"],
    certification: {
      certificationId: "report-certification-default-fund-2026-01",
      state: "ReadyForReview",
      actor: "fixture-operator",
      recordedAtUtc: "2026-02-05T20:00:00Z",
      summary: "Accounting report package is assembled and ready for human certification review.",
      evidenceLinks: ["evidence:report-package:2026-01"]
    },
    restatement: null
  },
  investorCapitalStatements: [
    {
      statementId: "investor-capital-statement-default-fund-2026-01-aggregate",
      fundProfileId: "default-fund",
      capitalAccountId: "capital-account:aggregate",
      investorId: null,
      periodId: "2026-01",
      beginningCapital: 100000,
      contributions: 25000,
      distributions: 5000,
      realizedGainLoss: 12500,
      endingCapital: 132500,
      currency: "USD",
      certificationState: "ReadyForReview",
      evidenceLinks: ["evidence:report-package:2026-01"]
    }
  ],
  realizedGainLoss: {
    reportId: "realized-gain-loss-default-fund-2026-01",
    fundProfileId: "default-fund",
    ledgerBookId: null,
    periodId: "2026-01",
    dimensions: {
      fundId: "default-fund",
      externalGlDimensions: {}
    },
    realizedGainLoss: 12500,
    currency: "USD",
    certificationState: "ReadyForReview",
    evidenceLinks: ["evidence:report-package:2026-01"]
  },
  navPackage: {
    packageId: "nav-package-default-fund-2026-01",
    fundProfileId: "default-fund",
    ledgerBookId: null,
    periodId: "2026-01",
    nav: 132500,
    currency: "USD",
    certificationState: "ReadyForReview",
    evidenceLinks: ["evidence:report-package:2026-01"],
    certification: {
      certificationId: "report-certification-default-fund-2026-01",
      state: "ReadyForReview",
      actor: "fixture-operator",
      recordedAtUtc: "2026-02-05T20:00:00Z",
      summary: "Accounting report package is assembled and ready for human certification review.",
      evidenceLinks: ["evidence:report-package:2026-01"]
    },
    restatement: null
  },
  certification: {
    certificationId: "report-certification-default-fund-2026-01",
    state: "ReadyForReview",
    actor: "fixture-operator",
    recordedAtUtc: "2026-02-05T20:00:00Z",
    summary: "Accounting report package is assembled and ready for human certification review.",
    evidenceLinks: ["evidence:report-package:2026-01"]
  },
  validationIssues: [
    {
      code: "PeriodNotLocked",
      severity: "Warning",
      message: "The close period is not locked; report package certification remains ready-for-review only.",
      targetId: "close-plan-fixture-202601",
      suggestedAction: "Lock the period after close approvals before final report certification."
    }
  ]
};

const fixturePortfolioMultiAssetCoverage = {
  fundAccountId: "all",
  entity: "portfolio",
  asOfUtc: "2026-06-02T00:00:00.0000000Z",
  metrics: [
    { id: "multi-asset-classes", label: "Asset classes", value: "8", delta: "covered", tone: "default" },
    { id: "multi-asset-ready", label: "Ready", value: "2", delta: "definition + evidence", tone: "default" },
    { id: "multi-asset-review", label: "Review required", value: "6", delta: "evidence gaps", tone: "warning" },
    { id: "multi-asset-blocked", label: "Blocked", value: "0", delta: "close gates", tone: "success" }
  ],
  assetClasses: [
    {
      assetClass: "Equity",
      displayName: "Equities",
      status: "Ready",
      statusLabel: "Ready",
      summary: "Listed equity positions carry quote, corporate-action, tax-lot, and ledger evidence.",
      evidenceRequirements: [
        { requirementId: "Equity:security-master-identifiers", label: "Security Master identifiers", category: "SecurityMaster", status: "Ready", evidenceRoute: "/api/workstation/security-master/securities", required: true }
      ],
      blockers: [],
      drillThroughTargets: [
        { targetId: "Equity:security-master-passport", targetType: "SecurityMasterPassport", label: "Security Master passport/profile", route: "/api/workstation/security-master/securities", evidenceLink: null, status: "Ready", source: "SecurityMaster" },
        { targetId: "Equity:provider-evidence", targetType: "ProviderEvidence", label: "Provider evidence", route: "/api/workstation/data-operations", evidenceLink: "fixture://provider/equity", status: "Ready", source: "ProviderLedgerReconciliation" },
        { targetId: "Equity:reconciliation-case", targetType: "ReconciliationCase", label: "Reconciliation break/case", route: "/api/reconciliation/runs", evidenceLink: null, status: "Ready", source: "ProviderLedgerReconciliation" },
        { targetId: "Equity:ledger-mapping", targetType: "LedgerMapping", label: "Ledger mapping/evidence", route: "/api/fund-structure/ledger-mapping-assignments", evidenceLink: null, status: "Ready", source: "LedgerPeriodPostingGuard" },
        { targetId: "Equity:close-readiness", targetType: "CloseReadiness", label: "Close readiness", route: "/api/workstation/portfolio/multi-asset-coverage", evidenceLink: null, status: "Ready", source: "FundAccountCloseReadinessService" }
      ],
      ledgerClassification: { classification: "Security position / realized and unrealized P&L / dividend income" },
      reconciliationSignals: { breaks: "quantity, market value, cash, corporate action, tax lot" }
    },
    {
      assetClass: "DirectLoan",
      displayName: "Private credit / loans",
      status: "ReviewRequired",
      statusLabel: "Review required",
      summary: "Private-credit readiness keeps borrower, commitment, covenant, paydown, and obligation evidence on the shared DirectLoan row.",
      evidenceRequirements: [
        { requirementId: "DirectLoan:security-master-identifiers", label: "Security Master identifiers", category: "SecurityMaster", status: "Ready", evidenceRoute: "/api/workstation/security-master/securities", required: true },
        { requirementId: "DirectLoan:provider-evidence", label: "Provider evidence feeds: Loan schedule, Borrower notice, Commitment schedule, Unfunded commitment, Paydown, Covenant, Accrual, Cash, Collateral, Valuation", category: "ProviderEvidence", status: "ReviewRequired", evidenceRoute: "/api/workstation/data-operations", required: true },
        { requirementId: "DirectLoan:ledger-classification", label: "Loan receivable / unfunded commitment obligation / interest income / fees", category: "Ledger", status: "Ready", evidenceRoute: "/api/workstation/accounting", required: true }
      ],
      blockers: [
        { code: "DirectLoan:provider-evidence-review", severity: "Review", message: "Retained loan schedule, unfunded commitment, covenant, paydown, and obligation evidence is required before close readiness can be marked complete.", source: "ProviderEvidence", evidenceRoute: "/api/workstation/portfolio/multi-asset-coverage" }
      ],
      drillThroughTargets: [
        { targetId: "DirectLoan:security-master-passport", targetType: "SecurityMasterPassport", label: "Security Master passport/profile", route: "/api/workstation/security-master/securities", evidenceLink: "fixture://security-master/direct-loan/acme", status: "Ready", source: "SecurityMaster" },
        { targetId: "DirectLoan:provider-evidence", targetType: "ProviderEvidence", label: "Provider evidence", route: "/api/workstation/data-operations", evidenceLink: "fixture://provider/direct-loan/acme", status: "ReviewRequired", source: "ProviderLedgerReconciliation" },
        { targetId: "DirectLoan:reconciliation-case", targetType: "ReconciliationCase", label: "Reconciliation break/case", route: "/api/workstation/reconciliation/runs", evidenceLink: "fixture://reconciliation/direct-loan", status: "ReviewRequired", source: "ProviderLedgerReconciliation" },
        { targetId: "DirectLoan:ledger-mapping", targetType: "LedgerMapping", label: "Ledger mapping/evidence", route: "/api/fund-structure/ledger-mapping-assignments", evidenceLink: "fixture://ledger/direct-loan", status: "Ready", source: "LedgerPeriodPostingGuard" },
        { targetId: "DirectLoan:close-readiness", targetType: "CloseReadiness", label: "Close readiness", route: "/api/workstation/portfolio/multi-asset-coverage", evidenceLink: null, status: "ReviewRequired", source: "FundAccountCloseReadinessService" },
        { targetId: "DirectLoan:loan-schedule-evidence", targetType: "LoanScheduleEvidence", label: "Loan schedule and borrower notices", route: "/api/workstation/data-operations", evidenceLink: "fixture://provider/direct-loan/acme", status: "ReviewRequired", source: "ProviderLedgerReconciliation" },
        { targetId: "DirectLoan:commitment-covenant-evidence", targetType: "CommitmentCovenantEvidence", label: "Commitment, unfunded commitment, and covenant evidence", route: "/api/workstation/data-operations", evidenceLink: "fixture://provider/direct-loan/acme", status: "ReviewRequired", source: "ProviderLedgerReconciliation" },
        { targetId: "DirectLoan:paydown-obligation-ledger", targetType: "PaydownObligationLedger", label: "Paydown and obligation ledger support", route: "/api/workstation/accounting", evidenceLink: "fixture://ledger/direct-loan", status: "Ready", source: "LoanAccountingProjector" }
      ],
      ledgerClassification: { classification: "Loan receivable / unfunded commitment obligation / interest income / fees / realized and unrealized P&L" },
      reconciliationSignals: { breaks: "loan schedule, commitment, paydown, obligation, cash, collateral" }
    },
    {
      assetClass: "CustomAsset",
      displayName: "MBS / ABS / CLO / CMBS / private assets",
      status: "ReviewRequired",
      statusLabel: "Review required",
      summary: "Structured and private assets require governed profiles, factor or NAV evidence, valuation approval, and profile-aware ledger classification.",
      evidenceRequirements: [
        { requirementId: "CustomAsset:governed-profile", label: "Approved custom/private asset profile coverage", category: "Governance", status: "Ready", evidenceRoute: "/api/security-master/asset-profiles", required: true },
        { requirementId: "CustomAsset:provider-evidence", label: "Provider evidence feeds", category: "ProviderEvidence", status: "ReviewRequired", evidenceRoute: "/api/workstation/data-operations", required: true }
      ],
      blockers: [
        { code: "CustomAsset:provider-evidence-review", severity: "Review", message: "Retained provider evidence is required before close readiness can be marked complete.", source: "ProviderEvidence", evidenceRoute: "/api/workstation/portfolio/multi-asset-coverage" }
      ],
      drillThroughTargets: [
        { targetId: "CustomAsset:security-master-passport", targetType: "SecurityMasterPassport", label: "Security Master passport/profile", route: "/api/security-master/asset-profiles", evidenceLink: "fixture://security-master/profile/custom-asset", status: "Ready", source: "SecurityMaster" },
        { targetId: "CustomAsset:provider-evidence", targetType: "ProviderEvidence", label: "Provider evidence", route: "/api/workstation/data-operations", evidenceLink: "fixture://provider/custom-asset", status: "ReviewRequired", source: "ProviderLedgerReconciliation" },
        { targetId: "CustomAsset:reconciliation-case", targetType: "ReconciliationCase", label: "Reconciliation break/case", route: "/api/reconciliation/runs", evidenceLink: "fixture://reconciliation/custom-profile-gap", status: "ReviewRequired", source: "ProviderLedgerReconciliation" },
        { targetId: "CustomAsset:ledger-mapping", targetType: "LedgerMapping", label: "Ledger mapping/evidence", route: "/api/fund-structure/ledger-mapping-assignments", evidenceLink: null, status: "Ready", source: "LedgerPeriodPostingGuard" },
        { targetId: "CustomAsset:close-readiness", targetType: "CloseReadiness", label: "Close readiness", route: "/api/workstation/portfolio/multi-asset-coverage", evidenceLink: "fixture://close/custom-profile", status: "ReviewRequired", source: "FundAccountCloseReadinessService" },
        { targetId: "CustomAsset:profile-lineage", targetType: "AssetProfileLineage", label: "Approved profile lineage", route: "/api/security-master/asset-profiles", evidenceLink: "fixture://security-master/profile/custom-asset", status: "Ready", source: "SecurityAssetProfileGovernanceService" },
        { targetId: "CustomAsset:servicer-trustee-evidence", targetType: "ServicerTrusteeEvidence", label: "Servicer, trustee, warehouse, and factor evidence", route: "/api/workstation/data-operations", evidenceLink: "fixture://provider/custom-asset", status: "ReviewRequired", source: "ProviderLedgerReconciliation" },
        { targetId: "CustomAsset:valuation-nav-evidence", targetType: "StructuredValuationEvidence", label: "NAV, dealer pricing, capital call, and distribution evidence", route: "/api/workstation/data-operations", evidenceLink: "fixture://provider/custom-asset", status: "ReviewRequired", source: "ProviderLedgerReconciliation" },
        { targetId: "CustomAsset:obligation-close-evidence", targetType: "ObligationCloseEvidence", label: "Obligation schedule and close-readiness evidence", route: "/api/workstation/portfolio/multi-asset-coverage", evidenceLink: "fixture://close/custom-profile", status: "ReviewRequired", source: "FundAccountCloseReadinessService" }
      ],
      ledgerClassification: { classification: "Profile-derived classification / valuation adjustment / income accrual / commitment accounting" },
      reconciliationSignals: { breaks: "quantity, market value, cash, factor schedule, custom-profile evidence" }
    }
  ],
  drillThroughRoutes: {
    portfolio: WORKSTATION_API_ENDPOINTS.portfolio,
    accounting: WORKSTATION_API_ENDPOINTS.accounting,
    coverage: WORKSTATION_API_ENDPOINTS.portfolioMultiAssetCoverage
  }
};

const fixtureQuantTemplates: QuantTemplatesResponse = {
  templates: [
    {
      id: "hello-quant-lab",
      title: "Hello Quant Lab",
      description: "Print a metric and verify the local script runtime path.",
      source: "Print(\"Hello from the Quant Lab fixture.\");\nPrintMetric(\"answer\", 42);\n"
    },
    {
      id: "parameter-sweep-preview",
      title: "Parameter sweep preview",
      description: "Exercise parameter extraction with a lookback and fee toggle.",
      source: "var lookback = Parameter(\"lookback\", 20);\nvar includeFees = Parameter(\"includeFees\", true);\nPrintMetric(\"lookback\", lookback);\nPrintMetric(\"fees\", includeFees ? 1 : 0);\n"
    }
  ]
};

const fixtureQuantParameters: QuantParametersResponse = {
  parameters: [
    {
      name: "lookback",
      label: "Lookback",
      typeName: "int",
      defaultValue: "20",
      min: 1,
      max: 252,
      description: "Rolling window length for the fixture run."
    },
    {
      name: "includeFees",
      label: "Include fees",
      typeName: "bool",
      defaultValue: "true",
      min: null,
      max: null,
      description: "Toggle transaction-cost assumptions in the fixture run."
    }
  ]
};

const fixtureStrategyDesignerDocument: StrategyDesignDocument = {
  documentId: "strategy-designer-fixture-1",
  name: "Quality momentum rotation",
  description: "No-host Strategy Designer sample that combines quality, momentum, and risk filters.",
  version: "1.0",
  datasetReference: "fixture://strategy-designer/quality-momentum",
  universe: ["AAPL", "MSFT", "NVDA", "QQQ"],
  cells: [
    {
      cellId: "universe",
      label: "Liquid equity universe",
      kind: "universe",
      purpose: "Seed the candidate universe with liquid large-cap equities.",
      source: "Provider historical bars / security master",
      fieldRefs: ["PRICE", "AVG_DOLLAR_VOLUME_20D"],
      parameters: { minimumDollarVolume: "25000000" },
      disabledReason: null
    },
    {
      cellId: "momentum-score",
      label: "Momentum score",
      kind: "score",
      purpose: "Rank candidates by medium-term momentum adjusted for volatility.",
      source: "Meridian factor library",
      fieldRefs: ["MOMENTUM_63D", "VOLATILITY_20D"],
      parameters: { lookback: "63" },
      disabledReason: null
    },
    {
      cellId: "risk-gate",
      label: "Risk gate",
      kind: "gate",
      purpose: "Reject symbols with drawdown or concentration risk beyond the paper-trading limit.",
      source: "Risk policy fixture",
      fieldRefs: ["MAX_DRAWDOWN_90D", "PORTFOLIO_WEIGHT"],
      parameters: { maxWeight: "0.15" },
      disabledReason: null
    }
  ],
  transitions: [
    {
      transitionId: "universe-to-score",
      fromCellId: "universe",
      toCellId: "momentum-score",
      kind: "filter",
      condition: "avgDollarVolume20d >= 25000000",
      maxIterations: null,
      rationale: "Keep low-liquidity symbols out of the scoring pass."
    },
    {
      transitionId: "score-to-risk",
      fromCellId: "momentum-score",
      toCellId: "risk-gate",
      kind: "gate",
      condition: "scorePercentile >= 0.80",
      maxIterations: 1,
      rationale: "Only top-ranked candidates continue to risk review."
    }
  ],
  metadata: {
    evidenceLane: "browser-screenshot",
    fixture: "true"
  },
  createdAt: "2026-05-15T15:00:00Z",
  updatedAt: "2026-05-15T15:00:00Z"
};

const fixtureStrategyDesignerFieldCatalog: StrategyDesignFieldCatalogItem[] = [
  {
    fieldId: "PRICE",
    label: "Price",
    source: "Provider historical bars / live quotes",
    dataSet: "market-data",
    typeName: "decimal",
    description: "Canonical last or close price resolved through Meridian providers.",
    isEnabled: true,
    disabledReason: null,
    synonyms: ["close", "last", "bar.close"]
  },
  {
    fieldId: "MOMENTUM_63D",
    label: "63-day momentum",
    source: "Provider historical bars",
    dataSet: "market-data",
    typeName: "decimal",
    description: "Return over the last 63 trading sessions.",
    isEnabled: true,
    disabledReason: null,
    synonyms: ["return", "trend"]
  },
  {
    fieldId: "AMX_PRIVATE_SCORE",
    label: "AMX private score",
    source: "External strategy upload",
    dataSet: "strategy-import",
    typeName: "decimal",
    description: "Analyst model extension field kept disabled until provenance is attached.",
    isEnabled: false,
    disabledReason: "No Meridian canonical source",
    synonyms: ["analyst", "custom", "amx"]
  }
];

const fixtureStrategyDesignerTemplates: StrategyDesignTemplate[] = [
  {
    templateId: "quality-momentum-rotation",
    name: "Quality momentum rotation",
    description: "Rank liquid equities by momentum, quality, and volatility before risk-gate review.",
    category: "Equity rotation",
    sourcePrototype: "Strategy Designer fixture",
    tags: ["momentum", "quality", "risk-gate"],
    document: fixtureStrategyDesignerDocument
  }
];

const fixtureStrategyDesignerDrafts: StrategyDesignDraftSummary[] = [
  {
    documentId: fixtureStrategyDesignerDocument.documentId,
    name: fixtureStrategyDesignerDocument.name,
    version: fixtureStrategyDesignerDocument.version,
    datasetReference: fixtureStrategyDesignerDocument.datasetReference,
    cellCount: fixtureStrategyDesignerDocument.cells.length,
    transitionCount: fixtureStrategyDesignerDocument.transitions.length,
    updatedAt: fixtureStrategyDesignerDocument.updatedAt,
    validationSummary: "Fixture draft passes no-host validation."
  }
];

const fixtureSecurityConflicts: SecurityMasterConflict[] = [
  {
    conflictId: "conflict-dev-001",
    securityId: "sec-dev-002",
    conflictKind: "IdentifierCollision",
    fieldPath: "identifiers.CUSIP",
    providerA: "Bloomberg",
    valueA: "69331C108",
    providerB: "Refinitiv",
    valueB: "69331C116",
    detectedAt: "2026-04-28T17:45:00Z",
    status: "Open"
  }
];

const fixtureTradingParameters: TradingParameters = {
  securityId: "sec-dev-001",
  lotSize: 1,
  tickSize: 0.01,
  contractMultiplier: null,
  marginRequirementPct: 25,
  tradingHoursUtc: "13:30–20:00",
  circuitBreakerThresholdPct: 20,
  asOf: "2026-04-28T18:15:00Z"
};

const fixtureCoveredCallRuns: CoveredCallRunSummary[] = [
  {
    runId: "covered-call-dev-1",
    underlyingSymbol: "SPY",
    from: "2025-05-01",
    to: "2026-05-01",
    label: "SPY overwrite fixture",
    status: "Completed",
    startedAt: "2026-05-08T15:00:00Z",
    endedAt: "2026-05-08T15:02:30Z",
    cagr: 0.083,
    sharpeRatio: 1.18,
    winRate: 0.64
  }
];

const fixtureCoveredCallChainPreview: CoveredCallChainPreview = {
  underlyingSymbol: "SPY",
  asOf: "2026-05-08",
  underlyingPrice: 512.46,
  totalContractsScanned: 4,
  filtersPassed: 2,
  candidates: [
    {
      strike: 515,
      expiration: "2026-06-19",
      daysToExpiration: 42,
      bid: 4.35,
      ask: 4.55,
      delta: 0.31,
      impliedVolatility: 0.187,
      openInterest: 1840,
      volume: 312,
      meetsAllFilters: true,
      rejectReason: null
    },
    {
      strike: 520,
      expiration: "2026-06-19",
      daysToExpiration: 42,
      bid: 3.05,
      ask: 3.25,
      delta: 0.24,
      impliedVolatility: 0.181,
      openInterest: 1264,
      volume: 206,
      meetsAllFilters: true,
      rejectReason: null
    },
    {
      strike: 525,
      expiration: "2026-07-17",
      daysToExpiration: 70,
      bid: 3.4,
      ask: 3.9,
      delta: 0.21,
      impliedVolatility: null,
      openInterest: 428,
      volume: 64,
      meetsAllFilters: false,
      rejectReason: "Open interest below minimum"
    },
    {
      strike: 510,
      expiration: "2026-06-19",
      daysToExpiration: 42,
      bid: 7.1,
      ask: 7.8,
      delta: 0.43,
      impliedVolatility: 0.205,
      openInterest: 2240,
      volume: 402,
      meetsAllFilters: false,
      rejectReason: "Delta above maximum"
    }
  ]
};

const fixtureCoveredCallResults: Record<string, CoveredCallRunResult> = {
  "covered-call-dev-1": {
    runId: "covered-call-dev-1",
    underlyingSymbol: "SPY",
    from: "2025-05-01",
    to: "2026-05-01",
    label: "SPY overwrite fixture",
    metrics: {
      cagr: 0.083,
      annualizedVolatility: 0.142,
      sharpeRatio: 1.18,
      sortinoRatio: 1.42,
      calmarRatio: 1.93,
      maxDrawdownPct: -0.043,
      winRate: 0.64,
      assignmentRate: 0.08,
      averageHoldingDays: 23,
      totalOptionTrades: 14,
      assignedTrades: 1,
      totalPremiumCollected: 4280,
      totalOptionPnl: 2760,
      upCapture: 0.58,
      downCapture: 0.72,
      monthlyVar1Pct: -0.036,
      monthlyVar5Pct: -0.024,
      monthlyCVar5Pct: -0.031,
      returnSkewness: -0.18,
      returnKurtosis: 3.2,
      annualizedTurnover: 5.4
    },
    equityCurve: [
      { date: "2025-05-01", strategyEquity: 100000, underlyingEquity: 100000 },
      { date: "2025-08-01", strategyEquity: 102750, underlyingEquity: 103200 },
      { date: "2025-11-01", strategyEquity: 106100, underlyingEquity: 108450 },
      { date: "2026-02-01", strategyEquity: 108250, underlyingEquity: 110700 },
      { date: "2026-05-01", strategyEquity: 111020, underlyingEquity: 113400 }
    ],
    trades: [
      {
        strike: 515,
        expiration: "2025-06-20",
        contracts: 1,
        multiplier: 100,
        entryDate: "2025-05-02",
        entryCredit: 4.35,
        exitDate: "2025-06-14",
        exitDebit: 1.2,
        exitReason: "TakeProfit",
        entryImpliedVolatility: 0.187,
        netPnlPerContract: 315,
        totalNetPnl: 315,
        holdingDays: 43,
        isWin: true,
        wasAssigned: false
      },
      {
        strike: 522,
        expiration: "2025-09-19",
        contracts: 1,
        multiplier: 100,
        entryDate: "2025-08-05",
        entryCredit: 3.1,
        exitDate: "2025-09-19",
        exitDebit: 0,
        exitReason: "Expired",
        entryImpliedVolatility: 0.174,
        netPnlPerContract: 310,
        totalNetPnl: 310,
        holdingDays: 45,
        isWin: true,
        wasAssigned: false
      }
    ],
    openPositionsAtEnd: [
      {
        positionId: "covered-call-dev-open-1",
        strike: 530,
        expiration: "2026-06-19",
        contracts: 1,
        multiplier: 100,
        entryDate: "2026-04-20",
        entryCredit: 3.85,
        markToClose: 1.55,
        currentDelta: 0.24,
        currentDte: 49,
        unrealisedPnl: 230,
        premiumCaptured: 0.597
      }
    ]
  }
};

const fixtureOperatorOverrides: Record<string, OperatorOverridesDto> = {
  "sec-dev-001": {
    securityId: "sec-dev-001",
    values: {
      issuer: "Apple Inc.",
      couponRate: "0.25",
      finalMaturity: "2032-06-30"
    },
    updatedBy: "dashboard-dev",
    updatedAt: "2026-04-28T18:15:00Z"
  }
};

const fixtureOperationsWorkflowId = "79f1f386-0bb1-4aef-9a85-fb9d6de8e1f6";
const fixtureAccountingRecordId = "accounting-record-2026-05";
const fixtureAccountingRecordEvidenceRoute = `/reporting/evidence?subjectKind=accounting-record&subjectId=${fixtureOperationsWorkflowId}`;

const fixtureAccountingRecordEvidenceLinks = [
  {
    evidenceId: "devhash-accounting-record-journal",
    label: "Journal and ledger evidence",
    route: fixtureAccountingRecordEvidenceRoute,
    source: "development-fixture",
    capturedAtUtc: "2026-05-08T15:15:00Z"
  }
];

const fixtureAccountingRecordEvidenceCategories = [
  {
    key: "source-records",
    label: "Retained source data",
    isComplete: true,
    status: "Complete",
    routeHint: fixtureAccountingRecordEvidenceRoute,
    evidenceLinks: fixtureAccountingRecordEvidenceLinks,
    requiredEvidence: ["provider statement", "custodian activity file", "bank or account source record"]
  },
  {
    key: "normalized-activity",
    label: "Normalized activity",
    isComplete: true,
    status: "Complete",
    routeHint: fixtureAccountingRecordEvidenceRoute,
    evidenceLinks: [],
    requiredEvidence: ["normalized activity projection"]
  },
  {
    key: "reconciliation-case-history",
    label: "Reconciliation case history",
    isComplete: false,
    status: "Review required",
    routeHint: "/accounting/reconciliation",
    evidenceLinks: [],
    requiredEvidence: ["case transition history", "operator decision notes"]
  },
  {
    key: "ledger-evidence",
    label: "Journal and ledger evidence",
    isComplete: true,
    status: "Complete",
    routeHint: "/accounting/ledger",
    evidenceLinks: fixtureAccountingRecordEvidenceLinks,
    requiredEvidence: ["journal preview", "trial balance impact"]
  },
  {
    key: "approvals",
    label: "Close approvals",
    isComplete: false,
    status: "Review required",
    routeHint: "/accounting/operations-continuity",
    evidenceLinks: [],
    requiredEvidence: ["distinct operator approval", "close checklist sign-off"]
  },
  {
    key: "report-pack",
    label: "Report pack",
    isComplete: false,
    status: "Review required",
    routeHint: "/reporting/evidence",
    evidenceLinks: [],
    requiredEvidence: ["report-pack manifest", "report-pack provenance", "report-pack validation"]
  },
  {
    key: "exports",
    label: "Exports and retained evidence",
    isComplete: false,
    status: "Review required",
    routeHint: "/reporting/evidence",
    evidenceLinks: [],
    requiredEvidence: ["export manifest", "retained evidence hash", "close-package publication"]
  },
  {
    key: "restatement-lineage",
    label: "Restatement lineage",
    isComplete: false,
    status: "Review required",
    routeHint: "/reporting/evidence",
    evidenceLinks: [],
    requiredEvidence: ["published baseline", "prior-version pointer when restated", "changed-line evidence"]
  }
];

const fixtureOperationsContinuityWorkflow: OperationsContinuityWorkflow = {
  workflowId: fixtureOperationsWorkflowId,
  fundAccountId: "53bf0251-17f6-4fb7-8dbe-6fb4966e2749",
  periodId: "2026-05",
  securityMasterSnapshotId: "9f2f0d07-f8d3-4d6e-a2f1-3116286de3d4",
  brokerSource: "alpaca-paper",
  status: "LedgerPostingDraft",
  version: 4,
  createdAtUtc: "2026-05-08T14:00:00Z",
  updatedAtUtc: "2026-05-08T15:10:00Z",
  brokerIntakeState: "Complete",
  securityMasterState: "Complete",
  ledgerPostingState: "Drafted",
  reconciliationState: "Pending",
  approvalState: "Pending",
  gates: [
    {
      gateKey: "BrokerIngest",
      displayName: "Broker intake",
      status: "Passed",
      isRequired: true,
      description: "Broker activity has been imported and normalized.",
      blockers: [],
      nextActions: [],
      completedAtUtc: "2026-05-08T14:20:00Z",
      completedBy: "ops-user"
    },
    {
      gateKey: "SecurityMaster",
      displayName: "Security Master",
      status: "Passed",
      isRequired: true,
      description: "External instruments are mapped to canonical Security Master records.",
      blockers: [],
      nextActions: [],
      completedAtUtc: "2026-05-08T14:40:00Z",
      completedBy: "ops-user"
    },
    {
      gateKey: "LedgerPosting",
      displayName: "Ledger posting",
      status: "Blocked",
      isRequired: true,
      description: "Balanced journal preview must be validated before posting.",
      blockers: [
        {
          code: "LEDGER_VALIDATION_REQUIRED",
          message: "Ledger posting requires a balanced and validated journal draft.",
          gate: "LedgerPosting",
          severity: "Critical",
          evidenceLinks: []
        }
      ],
      nextActions: [
        {
          code: "RESOLVE_LEDGERPOSTING_BLOCKERS",
          label: "Resolve Ledger Posting blockers",
          route: "/workstation/accounting",
          gate: "LedgerPosting"
        }
      ],
      completedAtUtc: null,
      completedBy: null
    },
    {
      gateKey: "Reconciliation",
      displayName: "Reconciliation",
      status: "NotStarted",
      isRequired: true,
      description: "Expected broker activity must match posted ledger entries.",
      blockers: [],
      nextActions: [],
      completedAtUtc: null,
      completedBy: null
    },
    {
      gateKey: "Approval",
      displayName: "Approval",
      status: "NotStarted",
      isRequired: true,
      description: "Operations lead approval closes the workflow.",
      blockers: [],
      nextActions: [],
      completedAtUtc: null,
      completedBy: null
    }
  ],
  timeline: [
    {
      auditId: "cdb9449e-7402-48b7-9acf-8568b7363e16",
      occurredAtUtc: "2026-05-08T14:00:00Z",
      workflowId: fixtureOperationsWorkflowId,
      fundAccountId: "53bf0251-17f6-4fb7-8dbe-6fb4966e2749",
      periodId: "2026-05",
      eventType: "workflow-started",
      fromState: "NotStarted",
      toState: "CollectingBrokerData",
      gate: "BrokerIngest",
      fromGateStatus: "NotStarted",
      toGateStatus: "InProgress",
      actor: "ops-user",
      rationale: "Open monthly close lane.",
      correlationId: "dev-continuity",
      references: [],
      previousHash: null,
      currentHash: "devhash-started"
    },
    {
      auditId: "2fb7a2f4-6301-4958-b3d1-76ca78390ad8",
      occurredAtUtc: "2026-05-08T15:10:00Z",
      workflowId: fixtureOperationsWorkflowId,
      fundAccountId: "53bf0251-17f6-4fb7-8dbe-6fb4966e2749",
      periodId: "2026-05",
      eventType: "ledger-draft-blocked",
      fromState: "LedgerPostingDraft",
      toState: "Blocked",
      gate: "LedgerPosting",
      fromGateStatus: "InProgress",
      toGateStatus: "Blocked",
      actor: "ops-user",
      rationale: "Journal validation is still required.",
      correlationId: "dev-continuity",
      references: [],
      previousHash: "devhash-started",
      currentHash: "devhash-ledger"
    }
  ],
  breakCases: [
    {
      breakId: "recon-break-factor-1",
      checkId: "mbs-factor-reconciliation",
      category: "MBS factor reconciliation",
      severity: "Warning",
      status: "Open",
      owner: null,
      dueDate: "2026-05-09T18:00:00Z",
      expectedSource: "custodian factor file",
      actualSource: "Security Master factor snapshot",
      expectedAmount: 0.847125,
      actualAmount: 0.8425,
      variance: 0.004625,
      securityId: "fixture-mbs-001",
      symbol: "FNMA 30Y 5.5",
      suggestedAction: "Assign controller review, validate the factor source, and retain resolution evidence before close approval.",
      evidenceLinks: [
        {
          evidenceId: "fixture-factor-break-evidence",
          label: "Fixture factor variance evidence",
          route: "/workstation/accounting/reconciliation/recon-break-factor-1",
          source: "development-fixture",
          capturedAtUtc: "2026-05-08T15:36:00Z"
        }
      ],
      escalationLevel: "Controller review",
      escalationReason: "Factor variance blocks NAV support and close package publication.",
      escalatedAtUtc: "2026-05-08T15:45:00Z",
      slaState: "DueSoon",
      slaDueAtUtc: "2026-05-09T18:00:00Z",
      materiality: 0.004625,
      rootCauseCode: "FactorSourceMismatch",
      approvalState: "Pending",
      blockedOutputs: ["NAV support package", "Close package"]
    }
  ],
  reconciliationLanes: [
    {
      laneId: "cash-reconciliation",
      label: "Cash reconciliation",
      status: "Ready",
      isReady: true,
      breakCount: 0,
      summary: "Cash reconciliation is covered by retained bank and custodian cash evidence.",
      routeHint: "/workstation/accounting/reconciliation",
      evidenceLinks: [
        {
          evidenceId: "fixture-cash-reconciliation-evidence",
          label: "Fixture cash reconciliation evidence",
          route: "/workstation/accounting/reconciliation/cash",
          source: "development-fixture",
          capturedAtUtc: "2026-05-08T15:35:00Z"
        }
      ],
      requiredActions: []
    },
    {
      laneId: "position-reconciliation",
      label: "Position reconciliation",
      status: "Ready",
      isReady: true,
      breakCount: 0,
      summary: "Position reconciliation has matched portfolio and custodian positions.",
      routeHint: "/workstation/accounting/reconciliation",
      evidenceLinks: [],
      requiredActions: []
    },
    {
      laneId: "trade-reconciliation",
      label: "Trade reconciliation",
      status: "Ready",
      isReady: true,
      breakCount: 0,
      summary: "Trade reconciliation matched fills, orders, and execution activity.",
      routeHint: "/workstation/accounting/reconciliation",
      evidenceLinks: [],
      requiredActions: []
    },
    {
      laneId: "income-reconciliation",
      label: "Income reconciliation",
      status: "Ready",
      isReady: true,
      breakCount: 0,
      summary: "Income reconciliation retained expected dividend, interest, and accrual evidence.",
      routeHint: "/workstation/accounting/reconciliation",
      evidenceLinks: [],
      requiredActions: []
    },
    {
      laneId: "mbs-factor-reconciliation",
      label: "MBS factor reconciliation",
      status: "ReviewRequired",
      isReady: false,
      breakCount: 1,
      summary: "MBS factor reconciliation has 1 open break requiring controller review.",
      routeHint: "/workstation/accounting/reconciliation",
      evidenceLinks: [
        {
          evidenceId: "fixture-factor-break-evidence",
          label: "Fixture factor variance evidence",
          route: "/workstation/accounting/reconciliation/recon-break-factor-1",
          source: "development-fixture",
          capturedAtUtc: "2026-05-08T15:36:00Z"
        }
      ],
      requiredActions: ["Resolve or assign MBS factor reconciliation breaks and retain evidence."]
    },
    {
      laneId: "bank-reconciliation",
      label: "Bank reconciliation",
      status: "Ready",
      isReady: true,
      breakCount: 0,
      summary: "Bank reconciliation retained normalized bank transaction evidence.",
      routeHint: "/workstation/accounting/reconciliation",
      evidenceLinks: [],
      requiredActions: []
    },
    {
      laneId: "gl-reconciliation",
      label: "GL reconciliation support",
      status: "Ready",
      isReady: true,
      breakCount: 0,
      summary: "GL reconciliation support has expected journal preview evidence.",
      routeHint: "/workstation/accounting/ledger",
      evidenceLinks: [],
      requiredActions: []
    }
  ],
  ledgerPreview: {
    previewId: "ledger-preview-dev",
    status: "Drafted",
    ledgerBatchId: null,
    generatedAtUtc: "2026-05-08T15:00:00Z",
    evidenceLinks: []
  },
  approvals: [],
  reportPackReadiness: {
    isReady: false,
    reportPackId: null,
    blockingReason: "Close workflow has unresolved ledger blockers.",
    evidenceLinks: []
  },
  accountingRecordSummary: {
    recordId: fixtureAccountingRecordId,
    isAuditReady: false,
    completeCategoryCount: 3,
    requiredCategoryCount: 8,
    summary: "Demo accounting record is partially retained; approvals, case history, report pack, exports, and restatement lineage still require review.",
    evidenceCategories: fixtureAccountingRecordEvidenceCategories,
    evidenceLinks: fixtureAccountingRecordEvidenceLinks,
    auditPackReadiness: {
      isComplete: false,
      generatedInSeconds: 0,
      slaTargetSeconds: 60,
      slaMet: true,
      missingEvidenceCategories: ["ReconciliationCases", "Approvals", "ReportPack", "Exports", "RestatementLineage"],
      warnings: ["Demo accounting record is missing required audit-pack evidence."],
      evidenceCategorySummaries: []
    }
  },
  closeChecklist: [
    {
      taskId: "close-gate-brokeringest",
      gate: "BrokerIngest",
      label: "Broker intake close gate",
      owner: "ops-user",
      requiredEvidence: "Normalized broker statement evidence retained for the close period.",
      dueDate: "2026-05-10",
      requiredApprovalCount: 1,
      expiresOn: "2026-05-15",
      status: "Done",
      blockingReason: null,
      evidencePointer: "ev-broker-ingest",
      remediationRoute: null,
      canAcknowledge: true,
      acknowledgedAtUtc: "2026-05-08T14:20:00Z",
      acknowledgedBy: "ops-user"
    }
  ],
  closeReadiness: null,
  closePackage: null,
  dashboardSummary: {
    dashboardId: "operations-dashboard:fixture:2026-05",
    stage: "Resolve Exceptions",
    status: "Blocked",
    isReady: false,
    readyMetricCount: 2,
    totalMetricCount: 6,
    summary: "Financial Operations dashboard is in Resolve Exceptions with 4 metrics requiring review.",
    metrics: [
      {
        metricId: "receive-activity",
        label: "Receive Activity",
        value: "Complete",
        status: "Ready",
        detail: "Broker activity has been received and normalized for this account-period workflow.",
        routeHint: "/workstation/accounting",
        evidenceLinks: [],
        requiredActions: []
      },
      {
        metricId: "match-records",
        label: "Match Records",
        value: "6/7 lanes ready",
        status: "ReviewRequired",
        detail: "Cash, position, trade, income, MBS factor, bank, and GL reconciliation lanes are tracked from the shared workflow detail.",
        routeHint: "/workstation/accounting/reconciliation",
        evidenceLinks: [],
        requiredActions: ["Complete source-backed reconciliation lanes before approval."]
      },
      {
        metricId: "resolve-exceptions",
        label: "Resolve Exceptions",
        value: "1 open",
        status: "Blocked",
        detail: "1 reconciliation break requires assignment, escalation, or resolution evidence.",
        routeHint: "/workstation/accounting/reconciliation",
        evidenceLinks: [],
        requiredActions: ["Assign, escalate, or resolve open exceptions and retain resolution evidence."]
      },
      {
        metricId: "approve-results",
        label: "Approve Results",
        value: "Pending",
        status: "ReviewRequired",
        detail: "Approval history is not complete for this workflow.",
        routeHint: "/workstation/accounting/approvals",
        evidenceLinks: [],
        requiredActions: ["Complete workflow approval and checklist-control approvals."]
      },
      {
        metricId: "produce-evidence",
        label: "Produce Evidence",
        value: "Evidence package pending",
        status: "Missing",
        detail: "Close workflow has unresolved ledger blockers.",
        routeHint: "/workstation/reporting/report-packs",
        evidenceLinks: [],
        requiredActions: ["Publish and retain the evidence package before period close."]
      },
      {
        metricId: "close-support",
        label: "Close Support",
        value: "Close readiness pending",
        status: "Missing",
        detail: "Close checklist, period lock, and reopen evidence are governed by the shared workflow.",
        routeHint: "/workstation/accounting/operations-continuity",
        evidenceLinks: [],
        requiredActions: ["Clear close readiness blockers and retain period-lock or reopen evidence."]
      }
    ],
    evidenceLinks: [],
    requiredActions: [
      "Assign, escalate, or resolve open exceptions and retain resolution evidence.",
      "Publish and retain the evidence package before period close."
    ]
  },
  reviewedAutomation: {
    summaryId: "reviewed-automation:fixture:2026-05",
    stage: "Report commentary and audit request list draft review",
    status: "ReviewRequired",
    requiresHumanReview: true,
    summary: "Demo automation may draft close commentary and audit request lists, but publication remains behind human approval.",
    allowedUseCases: ["Draft report commentary", "Draft audit request lists"],
    prohibitedActions: ["Approve workflow", "Publish close package", "Erase evidence"],
    evidenceLinks: [
      {
        evidenceId: "ev-reviewed-automation",
        label: "Reviewed automation draft packet",
        route: "/workstation/reporting/report-packs/automation-review",
        source: "operations-continuity",
        capturedAtUtc: "2026-05-08T15:40:00Z"
      }
    ],
    requiredActions: ["Review drafted report commentary and audit request lists against retained evidence before submission."],
    artifacts: [
      {
        artifactId: "reviewed-automation:report-commentary-draft",
        artifactKind: "Report commentary",
        title: "Report commentary draft",
        status: "ReviewRequired",
        requiresHumanReview: true,
        confidencePercent: 84,
        sourceSummary: "Draft commentary is generated from retained close, ledger, reconciliation, and report-pack evidence.",
        suggestedOperatorAction: "Review commentary against retained evidence before report approval or publication.",
        blockedMaterialAction: "Cannot publish reports or release support packages.",
        evidenceLinks: [
          {
            evidenceId: "ev-reviewed-automation",
            label: "Reviewed automation draft packet",
            route: "/workstation/reporting/report-packs/automation-review",
            source: "operations-continuity",
            capturedAtUtc: "2026-05-08T15:40:00Z"
          }
        ],
        reviewChecklist: ["Review drafted report commentary and audit request lists against retained evidence before submission."]
      },
      {
        artifactId: "reviewed-automation:audit-request-list-draft",
        artifactKind: "Audit request list",
        title: "Audit request list draft",
        status: "ReviewRequired",
        requiresHumanReview: true,
        confidencePercent: 79,
        sourceSummary: "Draft audit request lists summarize missing support and unresolved evidence gaps.",
        suggestedOperatorAction: "Review each requested support item and assign an owner before audit release.",
        blockedMaterialAction: "Cannot erase evidence or satisfy audit requests without retained support.",
        evidenceLinks: [],
        reviewChecklist: ["Review drafted report commentary and audit request lists against retained evidence before submission."]
      },
      {
        artifactId: "reviewed-automation:missing-support-flag",
        artifactKind: "Missing support",
        title: "Missing support flag",
        status: "ReviewRequired",
        requiresHumanReview: true,
        confidencePercent: 72,
        sourceSummary: "Missing support flags are derived from incomplete evidence package categories.",
        suggestedOperatorAction: "Attach or waive missing support through governed human review.",
        blockedMaterialAction: "Cannot approve its own missing-support disposition.",
        evidenceLinks: [],
        reviewChecklist: ["Review drafted report commentary and audit request lists against retained evidence before submission."]
      }
    ]
  },
  evidencePackages: [
    {
      packageId: fixtureAccountingRecordId,
      label: "Accounting record evidence",
      status: "ReviewRequired",
      isReady: false,
      summary: "Demo accounting record is partially retained; approvals, case history, report pack, exports, and restatement lineage still require review.",
      routeHint: "/workstation/accounting/operations-continuity",
      completeCategoryCount: 3,
      requiredCategoryCount: 8,
      evidenceLinkCount: fixtureAccountingRecordEvidenceLinks.length,
      evidenceLinks: fixtureAccountingRecordEvidenceLinks,
      requiredActions: ["Complete all accounting-record evidence categories before publishing the evidence package."]
    },
    {
      packageId: "report-pack:fixture:2026-05",
      label: "Report pack evidence",
      status: "Missing",
      isReady: false,
      summary: "Close workflow has unresolved ledger blockers.",
      routeHint: "/workstation/reporting/report-packs",
      completeCategoryCount: 0,
      requiredCategoryCount: 1,
      evidenceLinkCount: 0,
      evidenceLinks: [],
      requiredActions: ["Link ready report-pack evidence before close publication."]
    },
    {
      packageId: "close-package:fixture:2026-05",
      label: "Close package manifest",
      status: "Missing",
      isReady: false,
      summary: "Close package manifest and retained evidence hash have not been published.",
      routeHint: "/workstation/accounting/operations-continuity",
      completeCategoryCount: 0,
      requiredCategoryCount: 1,
      evidenceLinkCount: 0,
      evidenceLinks: [],
      requiredActions: ["Publish the close package manifest and retain the evidence hash."]
    },
    {
      packageId: "audit-support:fixture:2026-05",
      label: "Audit support package",
      status: "ReviewRequired",
      isReady: false,
      summary: "5 audit evidence categories are missing.",
      routeHint: "/workstation/reporting/evidence",
      completeCategoryCount: 3,
      requiredCategoryCount: 8,
      evidenceLinkCount: fixtureAccountingRecordEvidenceLinks.length,
      evidenceLinks: fixtureAccountingRecordEvidenceLinks,
      requiredActions: ["Complete missing audit evidence categories before releasing the package."]
    },
    {
      packageId: "period-lock-reopen:fixture:2026-05",
      label: "Period lock and reopen evidence",
      status: "Missing",
      isReady: false,
      summary: "Period 2026-05 has not been locked by a close package; governed reopen evidence will be required if a closed workflow is reopened.",
      routeHint: "/workstation/accounting/operations-continuity",
      completeCategoryCount: 1,
      requiredCategoryCount: 2,
      evidenceLinkCount: 0,
      evidenceLinks: [],
      requiredActions: ["Close the workflow and retain the period-lock package before evidence release."]
    }
  ],
  evidenceLinks: [],
  blockers: [
    {
      code: "LEDGER_VALIDATION_REQUIRED",
      message: "Ledger posting requires a balanced and validated journal draft.",
      gate: "LedgerPosting",
      severity: "Critical",
      evidenceLinks: []
    }
  ],
  nextActions: [
    {
      code: "RESOLVE_LEDGERPOSTING_BLOCKERS",
      label: "Resolve Ledger Posting blockers",
      route: "/workstation/accounting",
      gate: "LedgerPosting"
    }
  ]
};

const fixtureAccountingRecordEvidenceSubject: EvidenceSubject = {
  subjectId: fixtureOperationsWorkflowId,
  subjectKind: "accounting-record",
  label: "May 2026 accounting record",
  workspace: "Accounting",
  route: fixtureAccountingRecordEvidenceRoute,
  pageTag: "EvidenceWorkbench"
};

const fixtureAccountingRecordCompleteness: EvidenceCompleteness = {
  score: 63,
  status: "ReviewRequired",
  requiredIds: fixtureAccountingRecordEvidenceCategories.map((category) => `accounting-record:${category.key}`),
  readyIds: ["accounting-record:source-records", "accounting-record:normalized-activity", "accounting-record:ledger-evidence"],
  missingIds: ["accounting-record:reconciliation-case-history", "accounting-record:approvals", "accounting-record:report-pack", "accounting-record:exports", "accounting-record:restatement-lineage"],
  staleIds: [],
  blockingWorkItemIds: ["close-gate-approval", "exports"],
  validationIssues: [
    {
      code: "accounting-record-evidence-incomplete",
      severity: "Warning",
      message: "Demo accounting record still needs approval, report-pack, export, and restatement evidence."
    }
  ],
  blockingIssueCount: 0,
  warningIssueCount: 1,
  orphanEvidenceIds: [],
  slaPolicies: [
    {
      policyId: "accounting-record-freshness",
      evidenceKind: "accounting-record",
      workflowKind: "operations-continuity",
      freshnessMinutes: 1440,
      breachSeverity: "Warning",
      requiredForAssurance: true,
      description: "Accounting-record evidence should be regenerated daily while close work is open."
    }
  ],
  slaAssessments: [],
  assuranceScore: {
    score: 63,
    status: "ReviewRequired",
    components: [
      {
        componentId: "accounting-record-lineage",
        label: "Accounting record lineage",
        score: 63,
        status: "ReviewRequired",
        detail: "Source, normalization, and ledger evidence are retained; approval, report-pack, export, and restatement evidence remain open."
      }
    ],
    slaAssessments: []
  }
};

const fixtureAccountingRecordEvidencePacket: EvidencePacket = {
  subject: fixtureAccountingRecordEvidenceSubject,
  generatedAt: "2026-05-08T15:15:00Z",
  nodes: fixtureAccountingRecordEvidenceCategories.map((category) => ({
    evidenceId: `accounting-record:${category.key}`,
    subject: fixtureAccountingRecordEvidenceSubject,
    kind: category.key === "source-records" ? "accounting-record" : "accounting-record-category",
    status: category.isComplete ? "Ready" : "ReviewRequired",
    freshness: {
      asOf: category.isComplete ? "2026-05-08T15:15:00Z" : null,
      isStale: false,
      reason: null
    },
    sourceSystem: "development-fixture",
    summary: `${category.label}: ${category.requiredEvidence.join(", ")}`,
    artifactRefs: category.evidenceLinks.map((link, index) => ({
      artifactId: `${category.key}-artifact-${index + 1}`,
      kind: category.key,
      path: `fixtures/evidence/accounting-record/${category.key}.json`,
      route: link.route,
      generatedAt: "2026-05-08T15:15:00Z",
      hash: link.evidenceId,
      retained: true,
      canonicalSubjectKind: "accounting-record",
      canonicalSubjectId: fixtureOperationsWorkflowId
    })),
    relatedWorkItemIds: category.isComplete ? [] : [`accounting-record:${category.key}:review`]
  })),
  edges: [
    {
      fromId: "accounting-record:source-records",
      toId: "accounting-record:ledger-evidence",
      relationship: "supports",
      reason: "Retained source records support the journal and ledger evidence preview."
    },
    {
      fromId: "accounting-record:ledger-evidence",
      toId: "accounting-record:report-pack-lineage",
      relationship: "requires",
      reason: "Report-pack lineage cannot close until ledger evidence is retained."
    }
  ],
  completeness: fixtureAccountingRecordCompleteness,
  actions: [],
  warnings: ["Demo accounting record evidence is incomplete until approvals and report-pack lineage are retained."]
};

const fixtureAccountingRecordVaultIdentity: EvidenceVaultIdentity = {
  vaultId: "ev-accounting-record-demo",
  subjectKind: "accounting-record",
  subjectId: fixtureOperationsWorkflowId,
  manifestPath: `workstation/evidence/accounting-record/${fixtureOperationsWorkflowId}/manifest.json`,
  manifestRoute: `/workstation/evidence/accounting-record/${fixtureOperationsWorkflowId}/manifest.json`,
  retainedAt: "2026-05-08T15:15:00Z",
  contentHashSha256: "a".repeat(64),
  schemaVersion: 1,
  storageKind: "file-bundle",
  artifacts: [
    {
      artifactId: "accounting-record-ledger-artifact",
      kind: "ledger-evidence",
      relativePath: "workstation/evidence/_vault/ev-accounting-record-demo/artifacts/ledger-evidence.json",
      contentHashSha256: "b".repeat(64),
      sizeBytes: 2048,
      retainedAt: "2026-05-08T15:15:00Z",
      sourcePath: null,
      sourceRoute: "/accounting/ledger",
      canonicalSubjectKind: "accounting-record",
      canonicalSubjectId: fixtureOperationsWorkflowId
    }
  ],
  supportRequests: [
    {
      requestId: "support-request:validationissue:accounting-record-report-pack-lineage:report-pack-lineage-required",
      requestKind: "ValidationIssue",
      evidenceId: "accounting-record:report-pack-lineage",
      evidenceKind: "report-pack-lineage",
      severity: "Warning",
      status: "Open",
      summary: "Report-pack lineage cannot close until ledger evidence is retained.",
      sourceSystem: "operations",
      workItemId: null,
      blockedOutput: null
    }
  ]
};

const fixtureEvidenceVaultDocuments: EvidenceVaultDocumentEntry[] = [
  {
    document: {
      documentId: "doc:ev-accounting-record-demo",
      fileName: "operating-bank-statement.csv",
      classification: "BankEvidence",
      sourceHashSha256: "c".repeat(64),
      receivedAt: "2026-05-08T15:12:00Z",
      sourceChannel: "upload",
      actor: "fund-controller",
      tenantId: "tenant-alpha",
      scope: "fund-alpha",
      extractionStatus: "NeedsReview",
      objectLinks: [
        {
          linkKind: "CloseTask",
          objectId: "close-task:cash-support",
          label: "Cash support",
          route: "/workstation/accounting/close/tasks/cash-support",
          relationship: "blocks-close-readiness"
        },
        {
          linkKind: "Journal",
          objectId: fixtureOperationsWorkflowId,
          relationship: "supports-accounting-record"
        }
      ],
      reviewerState: {
        status: "NeedsReview",
        reviewer: "fund-controller",
        reviewedAt: null,
        notes: "Statement amount needs operating-account cross-check."
      },
      auditTrail: [
        {
          recordedAt: "2026-05-08T15:12:00Z",
          actor: "fund-controller",
          action: "DocumentIntakeRetained",
          summary: "Retained BankEvidence document 'operating-bank-statement.csv' through upload intake.",
          correlationId: "ev-accounting-record-demo"
        }
      ],
      contentType: "text/csv",
      sourceSystem: "operator-upload",
      sourceReference: "file://operating-bank-statement.csv",
      vaultId: "ev-accounting-record-demo",
      artifactId: "accounting-record-ledger-artifact",
      manifestRoute: `/workstation/evidence/accounting-record/${fixtureOperationsWorkflowId}/manifest.json`,
      extractorId: "manual-metadata-v1"
    },
    vaultId: fixtureAccountingRecordVaultIdentity.vaultId,
    subjectKind: fixtureAccountingRecordVaultIdentity.subjectKind,
    subjectId: fixtureAccountingRecordVaultIdentity.subjectId,
    manifestRoute: fixtureAccountingRecordVaultIdentity.manifestRoute,
    retainedAt: fixtureAccountingRecordVaultIdentity.retainedAt,
    storageKind: fixtureAccountingRecordVaultIdentity.storageKind,
    openRequestCount: 1,
    supportRequests: fixtureAccountingRecordVaultIdentity.supportRequests
  }
];

fixtureAccountingRecordVaultIdentity.documents = fixtureEvidenceVaultDocuments.map((entry) => entry.document);
fixtureAccountingRecordVaultIdentity.artifacts[0].document = fixtureEvidenceVaultDocuments[0].document;

const fixtureEvidenceVaultRequestLists: EvidenceVaultRequestListEntry[] = [
  {
    requestListId: "request-list:auditrequestlist:accounting-record:close-demo",
    requestListKind: "AuditRequestList",
    targetKind: "accounting-record",
    targetId: fixtureOperationsWorkflowId,
    highestSeverity: "Warning",
    status: "Open",
    requestCount: 1,
    openRequestCount: 1,
    requestIds: ["support-request:validationissue:accounting-record-report-pack-lineage:report-pack-lineage-required"],
    evidenceKinds: ["report-pack-lineage"],
    blockedOutputs: ["close-package/demo-close"],
    summary: "Accounting record demo close has 1 frozen support request waiting on report-pack lineage evidence.",
    vaultId: fixtureAccountingRecordVaultIdentity.vaultId,
    subjectKind: fixtureAccountingRecordVaultIdentity.subjectKind,
    subjectId: fixtureAccountingRecordVaultIdentity.subjectId,
    manifestRoute: fixtureAccountingRecordVaultIdentity.manifestRoute,
    retainedAt: fixtureAccountingRecordVaultIdentity.retainedAt,
    supportRequests: fixtureAccountingRecordVaultIdentity.supportRequests
  }
];

fixtureAccountingRecordVaultIdentity.requestLists = fixtureEvidenceVaultRequestLists.map((entry) => ({
  requestListId: entry.requestListId,
  requestListKind: entry.requestListKind,
  targetKind: entry.targetKind,
  targetId: entry.targetId,
  highestSeverity: entry.highestSeverity,
  status: entry.status,
  requestCount: entry.requestCount,
  requestIds: entry.requestIds,
  evidenceKinds: entry.evidenceKinds,
  blockedOutputs: entry.blockedOutputs,
  summary: entry.summary
}));

const fixtureAccountingRecordExportResponse: EvidencePacketExportResponse = {
  subjectKind: "accounting-record",
  subjectId: fixtureOperationsWorkflowId,
  generatedAt: "2026-05-08T15:15:00Z",
  manifestPath: fixtureAccountingRecordVaultIdentity.manifestPath,
  manifestRoute: fixtureAccountingRecordVaultIdentity.manifestRoute,
  evidenceCount: fixtureAccountingRecordEvidencePacket.nodes.length,
  warningCount: fixtureAccountingRecordEvidencePacket.warnings.length,
  retained: true,
  vaultIdentity: fixtureAccountingRecordVaultIdentity
};

const fixtureOperationsContinuityWorkflows: OperationsContinuityWorkflowSummary[] = [
  {
    workflowId: fixtureOperationsContinuityWorkflow.workflowId,
    fundAccountId: fixtureOperationsContinuityWorkflow.fundAccountId,
    periodId: fixtureOperationsContinuityWorkflow.periodId,
    securityMasterSnapshotId: fixtureOperationsContinuityWorkflow.securityMasterSnapshotId,
    brokerSource: fixtureOperationsContinuityWorkflow.brokerSource,
    status: fixtureOperationsContinuityWorkflow.status,
    version: fixtureOperationsContinuityWorkflow.version,
    createdAtUtc: fixtureOperationsContinuityWorkflow.createdAtUtc,
    updatedAtUtc: fixtureOperationsContinuityWorkflow.updatedAtUtc,
    gates: fixtureOperationsContinuityWorkflow.gates,
    nextActions: fixtureOperationsContinuityWorkflow.nextActions
  }
];

const fixtureRolePermissionCatalog: RolePermissionCatalog = {
  roles: [
    {
      role: "Accounting",
      displayName: "Accounting",
      description: "Accounting and fund-operations access for trade records, exports, and direct-lending operations.",
      isBuiltIn: true,
      permissions: ["ViewTrades", "ExportData", "ViewDirectLending", "ManageDirectLending"],
      permissionMask: 0
    },
    {
      role: "Admin",
      displayName: "Admin",
      description: "Full platform administration including users, configuration, credentials, storage, trading, and governed operations.",
      isBuiltIn: true,
      permissions: ["ManageUsers", "AdminMaintenance", "ModifyConfig", "ManageCredentials"],
      permissionMask: 0
    }
  ],
  permissions: [
    { name: "ManageUsers", value: 0, group: "Administration", description: "Create, modify, or delete user accounts." },
    { name: "AdminMaintenance", value: 0, group: "Administration", description: "Run admin maintenance routines." },
    { name: "ManageDirectLending", value: 0, group: "Direct lending", description: "Create and service direct-lending contracts." },
    { name: "ModifySecurityMaster", value: 0, group: "Security Master", description: "Create or update Security Master entries." }
  ]
};

const fixtureLedgerMappingWorkbench: LedgerMappingWorkbench = {
  asOf: "2026-05-28T00:00:00Z",
  accountCount: 3,
  mappedAccountCount: 2,
  unmappedAccountCount: 1,
  ledgerGroups: [
    {
      ledgerGroupId: "lg-direct-lending",
      displayName: "Direct lending ledger",
      accountIds: ["fund-account-1"],
      investmentPortfolioIds: [],
      clientIds: [],
      fundIds: [],
      sleeveIds: [],
      vehicleIds: []
    }
  ],
  accounts: [
    {
      accountId: "fund-account-1",
      accountCode: "DL-001",
      displayName: "Direct Lending SMA",
      accountType: "ManagedAccount",
      operationalStatus: "Active",
      baseCurrency: "USD",
      institution: "Meridian Bank",
      fundId: null,
      sleeveId: null,
      vehicleId: null,
      entityId: null,
      portfolioId: null,
      ledgerReference: "lg-direct-lending",
      mapping: {
        ledgerGroupId: "lg-direct-lending",
        source: "AccountLedgerReference",
        sourceNodeId: "fund-account-1",
        sourceNodeKind: "Account",
        sourceReference: "lg-direct-lending",
        requiresUserMapping: false,
        issueCodes: []
      },
      recommendedAction: "No mapping action required."
    },
    {
      accountId: "fund-account-2",
      accountCode: "OPS-REVIEW",
      displayName: "Close Review Account",
      accountType: "ManagedAccount",
      operationalStatus: "Active",
      baseCurrency: "USD",
      institution: "Meridian Bank",
      fundId: null,
      sleeveId: null,
      vehicleId: null,
      entityId: null,
      portfolioId: null,
      ledgerReference: null,
      mapping: {
        ledgerGroupId: "unassigned",
        source: "Unassigned",
        sourceNodeId: null,
        sourceNodeKind: null,
        sourceReference: null,
        requiresUserMapping: true,
        issueCodes: ["ledger-mapping.missing"]
      },
      recommendedAction: "Assign a ledger group before posting close journals."
    }
  ]
};

const fixtureOperationsApprovalPolicyMatrix: OperationsApprovalPolicyMatrix = {
  policyId: "operations-continuity-close",
  version: "2026.05",
  generatedAtUtc: "2026-05-28T00:00:00Z",
  rows: [
    {
      policyKey: "close-checklist-control-approvals",
      workflowArea: "Account close",
      action: "Approve close checklist controls",
      gate: "Approval",
      trigger: "ReadyForClose",
      requiredPermission: "AdminMaintenance",
      submitterRole: "Accounting",
      reviewerRole: "Admin",
      requiredDistinctApprovals: 2,
      requiresIndependentReviewer: true,
      requiresReportPack: true,
      requiresChecklistControlApprovals: true,
      evidenceRequirement: "Checklist control approvals and report pack evidence",
      auditEventType: "close-checklist-control-approved",
      route: "/accounting/operations-continuity",
      severity: "High"
    }
  ]
};

const fixtureOperationsCloseCalendar: OperationsCloseCalendar = {
  generatedAtUtc: "2026-05-28T00:00:00Z",
  items: [
    {
      workflowId: fixtureOperationsContinuityWorkflow.workflowId,
      fundAccountId: fixtureOperationsContinuityWorkflow.fundAccountId,
      periodId: fixtureOperationsContinuityWorkflow.periodId,
      status: fixtureOperationsContinuityWorkflow.status,
      version: fixtureOperationsContinuityWorkflow.version,
      nextDueDate: "2026-05-31",
      nextDueTaskId: "close-review",
      nextDueLabel: "Resolve ledger posting blockers",
      nextDueOwner: "Accounting",
      readinessSeverity: "Warning",
      readinessScore: 68,
      isReadyToClose: false,
      blockerCount: 1,
      openChecklistCount: 2,
      requiredApprovalCount: 2,
      completedApprovalCount: 1,
      route: "/accounting/operations-continuity"
    }
  ]
};

const fixturePrivateCapitalCloseCockpit: PrivateCapitalCloseCockpit = {
  fundProfileId: "default-fund",
  ledgerBookId: null,
  fundAccountId: fixtureOperationsContinuityWorkflow.fundAccountId,
  periodId: fixtureOperationsContinuityWorkflow.periodId,
  entityId: "entity-master",
  projectedAtUtc: "2026-05-28T00:00:00Z",
  cockpitRoute: "/accounting/operations-continuity",
  overallStatus: "ReviewRequired",
  isReadyToClose: false,
  readinessScore: 68,
  workflowCount: 1,
  fundEventCount: 3,
  capitalAccountCount: 4,
  reportOutputCount: 2,
  deliveredReportOutputCount: 1,
  readyLaneCount: 3,
  blockedLaneCount: 2,
  lanes: [
    {
      laneId: "fund-event-evidence",
      label: "Fund event evidence",
      status: "Ready",
      isReady: true,
      summary: "Capital activity events retain source, ledger, and approval evidence.",
      route: "/workstation/accounting/private-capital/fund-events",
      evidenceLinkCount: 2,
      evidenceLinks: fixtureAccountingRecordEvidenceLinks,
      requiredActions: []
    },
    {
      laneId: "partner-capital-tie-outs",
      label: "Partner capital account tie-outs",
      status: "Ready",
      isReady: true,
      summary: "Partner capital subledger, ledger, and investor statement evidence tie out.",
      route: "/workstation/accounting/private-capital/capital-account-subledger",
      evidenceLinkCount: 2,
      evidenceLinks: fixtureAccountingRecordEvidenceLinks.slice(0, 2),
      requiredActions: []
    },
    {
      laneId: "expense-fee-allocation",
      label: "Expense, fee, and allocation review",
      status: "Ready",
      isReady: true,
      summary: "Management fee, expense, and allocation evidence is retained for controller review.",
      route: "/workstation/accounting/private-capital/fund-events/management-fee",
      evidenceLinkCount: 2,
      evidenceLinks: fixtureAccountingRecordEvidenceLinks.slice(0, 2),
      requiredActions: []
    },
    {
      laneId: "ledger-reconciliation",
      label: "Ledger and reconciliation",
      status: "Blocked",
      isReady: false,
      summary: "Ledger posting is blocked until the controller validates the close journal draft.",
      route: "/workstation/accounting/ledger",
      evidenceLinkCount: 0,
      evidenceLinks: [],
      requiredActions: ["Validate ledger draft", "Resolve reconciliation blocker"]
    },
    {
      laneId: "nav-support",
      label: "NAV support",
      status: "ReviewRequired",
      isReady: false,
      summary: "Shadow NAV support package still needs retained position, cash, and pricing evidence.",
      route: "/workstation/portfolio/nav",
      evidenceLinkCount: 1,
      evidenceLinks: fixtureAccountingRecordEvidenceLinks.slice(0, 1),
      requiredActions: ["Retain NAV support for positions, cash, and pricing"]
    },
    {
      laneId: "close-package",
      label: "Evidence package",
      status: "Blocked",
      isReady: false,
      summary: "Close evidence package publication is blocked until the manifest is retained.",
      route: "/workstation/accounting/operations-continuity",
      evidenceLinkCount: 0,
      evidenceLinks: [],
      requiredActions: ["Publish the close package manifest"]
    },
    {
      laneId: "period-lock",
      label: "Period lock evidence",
      status: "ReviewRequired",
      isReady: false,
      summary: "The selected period remains open until close package publication succeeds.",
      route: "/workstation/accounting/operations-continuity",
      evidenceLinkCount: 0,
      evidenceLinks: [],
      requiredActions: ["Close the workflow and retain period-lock evidence"]
    }
  ],
  workflows: [
    {
      workflowId: fixtureOperationsContinuityWorkflow.workflowId,
      fundAccountId: fixtureOperationsContinuityWorkflow.fundAccountId,
      periodId: fixtureOperationsContinuityWorkflow.periodId,
      status: fixtureOperationsContinuityWorkflow.status,
      closeReadinessScore: 68,
      isReadyToClose: false,
      workflowRoute: "/workstation/accounting/operations-continuity",
      closePackageId: null,
      closePackageRoute: null,
      blockerCount: 1,
      openChecklistCount: 2,
      updatedAtUtc: fixtureOperationsContinuityWorkflow.updatedAtUtc
    }
  ],
  approvalHistory: [
    {
      approvalId: "approval-close-fixture-2026-05",
      workflowId: fixtureOperationsContinuityWorkflow.workflowId,
      fundAccountId: fixtureOperationsContinuityWorkflow.fundAccountId,
      periodId: fixtureOperationsContinuityWorkflow.periodId,
      status: "ReviewerAssigned",
      operator: "ops-user",
      reviewer: "fund-controller",
      rationale: "Pending final ledger validation before close sign-off.",
      submittedAtUtc: "2026-05-08T15:05:00Z",
      decidedAtUtc: null,
      workflowRoute: "/workstation/accounting/operations-continuity",
      evidenceLinkCount: 1,
      evidenceLinks: [
        {
          evidenceId: "fixture-approval-evidence",
          label: "Fixture approval assignment",
          route: "/workstation/accounting/approvals",
          source: "development-fixture",
          capturedAtUtc: "2026-05-08T15:05:00Z"
        }
      ]
    }
  ],
  navSupportPackages: [
    {
      packageId: "nav-support:fixture:2026-05",
      label: "NAV support package",
      status: "ReviewRequired",
      isReady: false,
      summary: "NAV support package has retained cash and position evidence but still needs pricing and shadow NAV evidence.",
      route: "/workstation/portfolio/nav",
      shadowNav: 1250000,
      currency: "USD",
      evidenceLinkCount: 1,
      evidenceLinks: [
        {
          evidenceId: "fixture-nav-support-evidence",
          label: "Fixture NAV support evidence",
          route: "/workstation/portfolio/nav/support-package",
          source: "development-fixture",
          capturedAtUtc: "2026-05-10T18:10:00Z"
        }
      ],
      components: [
        {
          componentId: "positions",
          label: "Positions",
          status: "Ready",
          isReady: true,
          summary: "Position support retained.",
          route: "/workstation/portfolio",
          score: 100
        },
        {
          componentId: "cash",
          label: "Cash",
          status: "Ready",
          isReady: true,
          summary: "Cash support retained.",
          route: "/workstation/accounting/cash",
          score: 100
        },
        {
          componentId: "pricing",
          label: "Pricing",
          status: "ReviewRequired",
          isReady: false,
          summary: "Pricing support still needs retained evidence.",
          route: "/workstation/data/pricing",
          score: 60
        },
        {
          componentId: "shadow-nav",
          label: "Shadow NAV",
          status: "ReviewRequired",
          isReady: false,
          summary: "Shadow NAV report output evidence is pending.",
          route: "/workstation/reporting/shadow-nav-pack",
          score: 50
        }
      ],
      requiredActions: ["Retain NAV support package for positions, cash, pricing, and shadow NAV evidence."]
    }
  ],
  blockers: [
    {
      code: "LEDGER_VALIDATION_REQUIRED",
      category: "Ledger",
      severity: "Critical",
      message: "Ledger posting requires a balanced and validated journal draft.",
      gate: "LedgerPosting",
      routeHint: "/workstation/accounting/ledger"
    }
  ],
  nextActions: [
    {
      code: "RESOLVE_LEDGERPOSTING_BLOCKERS",
      label: "Resolve Ledger Posting blockers",
      route: "/workstation/accounting/ledger",
      gate: "LedgerPosting"
    }
  ],
  liveCapabilities: [
    "workflow-readiness",
    "partner-tie-out-evidence",
    "allocation-review",
    "nav-support-lineage",
    "close-package-evidence",
    "period-lock-evidence"
  ],
  plannedCapabilities: ["tax-support-drilldown", "delivery-recipient-entitlement"]
};

const fixtureLedgerTrialBalance: LedgerTrialBalanceLine[] = [
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
    security: null,
    sourceJournalEntryId: "je-financing-1",
    sourceEventIds: ["evt-financing-1"],
    approvalIds: ["approval-financing-1"]
  }
];

type FixtureFinancialRecordExplorerSeed = {
  explorerId: string;
  title: string;
  description: string;
  sourceState: string;
  workstream: string;
  source: string;
  savedViewLabel: string;
  summaryItems: FinancialRecordExplorerDto["summaryItems"];
  filters: FinancialRecordExplorerDto["filters"];
  columns: FinancialRecordExplorerDto["columns"];
  row: {
    recordId: string;
    recordType: string;
    label: string;
    source: string;
    status: string;
    tone: FinancialRecordExplorerDto["rows"][number]["tone"];
    cells: FinancialRecordExplorerDto["rows"][number]["cells"];
    detailTitle: string;
    detailSubtitle: string;
    detailDescription: string;
    fields: FinancialRecordExplorerDto["summaryItems"];
    proofHref: string;
    fullRecordHref: string;
    usedInLabel: string;
    impactsLabel: string;
  };
};

function buildFixtureFinancialRecordExplorer(explorerId: string): FinancialRecordExplorerDto | undefined {
  switch (explorerId) {
    case "ledger":
      return createFixtureFinancialRecordExplorer({
        explorerId,
        title: "Ledger Explorer",
        description: "Explore retained trial-balance rows, journal support, and close evidence.",
        sourceState: "No-host fixture projection from run run-42 trial-balance evidence.",
        workstream: "Accounting",
        source: "Journal entries and ledger detail",
        savedViewLabel: "Controller review",
        summaryItems: [
          { label: "Rows", value: "2", detail: "Retained trial-balance rows.", tone: "Success" },
          { label: "Cash", value: "$120,500", detail: "Source-backed ledger cash balance.", tone: "Success" },
          { label: "Open breaks", value: "1", detail: "Cash variance remains under review.", tone: "Warning" }
        ],
        filters: [
          { filterId: "accounts", label: "Accounts", value: "All active accounts", operator: "equals", tone: "Info" }
        ],
        columns: [
          { columnId: "account", header: "Account", cellKind: "text", width: 220, isRightAligned: false },
          { columnId: "type", header: "Type", cellKind: "text", width: 120, isRightAligned: false },
          { columnId: "balance", header: "Balance", cellKind: "currency", width: 120, isRightAligned: true }
        ],
        row: {
          recordId: "ledger:run-42:cash",
          recordType: "Ledger account",
          label: "Cash",
          source: "Trial balance",
          status: "ReviewRequired",
          tone: "Warning",
          cells: [
            { columnId: "account", displayValue: "Cash", rawValue: "Cash", tone: "Success", linkHref: "" },
            { columnId: "type", displayValue: "Asset", rawValue: "Asset", tone: "Default", linkHref: "" },
            { columnId: "balance", displayValue: "$120,500", rawValue: "120500", tone: "Success", linkHref: "" }
          ],
          detailTitle: "Cash",
          detailSubtitle: "Asset - run-42",
          detailDescription: "Source-backed cash balance with one retained variance awaiting controller review.",
          fields: [
            { label: "Balance", value: "$120,500", detail: "Ledger cash from trial-balance fixture.", tone: "Success" },
            { label: "Entries", value: "12", detail: "Journal entry count retained with the row.", tone: "Default" },
            { label: "Approval", value: "approval-cash-1", detail: "Approval evidence remains linked.", tone: "Info" }
          ],
          proofHref: "/reporting/evidence?subjectKind=accounting-record&subjectId=accounting-record-2026-05",
          fullRecordHref: "/api/workstation/runs/run-42/ledger/trial-balance",
          usedInLabel: "Accounting close",
          impactsLabel: "Cash reconciliation"
        }
      });
    case "portfolio":
      return createFixtureFinancialRecordExplorer({
        explorerId,
        title: "Portfolio Explorer",
        description: "Explore retained account, position, and aggregate portfolio records.",
        sourceState: "No-host fixture projection from the Portfolio workspace and active paper session.",
        workstream: "Portfolio",
        source: "Trading and brokerage evidence",
        savedViewLabel: "Open positions + run evidence",
        summaryItems: [
          { label: "Positions", value: "1", detail: "Retained open position rows.", tone: "Success" },
          { label: "Exposure", value: "$18,900", detail: "Source-backed AAPL exposure.", tone: "Default" },
          { label: "Sync", value: "Stale", detail: "Brokerage sync requires review.", tone: "Warning" }
        ],
        filters: [
          { filterId: "symbol", label: "Symbol", value: "AAPL", operator: "equals", tone: "Info" }
        ],
        columns: [
          { columnId: "symbol", header: "Symbol", cellKind: "text", width: 100, isRightAligned: false },
          { columnId: "quantity", header: "Quantity", cellKind: "number", width: 100, isRightAligned: true },
          { columnId: "exposure", header: "Exposure", cellKind: "currency", width: 120, isRightAligned: true }
        ],
        row: {
          recordId: "portfolio:paper-dev-42:AAPL",
          recordType: "Portfolio position",
          label: "AAPL",
          source: "Portfolio",
          status: "Long",
          tone: "Success",
          cells: [
            { columnId: "symbol", displayValue: "AAPL", rawValue: "AAPL", tone: "Success", linkHref: "" },
            { columnId: "quantity", displayValue: "100", rawValue: "100", tone: "Default", linkHref: "" },
            { columnId: "exposure", displayValue: "$18,900", rawValue: "18900", tone: "Default", linkHref: "" }
          ],
          detailTitle: "AAPL",
          detailSubtitle: "Long - paper-dev-42",
          detailDescription: "Source-backed portfolio position retained for account and aggregate review.",
          fields: [
            { label: "Quantity", value: "100", detail: "Retained position quantity.", tone: "Default" },
            { label: "Unrealized P&L", value: "+$90", detail: "Source-backed unrealized P&L.", tone: "Success" },
            { label: "Exposure", value: "$18,900", detail: "Position exposure from the portfolio fixture.", tone: "Default" }
          ],
          proofHref: "/reporting/evidence?subjectKind=portfolio-position&subjectId=portfolio:paper-dev-42:AAPL",
          fullRecordHref: "/portfolio",
          usedInLabel: "Portfolio run",
          impactsLabel: "Portfolio equity"
        }
      });
    case "security-instrument":
      return createFixtureFinancialRecordExplorer({
        explorerId,
        title: "Security & Instrument Explorer",
        description: "Explore retained instrument identity, classification, and trading-control evidence.",
        sourceState: "No-host fixture projection from Security Master instrument coverage.",
        workstream: "Accounting",
        source: "Security Master instruments",
        savedViewLabel: "Instrument proof",
        summaryItems: [
          { label: "Coverage", value: "Verification pending", detail: "Confirm conflicts, reference routes, and passport evidence before relying on coverage.", tone: "Warning" },
          { label: "Conflicts", value: "0", detail: "No open identity conflict for AAPL.", tone: "Success" }
        ],
        filters: [
          { filterId: "asset-class", label: "Asset class", value: "Equity", operator: "equals", tone: "Info" }
        ],
        columns: [
          { columnId: "security", header: "Security", cellKind: "text", width: 220, isRightAligned: false },
          { columnId: "assetClass", header: "Asset class", cellKind: "text", width: 120, isRightAligned: false },
          { columnId: "status", header: "Status", cellKind: "text", width: 100, isRightAligned: false }
        ],
        row: {
          recordId: "security-instrument:sec-dev-001",
          recordType: "Security instrument",
          label: "Apple Inc.",
          source: "Security Master",
          status: "Ready",
          tone: "Success",
          cells: [
            { columnId: "security", displayValue: "Apple Inc.", rawValue: "Apple Inc.", tone: "Success", linkHref: "" },
            { columnId: "assetClass", displayValue: "Equity", rawValue: "Equity", tone: "Default", linkHref: "" },
            { columnId: "status", displayValue: "Ready", rawValue: "Ready", tone: "Success", linkHref: "" }
          ],
          detailTitle: "Apple Inc.",
          detailSubtitle: "Ticker AAPL - sec-dev-001",
          detailDescription: "Retained Security Master identity used by ledger, portfolio, and trading controls.",
          fields: [
            { label: "Ticker", value: "AAPL", detail: "Primary listed equity identifier.", tone: "Success" },
            { label: "Currency", value: "USD", detail: "Economic definition currency.", tone: "Default" }
          ],
          proofHref: "/accounting/security-master?query=AAPL",
          fullRecordHref: "/accounting/security-master",
          usedInLabel: "Ledger support",
          impactsLabel: "Trading controls"
        }
      });
    case "report-line-provenance":
      return createFixtureFinancialRecordExplorer({
        explorerId,
        title: "Report-Line Provenance Explorer",
        description: "Trace retained report lines back to source records, reconciliation, and approvals.",
        sourceState: "No-host fixture projection from report-pack evidence.",
        workstream: "Reporting",
        source: "Report pack",
        savedViewLabel: "Board report lines",
        summaryItems: [
          { label: "Report lines", value: "1", detail: "Retained report-line proof row.", tone: "Success" },
          { label: "Evidence", value: "Linked", detail: "Source record and approval routes are retained.", tone: "Success" }
        ],
        filters: [
          { filterId: "recipient", label: "Recipient", value: "Board", operator: "equals", tone: "Info" }
        ],
        columns: [
          { columnId: "line", header: "Line", cellKind: "text", width: 220, isRightAligned: false },
          { columnId: "source", header: "Source", cellKind: "text", width: 160, isRightAligned: false },
          { columnId: "status", header: "Status", cellKind: "text", width: 100, isRightAligned: false }
        ],
        row: {
          recordId: "report-line:board:pnl",
          recordType: "Report line",
          label: "Daily P&L",
          source: "Reporting",
          status: "Ready",
          tone: "Success",
          cells: [
            { columnId: "line", displayValue: "Daily P&L", rawValue: "Daily P&L", tone: "Success", linkHref: "" },
            { columnId: "source", displayValue: "Accounting P&L slice", rawValue: "pnl:daily", tone: "Default", linkHref: "" },
            { columnId: "status", displayValue: "Ready", rawValue: "Ready", tone: "Success", linkHref: "" }
          ],
          detailTitle: "Daily P&L",
          detailSubtitle: "Board report pack",
          detailDescription: "Report line retains source P&L slice, reconciliation, approval, and audit links.",
          fields: [
            { label: "Amount", value: "$4,400", detail: "Daily total P&L from source-backed reporting slice.", tone: "Success" },
            { label: "Source count", value: "2", detail: "Two retained source runs support the line.", tone: "Default" }
          ],
          proofHref: "/reporting/evidence?subjectKind=report-line&subjectId=report-line:board:pnl",
          fullRecordHref: "/reporting/report-packs",
          usedInLabel: "Board report pack",
          impactsLabel: "Distribution approval"
        }
      });
    default:
      return undefined;
  }
}

function createFixtureFinancialRecordExplorer(seed: FixtureFinancialRecordExplorerSeed): FinancialRecordExplorerDto {
  const detail = {
    recordId: seed.row.recordId,
    recordType: seed.row.recordType,
    title: seed.row.detailTitle,
    subtitle: seed.row.detailSubtitle,
    description: seed.row.detailDescription,
    tone: seed.row.tone,
    fields: seed.row.fields,
    proofActions: [
      {
        actionId: "open-source",
        label: "Open source record",
        description: "Open the retained source-backed record.",
        href: seed.row.proofHref,
        isEnabled: true,
        disabledReason: "",
        tone: "Info" as const
      }
    ],
    usedIn: [
      {
        relationshipId: `${seed.explorerId}:used-in`,
        label: seed.row.usedInLabel,
        description: `${seed.row.detailTitle} is used by the ${seed.row.usedInLabel.toLowerCase()} workflow.`,
        href: seed.row.fullRecordHref,
        tone: "Info" as const
      }
    ],
    impacts: [
      {
        relationshipId: `${seed.explorerId}:impacts`,
        label: seed.row.impactsLabel,
        description: `${seed.row.detailTitle} contributes to ${seed.row.impactsLabel.toLowerCase()} evidence.`,
        href: seed.row.proofHref,
        tone: seed.row.tone
      }
    ],
    fullRecordHref: seed.row.fullRecordHref
  };

  return {
    explorerId: seed.explorerId,
    title: seed.title,
    description: seed.description,
    sourceState: seed.sourceState,
    isBlocked: false,
    blockedReason: "",
    scopeItems: [
      { label: "Workstream", value: seed.workstream, tone: "Info" },
      { label: "Source", value: seed.source, tone: "Default" }
    ],
    savedViews: [
      {
        viewId: `system-${seed.explorerId}-default`,
        label: seed.savedViewLabel,
        description: "Default no-host fixture explorer view.",
        isSystem: true,
        isActive: true,
        filters: seed.filters,
        searchText: ""
      }
    ],
    summaryItems: seed.summaryItems,
    filters: seed.filters,
    columns: seed.columns,
    rows: [
      {
        recordId: seed.row.recordId,
        recordType: seed.row.recordType,
        label: seed.row.label,
        source: seed.row.source,
        status: seed.row.status,
        tone: seed.row.tone,
        cells: seed.row.cells,
        detail
      }
    ],
    selectedRecord: detail,
    proofActions: [
      {
        actionId: "evidence",
        label: "Open evidence packet",
        description: "Open retained evidence for this explorer.",
        href: seed.row.proofHref,
        isEnabled: true,
        disabledReason: "",
        tone: "Info"
      }
    ],
    recordGraph: {
      nodes: [
        { nodeId: seed.row.recordId, label: seed.row.label, nodeType: seed.row.recordType, tone: seed.row.tone, href: seed.row.fullRecordHref },
        { nodeId: `${seed.explorerId}:evidence`, label: "Evidence", nodeType: "Evidence", tone: "Info", href: seed.row.proofHref }
      ],
      edges: [
        { sourceNodeId: seed.row.recordId, targetNodeId: `${seed.explorerId}:evidence`, label: "retains", tone: "Info" }
      ]
    }
  };
}

const fixtures = {
  [WORKSTATION_API_ENDPOINTS.systemStatus]: fixtureSystemOverview,
  [WORKSTATION_API_ENDPOINTS.session]: fixtureSession,
  [WORKSTATION_API_ENDPOINTS.strategy]: fixtureStrategyWorkspace,
  [WORKSTATION_API_ENDPOINTS.strategyBriefing]: fixtureStrategyBriefing,
  "/api/workstation/research": fixtureStrategyWorkspace,
  [WORKSTATION_API_ENDPOINTS.trading]: fixtureTradingWorkspace,
  [WORKSTATION_API_ENDPOINTS.portfolio]: fixturePortfolioWorkspace,
  [workstationFinancialRecordExplorerEndpoint("portfolio")]: fixturePortfolioFinancialRecordExplorer,
  [WORKSTATION_API_ENDPOINTS.portfolioMultiAssetCoverage]: fixturePortfolioMultiAssetCoverage,
  [WORKSTATION_API_ENDPOINTS.tradingReadiness]: fixtureTradingReadiness,
  [WORKSTATION_API_ENDPOINTS.operatorInbox]: fixtureOperatorInbox,
  [WORKSTATION_API_ENDPOINTS.workflowSummary]: fixtureWorkflowSummary,
  [WORKSTATION_API_ENDPOINTS.featureCapabilities]: fixtureFeatureCapabilities,
  [WORKSTATION_API_ENDPOINTS.workflowLibrary]: fixtureWorkflowLibrary,
  [WORKSTATION_API_ENDPOINTS.workflowPresets]: fixtureWorkflowPresetLibrary,
  [WORKSTATION_API_ENDPOINTS.operationsContinuity]: fixtureOperationsContinuityWorkflows,
  [WORKSTATION_API_ENDPOINTS.operationsContinuityApprovalPolicyMatrix]: fixtureOperationsApprovalPolicyMatrix,
  [WORKSTATION_API_ENDPOINTS.operationsContinuityCloseCalendar]: fixtureOperationsCloseCalendar,
  [WORKSTATION_API_ENDPOINTS.operationsPrivateCapitalCloseCockpit]: fixturePrivateCapitalCloseCockpit,
  [WORKSTATION_API_ENDPOINTS.evidenceSubjects]: [fixtureAccountingRecordEvidenceSubject],
  [WORKSTATION_API_ENDPOINTS.evidenceVaultSearch]: [fixtureAccountingRecordVaultIdentity],
  [WORKSTATION_API_ENDPOINTS.evidenceVaultRequestLists]: fixtureEvidenceVaultRequestLists,
  [WORKSTATION_API_ENDPOINTS.evidenceVaultDocuments]: fixtureEvidenceVaultDocuments,
  [AUTH_API_ENDPOINTS.roles]: fixtureRolePermissionCatalog,
  [AUTH_API_ENDPOINTS.accessAssignments]: fixtureAccessAssignments,
  [FUND_STRUCTURE_API_ENDPOINTS.ledgerMappingWorkbench]: fixtureLedgerMappingWorkbench,
  [EXECUTION_API_ENDPOINTS.sessions]: fixturePaperSessionSummaries,
  [EXECUTION_API_ENDPOINTS.audit]: fixtureExecutionAudit,
  [EXECUTION_API_ENDPOINTS.controls]: fixtureExecutionControls,
  [REPLAY_API_ENDPOINTS.files]: fixtureReplayFiles,
  [PROMOTION_API_ENDPOINTS.history]: fixturePromotionHistory,
  [brokerageConnectionStatusEndpoint("alpaca")]: fixtureAlpacaConnection,
  [brokerageConnectionStatusEndpoint("robinhood")]: fixtureAlpacaConnection,
  [PORTFOLIO_API_ENDPOINTS.household]: fixtureAlpacaPortfolio,
  [WORKSTATION_API_ENDPOINTS.data]: fixtureDataWorkspace,
  "/api/workstation/data-operations": fixtureDataWorkspace,
  [PROVIDER_API_ENDPOINTS.connections]: fixtureProviderConnections,
  [PROVIDER_API_ENDPOINTS.readiness]: fixtureProviderReadiness,
  [PROVIDER_ROUTING_API_ENDPOINTS.connections]: fixtureProviderRoutingConnections,
  [PROVIDER_ROUTING_API_ENDPOINTS.bindings]: fixtureProviderRoutingBindings,
  [PROVIDER_ROUTING_API_ENDPOINTS.trustSnapshots]: fixtureProviderRoutingTrustSnapshots,
  [WORKSTATION_API_ENDPOINTS.accounting]: fixtureAccountingWorkspace,
  [WORKSTATION_API_ENDPOINTS.reporting]: fixtureAccountingWorkspace.reporting,
  [WORKSTATION_API_ENDPOINTS.accountingConfiguration]: fixtureAccountingConfiguration,
  [WORKSTATION_API_ENDPOINTS.accountingConfigurationPostingRuleDryRun]: fixtureAccountingRuleDryRun,
  [WORKSTATION_API_ENDPOINTS.closeManagementPeriodPlan]: fixtureLedgerClosePeriodPlan,
  [WORKSTATION_API_ENDPOINTS.closeManagementPeriodPlanConfiguration]: fixtureLedgerClosePeriodPlan,
  [WORKSTATION_API_ENDPOINTS.closeManagementLateAdjustments]: fixtureLedgerClosePeriodPlan,
  [WORKSTATION_API_ENDPOINTS.accountingReportPackage]: fixtureAccountingReportPackage,
  [WORKSTATION_API_ENDPOINTS.accountingReportPackages]: [fixtureAccountingReportPackage],
  [ACCOUNTING_SYSTEM_API_ENDPOINTS.providers]: fixtureAccountingSystemProviders,
  [ACCOUNTING_SYSTEM_API_ENDPOINTS.productionReadiness]: fixtureAccountingProductionReadiness,
  [ACCOUNTING_SYSTEM_API_ENDPOINTS.importPreview]: fixtureAccountingSystemImport,
  [ACCOUNTING_SYSTEM_API_ENDPOINTS.importLatest]: fixtureAccountingSystemImport,
  [ACCOUNTING_SYSTEM_API_ENDPOINTS.reconciliationLatest]: fixtureAccountingSystemReconciliation,
  [ACCOUNTING_SYSTEM_API_ENDPOINTS.mappingProfiles]: fixtureAccountingSystemMappingProfiles,
  [ACCOUNTING_SYSTEM_API_ENDPOINTS.exportPackages]: [fixtureAccountingSystemExportPackage],
  [ACCOUNTING_SYSTEM_API_ENDPOINTS.migrationRunArtifacts]: {
    fundProfileId: fixtureAccountingProductionReadiness.fundProfileId,
    ledgerBookId: fixtureAccountingProductionReadiness.ledgerBookId,
    artifacts: fixtureAccountingProductionReadiness.migrationRunArtifacts ?? []
  },
  [ACCOUNTING_SYSTEM_API_ENDPOINTS.migrationWorkerPlans]: {
    fundProfileId: fixtureAccountingProductionReadiness.fundProfileId,
    ledgerBookId: fixtureAccountingProductionReadiness.ledgerBookId,
    kind: null,
    tenantId: "fixture-tenant",
    companyId: "fixture-company",
    plans: [
      {
        planId: "fixture-historical-worker-plan",
        kind: "HistoricalJournalBackfill",
        fundProfileId: fixtureAccountingProductionReadiness.fundProfileId,
        ledgerBookId: fixtureAccountingProductionReadiness.ledgerBookId ?? "",
        sourceRecordCount: 275,
        migratedRecordCount: 275,
        evidenceReferences: ["fixture://migration-worker-plan/historical-journal"],
        tenantId: "fixture-tenant",
        companyId: "fixture-company",
        summary: "Fixture worker plan reconciles historical journal source and migrated rows."
      }
    ]
  },
  [ACCOUNTING_SYSTEM_API_ENDPOINTS.tenantAdministrationProfile]: {
    tenantId: "fixture-tenant",
    companyId: "fixture-company",
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
    updatedAtUtc: "2026-02-01T00:15:00Z",
    updatedBy: "fixture-controller",
    evidenceReferences: ["fixture:tenant-admin:gap"],
    correlationId: "fixture-tenant-admin"
  },
  [ACCOUNTING_SYSTEM_API_ENDPOINTS.productionCertificationProfile]: {
    fundProfileId: fixtureAccountingProductionReadiness.fundProfileId,
    ledgerBookId: fixtureAccountingProductionReadiness.ledgerBookId,
    postingRulesLedgerBookNativeCertified: false,
    journalLifecycleLedgerBookNativeCertified: false,
    closeReportingLedgerBookNativeCertified: false,
    externalGlLedgerBookNativeCertified: false,
    reconciliationLedgerBookNativeCertified: false,
    directLendingLedgerBookNativeCertified: false,
    strategyLedgerReadLedgerBookNativeCertified: false,
    periodReportDimensionQueriesCertified: true,
    crossPeriodReportDimensionQueriesCertified: false,
    journalQueryDimensionFiltersCertified: true,
    externalExportDimensionMappingCertified: false,
    ledgerLineDimensionsPersistedCertified: false,
    trialBalanceDimensionFiltersCertified: false,
    reportPackageDimensionProvenanceCertified: false,
    updatedAtUtc: "2026-02-01T00:15:00Z",
    updatedBy: "fixture-controller",
    evidenceReferences: ["fixture:production-certification:dimensions-gap"],
    correlationId: "fixture-production-certification"
  },
  "/api/workstation/runs/run-42/ledger/trial-balance": fixtureLedgerTrialBalance,
  "/api/workstation/governance": fixtureAccountingWorkspace,
  [RECONCILIATION_API_ENDPOINTS.breakQueue]: fixtureAccountingWorkspace.breakQueue,
  [RECONCILIATION_API_ENDPOINTS.statementRuns]: fixtureStatementRuns,
  [RECONCILIATION_API_ENDPOINTS.calibrationSummary]: fixtureCalibrationSummary,
  [STATEMENT_CONNECTOR_API_ENDPOINTS.connectors]: fixtureStatementConnectors,
  [STATEMENT_CONNECTOR_API_ENDPOINTS.mappingProfiles]: fixtureStatementMappingProfiles,
  [QUANT_API_ENDPOINTS.templates]: fixtureQuantTemplates,
  [QUANT_API_ENDPOINTS.parameters]: fixtureQuantParameters,
  [STRATEGY_DESIGNER_API_ENDPOINTS.templates]: fixtureStrategyDesignerTemplates,
  [STRATEGY_DESIGNER_API_ENDPOINTS.fieldCatalog]: fixtureStrategyDesignerFieldCatalog,
  [STRATEGY_DESIGNER_API_ENDPOINTS.drafts]: fixtureStrategyDesignerDrafts,
  [COVERED_CALL_API_ENDPOINTS.runs]: fixtureCoveredCallRuns,
  [COVERED_CALL_API_ENDPOINTS.chainPreview]: fixtureCoveredCallChainPreview,
  [SECURITY_MASTER_API_ENDPOINTS.assetProfiles]: fixtureSecurityAssetProfiles,
  [`${SECURITY_MASTER_API_ENDPOINTS.base}/conflicts`]: fixtureSecurityConflicts,
  [RISK_API_ENDPOINTS.rules]: fixtureRiskRules,
  [riskRuleConfigEndpoint("DrawdownCircuitBreaker")]: fixtureDrawdownRiskRuleConfig,
  ...marketDataFixtureRoutes
} satisfies Record<string, unknown>;

const financialRecordExplorerFixtureBase = WORKSTATION_API_ENDPOINTS.financialRecordExplorer.replace("/{explorerId}", "");

const dynamicFixturePatterns: DynamicFixturePattern[] = [
  {
    pattern: apiRoutePattern(financialRecordExplorerFixtureBase, "/[^/]+"),
    resolve: (cleanPath) => buildFixtureFinancialRecordExplorer(readDecodedPathSegment(cleanPath))
  },
  {
    pattern: apiRoutePattern(COVERED_CALL_API_ENDPOINTS.runs, "/[^/]+/result"),
    resolve: (cleanPath) => {
      const runId = readDecodedPathSegment(cleanPath, 1);
      return runId ? fixtureCoveredCallResults[runId] : undefined;
    }
  },
  {
    pattern: apiRoutePattern(SECURITY_MASTER_API_ENDPOINTS.workstationSecurities),
    resolve: (_cleanPath, path) => {
      const params = readFixtureSearchParams(path);
      const take = Number(params.get("take") ?? 25);
      const activeOnly = (params.get("activeOnly") ?? "true").toLowerCase() !== "false";
      return searchDevSecurityMasterEntries(
        params.get("query") ?? "",
        Number.isFinite(take) && take > 0 ? take : 25,
        activeOnly
      );
    }
  },
  {
    pattern: apiRoutePattern(SECURITY_MASTER_API_ENDPOINTS.workstationSecurities, "/[^/]+/identity"),
    resolve: (cleanPath) => {
      const securityId = cleanPath.split("/").at(-2);
      return securityId ? fixtureSecurityIdentities[securityId] : undefined;
    }
  },
  {
    pattern: apiRoutePattern(SECURITY_MASTER_API_ENDPOINTS.base, "/[^/]+/corporate-actions"),
    resolve: (cleanPath) => {
      const securityId = cleanPath.split("/").at(-2);
      return securityId ? fixtureCorporateActions.filter((action) => action.securityId === securityId) : fixtureCorporateActions;
    }
  },
  {
    pattern: apiRoutePattern(SECURITY_MASTER_API_ENDPOINTS.base, "/[^/]+/trading-parameters"),
    resolve: (cleanPath) => {
      const securityId = cleanPath.split("/").at(-2) ?? fixtureTradingParameters.securityId;
      return { ...fixtureTradingParameters, securityId };
    }
  },
  {
    pattern: apiRoutePattern(SECURITY_MASTER_API_ENDPOINTS.base, "/[^/]+/operator-overrides"),
    resolve: (cleanPath) => {
      const securityId = cleanPath.split("/").at(-2);
      return securityId
        ? fixtureOperatorOverrides[securityId] ?? {
          securityId,
          values: {},
          updatedBy: "",
          updatedAt: ""
        }
        : undefined;
    }
  },
  ...marketDataFixturePatterns,
  {
    pattern: apiRoutePattern(PROMOTION_API_ENDPOINTS.evaluate, "/[^/]+"),
    resolve: (cleanPath) => {
      const runId = readDecodedPathSegment(cleanPath);
      return runId ? fixturePromotionEvaluations[runId] : undefined;
    }
  },
  { pattern: apiRoutePattern(STRATEGY_DESIGNER_API_ENDPOINTS.drafts, "/[^/]+"), resolve: () => fixtureStrategyDesignerDocument },
  { pattern: apiRoutePattern(EXECUTION_API_ENDPOINTS.sessions, "/[^/]+"), resolve: () => fixturePaperSessionDetail },
  { pattern: apiRoutePattern(EXECUTION_API_ENDPOINTS.sessions, "/[^/]+/replay"), resolve: () => fixturePaperSessionReplayVerification },
  {
    pattern: apiRoutePattern("/api/workstation/provider-integrations/connections", "/[^/]+/monitor"),
    resolve: (cleanPath) => buildFixtureProviderIntegrationMonitor(readDecodedPathSegment(cleanPath, 1))
  },
  {
    pattern: apiRoutePattern("/api/workstation/provider-integrations/connections", "/[^/]+/sync-runs"),
    resolve: (cleanPath) => buildFixtureProviderIntegrationSyncHistory(readDecodedPathSegment(cleanPath, 1))
  },
  {
    pattern: apiRoutePattern("/api/workstation/provider-integrations/connections", "/[^/]+/sync-plan"),
    resolve: (cleanPath, path) => buildFixtureProviderIntegrationSyncPlan(readDecodedPathSegment(cleanPath, 1), path)
  },
  {
    pattern: apiRoutePattern("/api/workstation/provider-integrations/connections", "/[^/]+/staging"),
    resolve: (cleanPath) => buildFixtureProviderIntegrationStaging(readDecodedPathSegment(cleanPath, 1))
  },
  {
    pattern: apiRoutePattern("/api/workstation/provider-integrations/connections", "/[^/]+/identity-resolution"),
    resolve: (cleanPath) => buildFixtureProviderIntegrationIdentity(readDecodedPathSegment(cleanPath, 1))
  },
  {
    pattern: apiRoutePattern("/api/workstation/provider-integrations/connections", "/[^/]+/promotion-readiness"),
    resolve: (cleanPath) => buildFixtureProviderIntegrationPromotion(readDecodedPathSegment(cleanPath, 1))
  },
  {
    pattern: apiRoutePattern("/api/workstation/provider-integrations/connections", "/[^/]+/reconciliation-handoffs"),
    resolve: (cleanPath) => buildFixtureProviderIntegrationHandoffs(readDecodedPathSegment(cleanPath, 1))
  },
  {
    pattern: apiRoutePattern("/api/workstation/provider-integrations/connections", "/[^/]+/quarantine"),
    resolve: (cleanPath) => buildFixtureProviderIntegrationQuarantine(readDecodedPathSegment(cleanPath, 1))
  },
  { pattern: apiRoutePattern(WORKSTATION_API_ENDPOINTS.operationsContinuity, "/[^/]+"), resolve: () => fixtureOperationsContinuityWorkflow },
  {
    pattern: apiRoutePattern(WORKSTATION_API_ENDPOINTS.evidenceSubjects, "/accounting-record/[^/]+/packet"),
    resolve: () => fixtureAccountingRecordEvidencePacket
  },
  {
    pattern: apiRoutePattern(WORKSTATION_API_ENDPOINTS.evidenceSubjects, "/accounting-record/[^/]+/validate"),
    resolve: () => fixtureAccountingRecordCompleteness
  },
  {
    pattern: apiRoutePattern(WORKSTATION_API_ENDPOINTS.evidenceSubjects, "/accounting-record/[^/]+/export-manifest"),
    resolve: () => fixtureAccountingRecordExportResponse
  }
];

export function resolveDevFixture<T>(path: string): T | undefined {
  return resolveFixtureFromMaps<T>(path, fixtures, dynamicFixturePatterns);
}

export function searchDevSecurityMasterEntries(query: string, take = 25, activeOnly = true): SecurityMasterEntry[] {
  const trimmed = query.trim().toLowerCase();
  if (!trimmed) {
    return [];
  }

  return fixtureSecurityMasterEntries
    .filter((entry) => !activeOnly || entry.status === "Active")
    .filter((entry) => {
      const fields = [
        entry.displayName,
        entry.classification.assetClass,
        entry.classification.subType ?? "",
        entry.classification.primaryIdentifierKind ?? "",
        entry.classification.primaryIdentifierValue ?? "",
        entry.classification.matchedIdentifierKind ?? "",
        entry.classification.matchedIdentifierValue ?? "",
        entry.classification.matchedProvider ?? "",
        entry.economicDefinition.assetFamily ?? "",
        entry.economicDefinition.issuerType ?? "",
        fixtureSecurityIdentities[entry.securityId]?.identifiers.map((identifier) => `${identifier.kind} ${identifier.value}`).join(" ") ?? "",
        fixtureSecurityIdentities[entry.securityId]?.aliases.map((alias) => alias.aliasValue).join(" ") ?? ""
      ];

      return fields.some((field) => field.toLowerCase().includes(trimmed));
    })
    .slice(0, take)
    .map((entry) => cloneFixture(entry));
}
