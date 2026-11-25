import { fireEvent, render, screen } from "@testing-library/react";
import { CandleChart, type Candle } from "./CandleChart";
import { EquityCurve } from "./EquityCurve";
import { DrawdownChart } from "./DrawdownChart";
import { Histogram } from "./Histogram";
import { DepthChart } from "./DepthChart";
import { CorrelationHeatmap } from "./CorrelationHeatmap";
import { Sparkline } from "./Sparkline";
import { ChartCard } from "./ChartCard";
import { niceTicks } from "./ticks";

const bars: Candle[] = [
  { t: "10:00", o: 100, h: 105, l: 99, c: 104, v: 1200 },
  { t: "10:01", o: 104, h: 106, l: 102, c: 103, v: 900 },
  { t: "10:02", o: 103, h: 108, l: 103, c: 107, v: 1500 }
];

describe("CandleChart", () => {
  it("renders an accessible svg with a candle per bar", () => {
    const { container } = render(<CandleChart bars={bars} showVolume />);
    expect(screen.getByRole("img", { name: "Candlestick price chart" })).toBeInTheDocument();
    // wick line + body rect per bar exist
    expect(container.querySelectorAll("rect").length).toBeGreaterThanOrEqual(bars.length);
  });

  it("draws a crosshair price chip at the given index", () => {
    const { container } = render(<CandleChart bars={bars} crosshairIndex={2} />);
    // The close price (107.00) shows as the white-filled crosshair chip label (distinct from the
    // axis tick that may share the value).
    const chip = Array.from(container.querySelectorAll("text")).find(
      (t) => t.getAttribute("fill") === "#fff" && t.textContent === "107.00"
    );
    expect(chip).toBeTruthy();
  });

  it("adds no interaction overlay unless a callback is provided", () => {
    render(<CandleChart bars={bars} />);
    expect(screen.queryByRole("slider")).toBeNull();
  });

  it("emits crosshair and activation events from the interaction overlay", () => {
    const onCrosshairChange = vi.fn();
    const onPointActivate = vi.fn();
    render(
      <CandleChart bars={bars} crosshairIndex={1} onCrosshairChange={onCrosshairChange} onPointActivate={onPointActivate} />
    );

    const slider = screen.getByRole("slider");
    expect(slider).toHaveAttribute("aria-valuemax", String(bars.length - 1));

    fireEvent.keyDown(slider, { key: "ArrowRight" });
    expect(onCrosshairChange).toHaveBeenCalledWith(2);

    fireEvent.keyDown(slider, { key: "Enter" });
    expect(onPointActivate).toHaveBeenCalledWith(1);
  });
});

