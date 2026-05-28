import type {
  CoveredCallChainPreview,
  CoveredCallRunResult,
  CoveredCallRunSummary
} from "../types/covered-call";
import type {
  BrokerageConnectionStatus,
  BrokerageHouseholdPortfolio,
  CorporateAction,
  DataOperationsWorkspaceResponse,
  ExecutionAuditEntry,
  ExecutionControlSnapshot,
  GovernanceWorkspaceResponse,
  HistoricalBarsResponse,
  OrderBookResponse,
  OperatorInbox,
  OperationsApprovalPolicyMatrix,
  OperationsCloseCalendar,
  OperationsContinuityWorkflow,
  OperationsContinuityWorkflowSummary,
  LedgerMappingWorkbench,
  OperatorOverridesDto,
  PaperSessionDetail,
  PaperSessionReplayVerification,
  PaperSessionSummary,
  PortfolioWorkspaceResponse,
  PromotionEvaluationResult,
  PromotionRecord,
  QuantParametersResponse,
  QuantTemplatesResponse,
  QuotesResponse,
  QuotesSnapshotResponse,
  ReconciliationCalibrationSummary,
  ResearchWorkspaceResponse,
  ReplayFileRecord,
  SecurityIdentityDrillIn,
  SecurityMasterConflict,
  SecurityMasterEntry,
  SessionInfo,
  RolePermissionCatalog,
  StrategyDesignDocument,
  StrategyDesignDraftSummary,
  StrategyDesignFieldCatalogItem,
  StrategyDesignTemplate,
  SymbolRecord,
  SymbolStatistics,
  SystemOverviewResponse,
  TradingOperatorReadiness,
  TradingParameters,
  TradingWorkspaceResponse,
  TradesResponse,
  WorkflowAction,
  WorkflowLibrary,
  WorkflowPresetLibrary
} from "../types";
import {
  AUTH_API_ENDPOINTS,
  COVERED_CALL_API_ENDPOINTS,
  EXECUTION_API_ENDPOINTS,
  FUND_STRUCTURE_API_ENDPOINTS,
  MARKET_DATA_API_ENDPOINTS,
  PORTFOLIO_API_ENDPOINTS,
  PROMOTION_API_ENDPOINTS,
  QUANT_API_ENDPOINTS,
  RECONCILIATION_API_ENDPOINTS,
  REPLAY_API_ENDPOINTS,
  SECURITY_MASTER_API_ENDPOINTS,
  STRATEGY_DESIGNER_API_ENDPOINTS,
  SYMBOL_API_ENDPOINTS,
  WORKSTATION_API_ENDPOINTS,
  brokerageConnectionStatusEndpoint
} from "./workstation-endpoints";

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

const fixtureResearchWorkspace: ResearchWorkspaceResponse = {
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
    requiredOwners: ["Data Operations", "Provider Reliability", "Trading"],
    blockers: [],
    detail: "Signed DK1 parity packet is available for readiness projection.",
    operatorSignoff: {
      status: "signed",
      requiredBeforeDk1Exit: true,
      requiredOwners: ["Data Operations", "Provider Reliability", "Trading"],
      signedOwners: ["Data Operations", "Provider Reliability", "Trading"],
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
    message: "Replay matched the fixture paper session state.",
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
  risk: {
    state: "Observe",
    summary: "Guardrails are active.",
    netExposure: "$120,000",
    grossExposure: "$150,000",
    var95: "$9,000",
    maxDrawdown: "-1.1%",
    buyingPowerUsed: "58%",
    activeGuardrails: ["Cap per single-name", "Throttle at 70%"]
  },
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

const fixtureDataOperationsWorkspace: DataOperationsWorkspaceResponse = {
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
      target: "research pack",
      status: "Ready",
      rows: "124k",
      updatedAt: "4m ago"
    }
  ]
};

