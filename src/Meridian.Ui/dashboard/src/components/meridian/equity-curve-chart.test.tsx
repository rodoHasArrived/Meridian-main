import { render, screen } from "@testing-library/react";
import { EquityCurveChart } from "@/components/meridian/equity-curve-chart";
import type { EquityCurveSummary } from "@/types";

// Recharts uses SVG / ResizeObserver which is not present in jsdom.
// Provide a minimal stub so the component renders without throwing.
class ResizeObserverStub {
  observe() {}
  unobserve() {}
  disconnect() {}
}
Object.defineProperty(globalThis, "ResizeObserver", {
  writable: true,
  value: ResizeObserverStub
});

const mockData: EquityCurveSummary = {
  runId: "run-test-1",
  initialEquity: 100_000,
  finalEquity: 112_500,
  maxDrawdown: 5_200,
  maxDrawdownPercent: 5.2,
  maxDrawdownRecoveryDays: 18,
  sharpeRatio: 1.42,
  sortinoRatio: 1.91,
  points: [
    {
      date: "2024-01-02",
      totalEquity: 100_000,
      cash: 50_000,
      dailyReturn: 0,
      drawdownFromPeak: 0,
      drawdownFromPeakPercent: 0
    },
    {
      date: "2024-01-03",
      totalEquity: 101_200,
      cash: 50_000,
      dailyReturn: 0.012,
      drawdownFromPeak: 0,
      drawdownFromPeakPercent: 0
    },
    {
      date: "2024-01-04",
      totalEquity: 96_000,
      cash: 50_000,
      dailyReturn: -0.051,
      drawdownFromPeak: 5_200,
      drawdownFromPeakPercent: 5.14
    },
    {
      date: "2024-01-05",
      totalEquity: 112_500,
      cash: 50_000,
      dailyReturn: 0.172,
      drawdownFromPeak: 0,
      drawdownFromPeakPercent: 0
    }
  ]
};

const emptyData: EquityCurveSummary = {
  runId: "run-empty",
  initialEquity: 100_000,
  finalEquity: 100_000,
  maxDrawdown: 0,
  maxDrawdownPercent: 0,
  maxDrawdownRecoveryDays: 0,
  sharpeRatio: 0,
  sortinoRatio: 0,
  points: []
};

describe("EquityCurveChart", () => {
  it("renders key performance stats", () => {
    render(<EquityCurveChart data={mockData} />);
    expect(screen.getByText("Initial Equity")).toBeInTheDocument();
    expect(screen.getByText("Final Equity")).toBeInTheDocument();
    expect(screen.getByText("Max Drawdown")).toBeInTheDocument();
    expect(screen.getByText("Sharpe Ratio")).toBeInTheDocument();
  });

  it("renders formatted metric values", () => {
    render(<EquityCurveChart data={mockData} />);
    // Initial equity $100K
    expect(screen.getByText("$100K")).toBeInTheDocument();
    // Sharpe 1.42
    expect(screen.getByText("1.42")).toBeInTheDocument();
    // Max drawdown percent
    expect(screen.getByText("5.2%")).toBeInTheDocument();
  });

  it("renders extra stats row", () => {
    render(<EquityCurveChart data={mockData} />);
    expect(screen.getByText("Sortino Ratio")).toBeInTheDocument();
    expect(screen.getByText("Max DD Recovery")).toBeInTheDocument();
    expect(screen.getByText("Total Return")).toBeInTheDocument();
  });

  it("shows empty state when there are no data points", () => {
    render(<EquityCurveChart data={emptyData} />);
    expect(screen.getByText(/No equity curve data available/i)).toBeInTheDocument();
  });

  it("renders section headings for both charts", () => {
    render(<EquityCurveChart data={mockData} />);
    expect(screen.getByText("Equity Curve")).toBeInTheDocument();
    expect(screen.getByText("Drawdown from Peak")).toBeInTheDocument();
  });
});
