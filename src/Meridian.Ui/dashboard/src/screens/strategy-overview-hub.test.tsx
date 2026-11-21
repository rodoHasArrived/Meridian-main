import { screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderWithRouter } from "@/test/render";
import { RunLeaderboard } from "./strategy-overview-hub";
import type { StrategyRunRecord } from "@/types";

function run(id: string, sharpe: string, extra: Partial<StrategyRunRecord> = {}): StrategyRunRecord {
  return { id, strategyName: `Strategy ${id}`, engine: "Meridian", mode: "backtest", status: "Completed", dataset: "d", window: "w",
    pnl: "+1.0%", sharpe, lastUpdated: "today", notes: "", ...extra };
}

describe("RunLeaderboard", () => {
  it("ranks runs by Sharpe and lists unranked runs after them", () => {
    renderWithRouter(<RunLeaderboard runs={[run("a", "0.4", { maxDrawdown: -250, trendValues: [1, 2, 3] }), run("b", "1.6"), run("c", "")]} />);
    const table = screen.getByRole("table", { name: "Strategy runs ranked by Sharpe" });
    const rows = within(table).getAllByRole("row");
    expect(rows[1]).toHaveTextContent("Strategy b");
    expect(rows[2]).toHaveTextContent("Strategy a");
    expect(within(table).getByRole("columnheader", { name: "Max drawdown" })).toBeInTheDocument();
    expect(rows[3]).toHaveTextContent("Not ranked");
    expect(within(table).getByRole("link", { name: /Strategy c/ })).toHaveAttribute("href", "/strategy/promotions?runId=c");
  });

  it("says once that no run retains drawdown or a trend instead of a blank column", () => {
    renderWithRouter(<RunLeaderboard runs={[run("a", "1.0"), run("b", "0.5")]} />);
    expect(screen.queryByRole("columnheader", { name: "Max drawdown" })).not.toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: "Equity trend" })).not.toBeInTheDocument();
    expect(screen.getByText(/These runs do not retain maximum drawdown or an equity trend\./)).toBeInTheDocument();
  });

  it("renders nothing without runs", () => {
    const { container } = renderWithRouter(<RunLeaderboard runs={[]} />);
    expect(container).toBeEmptyDOMElement();
  });
});
