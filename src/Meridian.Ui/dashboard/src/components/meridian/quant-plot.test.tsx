import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { QuantPlotChart, histogramBins, prepareSeries } from "@/components/meridian/quant-plot";
import type { QuantPlot } from "@/types";

function point(date: string, value: number) {
  return { date, value };
}

describe("histogramBins", () => {
  it("returns empty when no values", () => {
    expect(histogramBins([], 10)).toEqual([]);
  });

  it("widens bin range when all values are equal", () => {
    const bins = histogramBins([5, 5, 5], 4);
    expect(bins).toHaveLength(4);
    expect(bins[0].lo).toBeLessThan(5);
    expect(bins[bins.length - 1].hi).toBeGreaterThan(5);
    const total = bins.reduce((sum, b) => sum + b.count, 0);
    expect(total).toBe(3);
  });

  it("places values into bins", () => {
    const bins = histogramBins([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10], 5);
    expect(bins).toHaveLength(5);
    const total = bins.reduce((sum, b) => sum + b.count, 0);
    expect(total).toBe(11);
  });
});

describe("prepareSeries", () => {
  const linePlot: QuantPlot = {
    title: "Series",
    type: "Line",
    series: [point("2026-01-01", 100), point("2026-01-02", 110), point("2026-01-03", 90)],
    multiSeries: null,
    candlestick: null,
    heatmapData: null,
    heatmapLabels: null
  };

  it("treats Line as a single-series line chart", () => {
    const prepared = prepareSeries(linePlot);
    expect(prepared.kind).toBe("line");
    if (prepared.kind === "line") {
      expect(prepared.series).toHaveLength(1);
      expect(prepared.series[0].values.map((v) => v.value)).toEqual([100, 110, 90]);
      expect(prepared.bounds.min).toBeLessThanOrEqual(90);
      expect(prepared.bounds.max).toBeGreaterThanOrEqual(110);
      expect(prepared.fillBaseline).toBe(false);
    }
  });

  it("converts CumulativeReturn to relative returns", () => {
    const prepared = prepareSeries({ ...linePlot, type: "CumulativeReturn" });
    if (prepared.kind !== "line") throw new Error("expected line");
    const values = prepared.series[0].values.map((v) => v.value);
    expect(values[0]).toBe(0);
    expect(values[1]).toBeCloseTo(0.1, 5);
    expect(values[2]).toBeCloseTo(-0.1, 5);
    expect(prepared.fillBaseline).toBe(true);
  });

  it("converts Drawdown to running peak ratio", () => {
    const prepared = prepareSeries({
      ...linePlot,
      type: "Drawdown",
      series: [point("d1", 100), point("d2", 120), point("d3", 90)]
    });
    if (prepared.kind !== "line") throw new Error("expected line");
    const values = prepared.series[0].values.map((v) => v.value);
    expect(values[0]).toBe(0);
    expect(values[1]).toBe(0);
    expect(values[2]).toBeCloseTo(-0.25, 5);
    expect(prepared.fillBaseline).toBe(true);
  });

  it("handles Bar/Scatter/Histogram via series field", () => {
    const points = [point("a", 1), point("b", -2), point("c", 3)];
    expect(prepareSeries({ ...linePlot, type: "Bar", series: points }).kind).toBe("bar");
    expect(prepareSeries({ ...linePlot, type: "Scatter", series: points }).kind).toBe("scatter");
    expect(prepareSeries({ ...linePlot, type: "Histogram", series: points }).kind).toBe("histogram");
  });

  it("returns unsupported when there is no data", () => {
    expect(prepareSeries({ ...linePlot, series: [] }).kind).toBe("unsupported");
    expect(prepareSeries({ ...linePlot, type: "Heatmap" }).kind).toBe("unsupported");
    expect(prepareSeries({ ...linePlot, type: "Candlestick" }).kind).toBe("unsupported");
  });
});

describe("QuantPlotChart", () => {
  const basePlot: QuantPlot = {
    title: "Equity curve",
    type: "MultiLine",
    series: null,
    multiSeries: [
      { label: "A", values: [point("2026-01-01", 100), point("2026-01-02", 110)] },
      { label: "B", values: [point("2026-01-01", 100), point("2026-01-02", 95)] }
    ],
    candlestick: null,
    heatmapData: null,
    heatmapLabels: null
  };

  it("renders an SVG with the plot title as accessible label", () => {
    render(<QuantPlotChart plot={basePlot} />);
    expect(screen.getByRole("img", { name: "Equity curve" })).toBeInTheDocument();
    expect(screen.getByText("A")).toBeInTheDocument();
    expect(screen.getByText("B")).toBeInTheDocument();
  });

  it("shows a fallback message for unsupported plot types", () => {
    render(
      <QuantPlotChart
        plot={{ ...basePlot, type: "Heatmap", multiSeries: null }}
      />
    );
    expect(screen.getByText(/not yet rendered in browser/i)).toBeInTheDocument();
  });
});
