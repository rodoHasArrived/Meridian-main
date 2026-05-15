import { describe, expect, it } from "vitest";
import {
  buildTodayPanelViewModel,
  parseSignedAmount
} from "@/screens/today-panel.view-model";
import type {
  PortfolioWorkspaceResponse,
  TradingWorkspaceResponse
} from "@/types";

const brokerage = {
  provider: "Interactive Brokers",
  account: "DU1009034",
  environment: "paper",
  connection: "Connected",
  lastHeartbeat: "2s ago",
  orderIngress: "healthy",
  fillFeed: "healthy",
  notes: ""
} as const;

const risk = {
  state: "Healthy",
  summary: "All clear.",
  netExposure: "$10,000",
  grossExposure: "$10,000",
  var95: "$200",
  maxDrawdown: "-0.5%",
  buyingPowerUsed: "30%",
  activeGuardrails: []
} as const;

function makeTrading(overrides: Partial<TradingWorkspaceResponse> = {}): TradingWorkspaceResponse {
  return {
    metrics: [],
    positions: [],
    openOrders: [],
    fills: [],
    risk: { ...risk, activeGuardrails: [...risk.activeGuardrails] },
    brokerage: { ...brokerage },
    readiness: null,
    ...overrides
  };
}

function makePortfolio(overrides: Partial<PortfolioWorkspaceResponse> = {}): PortfolioWorkspaceResponse {
  return {
    metrics: [],
    positions: [],
    risk: { ...risk, activeGuardrails: [...risk.activeGuardrails] },
    brokerage: { ...brokerage },
    runs: [],
    cashFlow: null,
    ...overrides
  };
}

