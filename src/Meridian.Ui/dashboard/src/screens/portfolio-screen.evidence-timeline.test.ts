import { describe, expect, it } from "vitest";
import { buildPortfolioEvidenceTimelineItems } from "@/screens/portfolio-screen.evidence-timeline";
import type { PortfolioWorkspaceResponse } from "@/types";

const portfolio: PortfolioWorkspaceResponse = {
  metrics: [],
  positions: [],
  risk: {
    state: "Healthy",
    summary: "",
    netExposure: "$0",
    grossExposure: "$0",
    var95: "$0",
    maxDrawdown: "0%",
    buyingPowerUsed: "0%",
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
  },
  runs: [
    {
      runId: "run-1",
      strategyName: "Mean Reversion FX",
      engine: "Meridian Native",
      mode: "paper",
      status: "Running",
      pnl: "+4.2%",
      sharpe: "1.41",
      dataset: "FX Majors",
      window: "90d",
      lastUpdated: "2026-05-07T12:00:00Z",
      notes: "",
      promotionState: "ReviewRequired"
    }
  ],
  cashFlow: null
};

describe("buildPortfolioEvidenceTimelineItems", () => {
  it("routes run evidence to the attribution view where the run detail is mounted", () => {
    const items = buildPortfolioEvidenceTimelineItems(portfolio);

    expect(items).toHaveLength(1);
    expect(items[0]).toMatchObject({
      id: "portfolio-run:run-1",
      route: "/portfolio/attribution",
      workspaceLabel: "Portfolio"
    });
  });

  it("returns no items without a portfolio payload", () => {
    expect(buildPortfolioEvidenceTimelineItems(null)).toEqual([]);
  });
});
