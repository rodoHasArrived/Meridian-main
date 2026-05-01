import { describe, expect, it } from "vitest";
import { buildPortfolioScreenViewModel } from "@/screens/portfolio-screen.view-model";
import type {
  GovernanceWorkspaceResponse,
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
    expect(vm.fallbackStats.find((s) => s.label === "Open positions")?.value).toBe("1");
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
});
