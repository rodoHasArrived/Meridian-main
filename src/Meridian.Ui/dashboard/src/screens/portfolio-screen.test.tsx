import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import { PortfolioScreen } from "@/screens/portfolio-screen";
import * as api from "@/lib/api";
import type {
  BrokerageConnectionStatus,
  BrokerageHouseholdPortfolio,
  AccountingWorkspaceResponse,
  FinancialRecordExplorerDto,
  MultiAssetCoverageSummary,
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

const multiAssetCoverage: MultiAssetCoverageSummary = {
  fundAccountId: "all",
  entity: "portfolio",
  asOfUtc: "2026-06-02T00:00:00Z",
  metrics: [],
  assetClasses: [],
  drillThroughRoutes: {
    coverage: "/api/workstation/portfolio/multi-asset-coverage"
  }
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

function createPortfolioFinancialRecordExplorer(): FinancialRecordExplorerDto {
  const detail = {
    recordId: "portfolio:run-1:AAPL",
    recordType: "Portfolio position",
    title: "AAPL",
    subtitle: "Long - run-1",
    description: "Source-backed portfolio position retained for account and aggregate review.",
    tone: "Success" as const,
    fields: [
      { label: "Quantity", value: "100", detail: "Retained position quantity.", tone: "Default" as const },
      { label: "Unrealized PnL", value: "+$90", detail: "Source-backed unrealized P&L.", tone: "Success" as const }
    ],
    proofActions: [
      {
        actionId: "open-source",
        label: "Open source record",
        description: "Open retained source.",
        href: "/portfolio",
        isEnabled: true,
        disabledReason: "",
        tone: "Info" as const
      }
    ],
    usedIn: [
      { relationshipId: "portfolio-run", label: "Portfolio run", description: "Used by the selected portfolio run.", href: "/portfolio", tone: "Info" as const }
    ],
    impacts: [
      { relationshipId: "portfolio-equity", label: "Portfolio equity", description: "Contributes to aggregate equity.", href: "/portfolio", tone: "Success" as const }
    ],
    fullRecordHref: "/portfolio"
  };

  return {
    explorerId: "portfolio",
    title: "Portfolio Explorer",
    description: "Explore retained account and aggregate position records.",
    sourceState: "Source-backed portfolio projection from run run-1.",
    isBlocked: false,
    blockedReason: "",
    scopeItems: [
      { label: "Workstream", value: "Portfolio", tone: "Info" },
      { label: "Source", value: "Trading workspace", tone: "Default" }
    ],
    savedViews: [
      {
        viewId: "system-portfolio-default",
        label: "Open positions + run evidence",
        description: "Default portfolio explorer view.",
        isSystem: true,
        isActive: true,
        filters: [],
        searchText: ""
      }
    ],
    summaryItems: [
      { label: "Positions", value: "1", detail: "Retained position rows.", tone: "Success" },
      { label: "Equity", value: "$375,000", detail: "Source-backed total equity.", tone: "Default" }
    ],
    filters: [
      { filterId: "symbol", label: "Symbol", value: "AAPL", operator: "equals", tone: "Info" }
    ],
    columns: [
      { columnId: "symbol", header: "Symbol", cellKind: "text", width: 100, isRightAligned: false },
      { columnId: "quantity", header: "Quantity", cellKind: "number", width: 100, isRightAligned: true }
    ],
    rows: [
      {
        recordId: "portfolio:run-1:AAPL",
        recordType: "portfolio",
        label: "AAPL",
        source: "Portfolio",
        status: "Long",
        tone: "Success",
        cells: [
          { columnId: "symbol", displayValue: "AAPL", rawValue: "AAPL", tone: "Success", linkHref: "" },
          { columnId: "quantity", displayValue: "100", rawValue: "100", tone: "Default", linkHref: "" }
        ],
        detail
      }
    ],
    selectedRecord: detail,
    proofActions: [
      {
        actionId: "evidence",
        label: "Open evidence packet",
        description: "Open retained evidence packet.",
        href: "/reporting/evidence",
        isEnabled: true,
        disabledReason: "",
        tone: "Info"
      }
    ],
    recordGraph: {
      nodes: [
        { nodeId: "portfolio:run-1:AAPL", label: "AAPL", nodeType: "portfolio", tone: "Success", href: "/portfolio" }
      ],
      edges: []
    }
  };
}

async function renderPortfolioScreen(...args: Parameters<typeof renderWithRouter>) {
  const result = renderWithRouter(...args);
  await waitForAsyncEffects();
  return result;
}

describe("PortfolioScreen", () => {
  beforeEach(() => {
    vi.spyOn(api, "getFinancialRecordExplorer").mockResolvedValue(createPortfolioFinancialRecordExplorer());
    vi.spyOn(api, "saveFinancialRecordExplorerView").mockImplementation((_explorerId, request) =>
      Promise.resolve({
        viewId: "operator-portfolio-test",
        label: request.label,
        description: request.description,
        isSystem: false,
        isActive: false,
        filters: request.filters,
        searchText: request.searchText
      })
    );
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("navigates between portfolio route views from the tab strip", async () => {
    await renderPortfolioScreen(<PortfolioScreen trading={trading} strategy={strategy} accounting={accounting} />, {
      initialEntries: ["/portfolio"]
    });

    const tablist = screen.getByRole("tablist", { name: "Portfolio routes" });
    expect(within(tablist).getByRole("tab", { name: "Overview", selected: true })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Execution-linked holdings" })).toBeInTheDocument();
    expect(screen.getByRole("treegrid", { name: /open positions/i })).toBeDefined();
    expect(screen.queryByText(/live brokerage portfolio/i)).toBeNull();
    expect(screen.queryByRole("treegrid", { name: /run-linked equity/i })).toBeNull();

    const user = userEvent.setup();
    await user.click(within(tablist).getByRole("tab", { name: "Attribution" }));
    expect(screen.getByRole("heading", { name: "Attribution" })).toBeInTheDocument();
    expect(screen.getByRole("treegrid", { name: /run-linked equity/i })).toBeDefined();
    expect(screen.queryByRole("treegrid", { name: /open positions/i })).toBeNull();

    await user.click(within(tablist).getByRole("tab", { name: "Brokerage sync" }));
    expect(screen.getAllByText(/live brokerage portfolio/i).length).toBeGreaterThan(0);
    expect(screen.queryByRole("treegrid", { name: /run-linked equity/i })).toBeNull();
  });

  it("renders position table with trading data", async () => {
    await renderPortfolioScreen(<PortfolioScreen trading={trading} strategy={strategy} accounting={accounting} />);
    expect(screen.getByRole("region", { name: /portfolio workbench context/i })).toBeDefined();
    expect(screen.getByRole("heading", { name: "Portfolio Explorer" })).toBeInTheDocument();
    expect(screen.getByLabelText("Explorer scope")).toHaveTextContent("Portfolio");
    expect(screen.getByLabelText("Explorer scope")).toHaveTextContent("Trading workspace");
    expect(screen.getByLabelText("Saved explorer views")).toHaveTextContent("Open positions + run evidence");
    expect(screen.getByLabelText("Applied explorer filters")).toHaveTextContent("AAPL");
    expect(screen.getByLabelText("Portfolio Explorer proof actions")).toHaveTextContent("Open evidence packet");
    expect(screen.getByRole("treegrid", { name: /open positions/i })).toBeDefined();
    expect(screen.getByRole("row", { name: /inspect aapl long holding/i })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("region", { name: /aapl holding detail/i })).toBeDefined();
    expect(screen.getByText(/\$18,900 exposure with \+\$90 unrealized p&l/i)).toBeDefined();
  });

  it("presents raw explorer fixture metadata as operator-facing portfolio labels", async () => {
    const explorer = createPortfolioFinancialRecordExplorer();
    explorer.description = "Explore retained portfolio records from no-host development fixtures.";
    explorer.sourceState = "Development fixture portfolio projection from run portfolio-run-dev-1.";
    explorer.scopeItems = [
      { label: "Workstream", value: "Portfolio", tone: "Info" },
      { label: "Source", value: "Development fixture portfolio", tone: "Default" },
      { label: "Run", value: "portfolio-run-dev-1", tone: "Info" },
      { label: "As of", value: "2026-04-28T18:14:30Z", tone: "Default" }
    ];
    explorer.filters = [
      { filterId: "run", label: "Run", value: "portfolio-run-dev-1", operator: "equals", tone: "Info" },
      { filterId: "source", label: "Source", value: "development fixture", operator: "equals", tone: "Default" }
    ];
    explorer.recordGraph.nodes.unshift({
      nodeId: "portfolio-run-dev-1",
      label: "portfolio-run-dev-1",
      nodeType: "run",
      tone: "Info",
      href: "/api/workstation/portfolio"
    });
    vi.mocked(api.getFinancialRecordExplorer).mockResolvedValue(explorer);

    await renderPortfolioScreen(<PortfolioScreen trading={trading} strategy={strategy} accounting={accounting} />);

    const scope = screen.getByLabelText("Explorer scope");
    expect(scope).toHaveTextContent("Demo portfolio data");
    expect(scope).toHaveTextContent("Mean Reversion");
    expect(scope).toHaveTextContent("Apr 28, 18:14 UTC");
    const filters = screen.getByLabelText("Applied explorer filters");
    expect(filters).toHaveTextContent("Demo portfolio data");
    expect(filters).toHaveTextContent("Mean Reversion");
    expect(screen.getByLabelText("Record graph")).toHaveTextContent("Mean Reversion");
    expect(screen.queryByText(/development fixture/i)).not.toBeInTheDocument();
    expect(screen.queryByText("portfolio-run-dev-1")).not.toBeInTheDocument();
    expect(screen.queryByText("2026-04-28T18:14:30Z")).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open evidence packet" })).toHaveAttribute("href", "/reporting/evidence");
  });

  it("shows source, scope, freshness, and completeness in one operational summary", async () => {
    await renderPortfolioScreen(
      <PortfolioScreen
        trading={trading}
        strategy={strategy}
        accounting={accounting}
        refreshStatus={{
          operation: "portfolio refresh",
          phase: "succeeded",
          inFlight: false,
          version: 1,
          message: "Refresh complete.",
          error: null,
          startedAt: new Date().toISOString(),
          settledAt: new Date().toISOString(),
          lastSucceededAt: new Date().toISOString(),
          staleDiscardCount: 0,
          backoff: { attempt: 1, retryCount: 0, nextRetryDelayMs: null, maxRetries: 0 }
        }}
      />
    );

    const summary = screen.getByLabelText("Portfolio data confidence");
    expect(summary).toHaveTextContent("Trading workspace");
    expect(summary).toHaveTextContent("Freshness");
    expect(summary).toHaveTextContent("Holdings and run evidence loaded");
  });

  it("uses loaded snapshot evidence when the refresh lifecycle has no success timestamp", async () => {
    await renderPortfolioScreen(
      <PortfolioScreen
        trading={trading}
        strategy={strategy}
        accounting={accounting}
        brokeragePortfolio={brokeragePortfolio}
        refreshStatus={{
          operation: "portfolio refresh",
          phase: "idle",
          inFlight: false,
          version: 0,
          message: "Not refreshed yet.",
          error: null,
          startedAt: null,
          settledAt: null,
          lastSucceededAt: null,
          staleDiscardCount: 0,
          backoff: { attempt: 0, retryCount: 0, nextRetryDelayMs: null, maxRetries: 0 }
        }}
      />
    );

    const summary = screen.getByLabelText("Portfolio data confidence");
    expect(summary).toHaveTextContent("Latest brokerage portfolio snapshot");
    expect(summary).not.toHaveTextContent("No data");
  });

  it("renders positions and runs from the Portfolio workspace payload when available", async () => {
    const { unmount } = await renderPortfolioScreen(
      <PortfolioScreen portfolio={portfolio} trading={trading} strategy={strategy} accounting={accounting} />
    );

    const positionsTable = screen.getByRole("treegrid", { name: /open positions/i });
    expect(within(positionsTable).getByText("NVDA")).toBeDefined();
    expect(within(positionsTable).queryByText("AAPL")).toBeNull();
    expect(screen.getAllByText("Portfolio workspace").length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText(/portfolio endpoint cash posture/i)).toBeDefined();
    unmount();

    await renderPortfolioScreen(
      <PortfolioScreen portfolio={portfolio} trading={trading} strategy={strategy} accounting={accounting} />,
      { initialEntries: ["/portfolio/attribution"] }
    );
    expect(screen.getByRole("row", { name: /inspect portfolio endpoint run run evidence/i })).toBeDefined();
  });

  it("renders a broad Portfolio readiness handoff with direct next routes", async () => {
    await renderPortfolioScreen(<PortfolioScreen trading={trading} strategy={strategy} accounting={accounting} />, {
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

  it("renders run-linked equity table with strategy data", async () => {
    await renderPortfolioScreen(<PortfolioScreen trading={trading} strategy={strategy} accounting={accounting} />, {
      initialEntries: ["/portfolio/attribution"]
    });
    expect(screen.getByRole("treegrid", { name: /run-linked equity/i })).toBeDefined();
    expect(screen.getByRole("row", { name: /inspect mean reversion run evidence/i })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("region", { name: /mean reversion run detail/i })).toBeDefined();
    expect(screen.getByText(/running paper run with \+4.2% p&l/i)).toBeDefined();
  });

  it("shows empty text when trading is null", async () => {
    await renderPortfolioScreen(<PortfolioScreen trading={null} strategy={strategy} accounting={accounting} />);
    expect(screen.getAllByText(/portfolio workspace data unavailable/i)).toHaveLength(2);
    expect(screen.getAllByText(/no holding selected/i).length).toBeGreaterThanOrEqual(1);
  });

  it("shows empty text when strategy is null", async () => {
    await renderPortfolioScreen(<PortfolioScreen trading={trading} strategy={null} accounting={accounting} />, {
      initialEntries: ["/portfolio/attribution"]
    });
    expect(screen.getByText(/strategy workspace data unavailable/i)).toBeDefined();
  });

  it("shows cash-flow posture when accounting data is available", async () => {
    await renderPortfolioScreen(<PortfolioScreen trading={trading} strategy={strategy} accounting={accounting} />);
    expect(screen.getByText(/1 run needs variance review/i)).toBeDefined();
  });

  it("renders Alpaca account and selectable current positions when brokerage sync data is available", async () => {
    const user = userEvent.setup();
    await renderPortfolioScreen(
      <PortfolioScreen
        trading={trading}
        strategy={strategy}
        accounting={accounting}
        brokerageConnection={brokerageConnection}
        brokeragePortfolio={brokeragePortfolio}
      />,
      { initialEntries: ["/portfolio/brokerage-sync"] }
    );

    expect(screen.getAllByText(/live brokerage portfolio/i).length).toBeGreaterThan(0);
    const trustSnapshot = screen.getByRole("region", { name: /alpaca paper brokerage sync snapshot/i });
    expect(within(trustSnapshot).getByText(/household synced/i)).toBeInTheDocument();
    expect(within(trustSnapshot).getAllByText("May 7, 12:00 UTC").length).toBeGreaterThan(0);
    expect(within(trustSnapshot).getAllByText("$375,000").length).toBeGreaterThan(0);
    expect(within(trustSnapshot).getByText("2 accounts")).toBeInTheDocument();
    expect(within(trustSnapshot).getByText("2 positions")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /show alpaca paper roth ira account/i })).toBeDefined();
    expect(screen.getByRole("treegrid", { name: /alpaca paper brokerage accounts/i })).toBeDefined();
    expect(screen.getByRole("region", { name: /all brokerage accounts detail/i })).toBeDefined();
    expect(screen.getByRole("treegrid", { name: /alpaca paper current positions/i })).toBeDefined();
    expect(screen.getAllByText(/alpaca roth ira/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText("AAPL").length).toBeGreaterThan(0);

    const defaultDetail = screen.getByRole("region", { name: /aapl brokerage position detail/i });
    expect(within(defaultDetail).getByText(/brokerage position inspector/i)).toBeInTheDocument();
    expect(within(defaultDetail).getAllByText(/security master missing/i).length).toBeGreaterThan(0);
    const positionIdentity = within(defaultDetail).getByText("Technical position identity").closest("details");
    expect(positionIdentity).not.toBeNull();
    const positionId = within(positionIdentity!).getByText("pos-aapl");
    expect(positionIdentity).not.toHaveAttribute("open");
    expect(positionId).not.toBeVisible();
    await user.click(within(positionIdentity!).getByText("Technical position identity"));
    expect(positionIdentity).toHaveAttribute("open");
    expect(positionId).toBeVisible();

    const msftRow = screen.getByRole("row", { name: /inspect msft brokerage live position/i });
    expect(msftRow).toHaveAttribute("aria-controls", "portfolio-brokerage-position-detail");
    expect(msftRow).toHaveAttribute("aria-expanded", "false");
    expect(msftRow).toHaveClass("bg-warning/5");
    await user.click(msftRow);

    const updatedDetail = screen.getByRole("region", { name: /msft brokerage position detail/i });
    expect(within(updatedDetail).getByText(/alpaca paper \/ alpaca brokerage \/ equity/i)).toBeInTheDocument();
    expect(within(updatedDetail).getByText("$1,750")).toBeInTheDocument();
    expect(msftRow).toHaveAttribute("aria-selected", "true");
    expect(msftRow).toHaveAttribute("aria-expanded", "true");
  });

  it("keeps the raw multi-asset coverage endpoint behind technical disclosure", async () => {
    const user = userEvent.setup();
    await renderPortfolioScreen(
      <PortfolioScreen
        trading={trading}
        strategy={strategy}
        accounting={accounting}
        multiAssetCoverage={multiAssetCoverage}
      />,
      { initialEntries: ["/portfolio/brokerage-sync"] }
    );

    const coverageDetails = screen.getByText("Coverage source details").closest("details");
    expect(coverageDetails).not.toBeNull();
    const endpointLink = within(coverageDetails!).getByRole("link", { hidden: true });
    expect(coverageDetails).not.toHaveAttribute("open");
    expect(endpointLink).not.toBeVisible();
    await user.click(within(coverageDetails!).getByText("Coverage source details"));
    expect(coverageDetails).toHaveAttribute("open");
    expect(endpointLink).toBeVisible();
    expect(endpointLink).toHaveTextContent("GET /api/workstation/portfolio/multi-asset-coverage");
    expect(endpointLink).toHaveAttribute("href", "/api/workstation/portfolio/multi-asset-coverage");
  });

  it("selects live brokerage positions from the row with keyboard activation", async () => {
    const user = userEvent.setup();
    await renderPortfolioScreen(
      <PortfolioScreen
        trading={trading}
        strategy={strategy}
        accounting={accounting}
        brokerageConnection={brokerageConnection}
        brokeragePortfolio={brokeragePortfolio}
      />,
      { initialEntries: ["/portfolio/brokerage-sync"] }
    );

    const msftRow = screen.getByRole("row", { name: /inspect msft brokerage live position/i });
    msftRow.focus();
    expect(msftRow).toHaveFocus();

    await user.keyboard("{Enter}");

    expect(msftRow).toHaveAttribute("aria-selected", "true");
    expect(msftRow).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("region", { name: /msft brokerage position detail/i })).toBeInTheDocument();
  });

  it("offers a provider setup handoff when brokerage portfolio sync is unavailable", async () => {
    await renderPortfolioScreen(<PortfolioScreen trading={trading} strategy={strategy} accounting={accounting} />, {
      initialEntries: ["/portfolio/brokerage-sync"]
    });

    const trustSnapshot = screen.getByRole("region", { name: /alpaca paper brokerage sync snapshot/i });
    expect(within(trustSnapshot).getByText(/provider setup needed/i)).toBeInTheDocument();
    expect(within(trustSnapshot).getByText(/no alpaca paper household snapshot has loaded yet/i)).toBeInTheDocument();

    const handoff = screen.getByRole("link", {
      name: /open alpaca paper provider setup from portfolio brokerage panel/i
    });
    expect(handoff).toHaveAttribute("href", "/settings#alpaca-provider-setup");
    expect(screen.getByText(/verify alpaca paper credentials before accepting brokerage portfolio state/i)).toBeInTheDocument();
  });

  it("renders portfolio and account sync warnings in the live brokerage panel", async () => {
    const warningPortfolio: BrokerageHouseholdPortfolio = {
      ...brokeragePortfolio,
      warnings: ["Portfolio sync is stale."],
      accounts: [
        { ...brokeragePortfolio.accounts[0], warnings: ["Roth IRA account sync stale."] },
        brokeragePortfolio.accounts[1]
      ]
    };

    await renderPortfolioScreen(
      <PortfolioScreen
        trading={trading}
        strategy={strategy}
        accounting={accounting}
        brokerageConnection={brokerageConnection}
        brokeragePortfolio={warningPortfolio}
      />,
      { initialEntries: ["/portfolio/brokerage-sync"] }
    );

    const warningSummary = screen.getByRole("status", { name: "2 brokerage warnings" });
    const trustSnapshot = screen.getByRole("region", { name: /alpaca paper brokerage sync snapshot/i });
    const accountDetail = screen.getByRole("region", { name: /all brokerage accounts detail/i });
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
    await renderPortfolioScreen(
      <PortfolioScreen
        trading={trading}
        strategy={strategy}
        accounting={accounting}
        brokerageConnection={brokerageConnection}
        brokeragePortfolio={brokeragePortfolio}
      />,
      { initialEntries: ["/portfolio/brokerage-sync"] }
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
    expect(screen.getByRole("region", { name: /roth ira brokerage account detail/i })).toBeInTheDocument();
    let brokerageTable = screen.getByRole("treegrid", { name: /alpaca paper current positions/i });
    expect(within(brokerageTable).getByText("AAPL")).toBeDefined();
    expect(within(brokerageTable).queryByText("MSFT")).toBeNull();

    rothButton.focus();
    await user.keyboard("{End}");
    expect(brokerageButton).toHaveFocus();
    expect(brokerageButton).toHaveAttribute("aria-pressed", "true");
    expect(rothButton).toHaveAttribute("tabindex", "-1");
    expect(brokerageButton).toHaveAttribute("tabindex", "0");
    brokerageTable = screen.getByRole("treegrid", { name: /alpaca paper current positions/i });
    expect(within(brokerageTable).getByText("MSFT")).toBeDefined();
    expect(within(brokerageTable).queryByText("AAPL")).toBeNull();
  });

  it("filters brokerage positions from the account table row with keyboard activation", async () => {
    const user = userEvent.setup();
    await renderPortfolioScreen(
      <PortfolioScreen
        trading={trading}
        strategy={strategy}
        accounting={accounting}
        brokerageConnection={brokerageConnection}
        brokeragePortfolio={brokeragePortfolio}
      />,
      { initialEntries: ["/portfolio/brokerage-sync"] }
    );

    const taxableRow = screen.getByRole("row", { name: /filter brokerage positions to brokerage account/i });
    taxableRow.focus();
    expect(taxableRow).toHaveFocus();

    await user.keyboard(" ");

    expect(taxableRow).toHaveAttribute("aria-selected", "true");
    expect(taxableRow).toHaveAttribute("aria-controls", "portfolio-brokerage-account-detail");
    expect(taxableRow).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("button", { name: /show alpaca paper brokerage account/i })).toHaveAttribute("aria-pressed", "true");
    const accountDetail = screen.getByRole("region", { name: /brokerage brokerage account detail/i });
    expect(within(accountDetail).getByText(/alpaca paper \/ alpaca brokerage/i)).toBeInTheDocument();
    const brokerageTable = screen.getByRole("treegrid", { name: /alpaca paper current positions/i });
    expect(within(brokerageTable).getByText("MSFT")).toBeDefined();
    expect(within(brokerageTable).queryByText("AAPL")).toBeNull();
  });

  it("renders a dedicated brokerage-sync workflow panel on the route", async () => {
    await renderPortfolioScreen(<PortfolioScreen trading={trading} strategy={strategy} accounting={accounting} />, {
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

    await renderPortfolioScreen(<PortfolioScreen trading={tradingWithTwoPositions} strategy={strategy} accounting={accounting} />);

    const msftRow = screen.getByRole("row", { name: /inspect msft short holding/i });
    expect(msftRow).toHaveAttribute("aria-controls", "portfolio-position-detail");
    expect(msftRow).toHaveAttribute("aria-expanded", "false");
    await user.click(msftRow);

    expect(msftRow).toHaveAttribute("aria-selected", "true");
    expect(msftRow).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("region", { name: /msft holding detail/i })).toBeDefined();
    expect(screen.getByText(/\$10,250 exposure with \+\$52.50 unrealized p&l/i)).toBeDefined();
    expect(screen.getByRole("link", { name: "Open asset detail for MSFT" })).toHaveAttribute(
      "href",
      "/portfolio/asset-detail?symbol=MSFT&source=portfolio"
    );
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

    await renderPortfolioScreen(<PortfolioScreen trading={trading} strategy={researchWithTwoRuns} accounting={accounting} />, {
      initialEntries: ["/portfolio/attribution"]
    });

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
    const detail = screen.getByRole("region", { name: /volatility carry run detail/i });
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

    await renderPortfolioScreen(<PortfolioScreen trading={trading} strategy={strategy} accounting={accounting} />, {
      initialEntries: ["/portfolio/attribution"]
    });

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

  it("charts the daily-return distribution from a multi-point drill-in equity curve", async () => {
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
      finalEquity: 101900,
      maxDrawdown: 800,
      maxDrawdownPercent: 0.008,
      maxDrawdownRecoveryDays: 2,
      sharpeRatio: 1.41,
      sortinoRatio: 1.8,
      points: [
        { date: "2026-05-01", totalEquity: 100000, cash: 50000, dailyReturn: 0, drawdownFromPeak: 0, drawdownFromPeakPercent: 0 },
        { date: "2026-05-02", totalEquity: 101200, cash: 50000, dailyReturn: 0.012, drawdownFromPeak: 0, drawdownFromPeakPercent: 0 },
        { date: "2026-05-03", totalEquity: 100390, cash: 50000, dailyReturn: -0.008, drawdownFromPeak: 810, drawdownFromPeakPercent: 0.008 },
        { date: "2026-05-04", totalEquity: 102398, cash: 50000, dailyReturn: 0.02, drawdownFromPeak: 0, drawdownFromPeakPercent: 0 },
        { date: "2026-05-05", totalEquity: 101886, cash: 50000, dailyReturn: -0.005, drawdownFromPeak: 512, drawdownFromPeakPercent: 0.005 }
      ]
    });
    vi.spyOn(api, "getRunCashFlows").mockResolvedValue({
      runId: "run-1",
      asOf: "2026-05-07T12:00:00Z",
      currency: "USD",
      totalEntries: 0,
      totalInflows: 0,
      totalOutflows: 0,
      netCashFlow: 0,
      entries: [],
      ladder: {
        asOf: "2026-05-07T12:00:00Z",
        currency: "USD",
        bucketDays: 7,
        totalProjectedInflows: 0,
        totalProjectedOutflows: 0,
        netPosition: 0,
        buckets: []
      }
    });
    vi.spyOn(api, "getRunFills").mockResolvedValue({
      runId: "run-1",
      totalFills: 0,
      totalCommissions: 0,
      fills: []
    });
    const user = userEvent.setup();

    await renderPortfolioScreen(<PortfolioScreen trading={trading} strategy={strategy} accounting={accounting} />, {
      initialEntries: ["/portfolio/attribution"]
    });

    await user.click(screen.getByRole("button", { name: "Load portfolio drill-in evidence for Mean Reversion" }));

    // Equity/drawdown curve renders for a multi-point profile.
    expect(await screen.findByRole("img", { name: "Equity performance curve" })).toBeDefined();

    // The daily-return distribution reuses the same fetched points — no extra request — and
    // surfaces the signed histogram with mean, best, worst, and positive-day readouts. Scope the
    // readout assertions to the distribution card so a matching value elsewhere (e.g. the equity
    // card's max-drawdown readout) cannot satisfy them.
    const distribution = await screen.findByRole("img", { name: "Distribution histogram" });
    const distributionCard = distribution.closest("div")!.parentElement!;
    expect(within(distributionCard).getByText("Daily return distribution")).toBeDefined();
    expect(within(distributionCard).getByText("+0.38%")).toBeDefined();
    expect(within(distributionCard).getByText("+2.00%")).toBeDefined();
    expect(within(distributionCard).getByText("-0.80%")).toBeDefined();
  });
});
