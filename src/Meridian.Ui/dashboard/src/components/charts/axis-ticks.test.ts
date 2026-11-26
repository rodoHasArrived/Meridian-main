// @vitest-environment node
import { describe, expect, it } from "vitest";
import { fitValueTicks, formatTimeTickLabel, selectTimeTickIndices, timeLabelSpanDays } from "./axis-ticks";
import { normalizeChartSize } from "./use-chart-size";
import { parseViewBoxSize } from "./stretched-chart-labels";

describe("selectTimeTickIndices", () => {
  it("keeps the first and last point and never puts two labels closer than the minimum gap", () => {
    const count = 13;
    const width = 400;
    const indices = selectTimeTickIndices(count, width, 7, 72);
    const spacing = width / (count - 1);
    expect(indices[0]).toBe(0);
    expect(indices.at(-1)).toBe(count - 1);
    for (let k = 1; k < indices.length; k += 1) {
      expect((indices[k]! - indices[k - 1]!) * spacing).toBeGreaterThanOrEqual(72);
    }
  });

  it("drops the label just before the last one when they would collide", () => {
    // At 300px, points sit ~27px apart; index 10 would be one step from the last label, so it is dropped.
    expect(selectTimeTickIndices(12, 300, 7, 72)).not.toContain(10);
    expect(selectTimeTickIndices(12, 300, 7, 72).at(-1)).toBe(11);
  });

  it("handles empty and single-point axes", () => {
    expect(selectTimeTickIndices(0, 500)).toEqual([]);
    expect(selectTimeTickIndices(1, 500)).toEqual([0]);
  });
});

describe("formatTimeTickLabel", () => {
  it("uses month and two-digit year for long spans and month-day for short ones", () => {
    expect(formatTimeTickLabel("2025-08-31", 365)).toBe("Aug '25");
    expect(formatTimeTickLabel("2026-06-05T00:00:00Z", 30)).toBe("Jun 5");
    expect(formatTimeTickLabel("Q3", 365)).toBe("Q3");
  });

  it("measures the span of ISO labels", () => {
    expect(timeLabelSpanDays(["2025-11-30", "2026-10-31"])).toBe(335);
    expect(timeLabelSpanDays(["Q1", "Q2"])).toBe(0);
  });
});

describe("chart sizing helpers", () => {
  it("rounds a real layout box and ignores hidden ones", () => {
    expect(normalizeChartSize(640.4, 239.6)).toEqual({ width: 640, height: 240 });
    expect(normalizeChartSize(0, 0)).toBeNull();
  });

  it("parses a viewBox", () => {
    expect(parseViewBoxSize("0 0 960 240")).toEqual({ width: 960, height: 240 });
    expect(parseViewBoxSize("bad")).toBeNull();
  });
});

describe("fitValueTicks", () => {
  it("drops divisions until value labels are at least 24px apart", () => {
    const ticks = fitValueTicks(0, 10_000_000, 6, 120);
    const step = ticks[1]! - ticks[0]!;
    expect((step / 10_000_000) * 120).toBeGreaterThanOrEqual(24);
    expect(fitValueTicks(0, 10_000_000, 6, 600).length).toBeGreaterThan(ticks.length);
  });
});
