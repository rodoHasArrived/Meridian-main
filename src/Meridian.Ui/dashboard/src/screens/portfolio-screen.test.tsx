import { describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithRouter } from "@/test/render";
import { PortfolioScreen } from "@/screens/portfolio-screen";
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

describe("PortfolioScreen", () => {
  it("renders position table with trading data", () => {
    renderWithRouter(<PortfolioScreen trading={trading} research={research} governance={governance} />);
    expect(screen.getByRole("table", { name: /open positions/i })).toBeDefined();
    expect(screen.getByRole("button", { name: /inspect aapl long holding/i })).toBeDefined();
    expect(screen.getByRole("complementary", { name: /aapl holding detail/i })).toBeDefined();
    expect(screen.getByText(/\$18,900 exposure with \+\$90 unrealized p&l/i)).toBeDefined();
  });

  it("renders run-linked equity table with research data", () => {
    renderWithRouter(<PortfolioScreen trading={trading} research={research} governance={governance} />);
    expect(screen.getByRole("table", { name: /run-linked equity/i })).toBeDefined();
    expect(screen.getByText("Mean Reversion")).toBeDefined();
  });

  it("shows empty text when trading is null", () => {
    renderWithRouter(<PortfolioScreen trading={null} research={research} governance={governance} />);
    expect(screen.getAllByText(/trading workspace data unavailable/i)).toHaveLength(2);
    expect(screen.getByText(/no holding selected/i)).toBeDefined();
  });

  it("shows empty text when research is null", () => {
    renderWithRouter(<PortfolioScreen trading={trading} research={null} governance={governance} />);
    expect(screen.getByText(/strategy workspace data unavailable/i)).toBeDefined();
  });

  it("shows cash-flow posture when governance data is available", () => {
    renderWithRouter(<PortfolioScreen trading={trading} research={research} governance={governance} />);
    expect(screen.getByText(/1 run needs variance review/i)).toBeDefined();
  });

  it("updates the holding detail panel from the inspect control", async () => {
    const user = userEvent.setup();
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

    renderWithRouter(<PortfolioScreen trading={tradingWithTwoPositions} research={research} governance={governance} />);

    const msftButton = screen.getByRole("button", { name: /inspect msft short holding/i });
    await user.click(msftButton);

    expect(msftButton).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("complementary", { name: /msft holding detail/i })).toBeDefined();
    expect(screen.getByText(/\$10,250 exposure with \+\$52.50 unrealized p&l/i)).toBeDefined();
  });
});