describe("chart accessibility", () => {
  const data = {
    columns: ["Period", "Value"],
    rows: [
      ["Jan", 100],
      ["Feb", 110]
    ]
  };
  const charts = [
    { name: "CandleChart", label: "Candlestick price chart", renderChart: (p: object) => <CandleChart bars={bars} {...p} /> },
    {
      name: "EquityCurve",
      label: "Equity performance curve",
      renderChart: (p: object) => <EquityCurve series={[{ label: "S", color: "#16885F", points: [100, 110] }]} {...p} />
    },
    { name: "DrawdownChart", label: "Drawdown underwater chart", renderChart: (p: object) => <DrawdownChart series={[0, -5]} {...p} /> },
    { name: "Histogram", label: "Distribution histogram", renderChart: (p: object) => <Histogram values={[-1, 0, 1]} {...p} /> },
    {
      name: "DepthChart",
      label: "Order-book depth chart",
      renderChart: (p: object) => <DepthChart bids={[{ price: 99, size: 100 }]} asks={[{ price: 101, size: 150 }]} {...p} />
    }
  ];

  it.each(charts)("$name keeps its default label and renders no description or table by default", ({ label, renderChart }) => {
    const { container } = render(renderChart({}));
    const img = screen.getByRole("img", { name: label });
    expect(img.tagName.toLowerCase()).toBe("svg");
    expect(img).not.toHaveAttribute("aria-describedby");
    expect(screen.queryByRole("table")).toBeNull();
    expect(container.querySelector("p")).toBeNull();
  });

  it.each(charts)("$name accepts a label, a description, and a hidden data table", ({ renderChart }) => {
    render(renderChart({ ariaLabel: "AAPL daily", ariaDescription: "Up 10% over two months.", accessibleData: data }));

    const img = screen.getByRole("img", { name: "AAPL daily" });
    expect(img).toHaveAccessibleDescription("Up 10% over two months.");

    // Caption defaults to the chart label; the table sits outside the role="img" subtree.
    const table = screen.getByRole("table", { name: "AAPL daily data" });
    expect(img.contains(table)).toBe(false);
    expect(table).toHaveStyle({ position: "absolute", width: "1px", height: "1px", overflow: "hidden" });
    expect(screen.getAllByRole("columnheader").map((h) => h.textContent)).toEqual(["Period", "Value"]);
    expect(screen.getAllByRole("rowheader").map((h) => h.textContent)).toEqual(["Jan", "Feb"]);
    expect(screen.getAllByRole("cell").map((c) => c.textContent)).toEqual(["100", "110"]);
  });

  it("uses an explicit table caption when one is supplied", () => {
    render(<Histogram values={[1, 2]} accessibleData={{ ...data, caption: "Return buckets" }} />);
    expect(screen.getByRole("table", { name: "Return buckets" })).toBeInTheDocument();
  });

  it.each([
    {
      name: "CandleChart",
      renderChart: () => <CandleChart bars={bars} onCrosshairChange={() => {}} ariaDescription="Three bars." />
    },
    {
      name: "EquityCurve",
      renderChart: () => (
        <EquityCurve
          series={[{ label: "S", color: "#16885F", points: [100, 110] }]}
          onCrosshairChange={() => {}}
          ariaDescription="Three bars."
        />
      )
    }
  ])("$name exposes the crosshair slider outside the role=img subtree", ({ renderChart }) => {
    const { container } = render(renderChart());
    const slider = screen.getByRole("slider");
    const img = screen.getByRole("img");

    expect(img.tagName.toLowerCase()).toBe("g");
    expect(img).toHaveAccessibleDescription("Three bars.");
    expect(img.contains(slider)).toBe(false);
    expect(slider.closest('[role="img"]')).toBeNull();
    expect(container.querySelector("svg")).toHaveAttribute("role", "group");
    // Still the top-most element of the same svg so it keeps capturing pointer input.
    expect(slider.closest("svg")).toBe(img.closest("svg"));
    expect(slider.closest("svg")?.lastElementChild).toBe(slider);
  });

  it("gives the crosshair slider a visible keyboard focus ring", () => {
    const { container } = render(<CandleChart bars={bars} onCrosshairChange={() => {}} />);
    const slider = screen.getByRole("slider");

    expect(slider).toHaveAttribute("tabindex", "0");
    expect(slider.style.outline).toBe("");
    const css = container.querySelector("style")?.textContent ?? "";
    expect(slider).toHaveClass("meridian-chart-overlay");
    expect(css).toContain(
      ".meridian-chart-overlay:focus-visible{outline:var(--focus-ring, 2px solid var(--focus, #2D6FB3))"
    );
  });
});

describe("EquityCurve", () => {
  it("renders a legend and a series path", () => {
    const { container } = render(
      <EquityCurve
        series={[{ label: "Strategy", color: "var(--chart-equity, #2E7655)", points: [100, 110, 105, 120] }]}
        labels={["Jan", "Feb", "Mar", "Apr"]}
      />
    );
    expect(screen.getByText("Strategy")).toBeInTheDocument();
    expect(container.querySelectorAll("path").length).toBeGreaterThan(0);
  });

  it("renders a drawdown subpane when drawdown is supplied", () => {
    render(
      <EquityCurve
        series={[{ label: "S", color: "#16885F", points: [100, 90, 95] }]}
        drawdown={[0, -10, -5]}
      />
    );
    expect(screen.getByText("drawdown")).toBeInTheDocument();
  });

  it("keeps negative drawdown paths inside the drawdown subpane", () => {
    const { container } = render(
      <EquityCurve
        series={[{ label: "S", color: "#16885F", points: [100, 90, 95] }]}
        drawdown={[0, -10, -5]}
      />
    );
    const drawdownPath = Array.from(container.querySelectorAll("path")).find((p) =>
      p.getAttribute("stroke") === "var(--chart-drawdown, #B54B42)"
    );
    expect(drawdownPath?.getAttribute("d")).toContain("432.0");
  });
});

