import type {
  DataOperationsWorkspaceResponse,
  GovernanceWorkspaceResponse,
  ResearchWorkspaceResponse,
  SessionInfo,
  SystemOverviewResponse,
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
  }
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
      note: "Realtime subscriptions are stable."
    },
    {
      provider: "Databento",
      status: "Warning",
      capability: "Backfill bars",
      latency: "42ms p50",
      note: "One options-chain backfill is waiting on operator review."
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

const fixtures = {
  "/api/status": fixtureSystemOverview,
  "/api/workstation/session": fixtureSession,
  "/api/workstation/research": fixtureResearchWorkspace,
  "/api/workstation/trading": fixtureTradingWorkspace,
  "/api/workstation/data-operations": fixtureDataOperationsWorkspace,
  "/api/workstation/governance": fixtureGovernanceWorkspace
} satisfies Record<string, unknown>;

export function resolveDevFixture<T>(path: string): T | undefined {
  const fixture = fixtures[path as keyof typeof fixtures];
  return fixture === undefined ? undefined : cloneFixture(fixture as T);
}

function cloneFixture<T>(fixture: T): T {
  if (typeof structuredClone === "function") {
    return structuredClone(fixture);
  }

  return JSON.parse(JSON.stringify(fixture)) as T;
}
