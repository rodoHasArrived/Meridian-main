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

describe("EquityCurve", () => {
  it("renders a legend and a series path", () => {
    const { container } = render(
      <EquityCurve
        series={[{ label: "Strategy", color: "var(--chart-equity, #16885F)", points: [100, 110, 105, 120] }]}
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
      p.getAttribute("stroke") === "var(--chart-drawdown, #BA3F55)"
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
