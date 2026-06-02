import { describe, expect, it, vi } from "vitest";
import {
  buildLinkedRunEvidenceLabel,
  buildPortfolioRunComparisonSummary,
  buildPortfolioRunDrillInSummary,
  buildPortfolioScreenViewModel,
  buildPortfolioFallbackMetrics,
  resolveBrokerageAccountFilterKeyCommand
} from "@/screens/portfolio-screen.view-model";
import type {
  BrokerageConnectionStatus,
  BrokerageHouseholdPortfolio,
  AccountingWorkspaceResponse,
  MultiAssetCoverageSummary,
  PortfolioWorkspaceResponse,
  StrategyWorkspaceResponse,
  StrategyRunContinuityDto,
  TradingWorkspaceResponse
} from "@/types";

const trading: TradingWorkspaceResponse = {
  metrics: [],
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
  openOrders: [],
  fills: [],
  risk: {
    state: "Healthy",
    summary: "",
    netExposure: "$18,900",
    grossExposure: "$18,900",
    var95: "$900",
    maxDrawdown: "0%",
    buyingPowerUsed: "10%",
    activeGuardrails: []
  },
  brokerage: {
    provider: "Alpaca",
    account: "PA-DEMO",
    environment: "paper",
    connection: "Connected",
    lastHeartbeat: "1s ago",
    orderIngress: "healthy",
    fillFeed: "healthy",
    notes: ""
  }
};

const strategy: StrategyWorkspaceResponse = {
  metrics: [],
  runs: [
    {
      id: "run-1",
      strategyName: "Mean Reversion",
      engine: "Native",
      mode: "paper",
      status: "Running",
      dataset: "US Equities",
      window: "90d",
      pnl: "+4.2%",
      sharpe: "1.41",
      lastUpdated: "2m ago",
      notes: "",
      promotionState: "Promoted"
    }
  ]
};

const accounting: AccountingWorkspaceResponse = {
  metrics: [],
  reconciliationQueue: [],
  breakQueue: [],
  cashFlow: {
    totalCash: 120000,
    totalLedgerCash: 120500,
    netVariance: 500,
    totalFinancing: 0,
    runsWithCashSignals: 2,
    runsWithCashVariance: 1,
    tone: "warning",
    summary: "1 run needs variance review."
  },
  reporting: {
    profileCount: 0,
    recommendedProfiles: [],
    profiles: [],
    reportPackTargets: [],
    summary: ""
  }
};

const multiAssetCoverage: MultiAssetCoverageSummary = {
  fundAccountId: "all",
  entity: "portfolio",
  asOfUtc: "2026-06-02T00:00:00Z",
  metrics: [
    { id: "multi-asset-classes", label: "Asset classes", value: "3", delta: "covered", tone: "default" },
    { id: "multi-asset-review", label: "Review required", value: "2", delta: "evidence gaps", tone: "warning" }
  ],
  assetClasses: [
    {
      assetClass: "Equity",
      displayName: "Equities",
      status: "Ready",
      statusLabel: "Ready",
      summary: "Listed equity coverage.",
      evidenceRequirements: [
        { requirementId: "Equity:security-master-identifiers", label: "Identifiers", category: "SecurityMaster", status: "Ready", evidenceRoute: "/api/workstation/security-master/securities", required: true }
      ],
      blockers: [],
      ledgerClassification: { classification: "Security position" },
      reconciliationSignals: { breaks: "quantity, market value, cash" }
    },
    {
      assetClass: "FixedIncome",
      displayName: "Corporate bonds",
      status: "Degraded",
      statusLabel: "Degraded",
      summary: "Bond coverage is usable with stale provider evidence.",
      evidenceRequirements: [
        { requirementId: "FixedIncome:security-master-identifiers", label: "Identifiers", category: "SecurityMaster", status: "Ready", evidenceRoute: "/api/workstation/security-master/securities", required: true },
        { requirementId: "FixedIncome:price-evidence", label: "Price evidence", category: "ProviderEvidence", status: "Degraded", evidenceRoute: "/api/workstation/data-operations", required: true }
      ],
      blockers: [],
      ledgerClassification: { classification: "Amortized-cost security position" },
      reconciliationSignals: { breaks: "principal, accrued interest, market value" }
    },
    {
      assetClass: "CustomAsset",
      displayName: "MBS / ABS / CLO / CMBS / private assets",
      status: "ReviewRequired",
      statusLabel: "Review required",
      summary: "Governed custom asset coverage.",
      evidenceRequirements: [
        { requirementId: "CustomAsset:governed-profile", label: "Profile", category: "Governance", status: "Ready", evidenceRoute: "/api/security-master/asset-profiles", required: true },
        { requirementId: "CustomAsset:provider-evidence", label: "Provider evidence", category: "ProviderEvidence", status: "ReviewRequired", evidenceRoute: "/api/workstation/data-operations", required: true }
      ],
      blockers: [
        { code: "CustomAsset:provider-evidence-review", severity: "Review", message: "Retained provider evidence is required.", source: "ProviderEvidence", evidenceRoute: "/api/workstation/portfolio/multi-asset-coverage" }
      ],
      ledgerClassification: { classification: "Profile-derived classification" },
      reconciliationSignals: { breaks: "custom-profile evidence" }
    }
  ],
  drillThroughRoutes: {
    coverage: "/api/workstation/portfolio/multi-asset-coverage"
  }
};

