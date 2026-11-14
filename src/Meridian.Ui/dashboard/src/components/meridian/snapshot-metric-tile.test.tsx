import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { SnapshotMetricTile } from "@/components/meridian/snapshot-metric-tile";

describe("SnapshotMetricTile", () => {
  it("adapts snapshot view-model state to the Concrete metric card", () => {
    render(
      <SnapshotMetricTile
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
