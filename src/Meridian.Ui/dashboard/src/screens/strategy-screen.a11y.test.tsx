import { describe, expect, it } from "vitest";
import { axe } from "jest-axe";
import { StrategyScreen } from "@/screens/strategy-screen";
import { renderWithRouter } from "@/test/render";
import type { StrategyWorkspaceResponse } from "@/types";

const twoRuns: StrategyWorkspaceResponse = {
  metrics: [
    { id: "1", label: "Runs", value: "24", delta: "+8%", tone: "success" },
    { id: "2", label: "Queued", value: "3", delta: "0%", tone: "default" },
    { id: "3", label: "Needs Review", value: "2", delta: "-1%", tone: "warning" },
    { id: "4", label: "Promotions", value: "5", delta: "+2%", tone: "default" }
  ],
  runs: [
    {
      id: "run-1",
      strategyName: "Mean Reversion FX",
      engine: "Meridian Native",
      mode: "paper",
      status: "Running",
      dataset: "FX Majors",
      window: "90d",
      pnl: "+4.2%",
      sharpe: "1.41",
      lastUpdated: "2m ago",
      notes: "Primary paper candidate."
    },
    {
      id: "run-2",
      strategyName: "Index Momentum",
      engine: "Lean",
      mode: "backtest",
      status: "Completed",
      dataset: "US Equities",
      window: "180d",
      pnl: "+1.9%",
      sharpe: "0.91",
      lastUpdated: "5m ago",
      notes: "Completed backtest run."
    }
  ]
};

describe("StrategyScreen accessibility", () => {
  it("has no basic accessibility violations in the loading state", async () => {
    const { container } = renderWithRouter(<StrategyScreen data={null} />);

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });

  it("has no basic accessibility violations with run history data", async () => {
    const { container } = renderWithRouter(<StrategyScreen data={twoRuns} />);

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });

  it.each([
    ["overview", "/strategy"],
    ["promotions", "/strategy/promotions"],
    ["lab", "/strategy/lab"]
  ])("has no basic accessibility violations on the %s route", async (_view, pathname) => {
    const { container } = renderWithRouter(<StrategyScreen data={twoRuns} />, {
      initialEntries: [pathname]
    });

    const results = await axe(container);
    expect(results.violations.map((violation) => ({
      id: violation.id,
      targets: violation.nodes.map((node) => node.target)
    }))).toEqual([]);
  });
});