const selectedRunContinuity: StrategyRunContinuityDto = {
  run: {
    summary: {
      runId: "run-1",
      strategyId: "strategy-1",
      strategyName: "Mean Reversion",
      mode: "Paper",
      engine: "Internal",
      status: "Running",
      startedAt: "2026-05-07T11:00:00Z",
      completedAt: null,
      datasetReference: "US Equities",
      feedReference: "alpaca-paper",
      portfolioId: null,
      ledgerReference: null,
      netPnl: 4200,
      totalReturn: 0.042,
      finalEquity: 104200,
      fillCount: 12,
      lastUpdatedAt: "2026-05-07T12:00:00Z",
      auditReference: "audit-run-1"
    },
    parameters: {},
    portfolio: null,
    ledger: null
  },
  lineage: {
    parentRunId: null,
    parentRun: null,
    childRuns: []
  },
  cashFlow: {
    asOf: "2026-05-07T12:00:00Z",
    currency: "USD",
    totalEntries: 3,
    totalInflows: 1000,
    totalOutflows: 500,
    netCashFlow: 500,
    projectedNetPosition: 120500,
    bucketCount: 1,
    nextBucketStart: null,
    nextBucketEnd: null,
    nextBucketNetFlow: null
  },
  reconciliation: null,
  continuityStatus: {
    hasRun: true,
    runHealth: "Healthy",
    hasFills: true,
    fillsHealth: "Healthy",
    hasPortfolio: false,
    portfolioHealth: "Missing",
    hasLedger: false,
    ledgerHealth: "Missing",
    hasCashFlow: true,
    cashFlowHealth: "Healthy",
    hasReconciliation: true,
    reconciliationHealth: "Healthy",
    asOfDriftMinutes: 0,
    openReconciliationBreaks: 0,
    securityCoverageIssueCount: 0,
    hasWarnings: true,
    warnings: [
      {
        code: "missing-portfolio",
        severity: "Critical",
        message: "Portfolio read model is missing for the selected run.",
        sourceSeam: "portfolio"
      },
      {
        code: "missing-ledger",
        severity: "Critical",
        message: "Ledger read model is missing for the selected run.",
        sourceSeam: "ledger"
      }
    ]
  }
};

const brokerageConnection: BrokerageConnectionStatus = {
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
  externalAccountId: "PA123",
  verifiedAt: "2026-05-07T11:50:00Z",
  maskedKeyId: "********1234"
};

const brokeragePortfolio: BrokerageHouseholdPortfolio = {
  providerId: "alpaca",
  asOf: "2026-05-07T12:00:00Z",
  totalCash: 150000,
  totalEquity: 375000,
  totalBuyingPower: 150000,
  currency: "USD",
  warnings: [],
  accounts: [
    {
      fundAccountId: "fund-roth",
      providerId: "alpaca",
      externalAccountId: "alpaca-roth",
      displayName: "Alpaca Roth IRA",
      accountKind: "RothIra",
      health: "Healthy",
      cash: 50000,
      equity: 125000,
      buyingPower: 50000,
      currency: "USD",
      syncedAt: "2026-05-07T12:00:00Z",
      positionCount: 1,
      cashTransactionCount: 1,
      warnings: []
    },
    {
      fundAccountId: "fund-taxable",
      providerId: "alpaca",
      externalAccountId: "alpaca-taxable",
      displayName: "Alpaca Brokerage",
      accountKind: "TaxableBrokerage",
      health: "Healthy",
      cash: 100000,
      equity: 250000,
      buyingPower: 100000,
      currency: "USD",
      syncedAt: "2026-05-07T12:00:00Z",
      positionCount: 1,
      cashTransactionCount: 1,
      warnings: []
    }
  ],
  positions: [
    {
      fundAccountId: "fund-roth",
      providerId: "alpaca",
      externalAccountId: "alpaca-roth",
      accountKind: "RothIra",
      symbol: "AAPL",
      quantity: 10,
      averageEntryPrice: 150,
      marketPrice: 170,
      marketValue: 1700,
      unrealizedPnl: 200,
      assetClass: "equity",
      security: null,
      description: "Apple Inc.",
      positionId: "pos-aapl",
      currency: "USD"
    },
    {
      fundAccountId: "fund-taxable",
      providerId: "alpaca",
      externalAccountId: "alpaca-taxable",
      accountKind: "TaxableBrokerage",
      symbol: "MSFT",
      quantity: 5,
      averageEntryPrice: 300,
      marketPrice: 350,
      marketValue: 1750,
      unrealizedPnl: 250,
      assetClass: "equity",
      security: null,
      description: "Microsoft Corporation",
      positionId: "pos-msft",
      currency: "USD"
    }
  ]
};

const portfolio: PortfolioWorkspaceResponse = {
  metrics: [
    {
      id: "portfolio-equity",
      label: "Portfolio equity",
      value: "$625,000",
      delta: "+1.4%",
      tone: "success"
    }
  ],
  positions: [
    {
      symbol: "NVDA",
      side: "Long",
      quantity: "12",
      averagePrice: "840.00",
      markPrice: "850.00",
      dayPnl: "+$120",
      unrealizedPnl: "+$120",
      exposure: "$10,200"
    }
  ],
  risk: {
    ...trading.risk,
    summary: "Portfolio endpoint risk posture.",
    buyingPowerUsed: "22%"
  },
  brokerage: {
    ...trading.brokerage,
    account: "PF-ENDPOINT"
  },
  runs: [
    {
      runId: "portfolio-run-1",
      strategyName: "Portfolio Endpoint Run",
      engine: "Native",
      mode: "paper",
      status: "Completed",
      pnl: "+2.1%",
      sharpe: "1.10",
      dataset: "Live portfolio",
      window: "30d",
      lastUpdated: "1m ago",
      notes: "Sourced from portfolio workspace.",
      promotionState: "Promoted"
    }
  ],
  cashFlow: {
    ...accounting.cashFlow,
    netVariance: -125,
    summary: "Portfolio endpoint cash posture."
  }
};

