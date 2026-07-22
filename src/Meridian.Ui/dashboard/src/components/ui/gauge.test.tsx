import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Gauge, LinearGauge } from "@/components/ui/gauge";

describe("Gauge", () => {
  it("renders the rounded percentage of value/max", () => {
    render(<Gauge value={30} max={120} label="Fill rate" />);
    expect(screen.getByText("25%")).toBeInTheDocument();
    expect(screen.getByText("Fill rate")).toBeInTheDocument();
  });

  it("clamps values above the maximum", () => {
    render(<Gauge value={200} max={100} />);
    expect(screen.getByText("100%")).toBeInTheDocument();
  });
});

describe("LinearGauge", () => {
  it("exposes a progressbar with the computed percentage", () => {
    render(<LinearGauge value={65} label="Memory" />);
    const bar = screen.getByRole("progressbar");
    expect(bar).toHaveAttribute("aria-valuenow", "65");
    expect(screen.getByText("65%")).toBeInTheDocument();
  });
});
