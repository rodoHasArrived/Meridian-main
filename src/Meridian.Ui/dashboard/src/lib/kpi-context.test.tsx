import { renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { KpiProvider, useKpis } from "@/lib/kpi-context";
import { CrossScreenCountsProvider } from "@/lib/cross-screen-counts-context";
import { WorkspaceFigureContextProvider, type WorkspaceFigureContextValue } from "@/lib/workspace-figure-context";
import type { KpiReadModels } from "@/lib/kpi-registry";

const evidence: WorkspaceFigureContextValue = {
  freshness: "fresh", dataLabel: "simulated data", staleSources: ["crossScreenCounts"],
  sourceLoadedAt: { portfolio: "2026-11-14T10:00:00Z", overview: "2026-11-14T11:00:00Z", crossScreenCounts: "2026-11-14T12:00:00Z", trading: "2026-11-14T09:00:00Z" },
  sourceScope: { portfolio: "unapplied-selection", crossScreenCounts: "account-A", trading: "account-B" }
};

describe("registered KPI owner evidence", () => {
  it("keeps mixed-source evidence and actual loaded account separate from aggregate holdings", () => {
    const models = {
      portfolio: { positions: [{}] }, overview: { activeRuns: 2 },
      crossScreenCounts: { asOf: "2026-11-14T12:00:00Z", providers: { healthy: 3, total: 4, definition: "Healthy" } }
    } as KpiReadModels;
    const { result } = renderHook(() => useKpis(["portfolio.open-positions", "system.active-runs", "system.providers-online"]), {
      wrapper: ({ children }) => <WorkspaceFigureContextProvider value={evidence}><KpiProvider models={models}>{children}</KpiProvider></WorkspaceFigureContextProvider>
    });
    const [positions, runs, providers] = result.current;
    expect(positions.provenance).toEqual({ source: "Portfolio workspace (simulated data)", scope: "All loaded accounts", asOf: "2026-11-14T10:00:00Z" });
    expect(JSON.parse(positions.scope).account).toBe("all");
    expect(runs.provenance?.asOf).toBe("2026-11-14T11:00:00Z");
    expect(providers.provenance).toEqual({ source: "Cross-screen counts (simulated data)", scope: "account-A", asOf: "2026-11-14T12:00:00Z" });
    expect(providers.freshness).toBe("stale");
    expect(JSON.parse(providers.scope).account).toBe("account-A");
    expect(positions.freshness).toBe("fresh");
  });

  it("preserves owner-supplied proof metadata and the strongest freshness verdict", () => {
    const models = {
      trading: { metrics: [{ id: "trading-net-pnl", label: "Net P&L", value: "$20", delta: "+2%", tone: "success", baseline: "vs prior session", freshness: "stale", provenance: { source: "Execution layer", asOf: "2026-11-13T12:00:00Z", scope: "account-B" } }] }
    } as KpiReadModels;
    const { result } = renderHook(() => useKpis(["trading.net-pnl"]), {
      wrapper: ({ children }) => <WorkspaceFigureContextProvider value={evidence}><KpiProvider models={models}>{children}</KpiProvider></WorkspaceFigureContextProvider>
    });
    expect(result.current[0]).toMatchObject({ value: "$20", baseline: "vs prior session", freshness: "stale", provenance: { source: "Execution layer (simulated data)", asOf: "2026-11-13T12:00:00Z", scope: "account-B" } });
    expect(JSON.parse(result.current[0].scope).account).toBe("account-B");
  });

  it("uses the shared context in isolated providers and does not erase parent models with undefined", () => {
    const counts = { asOf: "2026-11-14T12:00:00Z", providers: { healthy: 1, total: 2, definition: "Healthy" }, openOrders: null, openBreaks: null };
    const { result } = renderHook(() => useKpis(["portfolio.open-positions", "system.providers-online"]), {
      wrapper: ({ children }) => <CrossScreenCountsProvider counts={counts}><KpiProvider models={{ portfolio: { positions: [{}] } as KpiReadModels["portfolio"] }}><KpiProvider models={{ portfolio: undefined }}>{children}</KpiProvider></KpiProvider></CrossScreenCountsProvider>
    });
    expect(result.current.map((metric) => metric.value)).toEqual(["1", "1 / 2"]);
  });

  it("validates a requested account against the actual loaded scope and preserves unavailable request identity", () => {
    const models = { trading: { metrics: [{ id: "trading-net-pnl", label: "Net P&L", value: "$20", delta: "", tone: "success" }], brokerage: { account: "BROKER-LABEL" } } } as KpiReadModels;
    const { result } = renderHook(() => [useKpis(["trading.net-pnl"], { account: "account-B" })[0], useKpis(["trading.net-pnl"], { account: "account-A" })[0]], {
      wrapper: ({ children }) => <WorkspaceFigureContextProvider value={evidence}><KpiProvider models={models}>{children}</KpiProvider></WorkspaceFigureContextProvider>
    });
    expect(result.current[0].status).toBe("available");
    expect(JSON.parse(result.current[0].scope).account).toBe("account-B");
    expect(result.current[1].status).toBe("unavailable");
    expect(JSON.parse(result.current[1].scope).account).toBe("account-A");
  });

  it("keeps retained timestamps separate while respecting an unverified session", () => {
    const models = { portfolioExplorer: { scopeItems: [{ label: "Run", value: "run-42" }, { label: "As of", value: "2026-01-01T00:00:00Z" }], summaryItems: [{ label: "Market value", value: "$20" }] } } as KpiReadModels;
    const { result } = renderHook(() => useKpis(["portfolio.retained-market-value"]), {
      wrapper: ({ children }) => <WorkspaceFigureContextProvider value={{ ...evidence, freshness: "unverified" }}><KpiProvider models={models}>{children}</KpiProvider></WorkspaceFigureContextProvider>
    });
    expect(result.current[0].freshness).toBe("unverified");
    expect(result.current[0].provenance?.asOf).toBe("2026-01-01T00:00:00Z");
  });

  it("carries mark review into every route's portfolio exposure and P&L", () => {
    const models = { portfolio: { positions: [{ exposure: "$20", unrealizedPnl: "$2", markFreshness: { status: "ReviewRequired" } }] } } as KpiReadModels;
    const { result } = renderHook(() => useKpis(["portfolio.total-exposure", "portfolio.unrealized-pnl", "portfolio.open-positions"]), {
      wrapper: ({ children }) => <WorkspaceFigureContextProvider value={evidence}><KpiProvider models={models}>{children}</KpiProvider></WorkspaceFigureContextProvider>
    });
    expect(result.current.map((metric) => metric.freshness)).toEqual(["unverified", "unverified", "fresh"]);
  });
});
