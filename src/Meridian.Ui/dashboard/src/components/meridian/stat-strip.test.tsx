import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { StatStrip } from "./stat-strip";
import { KpiProvider } from "@/lib/kpi-context";
import type { PortfolioWorkspaceResponse, SystemOverviewResponse } from "@/types";

const portfolio = {
  positions: [
    { symbol: "AAPL", exposure: "$7,159", unrealizedPnl: "+$159" },
    { symbol: "MSFT", exposure: "$18,428", unrealizedPnl: "-$28" }
  ],
  metrics: []
} as PortfolioWorkspaceResponse;

describe("registry headline strips", () => {
  it("uses the owning holdings projection on every route, even when overview disagrees", () => {
    const { rerender, container } = render(
      <KpiProvider models={{ portfolio, overview: { openPositions: 5 } as SystemOverviewResponse }}>
        <StatStrip ids={["portfolio.total-exposure", "portfolio.open-positions"]} label="Portfolio" />
        <StatStrip ids={["portfolio.open-positions"]} label="Settings" />
      </KpiProvider>
    );
    expect(screen.getByLabelText("Portfolio")).toHaveTextContent("$25,587");
    for (const node of container.querySelectorAll('[data-kpi-id="portfolio.open-positions"]')) {
      expect(node.querySelector("[data-kpi-rendered-value]")).toHaveTextContent("2");
    }
    const original = container.querySelector('[data-kpi-id="portfolio.open-positions"]')!;
    const scope = original.getAttribute("data-kpi-scope");
    rerender(
      <KpiProvider models={{ portfolio }}>
        <KpiProvider models={{ overview: { openPositions: 99 } as SystemOverviewResponse }}>
          <StatStrip ids={["portfolio.open-positions"]} label="Today" />
        </KpiProvider>
      </KpiProvider>
    );
    const current = container.querySelector('[data-kpi-id="portfolio.open-positions"]')!;
    expect(current.getAttribute("data-kpi-scope")).toBe(scope);
    expect(current.querySelector("[data-kpi-rendered-value]")).toHaveTextContent("2");
  });

  it("does not replace missing holdings with a system-summary count", () => {
    render(
      <KpiProvider models={{ portfolio: null, overview: { openPositions: 5 } as SystemOverviewResponse }}>
        <StatStrip ids={["portfolio.open-positions"]} label="Settings" />
      </KpiProvider>
    );
    expect(screen.getByLabelText("Settings")).toHaveTextContent("Unavailable");
  });
});
