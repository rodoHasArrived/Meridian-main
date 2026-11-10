import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { StatStrip } from "@/components/meridian/stat-strip";
import { WorkspaceFigureContextProvider } from "@/lib/workspace-figure-context";
import type { MetricSnapshot } from "@/types";

const provenance = { source: "Portfolio workspace", asOf: "2026-06-30T18:05:00Z", scope: "All accounts" };

function metric(overrides: Partial<MetricSnapshot>): MetricSnapshot {
  return { id: "runs", label: "Runs", value: "24", delta: "", tone: "default", ...overrides };
}

describe("StatStrip", () => {
  it("renders nothing without metrics", () => {
    const { container } = render(<StatStrip metrics={[]} label="Headline metrics" />);

    expect(container).toBeEmptyDOMElement();
  });

  it("drops a change that states no baseline and keeps one that does", () => {
    render(
      <StatStrip
        label="Strategy headline metrics"
        metrics={[
          metric({ id: "runs", label: "Runs", value: "24", delta: "+8%" }),
          metric({ id: "queued", label: "Queued", value: "3", delta: "0%" }),
          metric({ id: "audit", label: "Audit ready", value: "4", delta: "+2", baseline: "since yesterday" }),
          metric({ id: "breaks", label: "Open breaks", value: "5", delta: "3 new since your last visit" })
        ]}
      />
    );

    const strip = screen.getByLabelText("Strategy headline metrics");
    expect(within(strip).queryByText("+8%")).not.toBeInTheDocument();
    expect(within(strip).queryByText("0%")).not.toBeInTheDocument();
    expect(within(strip).getByText("+2 since yesterday")).toBeInTheDocument();
    expect(within(strip).getByText("3 new since your last visit")).toBeInTheDocument();
  });

  it("states the source, as-of time and scope the figures share once under the row", () => {
    render(
      <StatStrip
        label="Portfolio headline metrics"
        metrics={[
          metric({ id: "exposure", label: "Total exposure", value: "$18,900", provenance }),
          metric({ id: "pnl", label: "Unrealized PnL", value: "+$90", tone: "success", provenance })
        ]}
      />
    );

    expect(screen.getByTestId("stat-strip-provenance")).toHaveTextContent(
      "Source: Portfolio workspace · As of Jun 30, 2026, 18:05 UTC · Scope: All accounts"
    );
    // Labels use the one P&L spelling whatever the read model sent.
    expect(screen.getByText("Unrealized P&L")).toBeInTheDocument();
    expect(screen.getByText("+$90")).toHaveClass("text-success", "tabular-nums");
  });

  it("mutes a stale figure, marks the strip and explains the age to assistive technology", () => {
    render(
      <StatStrip
        label="Portfolio headline metrics"
        metrics={[
          metric({ id: "pnl", label: "Unrealized P&L", value: "+$90", tone: "success", provenance, freshness: "stale" })
        ]}
      />
    );

    const value = screen.getByText("+$90");
    expect(value).toHaveClass("text-muted-foreground");
    expect(value).not.toHaveClass("text-success");
    expect(value.closest("[data-freshness]")).toHaveAttribute("data-freshness", "stale");
    expect(value.closest("[data-freshness]")).toHaveAttribute(
      "title",
      "Stale: as of Jun 30, 2026, 18:05 UTC. Source: Portfolio workspace · As of Jun 30, 2026, 18:05 UTC · Scope: All accounts"
    );
    expect(screen.getByText("Stale: as of Jun 30, 2026, 18:05 UTC.")).toHaveClass("sr-only");
    expect(screen.getByTestId("stat-strip-provenance")).toHaveTextContent(/^Stale figures · Source: Portfolio workspace/);
  });

  it("keeps each figure's own provenance available when the figures come from different sources", () => {
    render(
      <StatStrip
        label="Mixed metrics"
        metrics={[
          metric({ id: "a", label: "Cash", value: "$1", provenance }),
          metric({ id: "b", label: "Orders", value: "2", provenance: { source: "Trading workspace" }, freshness: "unverified" })
        ]}
      />
    );

    expect(screen.getByTestId("stat-strip-provenance")).toHaveTextContent(/^Unverified figures$/);
    expect(screen.getByText("Source: Trading workspace")).toHaveClass("sr-only");
    expect(screen.getByText("Unverified: no current evidence backs this figure.")).toHaveClass("sr-only");
  });

  it("takes the as-of time, freshness and data label from the shell for a workspace-fed strip", () => {
    render(
      <WorkspaceFigureContextProvider value={{ asOf: "2026-06-30T18:05:00Z", freshness: "stale", dataLabel: "seeded demo data" }}>
        <StatStrip label="Strategy headline metrics" source="Strategy workspace" metrics={[metric({ id: "runs", label: "Runs", value: "24" })]} />
      </WorkspaceFigureContextProvider>
    );

    expect(screen.getByTestId("stat-strip-provenance")).toHaveTextContent(
      "Stale figures · Source: Strategy workspace (seeded demo data) · As of Jun 30, 2026, 18:05 UTC"
    );
    expect(screen.getByText("24")).toHaveClass("text-muted-foreground");
  });

  it("names the source of a workspace-fed strip outside the shell without claiming freshness", () => {
    render(<StatStrip label="Data headline metrics" source="Data workspace" metrics={[metric({ id: "runs", label: "Runs", value: "24" })]} />);

    expect(screen.getByTestId("stat-strip-provenance")).toHaveTextContent(/^Source: Data workspace$/);
    expect(screen.getByText("24")).toHaveClass("text-foreground");
  });
});
