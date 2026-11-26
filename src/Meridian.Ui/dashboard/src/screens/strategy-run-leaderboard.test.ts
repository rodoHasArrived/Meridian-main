// @vitest-environment node
import { describe, expect, it } from "vitest";
import { buildRunLeaderboard } from "./strategy-run-leaderboard";
import type { StrategyRunRecord } from "@/types";

function run(id: string, sharpe: string, extra: Partial<StrategyRunRecord> = {}): StrategyRunRecord {
  return { id, strategyName: `Strategy ${id}`, engine: "Meridian", mode: "backtest", status: "Completed", dataset: "d", window: "w",
    pnl: "$1,000", sharpe, lastUpdated: "today", notes: "", ...extra };
}

describe("buildRunLeaderboard", () => {
  it("ranks the top runs by Sharpe and keeps the rest out of the top list", () => {
    const model = buildRunLeaderboard([run("a", "0.5"), run("b", "2.1"), run("c", "1.2"), run("d", "-0.3"), run("e", "1.9"), run("f", "0.9")]);
    expect(model.ranked.map((row) => row.id)).toEqual(["b", "e", "c", "f", "a"]);
    expect(model.unranked).toEqual([]);
  });

  it("lists runs with no Sharpe as unranked", () => {
    const model = buildRunLeaderboard([run("a", "1.0"), run("b", ""), run("c", "n/a")]);
    expect(model.ranked.map((row) => row.id)).toEqual(["a"]);
    expect(model.unranked.map((row) => row.id)).toEqual(["b", "c"]);
    expect(model.unranked[0]!.sharpeText).toBe("Unavailable");
  });

  it("carries drawdown and a trend only when it has two finite points", () => {
    const [first, second] = buildRunLeaderboard([
      run("a", "1.0", { maxDrawdown: -1200, trendValues: [1, 2, 3] }),
      run("b", "0.5", { trendValues: [1] })
    ]).ranked;
    expect(first).toMatchObject({ maxDrawdown: -1200, trendValues: [1, 2, 3], href: "/strategy/promotions?runId=a" });
    expect(second).toMatchObject({ maxDrawdown: null, trendValues: null });
  });
});
