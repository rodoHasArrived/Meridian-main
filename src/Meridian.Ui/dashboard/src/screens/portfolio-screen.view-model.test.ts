import { describe, expect, it, vi } from "vitest";
import {
  buildPortfolioScreenViewModel,
  buildPortfolioFallbackMetrics,
  resolveBrokerageAccountFilterKeyCommand
} from "@/screens/portfolio-screen.view-model";
import type {
  BrokerageConnectionStatus,
  BrokerageHouseholdPortfolio,
  GovernanceWorkspaceResponse,
  PortfolioWorkspaceResponse,
  ResearchWorkspaceResponse,
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

const research: ResearchWorkspaceResponse = {
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

const governance: GovernanceWorkspaceResponse = {
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
    ...governance.cashFlow,
    netVariance: -125,
    summary: "Portfolio endpoint cash posture."
  }
};

describe("buildPortfolioScreenViewModel", () => {
  it("returns position rows from trading data", () => {
    const vm = buildPortfolioScreenViewModel({ trading, research, governance });
    expect(vm.hasPositions).toBe(true);
    expect(vm.positionRows).toHaveLength(1);
    expect(vm.positionRows[0].symbol).toBe("AAPL");
    expect(vm.positionRows[0].pnlTone).toBe("success");
    expect(vm.positionRows[0].isSelected).toBe(true);
    expect(vm.selectedPosition?.title).toBe("AAPL");
  });

  it("returns run rows from research data", () => {
    const vm = buildPortfolioScreenViewModel({ trading, research, governance });
    expect(vm.hasRuns).toBe(true);
    expect(vm.runRows).toHaveLength(1);
    expect(vm.runRows[0].strategyName).toBe("Mean Reversion");
    expect(vm.runRows[0].promotionState).toBe("Promoted");
    expect(vm.runRows[0].modeBadgeVariant).toBe("paper");
    expect(vm.runRows[0].pnlTone).toBe("success");
    expect(vm.runRows[0].isSelected).toBe(true);
    expect(vm.runRows[0].selectAriaLabel).toBe("Inspect Mean Reversion run evidence");
    expect(vm.selectedRun?.title).toBe("Mean Reversion");
    expect(vm.selectedRun?.statusDetail).toContain("Running paper run with +4.2% P&L");
  });

  it("surfaces cash-flow summary from governance data", () => {
    const vm = buildPortfolioScreenViewModel({ trading, research, governance });
    expect(vm.cashFlowSummary).toBe("1 run needs variance review.");
    expect(vm.cashFlowTone).toBe("warning");
    expect(vm.cashVarianceLabel).toBe("$500");
  });

  it("returns empty state text when trading data is null", () => {
    const vm = buildPortfolioScreenViewModel({ trading: null, research, governance });
    expect(vm.hasPositions).toBe(false);
    expect(vm.positionEmptyText).toContain("unavailable");
    expect(vm.metricsFromTrading).toBe(false);
    expect(vm.selectedPosition).toBeNull();
  });

  it("returns empty run text when research data is null", () => {
    const vm = buildPortfolioScreenViewModel({ trading, research: null, governance });
    expect(vm.hasRuns).toBe(false);
    expect(vm.runEmptyText).toContain("unavailable");
    expect(vm.selectedRun).toBeNull();
  });

  it("computes danger pnl tone for negative values", () => {
    const tradingWithLoss: TradingWorkspaceResponse = {
      ...trading,
      positions: [{ ...trading.positions[0], unrealizedPnl: "-$200" }]
    };
    const vm = buildPortfolioScreenViewModel({ trading: tradingWithLoss, research, governance });
    expect(vm.positionRows[0].pnlTone).toBe("danger");
    expect(vm.selectedPosition?.fields.find((field) => field.label === "Unrealized P&L")?.tone).toBe("danger");
  });

  it("provides fallback stats when trading is available", () => {
    const vm = buildPortfolioScreenViewModel({ trading, research, governance });
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
    const vm = buildPortfolioScreenViewModel({ trading, research, governance });

    expect(vm.headerChips).toEqual([
      { label: "Alpaca paper equity", value: "—" },
      { label: "Alpaca paper cash", value: "—" },
      { label: "Open positions", value: "1" },
      { label: "Exposure", value: "$18,900" },
      { label: "Unrealized P&L", value: "+$90" },
      { label: "Cash variance", value: "$500" }
    ]);
  });

  it("does not add a workflow task panel on the broad portfolio route", () => {
    const vm = buildPortfolioScreenViewModel({ trading, research, governance, pathname: "/portfolio" });

    expect(vm.workflowTaskPanel).toBeNull();
  });

  it("builds a dedicated brokerage-sync task panel from trading posture", () => {
    const vm = buildPortfolioScreenViewModel({
      trading,
      research,
      governance,
      pathname: "/portfolio/brokerage-sync"
    });

    expect(vm.workflowTaskPanel).toMatchObject({
      regionLabel: "Brokerage sync task",
      title: "Brokerage sync review",
      statusLabel: "Brokerage synced",
      statusTone: "success",
      selectedSummary: expect.stringContaining("Alpaca / paper account PA-DEMO")
    });
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
  });

  it("uses the Portfolio workspace payload as the primary portfolio read model", () => {
    const vm = buildPortfolioScreenViewModel({
      portfolio,
      trading,
      research,
      governance,
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
      research,
      governance,
      pathname: "/portfolio/brokerage-sync"
    });

    expect(vm.workflowTaskPanel?.statusLabel).toBe("Portfolio unavailable");
    expect(vm.workflowTaskPanel?.statusTone).toBe("danger");
    expect(vm.workflowTaskPanel?.selectedSummary).toContain("Portfolio workspace data is unavailable");
  });

  it("uses stable placeholder header chips when trading data is unavailable", () => {
    const vm = buildPortfolioScreenViewModel({ trading: null, research, governance });

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
      research,
      governance,
      selectedPositionId: "msft-short-1"
    });

    expect(vm.positionRows.map((row) => row.isSelected)).toEqual([false, true]);
    expect(vm.positionRows[1].selectAriaLabel).toBe("Inspect MSFT Short holding");
    expect(vm.selectedPosition?.title).toBe("MSFT");
    expect(vm.selectedPosition?.statusDetail).toContain("$10,250 exposure");
    expect(vm.selectedPosition?.fields.find((field) => field.label === "Guardrails")?.value).toBe("No active guardrails");
  });

  it("keeps selected run evidence state in the view model", () => {
    const researchWithTwoRuns: ResearchWorkspaceResponse = {
      ...research,
      runs: [
        research.runs[0],
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
      research: researchWithTwoRuns,
      governance,
      selectedRunId: "run-2"
    });

    expect(vm.runRows.map((row) => row.isSelected)).toEqual([false, true]);
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

  it("derives provider-aware account selector and filtered brokerage positions", () => {
    const vm = buildPortfolioScreenViewModel({
      trading,
      research,
      governance,
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
    expect(vm.brokeragePositionRows).toHaveLength(1);
    expect(vm.brokeragePositionRows[0].symbol).toBe("AAPL");
    expect(vm.brokeragePositionRows[0].accountKind).toBe("Roth IRA");
    expect(vm.headerChips[0]).toEqual({ label: "Alpaca paper equity", value: "$375,000" });
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
      research,
      governance,
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
      warningText: "Roth IRA account sync stale."
    });
  });

  it("keeps adjacent brokerage account selector transitions in the view model", () => {
    const selectBrokerageAccount = vi.fn();
    const vm = buildPortfolioScreenViewModel({
      trading,
      research,
      governance,
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
