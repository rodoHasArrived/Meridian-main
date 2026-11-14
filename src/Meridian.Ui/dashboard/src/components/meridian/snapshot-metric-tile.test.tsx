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

describe("SnapshotMetricTile basis", () => {
  it("does not repeat an undeclared basis on the card", () => {
    render(<SnapshotMetricTile id="cash" label="Cash" value="$100" delta="" tone="default" />);

    const card = screen.getByRole("group", { name: /Cash metric/ });
    expect(card).not.toHaveTextContent(/basis/i);
    expect(card).not.toHaveTextContent(/unavailable/i);
    expect(card).not.toHaveTextContent("Not declared by source");
    // Only the label and value lines: no basis paragraph.
    expect(card.querySelectorAll(":scope > p")).toHaveLength(2);
  });

  it("keeps a top-level as-of time on the card when the basis is undeclared", () => {
    render(<SnapshotMetricTile id="cash" label="Cash" value="$100" delta="" tone="default" asOf="2026-11-10" />);

    const card = screen.getByRole("group", { name: /Cash metric/ });
    expect(card).toHaveTextContent("As of 2026-11-10");
    expect(card).not.toHaveTextContent(/basis/i);
    expect(card).not.toHaveTextContent(/unavailable/i);
  });

  it("shows a declared basis with its as-of time", () => {
    render(<SnapshotMetricTile id="nav" label="NAV" value="$100" delta="" tone="default" basis="Posted GAAP book" asOf="2026-11-10" />);

    expect(screen.getByRole("group", { name: /NAV metric/ })).toHaveTextContent("Posted GAAP book · as of 2026-11-10");
  });
});

describe("SnapshotMetricTile trust rules", () => {
  it("mutes a stale figure and prints its source under the value", () => {
    render(
      <SnapshotMetricTile
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
