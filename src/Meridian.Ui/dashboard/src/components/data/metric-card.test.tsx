import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { MetricCard } from "./metric-card";
import { KpiProvider } from "@/lib/kpi-context";
import { resolveDevFixture } from "@/lib/dev-fixtures";
import type { PortfolioWorkspaceResponse, SystemOverviewResponse, TradingWorkspaceResponse } from "@/types";

const portfolio = resolveDevFixture<PortfolioWorkspaceResponse>("/api/workstation/portfolio")!;
const trading = resolveDevFixture<TradingWorkspaceResponse>("/api/workstation/trading")!;

describe("MetricCard registry contract", () => {
  it("uses the portfolio owner even when the system overview disagrees", () => {
    const { container } = render(
      <KpiProvider models={{
        portfolio: { ...portfolio, positions: [] },
        trading,
        overview: { openPositions: 99 } as SystemOverviewResponse
      }}>
        <MetricCard id="portfolio.open-positions" />
      </KpiProvider>
    );
    expect(screen.getByRole("group", { name: /Open positions metric.*0/ })).toBeInTheDocument();
    const observation = container.querySelector('[data-kpi-id="portfolio.open-positions"]')!;
    expect(observation.querySelector("[data-kpi-rendered-value]")).toHaveTextContent("0");
    expect(observation.getAttribute("data-kpi-display")).toBe("0");
    expect(observation.getAttribute("data-kpi-scope")).toBeTruthy();
    expect(observation.getAttribute("data-kpi-value")).toBeTruthy();
  });

  it("withholds a missing owner instead of borrowing another model's count", () => {
    const { container } = render(
      <KpiProvider models={{ portfolio: null, trading }}>
        <MetricCard id="portfolio.open-positions" />
      </KpiProvider>
    );
    const observation = container.querySelector('[data-kpi-id="portfolio.open-positions"]')!;
    expect(observation.querySelector("[data-kpi-rendered-value]")).toHaveTextContent("Unavailable");
    expect(screen.queryByText(String(trading.positions.length))).not.toBeInTheDocument();
  });
});