describe("buildPortfolioScreenViewModel", () => {
  it("returns position rows from trading data", () => {
    const vm = buildPortfolioScreenViewModel({ trading, strategy, accounting });
    expect(vm.hasPositions).toBe(true);
    expect(vm.positionRows).toHaveLength(1);
    expect(vm.positionRows[0].symbol).toBe("AAPL");
    expect(vm.positionRows[0].pnlTone).toBe("success");
    expect(vm.positionRows[0].isSelected).toBe(true);
    expect(vm.positionRows[0].expanded).toBe(true);
    expect(vm.positionRows[0].detailPanelId).toBe(vm.positionDetailId);
    expect(vm.selectedPosition?.title).toBe("AAPL");
  });

  it("returns run rows from strategy data", () => {
    const vm = buildPortfolioScreenViewModel({ trading, strategy, accounting });
    expect(vm.hasRuns).toBe(true);
    expect(vm.runRows).toHaveLength(1);
    expect(vm.runRows[0].strategyName).toBe("Mean Reversion");
    expect(vm.runRows[0].promotionState).toBe("Promoted");
    expect(vm.runRows[0].modeBadgeVariant).toBe("paper");
    expect(vm.runRows[0].pnlTone).toBe("success");
    expect(vm.runRows[0].isSelected).toBe(true);
    expect(vm.runRows[0].expanded).toBe(true);
    expect(vm.runRows[0].detailPanelId).toBe(vm.runDetailId);
    expect(vm.runRows[0].selectAriaLabel).toBe("Inspect Mean Reversion run evidence");
    expect(vm.selectedRun?.title).toBe("Mean Reversion");
    expect(vm.selectedRun?.statusDetail).toContain("Running paper run with +4.2% P&L");
    expect(vm.runEvidenceChip).toEqual({ label: "Run evidence", value: "1 linked run" });
    expect(vm.selectedRunChip).toEqual({ label: "Selected run", value: "Mean Reversion" });
  });

  it("projects multi-asset coverage without recalculating readiness client-side", () => {
    const vm = buildPortfolioScreenViewModel({ trading, strategy, accounting, multiAssetCoverage });
    const customAssetRow = vm.multiAssetCoveragePanel?.rows.find((row) => row.assetClass === "CustomAsset");
    const fixedIncomeRow = vm.multiAssetCoveragePanel?.rows.find((row) => row.assetClass === "FixedIncome");

    expect(vm.multiAssetCoveragePanel?.statusLabel).toBe("2 review");
    expect(vm.multiAssetCoveragePanel?.rows).toHaveLength(3);
    expect(vm.multiAssetCoveragePanel?.groups.map((group) => ({
      id: group.id,
      label: group.label,
      summary: group.summary
    }))).toEqual([
      { id: "review", label: "Review required", summary: "2 asset classes" },
      { id: "ready", label: "Ready", summary: "1 asset class" }
    ]);
    expect(fixedIncomeRow).toMatchObject({
      displayName: "Corporate bonds",
      statusLabel: "Degraded",
      statusTone: "warning",
      readinessGroupId: "review",
      readinessDetail: "Degraded: 1/2 evidence targets ready with no blockers.",
      primaryEvidenceRoute: "/api/workstation/data-operations"
    });
    expect(customAssetRow).toMatchObject({
      displayName: "MBS / ABS / CLO / CMBS / private assets",
      statusLabel: "Review required",
      evidenceLabel: "1/2 ready",
      ledgerLabel: "Profile-derived classification",
      readinessGroupId: "review",
      readinessDetail: "Review required: 1/2 evidence targets ready with 1 blocker.",
      primaryEvidenceRoute: "/api/workstation/data-operations"
    });
    expect(customAssetRow?.evidenceTargets).toEqual([
      expect.objectContaining({
        id: "CustomAsset:governed-profile",
        statusLabel: "Ready",
        statusTone: "success",
        href: "/api/security-master/asset-profiles",
        requiredLabel: "Required"
      }),
      expect.objectContaining({
        id: "CustomAsset:provider-evidence",
        statusLabel: "Review required",
        statusTone: "warning",
        href: "/api/workstation/data-operations",
        ariaLabel: "Open MBS / ABS / CLO / CMBS / private assets Provider evidence target"
      })
    ]);
    expect(customAssetRow?.blockerTargets).toEqual([
      expect.objectContaining({
        id: "CustomAsset:provider-evidence-review",
        label: "Review",
        source: "ProviderEvidence",
        href: "/api/workstation/portfolio/multi-asset-coverage"
      })
    ]);
    expect(vm.multiAssetCoveragePanel?.evidenceRouteLabel).toBe("GET /api/workstation/portfolio/multi-asset-coverage");
    expect(vm.multiAssetCoveragePanel?.asOfLabel).toBe("As of 2026-06-02T00:00:00Z");
    expect(vm.multiAssetCoveragePanel?.blockerMessages[0]).toContain("Retained provider evidence is required.");
  });

  it("surfaces selected-run portfolio and ledger continuity blockers", () => {
    const balancedGovernance: AccountingWorkspaceResponse = {
      ...accounting,
      cashFlow: {
        ...accounting.cashFlow,
        netVariance: 0,
        tone: "success",
        summary: "Cash flow is balanced."
      }
    };
    const vm = buildPortfolioScreenViewModel({
      trading,
      strategy,
      accounting: balancedGovernance,
      selectedRunContinuity
    });

    expect(vm.selectedRun?.statusTitle).toBe("Mean Reversion continuity review");
    expect(vm.selectedRun?.statusTone).toBe("danger");
    expect(vm.selectedRun?.statusBadgeLabel).toBe("2 blockers");
    expect(vm.selectedRun?.statusDetail).toContain("Portfolio read model is missing for the selected run.");
    expect(vm.selectedRun?.statusDetail).toContain("Ledger read model is missing for the selected run.");
    expect(vm.selectedRun?.fields.find((field) => field.label === "Continuity")).toEqual({
      label: "Continuity",
      value: "2 blockers",
      tone: "danger"
    });
    expect(vm.workflowTaskPanel).toMatchObject({
      statusLabel: "Continuity blocked",
      statusTone: "danger",
      selectedSummary: expect.stringContaining("Resolve 2 selected-run continuity blockers before accepting the portfolio-to-ledger handoff.")
    });
    expect(vm.workflowTaskPanel?.statusRows.find((row) => row.label === "Run continuity")).toEqual({
      label: "Run continuity",
      value: "2 blockers",
      tone: "danger"
    });
  });

  it("keeps run-evidence chip pluralization in the view model", () => {
    expect(buildLinkedRunEvidenceLabel(0)).toBe("No linked runs");
    expect(buildLinkedRunEvidenceLabel(1)).toBe("1 linked run");
    expect(buildLinkedRunEvidenceLabel(2)).toBe("2 linked runs");
  });

  it("builds portfolio run comparison summary across strategy runs", () => {
    const multiRunResearch: StrategyWorkspaceResponse = {
      ...strategy,
      runs: [
        strategy.runs[0],
        {
          id: "run-2",
          strategyName: "Volatility Carry",
          engine: "Lean",
          mode: "backtest",
          status: "Completed",
          dataset: "US Options",
          window: "180d",
          pnl: "-1.5%",
          sharpe: "0.82",
          lastUpdated: "7m ago",
          notes: "Drawdown review required.",
          promotionState: null
        },
        {
          id: "run-3",
          strategyName: "Cash Bridge",
          engine: "Native",
          mode: "live",
          status: "Completed",
          dataset: "Broker",
          window: "30d",
          pnl: "+2.1%",
          sharpe: "1.74",
          lastUpdated: "1m ago",
          notes: "Live-adjacent comparison.",
          promotionState: "LiveManaged"
        }
      ]
    };

    const vm = buildPortfolioScreenViewModel({
      trading,
      strategy: multiRunResearch,
      accounting,
      selectedRunId: "run-3"
    });

    expect(vm.runComparisonSummary).toMatchObject({
      ariaLabel: "Portfolio run comparison summary",
      title: "Run comparison evidence",
      description: "3 strategy runs compared across 3 modes and 2 engines.",
      statusTone: "warning"
    });
    expect(vm.runComparisonSummary.cards).toEqual([
      expect.objectContaining({ id: "selected-rank", value: "#2", detail: "Cash Bridge vs 3 linked runs by P&L." }),
      expect.objectContaining({ id: "best-pnl", value: "+4.2%", detail: "Mean Reversion (paper, Native)" }),
      expect.objectContaining({ id: "weakest-pnl", value: "-1.5%", tone: "danger" }),
      expect.objectContaining({ id: "best-sharpe", value: "1.74", detail: "Cash Bridge risk-adjusted lead." }),
      expect.objectContaining({ id: "coverage", value: "3/2", detail: "backtest, live, paper; Lean, Native." })
    ]);
  });

  it("returns empty portfolio run comparison guidance when no runs exist", () => {
    const summary = buildPortfolioRunComparisonSummary([], null);

    expect(summary.description).toBe("No strategy runs are available for portfolio comparison.");
    expect(summary.statusTone).toBe("warning");
    expect(summary.cards[0]).toMatchObject({
      id: "selected-rank",
      value: "—",
      tone: "warning"
    });
  });

  it("summarizes selected-run drill-in evidence across attribution drawdown cash-flow and trades", () => {
    const vm = buildPortfolioScreenViewModel({ trading, strategy, accounting });
    const summary = buildPortfolioRunDrillInSummary(vm.runRows[0], {
      runId: "run-1",
      attribution: {
        runId: "run-1",
        totalRealizedPnl: 120,
        totalUnrealizedPnl: 80,
        totalCommissions: 5,
        bySymbol: [
          {
            symbol: "AAPL",
            realizedPnl: 120,
            unrealizedPnl: 80,
            totalPnl: 200,
            tradeCount: 2,
            commissions: 5,
            marginInterestAllocated: 0
          }
        ]
      },
      drawdownProfile: {
        runId: "run-1",
        initialEquity: 100000,
        finalEquity: 101250,
        maxDrawdown: 1250,
        maxDrawdownPercent: 0.0125,
        maxDrawdownRecoveryDays: 3,
        sharpeRatio: 1.41,
        sortinoRatio: 1.8,
        points: [
          { date: "2026-05-01", totalEquity: 100000, cash: 50000, dailyReturn: 0, drawdownFromPeak: 0, drawdownFromPeakPercent: 0 }
        ]
      },
      cashFlow: {
        runId: "run-1",
        asOf: "2026-05-07T12:00:00Z",
        currency: "USD",
        totalEntries: 2,
        totalInflows: 1500,
        totalOutflows: 300,
        netCashFlow: 1200,
        entries: [],
        ladder: {
          asOf: "2026-05-07T12:00:00Z",
          currency: "USD",
          bucketDays: 7,
          totalProjectedInflows: 1500,
          totalProjectedOutflows: 300,
          netPosition: 1200,
          buckets: []
        }
      },
      trades: {
        runId: "run-1",
        totalFills: 2,
        totalCommissions: 5,
        fills: [
          {
            fillId: "fill-older",
            orderId: "order-older",
            symbol: "AAPL",
            filledQuantity: 40,
            fillPrice: 187.25,
            commission: 2,
            filledAt: "2026-05-07T11:45:00Z",
            accountId: "paper-account"
          },
          {
            fillId: "fill-latest",
            orderId: "order-latest",
            symbol: "AAPL",
            filledQuantity: 60,
            fillPrice: 188.5,
            commission: 3,
            filledAt: "2026-05-07T11:50:00Z",
            accountId: "paper-account"
          }
        ]
      },
      isLoading: false,
      error: null
    });

    expect(summary).toMatchObject({
      description: "4/4 drill-in evidence slices loaded for Mean Reversion.",
      statusTone: "success"
    });
    expect(summary.cards).toEqual([
      expect.objectContaining({ id: "attribution", value: "+$200", detail: "Realized +$120; unrealized +$80; 1 symbol." }),
      expect.objectContaining({ id: "drawdown", value: "1.25%", detail: "1 equity point; recovery 3 days; final equity $101,250." }),
      expect.objectContaining({ id: "cash-flow", value: "+$1,200", detail: "2 cash-flow entries; inflows $1,500; outflows $300." }),
      expect.objectContaining({ id: "trades", value: "2", detail: "2 fills with $5 commissions." })
    ]);
    expect(summary.bridgeRows).toEqual([
      expect.objectContaining({ id: "realized-pnl", value: "+$120", detail: "AAPL contributes +$120 realized P&L across 2 trades." }),
      expect.objectContaining({ id: "unrealized-pnl", value: "+$80", detail: "AAPL carries +$80 unrealized P&L into the selected portfolio view." }),
      expect.objectContaining({ id: "commission-drag", value: "$5", tone: "warning" }),
      expect.objectContaining({ id: "cash-flow-bridge", value: "+$1,200" }),
      expect.objectContaining({ id: "trade-evidence", value: "2 fills", detail: "2 retained fills available for order, account, fill-price, and commission review." })
    ]);
    expect(summary.tradeEvidenceRows).toEqual([
      expect.objectContaining({
        id: "fill-latest",
        symbol: "AAPL",
        quantity: "60",
        price: "$188.50",
        commission: "$3.00",
        filledAt: "May 7, 11:50 UTC",
        accountId: "paper-account"
      }),
      expect.objectContaining({
        id: "fill-older",
        filledAt: "May 7, 11:45 UTC"
      })
    ]);
  });

  it("surfaces cash-flow summary from accounting data", () => {
    const vm = buildPortfolioScreenViewModel({ trading, strategy, accounting });
    expect(vm.cashFlowSummary).toBe("1 run needs variance review.");
    expect(vm.cashFlowTone).toBe("warning");
    expect(vm.cashVarianceLabel).toBe("$500");
  });

  it("returns empty state text when trading data is null", () => {
    const vm = buildPortfolioScreenViewModel({ trading: null, strategy, accounting });
    expect(vm.hasPositions).toBe(false);
    expect(vm.positionEmptyText).toContain("unavailable");
    expect(vm.metricsFromTrading).toBe(false);
    expect(vm.selectedPosition).toBeNull();
  });

  it("returns empty run text when strategy data is null", () => {
    const vm = buildPortfolioScreenViewModel({ trading, strategy: null, accounting });
    expect(vm.hasRuns).toBe(false);
    expect(vm.runEmptyText).toContain("unavailable");
    expect(vm.selectedRun).toBeNull();
  });

  it("computes danger pnl tone for negative values", () => {
    const tradingWithLoss: TradingWorkspaceResponse = {
      ...trading,
      positions: [{ ...trading.positions[0], unrealizedPnl: "-$200" }]
    };
    const vm = buildPortfolioScreenViewModel({ trading: tradingWithLoss, strategy, accounting });
    expect(vm.positionRows[0].pnlTone).toBe("danger");
    expect(vm.selectedPosition?.fields.find((field) => field.label === "Unrealized P&L")?.tone).toBe("danger");
  });

  it("provides fallback stats when trading is available", () => {
    const vm = buildPortfolioScreenViewModel({ trading, strategy, accounting });
    expect(vm.fallbackStats).toHaveLength(4);
    expect(vm.fallbackStats.find((s) => s.id === "portfolio-open-positions")).toMatchObject({
      label: "Open positions",
      value: "1",
      delta: "Selectable detail",
      tone: "success"
    });
    expect(vm.fallbackStats.find((s) => s.id === "portfolio-unrealized-pnl")).toMatchObject({
      value: "+$90",
      tone: "success"
    });
  });

  it("builds fallback portfolio metrics as shared MetricSnapshot view state", () => {
    expect(buildPortfolioFallbackMetrics({
      openPositionCount: 0,
      totalExposure: 0,
      totalUnrealizedPnl: 0
    })).toEqual([
      {
        id: "portfolio-total-exposure",
        label: "Total exposure",
        value: "—",
        delta: "No holdings",
        tone: "default"
      },
      {
        id: "portfolio-unrealized-pnl",
        label: "Unrealized P&L",
        value: "—",
        delta: "No holdings",
        tone: "default"
      },
      {
        id: "portfolio-cash",
        label: "Cash",
        value: "—",
        delta: "Awaiting portfolio cash feed",
        tone: "warning"
      },
      {
        id: "portfolio-open-positions",
        label: "Open positions",
        value: "0",
        delta: "No holdings",
        tone: "default"
      }
    ]);

    expect(buildPortfolioFallbackMetrics({
      openPositionCount: 2,
      totalExposure: 1000,
      totalUnrealizedPnl: -25
    }).find((metric) => metric.id === "portfolio-unrealized-pnl")).toMatchObject({
      value: "-$25",
      tone: "danger"
    });
  });

  it("derives named header chips without relying on fallback stat positions", () => {
    const vm = buildPortfolioScreenViewModel({ trading, strategy, accounting });

    expect(vm.headerChips).toEqual([
      { label: "Alpaca paper equity", value: "—" },
      { label: "Alpaca paper cash", value: "—" },
      { label: "Open positions", value: "1" },
      { label: "Exposure", value: "$18,900" },
      { label: "Unrealized P&L", value: "+$90" },
      { label: "Cash variance", value: "$500" }
    ]);
  });

  it("adds a Portfolio readiness handoff on the broad portfolio route", () => {
    const vm = buildPortfolioScreenViewModel({ trading, strategy, accounting, pathname: "/portfolio" });

    expect(vm.workflowTaskPanel).toMatchObject({
      regionLabel: "Portfolio readiness handoff",
      title: "Portfolio acceptance handoff",
      statusLabel: "Review blockers",
      statusTone: "warning",
      actionListLabel: "Portfolio readiness next actions",
      selectedSummary: expect.stringContaining("Review brokerage sync, trading readiness, cash variance, and linked run evidence")
    });
    expect(vm.workflowTaskPanel?.actions).toEqual([
      {
        id: "brokerage-sync",
        label: "Review brokerage sync",
        href: "/portfolio/brokerage-sync",
        ariaLabel: "Open brokerage sync review from Portfolio readiness",
        detail: "Inspect account sync, execution feed health, exposure, and brokerage evidence.",
        detailId: "portfolio-readiness-brokerage-sync-detail",
        variant: "default"
      },
      {
        id: "trading-readiness",
        label: "Inspect readiness",
        href: "/trading/readiness",
        ariaLabel: "Open Trading readiness from Portfolio readiness",
        detail: "Check paper-session, replay, execution-control, and readiness evidence.",
        detailId: "portfolio-readiness-trading-readiness-detail",
        variant: "outline"
      },
      {
        id: "evidence",
        label: "Open evidence",
        href: "/reporting/evidence?subjectKind=strategy-run&subjectId=run-1",
        ariaLabel: "Open Mean Reversion evidence from Portfolio readiness",
        detail: "Review the linked strategy-run evidence packet before accepting portfolio state.",
        detailId: "portfolio-readiness-evidence-detail",
        variant: "outline"
      }
    ]);
    expect(vm.workflowTaskPanel?.backendLinks.map((link) => link.href)).toEqual([
      "/api/workstation/portfolio",
      "/api/workstation/trading",
      "/api/workstation/trading/readiness",
      "/api/portfolio/exposure"
    ]);
  });

  it("builds a dedicated brokerage-sync task panel from trading posture", () => {
    const vm = buildPortfolioScreenViewModel({
      trading,
      strategy,
      accounting,
      pathname: "/portfolio/brokerage-sync"
    });

    expect(vm.workflowTaskPanel).toMatchObject({
      regionLabel: "Brokerage sync task",
      title: "Brokerage sync review",
      statusLabel: "Brokerage synced",
      statusTone: "success",
      actionListLabel: "Brokerage sync next actions",
      selectedSummary: expect.stringContaining("Alpaca / paper account PA-DEMO")
    });
    expect(vm.workflowTaskPanel?.actions).toEqual([
      {
        id: "trading-readiness",
        label: "Inspect readiness",
        href: "/trading/readiness",
        ariaLabel: "Open Trading readiness from brokerage sync review",
        detail: "Check paper-session, replay, execution-control, and readiness evidence.",
        detailId: "portfolio-brokerage-sync-trading-readiness-detail",
        variant: "default"
      },
      {
        id: "trading-cockpit",
        label: "Open Trading cockpit",
        href: "/trading",
        ariaLabel: "Open Trading cockpit from brokerage sync review",
        detail: "Review active positions, orders, and paper execution controls.",
        detailId: "portfolio-brokerage-sync-trading-cockpit-detail",
        variant: "outline"
      }
    ]);
    expect(vm.workflowTaskPanel?.statusRows.find((row) => row.label === "Order ingress")).toMatchObject({
      value: "healthy",
      tone: "success"
    });
    expect(vm.workflowTaskPanel?.backendLinks.map((link) => link.href)).toEqual([
      "/api/workstation/trading",
      "/api/workstation/trading/readiness",
      "/api/portfolio/aggregate",
      "/api/portfolio/exposure"
    ]);
    expect(vm.workflowTaskPanel?.backendLinks.map((link) => link.ariaLabel)).toEqual(
      vm.workflowTaskPanel?.backendLinks.map((link) => `Open GET ${link.href} backend payload`)
    );
  });

  it("uses the Portfolio workspace payload as the primary portfolio read model", () => {
    const vm = buildPortfolioScreenViewModel({
      portfolio,
      trading,
      strategy,
      accounting,
      pathname: "/portfolio/brokerage-sync"
    });

    expect(vm.metricCards).toEqual(portfolio.metrics);
    expect(vm.positionSourceLabel).toBe("Portfolio workspace");
    expect(vm.positionRows).toHaveLength(1);
    expect(vm.positionRows[0].symbol).toBe("NVDA");
    expect(vm.selectedPosition?.statusDetail).toContain("Portfolio endpoint risk posture.");
    expect(vm.selectedPosition?.fields.find((field) => field.label === "Buying power")?.value).toBe("22%");
    expect(vm.runRows).toHaveLength(1);
    expect(vm.runRows[0].id).toBe("portfolio-run-1");
    expect(vm.selectedRun?.title).toBe("Portfolio Endpoint Run");
    expect(vm.cashFlowSummary).toBe("Portfolio endpoint cash posture.");
    expect(vm.cashVarianceLabel).toBe("-$125");
    expect(vm.workflowTaskPanel?.selectedSummary).toContain("Alpaca / paper account PF-ENDPOINT");
  });

  it("marks the brokerage-sync panel as blocked when portfolio posture is unavailable", () => {
    const vm = buildPortfolioScreenViewModel({
      trading: null,
      strategy,
      accounting,
      pathname: "/portfolio/brokerage-sync"
    });

    expect(vm.workflowTaskPanel?.statusLabel).toBe("Portfolio unavailable");
    expect(vm.workflowTaskPanel?.statusTone).toBe("danger");
    expect(vm.workflowTaskPanel?.selectedSummary).toContain("Portfolio workspace data is unavailable");
    expect(vm.workflowTaskPanel?.actions[0]).toEqual({
      id: "provider-setup",
      label: "Repair provider setup",
      href: "/settings#alpaca-provider-setup",
      ariaLabel: "Repair provider setup from brokerage sync review",
      detail: "Verify credentials and connection posture before accepting brokerage-sync state.",
      detailId: "portfolio-brokerage-sync-provider-setup-detail",
      variant: "default"
    });
  });

  it("uses stable placeholder header chips when trading data is unavailable", () => {
    const vm = buildPortfolioScreenViewModel({ trading: null, strategy, accounting });

    expect(vm.headerChips).toEqual([
      { label: "Alpaca paper equity", value: "—" },
      { label: "Alpaca paper cash", value: "—" },
      { label: "Open positions", value: "0" },
      { label: "Exposure", value: "—" },
      { label: "Unrealized P&L", value: "—" },
      { label: "Cash variance", value: "$500" }
    ]);
  });

  it("keeps selected holding state in the view model", () => {
    const tradingWithTwoPositions: TradingWorkspaceResponse = {
      ...trading,
      positions: [
        trading.positions[0],
        {
          symbol: "MSFT",
          side: "Short",
          quantity: "25",
          averagePrice: "412.10",
          markPrice: "410.00",
          dayPnl: "+$52.50",
          unrealizedPnl: "+$52.50",
          exposure: "$10,250"
        }
      ]
    };

    const vm = buildPortfolioScreenViewModel({
      trading: tradingWithTwoPositions,
      strategy,
      accounting,
      selectedPositionId: "msft-short-1"
    });

    expect(vm.positionRows.map((row) => row.isSelected)).toEqual([false, true]);
    expect(vm.positionRows.map((row) => row.expanded)).toEqual([false, true]);
    expect(vm.positionRows.map((row) => row.detailPanelId)).toEqual([
      "portfolio-position-detail",
      "portfolio-position-detail"
    ]);
    expect(vm.positionRows[1].selectAriaLabel).toBe("Inspect MSFT Short holding");
    expect(vm.selectedPosition?.title).toBe("MSFT");
    expect(vm.selectedPositionChip).toEqual({ label: "Selected detail", value: "MSFT" });
    expect(vm.selectedPosition?.statusDetail).toContain("$10,250 exposure");
    expect(vm.selectedPosition?.fields.find((field) => field.label === "Guardrails")?.value).toBe("No active guardrails");
  });

  it("keeps selected run evidence state in the view model", () => {
    const researchWithTwoRuns: StrategyWorkspaceResponse = {
      ...strategy,
      runs: [
        strategy.runs[0],
        {
          id: "run-2",
          strategyName: "Volatility Carry",
          engine: "QuantConnect",
          mode: "backtest",
          status: "Needs Review",
          dataset: "US Options",
          window: "180d",
          pnl: "-1.2%",
          sharpe: "0.82",
          lastUpdated: "5m ago",
          notes: "Drawdown review required.",
          promotionState: null
        }
      ]
    };

    const vm = buildPortfolioScreenViewModel({
      trading,
      strategy: researchWithTwoRuns,
      accounting,
      selectedRunId: "run-2"
    });

    expect(vm.runRows.map((row) => row.isSelected)).toEqual([false, true]);
    expect(vm.runRows.map((row) => row.expanded)).toEqual([false, true]);
    expect(vm.runRows.map((row) => row.detailPanelId)).toEqual([
      "portfolio-run-detail",
      "portfolio-run-detail"
    ]);
    expect(vm.selectedRun?.title).toBe("Volatility Carry");
    expect(vm.selectedRun?.statusTone).toBe("warning");
    expect(vm.selectedRun?.statusBadgeLabel).toBe("Needs Review");
    expect(vm.selectedRun?.statusBadgeVariant).toBe("warning");
    expect(vm.selectedRun?.statusDetail).toContain("Drawdown review required.");
    expect(vm.selectedRun?.fields.find((field) => field.label === "Promotion")?.value).toBe("Not promoted");
    expect(vm.selectedRun?.evidenceAction).toEqual({
      label: "Open evidence packet",
      href: "/reporting/evidence?subjectKind=strategy-run&subjectId=run-2",
      ariaLabel: "Open Volatility Carry evidence packet"
    });
  });

  it("owns portfolio detail empty-state labels", () => {
    const vm = buildPortfolioScreenViewModel({ trading: null, strategy: null, accounting });

    expect(vm.selectedPositionChip).toEqual({ label: "Selected detail", value: "None" });
    expect(vm.runEvidenceChip).toEqual({ label: "Run evidence", value: "No linked runs" });
    expect(vm.selectedRunChip).toEqual({ label: "Selected run", value: "None" });
    expect(vm.positionDetailEmptyTitle).toBe("No holding selected");
    expect(vm.runDetailEmptyTitle).toBe("No run selected");
  });

  it("derives provider-aware account selector and filtered brokerage positions", () => {
    const vm = buildPortfolioScreenViewModel({
      trading,
      strategy,
      accounting,
      brokerageConnection,
      brokeragePortfolio,
      selectedBrokerageAccountKey: "fund-roth"
    });

    expect(vm.brokerageConnectionLabel).toBe("Connected");
    expect(vm.brokerageConnectionTone).toBe("success");
    expect(vm.brokerageProviderLabel).toBe("Alpaca paper");
    expect(vm.brokerageAccountFilterLabel).toBe("Alpaca paper account filter");
    expect(vm.brokerageAccountOptions.map((option) => option.label)).toEqual(["All", "Roth IRA", "Brokerage"]);
    expect(vm.brokerageAccountOptions.find((option) => option.key === "fund-roth")?.isSelected).toBe(true);
    expect(vm.brokerageAccountOptions.map((option) => option.tabIndex)).toEqual([-1, 0, -1]);
    expect(vm.brokerageAccountRows).toHaveLength(2);
    expect(vm.brokerageAccountsTableLabel).toBe("Alpaca paper brokerage accounts");
    expect(vm.brokerageAccountDetailId).toBe("portfolio-brokerage-account-detail");
    expect(vm.brokerageAccountRows[0]).toMatchObject({
      id: "fund-roth",
      healthBadgeVariant: "success",
      positionCount: "1 position",
      warningCount: "0 warnings",
      isSelected: true,
      expanded: true,
      detailPanelId: vm.brokerageAccountDetailId,
      selectAriaLabel: "Filter brokerage positions to Roth IRA account"
    });
    expect(vm.selectedBrokerageAccount).toMatchObject({
      id: "fund-roth",
      title: "Roth IRA",
      ariaLabel: "Roth IRA brokerage account detail",
      statusBadgeLabel: "Healthy",
      statusBadgeVariant: "success"
    });
    expect(vm.selectedBrokerageAccount.fields.find((field) => field.label === "Buying power")?.value).toBe("$50,000");
    expect(vm.brokeragePositionRows).toHaveLength(1);
    expect(vm.brokeragePositionRows[0].symbol).toBe("AAPL");
    expect(vm.brokeragePositionRows[0].accountKind).toBe("Roth IRA");
    expect(vm.brokeragePositionRows[0].isSelected).toBe(true);
    expect(vm.brokeragePositionRows[0].expanded).toBe(true);
    expect(vm.brokeragePositionRows[0].detailPanelId).toBe(vm.brokeragePositionDetailId);
    expect(vm.selectedBrokeragePositionId).toBe("fund-roth-AAPL-pos-aapl");
    expect(vm.selectedBrokeragePosition?.title).toBe("AAPL");
    expect(vm.selectedBrokeragePosition?.fields.find((field) => field.label === "Security coverage")?.value).toBe("Security master missing");
    expect(vm.headerChips[0]).toEqual({ label: "Alpaca paper equity", value: "$375,000" });
    expect(vm.brokerageSetupAction).toBeNull();
  });

  it("keeps all-account brokerage detail as aggregate view state", () => {
    const vm = buildPortfolioScreenViewModel({
      trading,
      strategy,
      accounting,
      brokerageConnection,
      brokeragePortfolio
    });

    expect(vm.brokerageAccountRows.map((row) => row.isSelected)).toEqual([false, false]);
    expect(vm.brokerageAccountRows.map((row) => row.expanded)).toEqual([false, false]);
    expect(vm.selectedBrokerageAccount).toMatchObject({
      id: "all",
      title: "All brokerage accounts",
      statusTitle: "Household account scope",
      statusBadgeLabel: "Synced",
      statusBadgeVariant: "success"
    });
    expect(vm.selectedBrokerageAccount.fields).toEqual([
      { label: "Accounts", value: "2 accounts", tone: "default" },
      { label: "Equity", value: "$375,000", tone: "default" },
      { label: "Cash", value: "$150,000", tone: "default" },
      { label: "Buying power", value: "$150,000", tone: "default" },
      { label: "Warnings", value: "0 warnings", tone: "success" },
      { label: "Latest sync", value: "May 7, 12:00 UTC", tone: "muted" }
    ]);
  });

  it("keeps selected brokerage position state in the view model", () => {
    const selectBrokeragePosition = vi.fn();
    const vm = buildPortfolioScreenViewModel({
      trading,
      strategy,
      accounting,
      brokerageConnection,
      brokeragePortfolio,
      selectedBrokeragePositionId: "fund-taxable-MSFT-pos-msft",
      selectBrokeragePosition
    });

    expect(vm.brokeragePositionRows.map((row) => row.isSelected)).toEqual([false, true]);
    expect(vm.brokeragePositionRows.map((row) => row.expanded)).toEqual([false, true]);
    expect(vm.brokeragePositionRows[1].selectAriaLabel).toBe("Inspect MSFT Brokerage live position");
    expect(vm.brokeragePositionRows[1].detailPanelId).toBe("portfolio-brokerage-position-detail");
    expect(vm.brokeragePositionRows[1].rowClassName).toBe("bg-warning/5");
    expect(vm.selectedBrokeragePositionId).toBe("fund-taxable-MSFT-pos-msft");
    expect(vm.selectedBrokeragePosition?.title).toBe("MSFT");
    expect(vm.selectedBrokeragePosition?.statusDetail).toContain("$1,750 market value");
    expect(vm.selectedBrokeragePosition?.fields.find((field) => field.label === "Position ID")?.value).toBe("pos-msft");

    vm.selectBrokeragePosition("fund-roth-AAPL-pos-aapl");
    expect(selectBrokeragePosition).toHaveBeenCalledWith("fund-roth-AAPL-pos-aapl");
  });

  it("routes missing brokerage portfolio state to Settings provider setup", () => {
    const vm = buildPortfolioScreenViewModel({
      trading,
      strategy,
      accounting
    });

    expect(vm.brokerageConnectionLabel).toBe("Not configured");
    expect(vm.brokerageEmptyText).toBe("Alpaca paper portfolio sync has not produced a household projection yet.");
    expect(vm.brokerageSetupAction).toEqual({
      label: "Open provider setup",
      href: "/settings#alpaca-provider-setup",
      ariaLabel: "Open Alpaca paper provider setup from Portfolio brokerage panel",
      detail: "Verify Alpaca paper credentials before accepting brokerage portfolio state."
    });
  });

  it("keeps connected brokerage portfolios in warning posture when sync warnings exist", () => {
    const warningPortfolio: BrokerageHouseholdPortfolio = {
      ...brokeragePortfolio,
      warnings: ["Portfolio sync is stale."],
      accounts: [
        { ...brokeragePortfolio.accounts[0], warnings: ["Roth IRA account sync stale."] },
        brokeragePortfolio.accounts[1]
      ]
    };

    const vm = buildPortfolioScreenViewModel({
      trading,
      strategy,
      accounting,
      brokerageConnection,
      brokeragePortfolio: warningPortfolio
    });

    expect(vm.brokerageConnectionLabel).toBe("Connected");
    expect(vm.brokerageConnectionTone).toBe("warning");
    expect(vm.brokerageWarningCountLabel).toBe("2 brokerage warnings");
    expect(vm.brokerageWarningRows).toEqual([
      expect.objectContaining({
        label: "Alpaca paper portfolio",
        detail: "Portfolio sync is stale."
      }),
      expect.objectContaining({
        label: "Roth IRA account",
        detail: "Roth IRA account sync stale."
      })
    ]);
    expect(vm.brokerageAccountRows[0]).toMatchObject({
      hasWarning: true,
      warningText: "Roth IRA account sync stale.",
      warningCount: "1 warning",
      rowClassName: "bg-warning/5",
      healthBadgeVariant: "success"
    });
    expect(vm.brokerageAccountRows[1].rowClassName).toBe("bg-background/50");
    expect(vm.selectedBrokerageAccount).toMatchObject({
      id: "all",
      statusBadgeLabel: "Review",
      statusBadgeVariant: "warning"
    });
    expect(vm.selectedBrokerageAccount.fields.find((field) => field.label === "Warnings")).toEqual({
      label: "Warnings",
      value: "2 warnings",
      tone: "warning"
    });
  });

  it("keeps adjacent brokerage account selector transitions in the view model", () => {
    const selectBrokerageAccount = vi.fn();
    const vm = buildPortfolioScreenViewModel({
      trading,
      strategy,
      accounting,
      brokerageConnection,
      brokeragePortfolio,
      selectedBrokerageAccountKey: "fund-roth",
      selectBrokerageAccount
    });

    vm.selectAdjacentBrokerageAccount("next");
    expect(selectBrokerageAccount).toHaveBeenLastCalledWith("fund-taxable");

    vm.selectAdjacentBrokerageAccount("previous");
    expect(selectBrokerageAccount).toHaveBeenLastCalledWith("all");

    vm.selectAdjacentBrokerageAccount("first");
    expect(selectBrokerageAccount).toHaveBeenLastCalledWith("all");

    vm.selectAdjacentBrokerageAccount("last");
    expect(selectBrokerageAccount).toHaveBeenLastCalledWith("fund-taxable");
  });

  it("keeps brokerage account selector keyboard commands in the view model", () => {
    expect(resolveBrokerageAccountFilterKeyCommand("ArrowRight")).toBe("next");
    expect(resolveBrokerageAccountFilterKeyCommand("ArrowDown")).toBe("next");
    expect(resolveBrokerageAccountFilterKeyCommand("ArrowLeft")).toBe("previous");
    expect(resolveBrokerageAccountFilterKeyCommand("ArrowUp")).toBe("previous");
    expect(resolveBrokerageAccountFilterKeyCommand("Home")).toBe("first");
    expect(resolveBrokerageAccountFilterKeyCommand("End")).toBe("last");
    expect(resolveBrokerageAccountFilterKeyCommand("Tab")).toBeNull();
  });
});