const fixtureGovernanceWorkspace: GovernanceWorkspaceResponse = {
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
    summary: "4 export/reporting profiles are available for governance workflows.",
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
        publication: null
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
  cashFlow: fixtureGovernanceWorkspace.cashFlow
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
    source: "External research upload",
    dataSet: "research-import",
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

interface FixtureMarketProfile {
  bidPrice: number;
  bidSize: number;
  askPrice: number;
  askSize: number;
  lastPrice: number;
  venue: string;
  streamId: string;
}

const fixtureMarketTimestamp = "2026-05-08T15:00:00.000Z";

const fixtureMarketProfiles: Record<string, FixtureMarketProfile> = {
  AAPL: { bidPrice: 188.05, bidSize: 200, askPrice: 188.07, askSize: 150, lastPrice: 188.06, venue: "NASDAQ", streamId: "fixture-aapl" },
  MSFT: { bidPrice: 421.1, bidSize: 300, askPrice: 421.2, askSize: 250, lastPrice: 421.15, venue: "NASDAQ", streamId: "fixture-msft" },
  NVDA: { bidPrice: 950.2, bidSize: 80, askPrice: 950.45, askSize: 65, lastPrice: 950.35, venue: "NASDAQ", streamId: "fixture-nvda" },
  QQQ: { bidPrice: 438.24, bidSize: 420, askPrice: 438.28, askSize: 390, lastPrice: 438.26, venue: "NASDAQ", streamId: "fixture-qqq" },
  SPY: { bidPrice: 512.44, bidSize: 500, askPrice: 512.48, askSize: 520, lastPrice: 512.46, venue: "NYSE Arca", streamId: "fixture-spy" }
};

const fixtureSymbolRecords: SymbolRecord[] = [
  { symbol: "AAPL", status: "Active", provider: "Alpaca", lastEventAt: fixtureMarketTimestamp, eventCount: 1842, hasHistoricalData: true },
  { symbol: "MSFT", status: "Active", provider: "Alpaca", lastEventAt: fixtureMarketTimestamp, eventCount: 1328, hasHistoricalData: true },
  { symbol: "QQQ", status: "Monitored", provider: "Alpaca", lastEventAt: fixtureMarketTimestamp, eventCount: 942, hasHistoricalData: true },
  { symbol: "SPY", status: "Monitored", provider: "Alpaca", lastEventAt: fixtureMarketTimestamp, eventCount: 1104, hasHistoricalData: true }
];

const fixtureSymbolStatistics: SymbolStatistics = {
  totalSymbols: fixtureSymbolRecords.length,
  monitoredSymbols: fixtureSymbolRecords.filter((symbol) => symbol.status === "Active" || symbol.status === "Monitored").length,
  archivedSymbols: 0,
  symbolsWithErrors: 0,
  totalEventsLast24h: fixtureSymbolRecords.reduce((total, symbol) => total + symbol.eventCount, 0)
};

const fixtureOperationsContinuityWorkflow: OperationsContinuityWorkflow = {
  workflowId: "79f1f386-0bb1-4aef-9a85-fb9d6de8e1f6",
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
      workflowId: "79f1f386-0bb1-4aef-9a85-fb9d6de8e1f6",
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
      workflowId: "79f1f386-0bb1-4aef-9a85-fb9d6de8e1f6",
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
  breakCases: [],
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
  closePackage: null,
  closeReadiness: null,
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

const fixtures = {
  [WORKSTATION_API_ENDPOINTS.systemStatus]: fixtureSystemOverview,
  [WORKSTATION_API_ENDPOINTS.session]: fixtureSession,
  [WORKSTATION_API_ENDPOINTS.strategy]: fixtureResearchWorkspace,
  "/api/workstation/research": fixtureResearchWorkspace,
  [WORKSTATION_API_ENDPOINTS.trading]: fixtureTradingWorkspace,
  [WORKSTATION_API_ENDPOINTS.portfolio]: fixturePortfolioWorkspace,
  [WORKSTATION_API_ENDPOINTS.tradingReadiness]: fixtureTradingReadiness,
  [WORKSTATION_API_ENDPOINTS.operatorInbox]: fixtureOperatorInbox,
  [WORKSTATION_API_ENDPOINTS.workflowLibrary]: fixtureWorkflowLibrary,
  [WORKSTATION_API_ENDPOINTS.workflowPresets]: fixtureWorkflowPresetLibrary,
  [WORKSTATION_API_ENDPOINTS.operationsContinuity]: fixtureOperationsContinuityWorkflows,
  [WORKSTATION_API_ENDPOINTS.operationsContinuityApprovalPolicyMatrix]: fixtureOperationsApprovalPolicyMatrix,
  [WORKSTATION_API_ENDPOINTS.operationsContinuityCloseCalendar]: fixtureOperationsCloseCalendar,
  [AUTH_API_ENDPOINTS.roles]: fixtureRolePermissionCatalog,
  [FUND_STRUCTURE_API_ENDPOINTS.ledgerMappingWorkbench]: fixtureLedgerMappingWorkbench,
  [EXECUTION_API_ENDPOINTS.sessions]: fixturePaperSessionSummaries,
  [EXECUTION_API_ENDPOINTS.audit]: fixtureExecutionAudit,
  [EXECUTION_API_ENDPOINTS.controls]: fixtureExecutionControls,
  [REPLAY_API_ENDPOINTS.files]: fixtureReplayFiles,
  [PROMOTION_API_ENDPOINTS.history]: fixturePromotionHistory,
  [brokerageConnectionStatusEndpoint("alpaca")]: fixtureAlpacaConnection,
  [brokerageConnectionStatusEndpoint("robinhood")]: fixtureAlpacaConnection,
  [PORTFOLIO_API_ENDPOINTS.household]: fixtureAlpacaPortfolio,
  [WORKSTATION_API_ENDPOINTS.data]: fixtureDataOperationsWorkspace,
  "/api/workstation/data-operations": fixtureDataOperationsWorkspace,
  [WORKSTATION_API_ENDPOINTS.accounting]: fixtureGovernanceWorkspace,
  [WORKSTATION_API_ENDPOINTS.reporting]: fixtureGovernanceWorkspace,
  "/api/workstation/governance": fixtureGovernanceWorkspace,
  [RECONCILIATION_API_ENDPOINTS.breakQueue]: fixtureGovernanceWorkspace.breakQueue,
  [RECONCILIATION_API_ENDPOINTS.calibrationSummary]: fixtureCalibrationSummary,
  [QUANT_API_ENDPOINTS.templates]: fixtureQuantTemplates,
  [QUANT_API_ENDPOINTS.parameters]: fixtureQuantParameters,
  [STRATEGY_DESIGNER_API_ENDPOINTS.templates]: fixtureStrategyDesignerTemplates,
  [STRATEGY_DESIGNER_API_ENDPOINTS.fieldCatalog]: fixtureStrategyDesignerFieldCatalog,
  [STRATEGY_DESIGNER_API_ENDPOINTS.drafts]: fixtureStrategyDesignerDrafts,
  [COVERED_CALL_API_ENDPOINTS.runs]: fixtureCoveredCallRuns,
  [COVERED_CALL_API_ENDPOINTS.chainPreview]: fixtureCoveredCallChainPreview,
  [`${SECURITY_MASTER_API_ENDPOINTS.base}/conflicts`]: fixtureSecurityConflicts,
  [SYMBOL_API_ENDPOINTS.symbols]: fixtureSymbolRecords,
  [SYMBOL_API_ENDPOINTS.statistics]: fixtureSymbolStatistics
} satisfies Record<string, unknown>;

type DynamicFixturePattern = {
  pattern: RegExp;
  resolve: (cleanPath: string, path: string) => unknown | undefined;
};

const dynamicFixturePatterns: DynamicFixturePattern[] = [
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
  { pattern: apiRoutePattern(MARKET_DATA_API_ENDPOINTS.quotes, "/[^/]+"), resolve: (cleanPath) => buildFixtureQuote(readSymbolFromPath(cleanPath)) },
  { pattern: apiRoutePattern(MARKET_DATA_API_ENDPOINTS.trades, "/[^/]+"), resolve: (cleanPath) => buildFixtureTrades(readSymbolFromPath(cleanPath)) },
  { pattern: apiRoutePattern(MARKET_DATA_API_ENDPOINTS.orderbook, "/[^/]+"), resolve: (cleanPath) => buildFixtureOrderbook(readSymbolFromPath(cleanPath)) },
  {
    pattern: apiRoutePattern(MARKET_DATA_API_ENDPOINTS.historical, "/[^/]+/bars"),
    resolve: (cleanPath, path) => buildFixtureHistoricalBars(readSymbolFromPath(cleanPath, 1), path)
  },
  { pattern: apiRoutePattern(MARKET_DATA_API_ENDPOINTS.quotesSnapshot), resolve: (_cleanPath, path) => buildFixtureQuotesSnapshot(path) },
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
  { pattern: apiRoutePattern(WORKSTATION_API_ENDPOINTS.operationsContinuity, "/[^/]+"), resolve: () => fixtureOperationsContinuityWorkflow }
];

export function resolveDevFixture<T>(path: string): T | undefined {
  const cleanPath = path.split("?")[0];
  const exact = fixtures[cleanPath as keyof typeof fixtures];
  if (exact !== undefined) {
    return cloneFixture(exact as T);
  }

  for (const { pattern, resolve } of dynamicFixturePatterns) {
    if (pattern.test(cleanPath)) {
      const fixture = resolve(cleanPath, path);
      if (fixture !== undefined) {
        return cloneFixture(fixture as T);
      }
    }
  }

  return undefined;
}

function readSymbolFromPath(cleanPath: string, segmentFromEnd = 0): string {
  const rawSymbol = cleanPath.split("/").at(-1 - segmentFromEnd) ?? "AAPL";
  try {
    return decodeURIComponent(rawSymbol).trim().toUpperCase() || "AAPL";
  } catch {
    return rawSymbol.trim().toUpperCase() || "AAPL";
  }
}

function readDecodedPathSegment(cleanPath: string, segmentFromEnd = 0): string {
  const rawSegment = cleanPath.split("/").at(-1 - segmentFromEnd) ?? "";
  try {
    return decodeURIComponent(rawSegment).trim();
  } catch {
    return rawSegment.trim();
  }
}

function apiRoutePattern(baseRoute: string, suffixPattern = ""): RegExp {
  return new RegExp(`^${escapeRegExp(baseRoute)}${suffixPattern}$`);
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function getFixtureMarketProfile(symbol: string): FixtureMarketProfile {
  const normalized = symbol.trim().toUpperCase();
  const known = fixtureMarketProfiles[normalized];
  if (known) {
    return known;
  }

  const offset = Math.max(0, Math.min(8, normalized.length)) * 0.13;
  return {
    ...fixtureMarketProfiles.AAPL!,
    bidPrice: roundMarketPrice(fixtureMarketProfiles.AAPL!.bidPrice + offset),
    askPrice: roundMarketPrice(fixtureMarketProfiles.AAPL!.askPrice + offset),
    lastPrice: roundMarketPrice(fixtureMarketProfiles.AAPL!.lastPrice + offset),
    streamId: `fixture-${normalized.toLowerCase()}`
  };
}

function buildFixtureQuote(symbol: string): QuotesResponse {
  const normalized = symbol.trim().toUpperCase() || "AAPL";
  const profile = getFixtureMarketProfile(normalized);
  return {
    symbol: normalized,
    timestamp: fixtureMarketTimestamp,
    quote: {
      symbol: normalized,
      timestamp: fixtureMarketTimestamp,
      bidPrice: profile.bidPrice,
      bidSize: profile.bidSize,
      askPrice: profile.askPrice,
      askSize: profile.askSize,
      midPrice: roundMarketPrice((profile.bidPrice + profile.askPrice) / 2),
      spread: roundMarketPrice(profile.askPrice - profile.bidPrice),
      sequenceNumber: 42,
      streamId: profile.streamId,
      venue: profile.venue,
      session: null
    }
  };
}

function buildFixtureQuotesSnapshot(path: string): QuotesSnapshotResponse {
  const params = readFixtureSearchParams(path);
  const requestedSymbols = (params.get("symbols") ?? "")
    .split(",")
    .map((symbol) => symbol.trim().toUpperCase())
    .filter(Boolean);
  const symbols = requestedSymbols.length > 0 ? requestedSymbols : fixtureSymbolRecords.map((symbol) => symbol.symbol);

  return {
    timestamp: fixtureMarketTimestamp,
    count: symbols.length,
    quotes: symbols.map((symbol, index) => {
      const profile = getFixtureMarketProfile(symbol);
      return {
        symbol,
        timestamp: fixtureMarketTimestamp,
        bidPrice: profile.bidPrice,
        bidSize: profile.bidSize,
        askPrice: profile.askPrice,
        askSize: profile.askSize,
        midPrice: roundMarketPrice((profile.bidPrice + profile.askPrice) / 2),
        spread: roundMarketPrice(profile.askPrice - profile.bidPrice),
        lastPrice: profile.lastPrice,
        lastSize: 100 + index * 25,
        lastTradeTimestamp: fixtureMarketTimestamp,
        sequenceNumber: 1000 + index,
        streamId: profile.streamId,
        venue: profile.venue,
        session: null
      };
    })
  };
}

function buildFixtureTrades(symbol: string): TradesResponse {
  const normalized = symbol.trim().toUpperCase() || "AAPL";
  const profile = getFixtureMarketProfile(normalized);
  const baseTimestamp = new Date(fixtureMarketTimestamp).getTime();
  const offsets = [0.03, -0.01, 0.07, -0.04, -0.08, 0.02, -0.12, -0.05];
  const trades = offsets.map((offset, index) => ({
    symbol: normalized,
    timestamp: new Date(baseTimestamp - index * 30_000).toISOString(),
    price: roundMarketPrice(profile.lastPrice + offset),
    size: 50 + index * 25,
    aggressor: index % 3 === 0 ? "Buy" : index % 3 === 1 ? "Sell" : "Neutral",
    sequenceNumber: 500 - index,
    streamId: profile.streamId,
    venue: profile.venue
  }));

  return {
    symbol: normalized,
    trades,
    count: trades.length,
    timestamp: fixtureMarketTimestamp
  };
}

function buildFixtureOrderbook(symbol: string): OrderBookResponse {
  const normalized = symbol.trim().toUpperCase() || "AAPL";
  const profile = getFixtureMarketProfile(normalized);
  return {
    symbol: normalized,
    timestamp: fixtureMarketTimestamp,
    bids: [0, 1, 2, 3, 4].map((level) => ({
      side: "Bid",
      level: level + 1,
      price: roundMarketPrice(profile.bidPrice - level * 0.02),
      size: Math.max(25, profile.bidSize - level * 20),
      marketMaker: null
    })),
    asks: [0, 1, 2, 3, 4].map((level) => ({
      side: "Ask",
      level: level + 1,
      price: roundMarketPrice(profile.askPrice + level * 0.02),
      size: Math.max(25, profile.askSize - level * 15),
      marketMaker: null
    })),
    midPrice: roundMarketPrice((profile.bidPrice + profile.askPrice) / 2),
    imbalance: roundMarketPrice((profile.bidSize - profile.askSize) / Math.max(1, profile.bidSize + profile.askSize)),
    marketState: "Open",
    sequenceNumber: 42,
    isStale: false,
    streamId: profile.streamId,
    venue: profile.venue
  };
}

function buildFixtureHistoricalBars(symbol: string, path: string): HistoricalBarsResponse {
  const normalized = symbol.trim().toUpperCase() || "AAPL";
  const profile = getFixtureMarketProfile(normalized);
  const params = readFixtureSearchParams(path);
  const intervalMinutes = Number(params.get("intervalMinutes") ?? 5);
  const start = new Date("2026-05-08T13:30:00.000Z").getTime();
  const offsets = [-0.72, -0.46, -0.3, -0.16, 0.05, 0.12, 0.01, 0.18, 0.31, 0.24, 0.36, 0.29];
  const bars = offsets.map((offset, index) => {
    const open = roundMarketPrice(profile.lastPrice + offset);
    const close = roundMarketPrice(profile.lastPrice + offsets[Math.min(index + 1, offsets.length - 1)]!);
    const high = roundMarketPrice(Math.max(open, close) + 0.08);
    const low = roundMarketPrice(Math.min(open, close) - 0.07);
    const volume = 15_000 + index * 1_250;
    return {
      start: new Date(start + index * intervalMinutes * 60_000).toISOString(),
      open,
      high,
      low,
      close,
      volume,
      vwap: roundMarketPrice((open + high + low + close) / 4),
      tradeCount: 40 + index * 3
    };
  });

  return {
    success: true,
    message: null,
    symbol: normalized,
    intervalMinutes: Number.isFinite(intervalMinutes) && intervalMinutes > 0 ? intervalMinutes : 5,
    from: params.get("from"),
    to: params.get("to"),
    totalBars: bars.length,
    filesProcessed: 1,
    totalFiles: 1,
    queryTimeMs: 3,
    bars
  };
}

function readFixtureSearchParams(path: string): URLSearchParams {
  try {
    return new URL(path, "http://meridian.local").searchParams;
  } catch {
    return new URLSearchParams();
  }
}

function roundMarketPrice(value: number): number {
  return Math.round(value * 10000) / 10000;
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

function cloneFixture<T>(fixture: T): T {
  if (typeof structuredClone === "function") {
    return structuredClone(fixture);
  }

  return JSON.parse(JSON.stringify(fixture)) as T;
}
