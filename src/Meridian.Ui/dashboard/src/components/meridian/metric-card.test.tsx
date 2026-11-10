import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { MetricSnapshotCard } from "@/components/meridian/metric-card";

describe("MetricSnapshotCard", () => {
  it("adapts snapshot view-model state to the Concrete metric card", () => {
    render(
      <MetricSnapshotCard
        id="drawdown"
        label="DRAWDOWN"
        value="-2.41%"
        delta="▼ -0.30% · 90D"
        tone="danger"
      />
    );

    expect(screen.getByRole("group", { name: /DRAWDOWN metric/ })).toBeInTheDocument();
    expect(screen.getByLabelText("Change down -0.30% · 90D")).toHaveTextContent("▼ -0.30% · 90D");
    expect(screen.getByRole("group", { name: /Status critical/ })).toHaveClass("mds-metric--danger");
  });
});

describe("MetricSnapshotCard trust rules", () => {
  it("mutes a stale figure and prints its source under the value", () => {
    render(
      <MetricSnapshotCard
        id="pnl"
        label="Unrealized PnL"
        value="+$90"
        delta="+8%"
        tone="success"
        freshness="stale"
        provenance={{ source: "Portfolio workspace", asOf: "2026-06-30T18:05:00Z" }}
      />
    );

    const card = screen.getByRole("group", { name: /Unrealized P&L metric/ });
    expect(card).toHaveClass("mds-metric--degraded");
    expect(card).toHaveTextContent("Stale · Source: Portfolio workspace · As of Jun 30, 2026, 18:05 UTC");
    expect(card).not.toHaveTextContent("+8%");
  });
});
