import { describe, expect, it } from "vitest";
import { KPI_REGISTRY, kpiAttributes, parseKpiMoney, resolveKpi, workspaceKpiIds, type KpiReadModels } from "@/lib/kpi-registry";
import type { FinancialRecordExplorerDto, PortfolioWorkspaceResponse, TradingPosition } from "@/types";

function portfolio(positions: Partial<TradingPosition>[], metrics: PortfolioWorkspaceResponse["metrics"] = []): PortfolioWorkspaceResponse {
  return {
    positions: positions.map((position, index) => ({
      symbol: `SYMBOL-${index}`, side: "Long", quantity: "1", averagePrice: "1", markPrice: "1", dayPnl: "$0", unrealizedPnl: "$0", exposure: "$1", ...position
    })), metrics,
    brokerage: { account: "PAPER-1", provider: "Example", environment: "paper", connection: "Connected", lastHeartbeat: "now", orderIngress: "healthy", fillFeed: "healthy", notes: "" },
    risk: null, cashFlow: null, runs: []
  } as PortfolioWorkspaceResponse;
}

function retained(overrides: Partial<FinancialRecordExplorerDto> = {}): FinancialRecordExplorerDto {
  return {
    explorerId: "portfolio", isBlocked: false, blockedReason: "", scopeItems: [{ label: "Run ID", value: "run-42", tone: "Default" }],
    summaryItems: [{ label: "Market value", value: "$25,587", detail: "Retained run", tone: "Default" }, { label: "Positions", value: "3", detail: "Retained records", tone: "Default" }], ...overrides
  } as FinancialRecordExplorerDto;
}