describe("today-panel view model", () => {
  it("returns an empty placeholder when no trading or portfolio data is present", () => {
    const vm = buildTodayPanelViewModel(null, null);

    expect(vm.hasData).toBe(false);
    expect(vm.subheadline).toContain("Connect a brokerage");
    expect(vm.metrics.map((m) => m.value)).toEqual(["—", "—", "—", "—"]);
    expect(vm.metrics.every((m) => m.tone === "default")).toBe(true);
    expect(vm.hasMovers).toBe(false);
    expect(vm.hasOrders).toBe(false);
    expect(vm.hasFills).toBe(false);
    expect(vm.moversTableLabel).toBe("0 top movers");
    expect(vm.ordersTableLabel).toBe("0 open orders");
    expect(vm.fillsTableLabel).toBe("0 recent fills");
    expect(vm.quickActionsLabel).toBe("Today quick actions");
    expect(vm.quickActionsEyebrow).toBe("Quick actions");
    expect(vm.emptyActionHref).toBe("/settings#alpaca-provider-setup");
    expect(vm.emptyActionAriaLabel).toBe("Open Alpaca paper provider setup checklist to connect a brokerage account");
    expect(vm.quickActions.map((a) => a.id)).toEqual([
      "place-order",
      "add-symbol",
      "live-quote",
      "reconcile"
    ]);
    expect(vm.brokerageLabel).toBeNull();
  });

  it("aggregates day P&L across positions and tones the headline metric green when net positive", () => {
    const trading = makeTrading({
      positions: [
        { symbol: "AAPL", side: "Long", quantity: "100", averagePrice: "188", markPrice: "189", dayPnl: "+$90", unrealizedPnl: "+$90", exposure: "$18,900" },
        { symbol: "MSFT", side: "Long", quantity: "50", averagePrice: "410", markPrice: "414", dayPnl: "+$200", unrealizedPnl: "+$200", exposure: "$20,700" },
        { symbol: "TSLA", side: "Short", quantity: "20", averagePrice: "180", markPrice: "175", dayPnl: "-$50", unrealizedPnl: "+$100", exposure: "-$3,500" }
      ],
      metrics: [
        { id: "fills", label: "Fills", value: "13", delta: "+3", tone: "success" }
      ]
    });
    const portfolio = makePortfolio({
      metrics: [
        { id: "portfolio-equity", label: "Portfolio Equity", value: "$120,000", delta: "+1.1%", tone: "success" },
        { id: "portfolio-cash", label: "Cash", value: "$30,000", delta: "Available", tone: "default" }
      ]
    });

    const vm = buildTodayPanelViewModel(trading, portfolio);

    expect(vm.hasData).toBe(true);
    const dayPnl = vm.metrics.find((m) => m.id === "today-day-pnl");
    expect(dayPnl?.value).toBe("+$240");
    expect(dayPnl?.tone).toBe("success");
    expect(dayPnl?.detail).toBe("Across 3 open positions");

    const equity = vm.metrics.find((m) => m.id === "today-equity");
    expect(equity?.value).toBe("$120,000");
    expect(equity?.tone).toBe("success");

    const cash = vm.metrics.find((m) => m.id === "today-cash");
    expect(cash?.value).toBe("$30,000");

    expect(vm.brokerageLabel).toBe("Interactive Brokers · DU1009034 · paper");
    expect(vm.subheadline).toContain("3 open positions");
    expect(vm.subheadline).toContain("13 fills today");
  });

  it("ranks movers by absolute day P&L magnitude regardless of direction", () => {
    const trading = makeTrading({
      positions: [
        { symbol: "FLAT", side: "Long", quantity: "10", averagePrice: "10", markPrice: "10", dayPnl: "+$0", unrealizedPnl: "$0", exposure: "$100" },
        { symbol: "AAPL", side: "Long", quantity: "100", averagePrice: "188", markPrice: "189", dayPnl: "+$90", unrealizedPnl: "+$90", exposure: "$18,900" },
        { symbol: "TSLA", side: "Short", quantity: "20", averagePrice: "180", markPrice: "175", dayPnl: "-$500", unrealizedPnl: "+$100", exposure: "-$3,500" },
        { symbol: "MSFT", side: "Long", quantity: "50", averagePrice: "410", markPrice: "414", dayPnl: "+$200", unrealizedPnl: "+$200", exposure: "$20,700" }
      ]
    });

    const vm = buildTodayPanelViewModel(trading, null);

    expect(vm.movers.map((m) => m.symbol)).toEqual(["TSLA", "MSFT", "AAPL"]);
    expect(vm.movers[0].dayPnlTone).toBe("danger");
    expect(vm.movers[0].rowClassName).toBe("bg-danger/5");
    expect(vm.movers[1].dayPnlTone).toBe("success");
    expect(vm.movers[1].rowClassName).toBe("bg-success/5");
    expect(vm.movers.find((m) => m.symbol === "FLAT")).toBeUndefined();
    expect(vm.moversTotal).toBe(4);
    expect(vm.moversTableLabel).toBe("3 top movers");
    expect(vm.moversTableCaption).toContain("3 top movers shown from 4 open positions");
  });

  it("previews up to five open orders and emits a more-label when there are extras", () => {
    const trading = makeTrading({
      openOrders: Array.from({ length: 7 }).map((_, i) => ({
        orderId: `OR-${i + 1}`,
        symbol: i % 2 === 0 ? "AAPL" : "MSFT",
        side: "Buy" as const,
        type: i === 0 ? ("Market" as const) : ("Limit" as const),
        quantity: "10",
        limitPrice: i === 0 ? "—" : `100.0${i}`,
        status: "Working" as const,
        submittedAt: `09:4${i}:00 ET`
      }))
    });

    const vm = buildTodayPanelViewModel(trading, null);

    expect(vm.orders).toHaveLength(5);
    expect(vm.orders[0].priceLabel).toBe("Market");
    expect(vm.orders[1].priceLabel).toBe("100.01");
    expect(vm.ordersTotal).toBe(7);
    expect(vm.ordersMoreLabel).toBe("+2 more in trading cockpit");
    expect(vm.ordersTableLabel).toBe("5 open orders");
    expect(vm.ordersTableCaption).toContain("5 open orders shown from 7 working orders");
  });

  it("previews up to five fills and emits a more-label when there are extras", () => {
    const trading = makeTrading({
      fills: Array.from({ length: 6 }).map((_, i) => ({
        fillId: `FL-${i + 1}`,
        orderId: `OR-${i + 1}`,
        symbol: "AAPL",
        side: "Buy" as const,
        quantity: "10",
        price: `189.0${i}`,
        venue: "NASDAQ",
        timestamp: `09:5${i}:00 ET`
      }))
    });

    const vm = buildTodayPanelViewModel(trading, null);

    expect(vm.fills).toHaveLength(5);
    expect(vm.fillsTotal).toBe(6);
    expect(vm.fillsMoreLabel).toBe("+1 more in trading cockpit");
    expect(vm.fillsTableLabel).toBe("5 recent fills");
    expect(vm.fillsTableCaption).toContain("5 recent fills shown from 6 fills today");
  });

  it("falls back to portfolio data when only portfolio is available", () => {
    const portfolio = makePortfolio({
      positions: [
        { symbol: "AAPL", side: "Long", quantity: "100", averagePrice: "188", markPrice: "189", dayPnl: "+$90", unrealizedPnl: "+$90", exposure: "$18,900" }
      ],
      metrics: [
        { id: "portfolio-equity", label: "Portfolio Equity", value: "$50,000", delta: "+0.5%", tone: "success" }
      ]
    });

    const vm = buildTodayPanelViewModel(null, portfolio);

    expect(vm.hasData).toBe(true);
    expect(vm.metrics.find((m) => m.id === "today-day-pnl")?.value).toBe("+$90");
    expect(vm.metrics.find((m) => m.id === "today-equity")?.value).toBe("$50,000");
    expect(vm.orders).toEqual([]);
    expect(vm.fills).toEqual([]);
    expect(vm.brokerageLabel).toBe("Interactive Brokers · DU1009034 · paper");
  });
});

describe("parseSignedAmount", () => {
  it("returns 0 for empty, null, undefined, dash, and lone sign inputs", () => {
    expect(parseSignedAmount(null)).toBe(0);
    expect(parseSignedAmount(undefined)).toBe(0);
    expect(parseSignedAmount("")).toBe(0);
    expect(parseSignedAmount("  ")).toBe(0);
    expect(parseSignedAmount("—")).toBe(0);
    expect(parseSignedAmount("-")).toBe(0);
    expect(parseSignedAmount("+")).toBe(0);
  });

  it("strips currency symbols and thousand separators", () => {
    expect(parseSignedAmount("+$1,250")).toBe(1250);
    expect(parseSignedAmount("-$2,500.50")).toBe(-2500.5);
    expect(parseSignedAmount("$300")).toBe(300);
  });
});
