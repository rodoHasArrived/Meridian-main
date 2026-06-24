import { describe, expect, it } from "vitest";
import { buildChartViz, cellStyleClassName, resolveCellStyle } from "@/lib/report-writer-grid-format";
import type { ReportWriterChartRender } from "@/types";

describe("report-writer grid formatting", () => {
  it("resolves cell style hints, treating None and missing as no style", () => {
    const row = { styleHints: { pnl: "Danger" as const, weight: "None" as const } };
    expect(resolveCellStyle(row, "pnl")).toBe("Danger");
    expect(resolveCellStyle(row, "weight")).toBeNull();
    expect(resolveCellStyle(row, "missing")).toBeNull();
    expect(resolveCellStyle({}, "pnl")).toBeNull();
  });

  it("maps cell styles to theme classes", () => {
    expect(cellStyleClassName("Danger")).toContain("text-danger");
    expect(cellStyleClassName("Success")).toContain("text-success");
    expect(cellStyleClassName("Warning")).toContain("text-warning");
    expect(cellStyleClassName("Info")).toContain("text-primary");
    expect(cellStyleClassName("None")).toBe("");
    expect(cellStyleClassName(null)).toBe("");
  });

  it("normalizes chart geometry relative to the largest absolute value", () => {
    const chart: ReportWriterChartRender = {
      type: "Bar",
      title: "Sector",
      categoryField: "sector",
      categories: ["Technology", "Rates"],
      series: [
        { key: "marketValue", label: "Market value", values: [100, 40] },
        { key: "pnl", label: "PnL", values: [10, -2] }
      ],
      warnings: []
    };

    const viz = buildChartViz(chart);

    expect(viz.maxAbs).toBe(100);
    expect(viz.hasNegative).toBe(true);
    expect(viz.categories).toHaveLength(2);

    const technology = viz.categories[0];
    expect(technology.category).toBe("Technology");
    expect(technology.bars[0]).toMatchObject({ seriesKey: "marketValue", value: 100, widthPct: 100, isNegative: false });
    expect(technology.bars[1]).toMatchObject({ seriesKey: "pnl", value: 10, widthPct: 10 });

    const rates = viz.categories[1];
    expect(rates.bars[1]).toMatchObject({ value: -2, widthPct: 2, isNegative: true });
  });

  it("handles an all-zero chart without dividing by zero", () => {
    const chart: ReportWriterChartRender = {
      type: "Line",
      title: "Flat",
      categoryField: "month",
      categories: ["Jan", ""],
      series: [{ key: "v", label: "V", values: [0, 0] }],
      warnings: []
    };

    const viz = buildChartViz(chart);
    expect(viz.maxAbs).toBe(0);
    expect(viz.hasNegative).toBe(false);
    expect(viz.categories[0].bars[0].widthPct).toBe(0);
    expect(viz.categories[1].category).toBe("(blank)");
  });
});