describe("DrawdownChart", () => {
  it("marks the maximum drawdown", () => {
    render(<DrawdownChart series={[0, -5, -12, -8]} labels={["a", "b", "c", "d"]} />);
    expect(screen.getByText(/max/)).toBeInTheDocument();
  });

  it("labels a threshold rule", () => {
    render(<DrawdownChart series={[0, -5, -12]} threshold={-10} />);
    expect(screen.getByText(/limit/)).toBeInTheDocument();
  });
});

describe("Histogram", () => {
  it("auto-bins raw values into an svg", () => {
    const { container } = render(<Histogram values={[-2, -1, 0, 1, 2, 1, 0, -1]} binCount={6} />);
    expect(screen.getByRole("img", { name: "Distribution histogram" })).toBeInTheDocument();
    expect(container.querySelectorAll("rect").length).toBeGreaterThan(1);
  });

  it("renders precomputed bins", () => {
    const { container } = render(
      <Histogram
        bins={[
          { x0: -1, x1: 0, count: 3 },
          { x0: 0, x1: 1, count: 5 }
        ]}
      />
    );
    expect(container.querySelector("svg")).toBeInTheDocument();
  });
});

describe("DepthChart", () => {
  it("renders bid/ask step areas and the mid label", () => {
    render(
      <DepthChart
        bids={[
          { price: 99, size: 100 },
          { price: 98, size: 200 }
        ]}
        asks={[
          { price: 101, size: 150 },
          { price: 102, size: 120 }
        ]}
      />
    );
    expect(screen.getByText(/mid/)).toBeInTheDocument();
  });
});

describe("CorrelationHeatmap", () => {
  it("renders a grid with the diagonal reading 1.00", () => {
    render(
      <CorrelationHeatmap
        labels={["AAPL", "MSFT"]}
        matrix={[
          [1, 0.4],
          [0.4, 1]
        ]}
      />
    );
    expect(screen.getByRole("grid", { name: "Correlation matrix" })).toBeInTheDocument();
    expect(screen.getAllByText("1.00").length).toBe(2);
  });
});

describe("Sparkline", () => {
  it("renders nothing for an empty series", () => {
    const { container } = render(<Sparkline points={[]} />);
    expect(container.querySelector("svg")).toBeNull();
  });

  it("renders a line variant path", () => {
    const { container } = render(<Sparkline points={[1, 3, 2, 5]} />);
    expect(container.querySelector("path")).toBeInTheDocument();
  });

  it("renders a bar variant with one rect per point", () => {
    const { container } = render(<Sparkline points={[1, -2, 3]} variant="bar" baseline={0} />);
    expect(container.querySelectorAll("rect").length).toBe(3);
  });
});

describe("ChartCard", () => {
  it("renders title, readout, and children", () => {
    render(
      <ChartCard title="AAPL" subtitle="1m" readout={[{ label: "Last", value: "104.00" }]}>
        <div data-testid="plot">plot</div>
      </ChartCard>
    );
    expect(screen.getByText("AAPL")).toBeInTheDocument();
    expect(screen.getByText("Last")).toBeInTheDocument();
    expect(screen.getByTestId("plot")).toBeInTheDocument();
  });
});

describe("niceTicks", () => {
  it("returns no ticks when the computed step underflows", () => {
    expect(niceTicks(0, Number.MIN_VALUE, 4)).toEqual([]);
  });
});
