import type {
  DataOperationsWorkspaceResponse,
  GovernanceWorkspaceResponse,
  OperatorInbox,
  ResearchWorkspaceResponse,
  SessionInfo,
  SystemOverviewResponse,
  TradingOperatorReadiness,
  TradingWorkspaceResponse
} from "@/types";

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
  recentEvents: [
    {
      id: "evt-dev-1",
      type: "warning",
      message: "Dashboard is using local development fixture data because the Meridian API did not respond.",
      source: "dashboard-dev",
      timestamp: "2026-04-28T18:15:00Z"
    }
  ]
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
    summary: "4 export/reporting profiles are available for governance workflows."
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

const fixtures = {
  "/api/status": fixtureSystemOverview,
  "/api/workstation/session": fixtureSession,
  "/api/workstation/research": fixtureResearchWorkspace,
  "/api/workstation/trading": fixtureTradingWorkspace,
  "/api/workstation/trading/readiness": fixtureTradingReadiness,
  "/api/workstation/operator/inbox": fixtureOperatorInbox,
  "/api/workstation/data-operations": fixtureDataOperationsWorkspace,
  "/api/workstation/governance": fixtureGovernanceWorkspace
} satisfies Record<string, unknown>;

export function resolveDevFixture<T>(path: string): T | undefined {
  const fixture = fixtures[path.split("?")[0] as keyof typeof fixtures];
  return fixture === undefined ? undefined : cloneFixture(fixture as T);
}

function cloneFixture<T>(fixture: T): T {
  if (typeof structuredClone === "function") {
    return structuredClone(fixture);
  }

  return JSON.parse(JSON.stringify(fixture)) as T;
}
