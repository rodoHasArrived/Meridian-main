import { describe, expect, it } from "vitest";
import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithRouter } from "@/test/render";
import { PortfolioScreen } from "@/screens/portfolio-screen";
import type {
  BrokerageConnectionStatus,
  BrokerageHouseholdPortfolio,
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
    }
  ]
};

describe("PortfolioScreen", () => {
  it("renders position table with trading data", () => {
    renderWithRouter(<PortfolioScreen trading={trading} research={research} governance={governance} />);
    expect(screen.getByRole("region", { name: /portfolio workbench context/i })).toBeDefined();
    expect(screen.getByRole("table", { name: /open positions/i })).toBeDefined();
    expect(screen.getByRole("button", { name: /inspect aapl long holding/i })).toBeDefined();
    expect(screen.getByRole("complementary", { name: /aapl holding detail/i })).toBeDefined();
    expect(screen.getByText(/\$18,900 exposure with \+\$90 unrealized p&l/i)).toBeDefined();
  });

  it("renders run-linked equity table with research data", () => {
    renderWithRouter(<PortfolioScreen trading={trading} research={research} governance={governance} />);
    expect(screen.getByRole("table", { name: /run-linked equity/i })).toBeDefined();
    expect(screen.getByRole("button", { name: /inspect mean reversion run evidence/i })).toBeDefined();
    expect(screen.getByRole("complementary", { name: /mean reversion run detail/i })).toBeDefined();
    expect(screen.getByText(/running paper run with \+4.2% p&l/i)).toBeDefined();
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

  it("renders Alpaca account and current positions when brokerage sync data is available", () => {
    renderWithRouter(
      <PortfolioScreen
        trading={trading}
        research={research}
        governance={governance}
        brokerageConnection={brokerageConnection}
        brokeragePortfolio={brokeragePortfolio}
      />
    );

    expect(screen.getByText(/live brokerage portfolio/i)).toBeDefined();
    expect(screen.getByRole("button", { name: /show alpaca paper roth ira account/i })).toBeDefined();
    expect(screen.getByRole("table", { name: /alpaca paper current positions/i })).toBeDefined();
    expect(screen.getAllByText(/alpaca roth ira/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText("AAPL").length).toBeGreaterThan(0);
  });

  it("renders a dedicated brokerage-sync workflow panel on the route", () => {
    renderWithRouter(<PortfolioScreen trading={trading} research={research} governance={governance} />, {
      initialEntries: ["/portfolio/brokerage-sync"]
    });

    const panel = screen.getByRole("region", { name: /brokerage sync task/i });
    expect(within(panel).getByText(/brokerage sync review/i)).toBeDefined();
    expect(within(panel).getByText(/brokerage synced/i)).toBeDefined();
    expect(within(panel).getByText(/alpaca \/ paper account pa-demo/i)).toBeDefined();
    expect(within(panel).getByRole("link", { name: /get \/api\/portfolio\/aggregate/i })).toBeDefined();
    expect(within(panel).getByRole("link", { name: /get \/api\/workstation\/trading\/readiness/i })).toBeDefined();
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

  it("updates the run evidence detail panel from the inspect control", async () => {
    const user = userEvent.setup();
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

    renderWithRouter(<PortfolioScreen trading={trading} research={researchWithTwoRuns} governance={governance} />);

    const volatilityButton = screen.getByRole("button", { name: /inspect volatility carry run evidence/i });
    await user.click(volatilityButton);

    expect(volatilityButton).toHaveAttribute("aria-pressed", "true");
    const detail = screen.getByRole("complementary", { name: /volatility carry run detail/i });
    expect(detail).toBeDefined();
    expect(screen.getByText(/drawdown review required/i)).toBeDefined();
    expect(within(detail).getAllByText("Needs Review").length).toBeGreaterThan(0);
    expect(screen.getByText("run-2")).toBeDefined();
  });
});
