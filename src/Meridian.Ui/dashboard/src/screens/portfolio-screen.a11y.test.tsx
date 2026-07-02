import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import { PortfolioScreen } from "@/screens/portfolio-screen";
import * as api from "@/lib/api";
import type {
  AccountingWorkspaceResponse,
  FinancialRecordExplorerDto,
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

describe("PortfolioScreen accessibility", () => {
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

  it("has no basic accessibility violations in the portfolio workbench", async () => {
    const { container } = renderWithRouter(
      <PortfolioScreen trading={trading} strategy={strategy} accounting={accounting} />,
      { initialEntries: ["/portfolio"] }
    );
    await waitForAsyncEffects();

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });
});
