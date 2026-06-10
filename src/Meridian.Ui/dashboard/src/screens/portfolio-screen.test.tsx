import { afterEach, describe, expect, it, vi } from "vitest";
import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithRouter } from "@/test/render";
import { PortfolioScreen } from "@/screens/portfolio-screen";
import * as api from "@/lib/api";
import type {
  BrokerageConnectionStatus,
  BrokerageHouseholdPortfolio,
  AccountingWorkspaceResponse,
  PortfolioWorkspaceResponse,
  StrategyWorkspaceResponse,
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
  metrics: [],
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
    summary: "Portfolio endpoint cash posture."
  }
};

describe("PortfolioScreen", () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders position table with trading data", () => {
    renderWithRouter(<PortfolioScreen trading={trading} strategy={strategy} accounting={accounting} />);
    expect(screen.getByRole("region", { name: /portfolio workbench context/i })).toBeDefined();
    expect(screen.getByRole("heading", { name: "Portfolio Explorer" })).toBeInTheDocument();
    expect(screen.getByLabelText("Explorer scope")).toHaveTextContent("Portfolio");
    expect(screen.getByLabelText("Explorer scope")).toHaveTextContent("Trading workspace");
    expect(screen.getByLabelText("Saved explorer views")).toHaveTextContent("Open positions + run evidence");
    expect(screen.getByLabelText("Applied explorer filters")).toHaveTextContent("AAPL");
    expect(screen.getByLabelText("Portfolio Explorer proof actions")).toHaveTextContent("Open evidence packet");
    expect(screen.getByRole("table", { name: /open positions/i })).toBeDefined();
    expect(screen.getByRole("row", { name: /inspect aapl long holding/i })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("complementary", { name: /aapl holding detail/i })).toBeDefined();
    expect(screen.getByText(/\$18,900 exposure with \+\$90 unrealized p&l/i)).toBeDefined();
  });

  it("renders positions and runs from the Portfolio workspace payload when available", () => {
    renderWithRouter(
      <PortfolioScreen portfolio={portfolio} trading={trading} strategy={strategy} accounting={accounting} />
    );

    const positionsTable = screen.getByRole("table", { name: /open positions/i });
    expect(within(positionsTable).getByText("NVDA")).toBeDefined();
    expect(within(positionsTable).queryByText("AAPL")).toBeNull();
    expect(screen.getAllByText("Portfolio workspace").length).toBeGreaterThanOrEqual(1);
    expect(screen.getByRole("row", { name: /inspect portfolio endpoint run run evidence/i })).toBeDefined();
    expect(screen.getByText(/portfolio endpoint cash posture/i)).toBeDefined();
  });

  it("renders a broad Portfolio readiness handoff with direct next routes", () => {
    renderWithRouter(<PortfolioScreen trading={trading} strategy={strategy} accounting={accounting} />, {
      initialEntries: ["/portfolio"]
    });

    const handoff = screen.getByRole("region", { name: "Portfolio readiness handoff" });
    expect(within(handoff).getByText("Portfolio acceptance handoff")).toBeInTheDocument();
    const actions = within(handoff).getByLabelText("Portfolio readiness next actions");
    const brokerageSyncLink = within(actions).getByRole("link", { name: "Open brokerage sync review from Portfolio readiness" });
    expect(brokerageSyncLink).toHaveAttribute("href", "/portfolio/brokerage-sync");
    expect(brokerageSyncLink).toHaveAttribute("aria-describedby", "portfolio-readiness-brokerage-sync-detail");
    expect(within(handoff).getByRole("link", { name: "Open Trading readiness from Portfolio readiness" })).toHaveAttribute(
      "href",
      "/trading/readiness"
    );
    expect(within(handoff).getByRole("link", { name: "Open Mean Reversion evidence from Portfolio readiness" })).toHaveAttribute(
      "href",
      "/reporting/evidence?subjectKind=strategy-run&subjectId=run-1"
    );
  });

  it("renders run-linked equity table with strategy data", () => {
    renderWithRouter(<PortfolioScreen trading={trading} strategy={strategy} accounting={accounting} />);
    expect(screen.getByRole("table", { name: /run-linked equity/i })).toBeDefined();
    expect(screen.getByRole("row", { name: /inspect mean reversion run evidence/i })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("complementary", { name: /mean reversion run detail/i })).toBeDefined();
    expect(screen.getByText(/running paper run with \+4.2% p&l/i)).toBeDefined();
  });

  it("shows empty text when trading is null", () => {
    renderWithRouter(<PortfolioScreen trading={null} strategy={strategy} accounting={accounting} />);
    expect(screen.getAllByText(/portfolio workspace data unavailable/i)).toHaveLength(2);
    expect(screen.getAllByText(/no holding selected/i).length).toBeGreaterThanOrEqual(1);
  });

  it("shows empty text when strategy is null", () => {
    renderWithRouter(<PortfolioScreen trading={trading} strategy={null} accounting={accounting} />);
    expect(screen.getByText(/strategy workspace data unavailable/i)).toBeDefined();
  });

  it("shows cash-flow posture when accounting data is available", () => {
    renderWithRouter(<PortfolioScreen trading={trading} strategy={strategy} accounting={accounting} />);
    expect(screen.getByText(/1 run needs variance review/i)).toBeDefined();
  });

  it("renders Alpaca account and selectable current positions when brokerage sync data is available", async () => {
    const user = userEvent.setup();
    renderWithRouter(
      <PortfolioScreen
        trading={trading}
        strategy={strategy}
        accounting={accounting}
        brokerageConnection={brokerageConnection}
        brokeragePortfolio={brokeragePortfolio}
      />
    );

    expect(screen.getByText(/live brokerage portfolio/i)).toBeDefined();
    const trustSnapshot = screen.getByRole("region", { name: /alpaca paper brokerage sync snapshot/i });
    expect(within(trustSnapshot).getByText(/household synced/i)).toBeInTheDocument();
    expect(within(trustSnapshot).getAllByText("May 7, 12:00 UTC").length).toBeGreaterThan(0);
    expect(within(trustSnapshot).getAllByText("$375,000").length).toBeGreaterThan(0);
    expect(within(trustSnapshot).getByText("2 accounts")).toBeInTheDocument();
    expect(within(trustSnapshot).getByText("2 positions")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /show alpaca paper roth ira account/i })).toBeDefined();
    expect(screen.getByRole("table", { name: /alpaca paper brokerage accounts/i })).toBeDefined();
    expect(screen.getByRole("complementary", { name: /all brokerage accounts detail/i })).toBeDefined();
    expect(screen.getByRole("table", { name: /alpaca paper current positions/i })).toBeDefined();
    expect(screen.getAllByText(/alpaca roth ira/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText("AAPL").length).toBeGreaterThan(0);

    const defaultDetail = screen.getByRole("complementary", { name: /aapl brokerage position detail/i });
    expect(within(defaultDetail).getByText(/brokerage position inspector/i)).toBeInTheDocument();
    expect(within(defaultDetail).getAllByText(/security master missing/i).length).toBeGreaterThan(0);

    const msftRow = screen.getByRole("row", { name: /inspect msft brokerage live position/i });
    expect(msftRow).toHaveAttribute("aria-controls", "portfolio-brokerage-position-detail");
    expect(msftRow).toHaveAttribute("aria-expanded", "false");
    expect(msftRow).toHaveClass("bg-warning/5");
    await user.click(msftRow);

    const updatedDetail = screen.getByRole("complementary", { name: /msft brokerage position detail/i });
    expect(within(updatedDetail).getByText(/alpaca paper \/ alpaca brokerage \/ equity/i)).toBeInTheDocument();
    expect(within(updatedDetail).getByText("$1,750")).toBeInTheDocument();
    expect(msftRow).toHaveAttribute("aria-selected", "true");
    expect(msftRow).toHaveAttribute("aria-expanded", "true");
  });

  it("selects live brokerage positions from the row with keyboard activation", async () => {
    const user = userEvent.setup();
    renderWithRouter(
      <PortfolioScreen
        trading={trading}
        strategy={strategy}
        accounting={accounting}
        brokerageConnection={brokerageConnection}
        brokeragePortfolio={brokeragePortfolio}
      />
    );

    const msftRow = screen.getByRole("row", { name: /inspect msft brokerage live position/i });
    msftRow.focus();
    expect(msftRow).toHaveFocus();

    await user.keyboard("{Enter}");

    expect(msftRow).toHaveAttribute("aria-selected", "true");
    expect(msftRow).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("complementary", { name: /msft brokerage position detail/i })).toBeInTheDocument();
  });

  it("offers a provider setup handoff when brokerage portfolio sync is unavailable", () => {
    renderWithRouter(<PortfolioScreen trading={trading} strategy={strategy} accounting={accounting} />);

    const trustSnapshot = screen.getByRole("region", { name: /alpaca paper brokerage sync snapshot/i });
    expect(within(trustSnapshot).getByText(/provider setup needed/i)).toBeInTheDocument();
    expect(within(trustSnapshot).getByText(/no alpaca paper household snapshot has loaded yet/i)).toBeInTheDocument();

    const handoff = screen.getByRole("link", {
      name: /open alpaca paper provider setup from portfolio brokerage panel/i
    });
    expect(handoff).toHaveAttribute("href", "/settings#alpaca-provider-setup");
    expect(screen.getByText(/verify alpaca paper credentials before accepting brokerage portfolio state/i)).toBeInTheDocument();
  });

  it("renders portfolio and account sync warnings in the live brokerage panel", () => {
    const warningPortfolio: BrokerageHouseholdPortfolio = {
      ...brokeragePortfolio,
      warnings: ["Portfolio sync is stale."],
      accounts: [
        { ...brokeragePortfolio.accounts[0], warnings: ["Roth IRA account sync stale."] },
        brokeragePortfolio.accounts[1]
      ]
    };

    renderWithRouter(
      <PortfolioScreen
        trading={trading}
        strategy={strategy}
        accounting={accounting}
        brokerageConnection={brokerageConnection}
        brokeragePortfolio={warningPortfolio}
      />
    );

    const warningSummary = screen.getByRole("status", { name: "2 brokerage warnings" });
    const trustSnapshot = screen.getByRole("region", { name: /alpaca paper brokerage sync snapshot/i });
    const accountDetail = screen.getByRole("complementary", { name: /all brokerage accounts detail/i });
    expect(within(trustSnapshot).getByText(/review sync/i)).toBeInTheDocument();
    expect(within(trustSnapshot).getByText("2 issues")).toBeInTheDocument();
    expect(within(warningSummary).getByText("Portfolio sync is stale.")).toBeInTheDocument();
    expect(within(warningSummary).getByText("Roth IRA account sync stale.")).toBeInTheDocument();
    expect(within(accountDetail).getByText(/positions table is showing all alpaca paper brokerage accounts/i)).toBeInTheDocument();
    expect(within(accountDetail).getAllByText("2 warnings").length).toBeGreaterThan(0);
    expect(screen.getByRole("row", { name: /filter brokerage positions to roth ira account/i })).toHaveClass("bg-warning/5");
  });

  it("renders the brokerage account filter as a keyboard-operable button group", async () => {
    const user = userEvent.setup();
    renderWithRouter(
      <PortfolioScreen
        trading={trading}
        strategy={strategy}
        accounting={accounting}
        brokerageConnection={brokerageConnection}
        brokeragePortfolio={brokeragePortfolio}
      />
    );

    expect(screen.queryByRole("tablist", { name: /alpaca paper account filter/i })).toBeNull();
    const filter = screen.getByRole("group", { name: /alpaca paper account filter/i });
    const allButton = within(filter).getByRole("button", { name: /show all alpaca paper accounts/i });
    const rothButton = within(filter).getByRole("button", { name: /show alpaca paper roth ira account/i });
    const brokerageButton = within(filter).getByRole("button", { name: /show alpaca paper brokerage account/i });

    allButton.focus();
    expect(allButton).toHaveFocus();
    expect(allButton).toHaveAttribute("aria-pressed", "true");
    expect(allButton).toHaveAttribute("tabindex", "0");
    expect(rothButton).toHaveAttribute("tabindex", "-1");

    await user.keyboard("{ArrowRight}");
    expect(rothButton).toHaveFocus();
    expect(rothButton).toHaveAttribute("aria-pressed", "true");
    expect(allButton).toHaveAttribute("tabindex", "-1");
    expect(rothButton).toHaveAttribute("tabindex", "0");
    const rothRow = screen.getByRole("row", { name: /filter brokerage positions to roth ira account/i });
    expect(rothRow).toHaveAttribute("aria-selected", "true");
    expect(rothRow).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("complementary", { name: /roth ira brokerage account detail/i })).toBeInTheDocument();
    let brokerageTable = screen.getByRole("table", { name: /alpaca paper current positions/i });
    expect(within(brokerageTable).getByText("AAPL")).toBeDefined();
    expect(within(brokerageTable).queryByText("MSFT")).toBeNull();

    rothButton.focus();
    await user.keyboard("{End}");
    expect(brokerageButton).toHaveFocus();
    expect(brokerageButton).toHaveAttribute("aria-pressed", "true");
    expect(rothButton).toHaveAttribute("tabindex", "-1");
    expect(brokerageButton).toHaveAttribute("tabindex", "0");
    brokerageTable = screen.getByRole("table", { name: /alpaca paper current positions/i });
    expect(within(brokerageTable).getByText("MSFT")).toBeDefined();
    expect(within(brokerageTable).queryByText("AAPL")).toBeNull();
  });

  it("filters brokerage positions from the account table row with keyboard activation", async () => {
    const user = userEvent.setup();
    renderWithRouter(
      <PortfolioScreen
        trading={trading}
        strategy={strategy}
        accounting={accounting}
        brokerageConnection={brokerageConnection}
        brokeragePortfolio={brokeragePortfolio}
      />
    );

    const taxableRow = screen.getByRole("row", { name: /filter brokerage positions to brokerage account/i });
    taxableRow.focus();
    expect(taxableRow).toHaveFocus();

    await user.keyboard(" ");

    expect(taxableRow).toHaveAttribute("aria-selected", "true");
    expect(taxableRow).toHaveAttribute("aria-controls", "portfolio-brokerage-account-detail");
    expect(taxableRow).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("button", { name: /show alpaca paper brokerage account/i })).toHaveAttribute("aria-pressed", "true");
    const accountDetail = screen.getByRole("complementary", { name: /brokerage brokerage account detail/i });
    expect(within(accountDetail).getByText(/alpaca paper \/ alpaca brokerage/i)).toBeInTheDocument();
    const brokerageTable = screen.getByRole("table", { name: /alpaca paper current positions/i });
    expect(within(brokerageTable).getByText("MSFT")).toBeDefined();
    expect(within(brokerageTable).queryByText("AAPL")).toBeNull();
  });

  it("renders a dedicated brokerage-sync workflow panel on the route", () => {
    renderWithRouter(<PortfolioScreen trading={trading} strategy={strategy} accounting={accounting} />, {
      initialEntries: ["/portfolio/brokerage-sync"]
    });

    const panel = screen.getByRole("region", { name: /brokerage sync task/i });
    expect(within(panel).getByText(/brokerage sync review/i)).toBeDefined();
    expect(within(panel).getByText(/brokerage synced/i)).toBeDefined();
    expect(within(panel).getByText(/alpaca \/ paper account pa-demo/i)).toBeDefined();
    expect(within(panel).getByRole("link", { name: /open trading readiness from brokerage sync review/i })).toHaveAttribute(
      "href",
      "/trading/readiness"
    );
    expect(within(panel).getByRole("link", { name: /open trading cockpit from brokerage sync review/i })).toHaveAttribute(
      "href",
      "/trading"
    );
    expect(within(panel).getByRole("link", { name: /get \/api\/portfolio\/aggregate/i })).toBeDefined();
    expect(within(panel).getByRole("link", { name: /get \/api\/workstation\/trading\/readiness/i })).toBeDefined();
  });

  it("updates the holding detail panel from the selectable row", async () => {
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

    renderWithRouter(<PortfolioScreen trading={tradingWithTwoPositions} strategy={strategy} accounting={accounting} />);

    const msftRow = screen.getByRole("row", { name: /inspect msft short holding/i });
    expect(msftRow).toHaveAttribute("aria-controls", "portfolio-position-detail");
    expect(msftRow).toHaveAttribute("aria-expanded", "false");
    await user.click(msftRow);

    expect(msftRow).toHaveAttribute("aria-selected", "true");
    expect(msftRow).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("complementary", { name: /msft holding detail/i })).toBeDefined();
    expect(screen.getByText(/\$10,250 exposure with \+\$52.50 unrealized p&l/i)).toBeDefined();
  });

  it("updates the run evidence detail panel from the selectable row", async () => {
    const user = userEvent.setup();
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

    renderWithRouter(<PortfolioScreen trading={trading} strategy={researchWithTwoRuns} accounting={accounting} />);

    const comparison = screen.getByLabelText("Portfolio run comparison summary");
    expect(comparison).toHaveTextContent("2 strategy runs compared across 2 modes and 2 engines.");
    expect(comparison).toHaveTextContent("Best P&L");
    expect(comparison).toHaveTextContent("+4.2%");
    expect(comparison).toHaveTextContent("Weakest P&L");
    expect(comparison).toHaveTextContent("-1.2%");
    expect(comparison).toHaveTextContent("backtest, paper; Native, QuantConnect.");

    const volatilityRow = screen.getByRole("row", { name: /inspect volatility carry run evidence/i });
    expect(volatilityRow).toHaveAttribute("aria-controls", "portfolio-run-detail");
    expect(volatilityRow).toHaveAttribute("aria-expanded", "false");
    await user.click(volatilityRow);

    expect(volatilityRow).toHaveAttribute("aria-selected", "true");
    expect(volatilityRow).toHaveAttribute("aria-expanded", "true");
    const detail = screen.getByRole("complementary", { name: /volatility carry run detail/i });
    expect(detail).toBeDefined();
    expect(screen.getByText(/drawdown review required/i)).toBeDefined();
    expect(within(detail).getAllByText("Needs Review").length).toBeGreaterThan(0);
    expect(screen.getByText("run-2")).toBeDefined();
  });

  it("loads shared selected-run portfolio drill-in evidence on demand", async () => {
    vi.spyOn(api, "getRunAttribution").mockResolvedValue({
      runId: "run-1",
      totalRealizedPnl: 120,
      totalUnrealizedPnl: 80,
      totalCommissions: 5,
      bySymbol: []
    });
    vi.spyOn(api, "getRunEquityCurve").mockResolvedValue({
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
    });
    vi.spyOn(api, "getRunCashFlows").mockResolvedValue({
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
    });
    vi.spyOn(api, "getRunFills").mockResolvedValue({
      runId: "run-1",
      totalFills: 2,
      totalCommissions: 5,
      fills: []
    });
    const user = userEvent.setup();

    renderWithRouter(<PortfolioScreen trading={trading} strategy={strategy} accounting={accounting} />);

    await user.click(screen.getByRole("button", { name: "Load portfolio drill-in evidence for Mean Reversion" }));

    const drillIn = await screen.findByLabelText("Selected run portfolio drill-in evidence");
    expect(drillIn).toHaveTextContent("4/4 drill-in evidence slices loaded for Mean Reversion.");
    expect(drillIn).toHaveTextContent("Realized +$120; unrealized +$80");
    expect(drillIn).toHaveTextContent("1.25%");
    expect(drillIn).toHaveTextContent("+$1,200");
    expect(api.getRunAttribution).toHaveBeenCalledWith("run-1");
    expect(api.getRunEquityCurve).toHaveBeenCalledWith("run-1");
    expect(api.getRunCashFlows).toHaveBeenCalledWith("run-1");
    expect(api.getRunFills).toHaveBeenCalledWith("run-1");
  });
});
