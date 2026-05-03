import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { MetricCard } from "@/components/meridian/metric-card";

describe("MetricCard", () => {
  it("renders the KPI as a named metric group with the derived delta label", () => {
    render(
      <MetricCard
        id="drawdown"
        label="DRAWDOWN"
        value="-2.41%"
        delta="▼ -0.30% · 90D"
        tone="danger"
      />
    );

    expect(screen.getByRole("group", { name: /DRAWDOWN metric/ })).toBeInTheDocument();
    expect(screen.getByLabelText("Change down -0.30% · 90D")).toHaveTextContent("▼ -0.30% · 90D");
    expect(screen.getByText("-2.41%")).toHaveClass("text-danger");
  });
});