describe("KPI registry ownership and financial meaning", () => {
  it("resolves the same live count independently of the system overview's conflicting count", () => {
    const models = { portfolio: portfolio([{}, {}]), overview: { openPositions: 5 } } as KpiReadModels;
    expect(resolveKpi("portfolio.open-positions", models)).toMatchObject({ value: "2", status: "available" });
    expect(KPI_REGISTRY["portfolio.open-positions"].owner).toBe("portfolio");
  });

  it("does not fall back to Trading or overview when Portfolio fails", () => {
    const models = { portfolio: null, trading: { positions: [{}] }, overview: { openPositions: 5 } } as KpiReadModels;
    expect(resolveKpi("portfolio.open-positions", models)).toMatchObject({ value: "Unavailable", status: "unavailable" });
    expect(resolveKpi("portfolio.total-exposure", models).status).toBe("unavailable");
  });

  it("distinguishes an empty live model from an unavailable one without inventing a currency", () => {
    expect(resolveKpi("portfolio.open-positions", { portfolio: portfolio([]) }).value).toBe("0");
    expect(resolveKpi("portfolio.total-exposure", { portfolio: portfolio([]) })).toMatchObject({ value: "—", status: "empty", currency: null });
    expect(resolveKpi("portfolio.total-exposure", {})).toMatchObject({ value: "Unavailable", status: "unavailable" });
  });

  it("computes gross exposure with shorts without parsing binary floating point", () => {
    const models = { portfolio: portfolio([{ exposure: "$0.10" }, { side: "Short", exposure: "-$0.20" }]) };
    expect(resolveKpi("portfolio.total-exposure", models)).toMatchObject({ value: "$0.30", currency: "USD" });
    expect(JSON.parse(resolveKpi("portfolio.total-exposure", models).canonicalValue).value).toBe("0.3");
  });

  it("retains exact large and fractional monetary inputs", () => {
    const models = { portfolio: portfolio([{ exposure: "USD 9007199254740993.0001" }, { exposure: "$0.0002" }]) };
    expect(resolveKpi("portfolio.total-exposure", models).value).toBe("$9,007,199,254,740,993.0003");
  });

  it("sums unrealized P&L with its sign and normalizes negative zero", () => {
    const models = { portfolio: portfolio([{ unrealizedPnl: "-$12.30" }, { unrealizedPnl: "+$2.10" }]) };
    expect(resolveKpi("portfolio.unrealized-pnl", models)).toMatchObject({ value: "-$10.20", tone: "danger" });
    expect(resolveKpi("portfolio.unrealized-pnl", { portfolio: portfolio([{ unrealizedPnl: "-$0.00" }]) }).value).toBe("+$0");
  });

  it.each(["7159", "Unavailable", "$1,23", "$1.2.3", "NaN", "USD $20", "($-20)", "-USD -20"])("does not invent denomination or accept invalid amount %s", (value) => {
    expect(parseKpiMoney(value)).toBeNull();
  });

  it("rejects partial sums when one holding lacks a value or currency", () => {
    const result = resolveKpi("portfolio.total-exposure", { portfolio: portfolio([{ exposure: "$7,159" }, { exposure: "500" }]) });
    expect(result).toMatchObject({ value: "Unavailable", status: "unavailable", currency: null });
    expect(result.delta).toContain("every holding");
  });

  it("refuses mixed currencies and does not perform implicit FX conversion", () => {
    const result = resolveKpi("portfolio.total-exposure", { portfolio: portfolio([{ exposure: "$100" }, { exposure: "EUR 100" }]) });
    expect(result.value).toBe("Unavailable");
    expect(result.delta).toContain("Mixed currencies");
    expect(resolveKpi("portfolio.total-exposure", { portfolio: portfolio([{ exposure: "EUR 100" }, { exposure: "€1.50" }]) }).value).toBe("€101.50");
  });

  it("never uses another monetary headline to fill absent portfolio cash", () => {
    const model = portfolio([], [{ id: "portfolio-value", label: "Portfolio value", value: "$300", delta: "", tone: "default" }]);
    expect(resolveKpi("portfolio.cash", { portfolio: model }).status).toBe("unavailable");
  });

  it("keeps retained market value separate from live exposure with the actual retained run", () => {
    const models = { portfolio: portfolio([{ exposure: "$37,699" }]), portfolioExplorer: retained() };
    expect(resolveKpi("portfolio.total-exposure", models).value).toBe("$37,699");
    const result = resolveKpi("portfolio.retained-market-value", models);
    expect(result).toMatchObject({ value: "$25,587", label: "Market value" });
    expect(JSON.parse(result.scope)).toMatchObject({ source: "retained-records", run: "run-42", account: null });
    expect(resolveKpi("portfolio.retained-records", models).value).toBe("3");
  });

  it("withholds blocked, absent or ambiguously supplied retained summaries", () => {
    expect(resolveKpi("portfolio.retained-market-value", { portfolioExplorer: retained({ isBlocked: true, blockedReason: "Not available" }) }).status).toBe("unavailable");
    expect(resolveKpi("portfolio.retained-market-value", { portfolioExplorer: retained({ summaryItems: [] }) }).status).toBe("unavailable");
    const model = retained();
    model.summaryItems.push(model.summaryItems[0]);
    expect(resolveKpi("portfolio.retained-market-value", { portfolioExplorer: model }).status).toBe("unavailable");
    expect(resolveKpi("portfolio.retained-market-value", { portfolioExplorer: retained({ scopeItems: [{ label: "Run", value: "run-1", tone: "Default" }, { label: "Run ID", value: "run-2", tone: "Default" }] }) }).status).toBe("unavailable");
  });

  it("cannot relabel aggregate holdings as filtered by an unapplied account or run", () => {
    const models = { portfolio: portfolio([{}]) };
    expect(JSON.parse(resolveKpi("portfolio.open-positions", models).scope)).toEqual({ account: "all", source: "live-holdings" });
    expect(resolveKpi("portfolio.open-positions", models, { account: "PAPER-2" }).status).toBe("unavailable");
    expect(resolveKpi("portfolio.open-positions", models, { run: "run-42" }).status).toBe("unavailable");
  });

  it("selects only registered IDs and keeps total runs separate from active runs", () => {
    expect(workspaceKpiIds("strategy", [{ id: "runs" }, { id: "active-runs" }, { id: "queued" }, { id: "unknown" }])).toEqual(["strategy.runs", "strategy.active-runs", "strategy.queued"]);
    expect(workspaceKpiIds("data", [{ id: "providers" }, { id: "providers-healthy" }, { id: "kernel-critical-jumps" }])).toEqual(["data.providers-healthy", "data.kernel-critical-jumps"]);
  });

  it("retains compact replacement cost as a rounded USD display, not an exact decimal", () => {
    const models = { data: { metrics: [{ id: "data-replacement-cost", label: "Est. Replacement Cost", value: "$1.2M", delta: "42 GB local", tone: "success" }] } } as KpiReadModels;
    expect(resolveKpi("data.replacement-cost", models)).toMatchObject({ value: "$1.2M", currency: "USD", status: "available" });
    expect(JSON.parse(resolveKpi("data.replacement-cost", models).canonicalValue).value).toBe("$1.2M");
    expect(KPI_REGISTRY["data.replacement-cost"].currencyRule).toContain("rounded");
  });

  it("fails closed for malformed owner position lists", () => {
    const models = { portfolio: {} } as KpiReadModels;
    expect(resolveKpi("portfolio.open-positions", models).status).toBe("unavailable");
    expect(resolveKpi("portfolio.total-exposure", models).status).toBe("unavailable");
  });

  it("publishes canonical observations from the exact rendered resolution", () => {
    const result = resolveKpi("portfolio.total-exposure", { portfolio: portfolio([{ exposure: "$37,699" }]) });
    expect(kpiAttributes(result)).toEqual({ "data-kpi-id": result.id, "data-kpi-scope": result.scope, "data-kpi-value": result.canonicalValue, "data-kpi-display": "$37,699" });
    expect(Object.values(KPI_REGISTRY).every((definition) => definition.owner && definition.computation && definition.scopeDimensions.length && definition.currencyRule)).toBe(true);
  });
});
