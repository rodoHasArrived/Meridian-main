import { describe, expect, it } from "vitest";
import { buildRunComparisonOverlay, describeRunSelectionRefusal, RUN_OVERLAY_MAX_POINTS, type RunCurveInput } from "./strategy-screen.comparison.view-model";
import type { EquityCurvePoint } from "@/types";

function points(entries: Array<[string, number]>): EquityCurvePoint[] {
  return entries.map(([date, totalEquity]) => ({ date: `${date}T00:00:00Z`, totalEquity, cash: 0, dailyReturn: 0, drawdownFromPeak: 0, drawdownFromPeakPercent: 0 }));
}
const run = (runId: string, entries: Array<[string, number]> | null, error: string | null = null): RunCurveInput =>
  ({ runId, label: `Run ${runId}`, points: entries ? points(entries) : null, error });

describe("buildRunComparisonOverlay", () => {
  it("draws only the dates every run shares, carrying each run's last value forward, rebased to 100", () => {
    const overlay = buildRunComparisonOverlay([
      run("a", [["2026-01-01", 200], ["2026-01-03", 220], ["2026-01-05", 210]]),
      run("b", [["2026-01-02", 50], ["2026-01-04", 60], ["2026-01-06", 40]])
    ]);
    if ("unavailable" in overlay) throw new Error(overlay.unavailable);
    expect(overlay.window).toEqual({ start: "2026-01-02", end: "2026-01-05" });
    expect(overlay.labels).toEqual(["2026-01-02", "2026-01-03", "2026-01-04", "2026-01-05"]);
    // Run a on 01-02 is its 01-01 value (200), carried forward.
    const rounded = (values: number[]) => values.map((value) => Math.round(value * 1e9) / 1e9);
    expect(rounded(overlay.equity[0]!.points)).toEqual([100, 110, 110, 105]);
    expect(rounded(overlay.equity[1]!.points)).toEqual([100, 100, 120, 120]);
  });

  it("measures drawdown from each run's peak within the window, in percent", () => {
    const overlay = buildRunComparisonOverlay([
      run("a", [["2026-01-01", 100], ["2026-01-02", 120], ["2026-01-03", 90]]),
      run("b", [["2026-01-01", 100], ["2026-01-02", 100], ["2026-01-03", 100]])
    ]);
    if ("unavailable" in overlay) throw new Error(overlay.unavailable);
    expect(overlay.drawdown[0]!.points.map((value) => Number(value.toFixed(2)))).toEqual([0, 0, -25]);
    expect(overlay.drawdown[1]!.points).toEqual([0, 0, 0]);
    expect(overlay.equityTable.columns).toEqual(["Date", "Run a", "Run b"]);
    expect(overlay.equityTable.rows[2]).toEqual(["2026-01-03", 90, 100]);
  });

  it("names runs it cannot draw and still draws the others", () => {
    const overlay = buildRunComparisonOverlay([
      run("a", [["2026-01-01", 100], ["2026-01-02", 101]]),
      run("b", [["2026-01-01", 100], ["2026-01-02", 99]]),
      run("c", null, "Not found"),
      run("d", [["2026-01-01", 100]])
    ]);
    if ("unavailable" in overlay) throw new Error(overlay.unavailable);
    expect(overlay.equity.map((series) => series.runId)).toEqual(["a", "b"]);
    expect(overlay.omitted).toEqual(["Run c: equity curve unavailable", "Run d: fewer than two equity points"]);
  });

  it("explains when nothing can be overlaid", () => {
    expect(buildRunComparisonOverlay([run("a", [["2026-01-01", 1], ["2026-01-02", 2]]), run("b", null, "boom")]))
      .toMatchObject({ unavailable: expect.stringMatching(/at least two runs/) });
    expect(buildRunComparisonOverlay([run("a", [["2026-01-01", 1], ["2026-01-02", 2]]), run("b", [["2026-02-01", 1], ["2026-02-02", 2]])]))
      .toMatchObject({ unavailable: "The selected runs share no dates, so their curves cannot be overlaid." });
  });

  it("samples a long window evenly, keeping both ends", () => {
    const days = Array.from({ length: 400 }, (_, index) => [new Date(Date.UTC(2025, 0, 1 + index)).toISOString().slice(0, 10), 100 + index] as [string, number]);
    const overlay = buildRunComparisonOverlay([run("a", days), run("b", days)]);
    if ("unavailable" in overlay) throw new Error(overlay.unavailable);
    expect(overlay.labels).toHaveLength(RUN_OVERLAY_MAX_POINTS);
    expect(overlay.labels[0]).toBe(days[0]![0]);
    expect(overlay.labels.at(-1)).toBe(days.at(-1)![0]);
  });
});

describe("describeRunSelectionRefusal", () => {
  it("explains a refused fifth run and is silent otherwise", () => {
    expect(describeRunSelectionRefusal(["a", "b", "c", "d"], "e", "Carry Alpha")).toBe("Compare holds 4 runs. Remove one from the selection to add Carry Alpha.");
    expect(describeRunSelectionRefusal(["a", "b", "c", "d"], "a", "Run a")).toBeNull();
    expect(describeRunSelectionRefusal(["a"], "b", "Run b")).toBeNull();
  });
});
