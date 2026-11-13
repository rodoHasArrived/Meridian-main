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

  it("states each source with the figures it covers when the figures come from different sources", () => {
    render(
      <StatStrip
        label="Mixed metrics"
        metrics={[
          metric({ id: "a", label: "Cash", value: "$1", provenance }),
          metric({ id: "b", label: "Orders", value: "2", provenance: { source: "Trading workspace" }, freshness: "unverified" })
        ]}
      />
    );

    expect(screen.getByTestId("stat-strip-provenance")).toHaveTextContent(
      "Unverified figures · Cash from Portfolio workspace · As of Jun 30, 2026, 18:05 UTC · Scope: All accounts; Orders from Trading workspace"
    );
    expect(screen.getByText("2").closest("[data-freshness]")).toHaveAttribute(
      "title",
      "Unverified: no current evidence backs this figure. Source: Trading workspace"
    );
    expect(screen.getByText("Unverified: no current evidence backs this figure.")).toHaveClass("sr-only");
  });

  it("keeps a figure's own provenance for screen readers when another figure states none", () => {
    render(
      <StatStrip
        label="Partly sourced metrics"
        metrics={[
          metric({ id: "a", label: "Cash", value: "$1" }),
          metric({ id: "b", label: "Orders", value: "2", provenance: { source: "Trading workspace" } })
        ]}
      />
    );

    expect(screen.queryByTestId("stat-strip-provenance")).not.toBeInTheDocument();
    expect(screen.getByText("Source: Trading workspace")).toHaveClass("sr-only");
  });

  it("gives a figure supplied by another payload that payload's evidence inside a workspace-fed strip", () => {
    // Settings: the overview reloaded at 18:10, the cross-screen counts reload failed and its
    // provider-health figure is still the 17:40 one.
    render(
      <WorkspaceFigureContextProvider
        value={{
          freshness: "fresh",
          dataLabel: null,
          staleSources: ["crossScreenCounts"],
          sourceLoadedAt: { overview: "2026-06-30T18:10:00Z", crossScreenCounts: "2026-06-30T17:40:00Z" }
        }}
      >
        <StatStrip
          label="Operator control posture metrics"
          source="System overview"
          figureSource="overview"
          metrics={[
            metric({
              id: "providers-healthy",
              label: "Providers healthy",
              value: "3 / 4",
              provenance: { source: "Cross-screen counts", asOf: "2026-06-30T17:40:00Z" },
              freshness: "stale"
            }),
            metric({ id: "active-runs", label: "Active runs", value: "7" }),
            metric({ id: "open-positions", label: "Open positions", value: "12" })
          ]}
        />
      </WorkspaceFigureContextProvider>
    );

    expect(screen.getByTestId("stat-strip-provenance")).toHaveTextContent(
      "Stale figures · Active runs, Open positions from System overview · As of Jun 30, 2026, 18:10 UTC; Providers healthy from Cross-screen counts · As of Jun 30, 2026, 17:40 UTC"
    );
    expect(screen.getByText("3 / 4")).toHaveClass("text-muted-foreground");
    expect(screen.getByText("7")).toHaveClass("text-foreground");
  });

  it("takes the as-of time, freshness and data label from the shell for a workspace-fed strip", () => {
    render(
      <WorkspaceFigureContextProvider value={{ freshness: "fresh", dataLabel: "seeded demo data", staleSources: ["strategy"], sourceLoadedAt: { strategy: "2026-06-30T18:05:00Z" } }}>
        <StatStrip label="Strategy headline metrics" source="Strategy workspace" figureSource="strategy" metrics={[metric({ id: "runs", label: "Runs", value: "24" })]} />
      </WorkspaceFigureContextProvider>
    );

    expect(screen.getByTestId("stat-strip-provenance")).toHaveTextContent(
      "Stale figures · Source: Strategy workspace (seeded demo data) · As of Jun 30, 2026, 18:05 UTC"
    );
    expect(screen.getByText("24")).toHaveClass("text-muted-foreground");
  });

  it("shows a workspace's retained figures as stale after a partial refresh while others stay fresh", () => {
    const context = {
      freshness: "fresh" as const,
      dataLabel: null,
      staleSources: ["accounting" as const],
      sourceLoadedAt: { accounting: "2026-06-30T18:05:00Z", strategy: "2026-06-30T18:10:00Z" }
    };
    render(
      <WorkspaceFigureContextProvider value={context}>
        <StatStrip label="Accounting headline metrics" source="Accounting workspace" figureSource="accounting" metrics={[metric({ id: "breaks", label: "Open breaks", value: "5" })]} />
        <StatStrip label="Strategy headline metrics" source="Strategy workspace" figureSource="strategy" metrics={[metric({ id: "runs", label: "Runs", value: "24" })]} />
      </WorkspaceFigureContextProvider>
    );

    const [accounting, strategy] = screen.getAllByTestId("stat-strip-provenance");
    expect(accounting).toHaveTextContent("Stale figures · Source: Accounting workspace · As of Jun 30, 2026, 18:05 UTC");
    expect(screen.getByText("5")).toHaveClass("text-muted-foreground");
    expect(strategy).toHaveTextContent(/^Source: Strategy workspace · As of Jun 30, 2026, 18:10 UTC$/);
    expect(screen.getByText("24")).toHaveClass("text-foreground");
  });

  it("states each workspace's own load time, not the time of the latest refresh", () => {
    // Strategy was reloaded at 18:10; Accounting's figures are still the 17:40 load.
    const context = {
      freshness: "fresh" as const,
      dataLabel: null,
      staleSources: [],
      sourceLoadedAt: { accounting: "2026-06-30T17:40:00Z", strategy: "2026-06-30T18:10:00Z" }
    };
    render(
      <WorkspaceFigureContextProvider value={context}>
        <StatStrip label="Accounting headline metrics" source="Accounting workspace" figureSource="accounting" metrics={[metric({ id: "breaks", label: "Open breaks", value: "5" })]} />
        <StatStrip label="Strategy headline metrics" source="Strategy workspace" figureSource="strategy" metrics={[metric({ id: "runs", label: "Runs", value: "24" })]} />
      </WorkspaceFigureContextProvider>
    );

    const [accounting, strategy] = screen.getAllByTestId("stat-strip-provenance");
    expect(accounting).toHaveTextContent(/^Source: Accounting workspace · As of Jun 30, 2026, 17:40 UTC$/);
    expect(strategy).toHaveTextContent(/^Source: Strategy workspace · As of Jun 30, 2026, 18:10 UTC$/);
  });

  it("states no as-of time when the workspace's load was never recorded", () => {
    render(
      <WorkspaceFigureContextProvider value={{ freshness: "fresh", dataLabel: null, staleSources: [], sourceLoadedAt: {} }}>
        <StatStrip label="Data headline metrics" source="Data workspace" figureSource="data" metrics={[metric({ id: "runs", label: "Runs", value: "24" })]} />
      </WorkspaceFigureContextProvider>
    );

    expect(screen.getByTestId("stat-strip-provenance")).toHaveTextContent(/^Source: Data workspace$/);
  });

  it("does not print a basis line when no figure declares one", () => {
    render(
      <StatStrip
        label="Undeclared basis metrics"
        metrics={[
          metric({ id: "cash", label: "Cash", value: "$1" }),
          metric({ id: "orders", label: "Orders", value: "2" })
        ]}
      />
    );

    const strip = screen.getByLabelText("Undeclared basis metrics");
    expect(within(strip).queryByText("Basis", { selector: "dt" })).not.toBeInTheDocument();
    expect(strip).not.toHaveTextContent(/not declared by source/i);
    expect(strip).not.toHaveTextContent(/unavailable/i);
    for (const value of ["$1", "2"]) {
      expect(within(strip).getByText(value).closest("dd")).not.toHaveAttribute("aria-describedby");
    }
  });

  it("states a declared basis the figures share once and ties each figure to it", () => {
    render(
      <StatStrip
        label="Declared basis metrics"
        metrics={[
          metric({ id: "nav", label: "NAV", value: "$1", basis: "Posted GAAP book", asOf: "2026-11-10" }),
          metric({ id: "cash", label: "Cash", value: "$2", basis: "Posted GAAP book", asOf: "2026-11-10" })
        ]}
      />
    );

    const strip = screen.getByLabelText("Declared basis metrics");
    expect(within(strip).getAllByText("Basis", { selector: "dt" })).toHaveLength(1);
    const shared = within(strip).getByText("Posted GAAP book · as of 2026-11-10");
    expect(shared.tagName).toBe("DD");
    expect(within(strip).getAllByText("Posted GAAP book · as of 2026-11-10")).toHaveLength(1);
    const sharedId = shared.getAttribute("id");
    const termId = within(strip).getByText("Basis", { selector: "dt" }).getAttribute("id");
    expect(sharedId).toBeTruthy();
    expect(termId).toBeTruthy();
    for (const value of ["$1", "$2"]) {
      const figure = within(strip).getByText(value).closest("dd");
      expect(figure).toHaveAttribute("aria-describedby", `${termId} ${sharedId}`);
      expect(figure).toHaveAccessibleDescription("Basis Posted GAAP book · as of 2026-11-10");
    }
  });

  it("states each declared basis when the bases differ, without repeating an undeclared one", () => {
    render(
      <StatStrip
        label="Mixed basis metrics"
        metrics={[
          metric({ id: "nav", label: "NAV", value: "$1", basis: "Posted GAAP book" }),
          metric({ id: "orders", label: "Orders", value: "2" })
        ]}
      />
    );

    const strip = screen.getByLabelText("Mixed basis metrics");
    expect(within(strip).queryByText("Basis", { selector: "dt" })).not.toBeInTheDocument();
    expect(within(strip).getByText("Posted GAAP book")).toBeInTheDocument();
    expect(within(strip).queryByText("Basis not declared by source")).not.toBeInTheDocument();
    expect(within(strip).getByText("2").closest("dd")).not.toHaveAttribute("aria-describedby");
    expect(strip).not.toHaveTextContent(/unavailable/i);
  });

  it("names the source of a workspace-fed strip outside the shell without claiming freshness", () => {
    render(<StatStrip label="Data headline metrics" source="Data workspace" metrics={[metric({ id: "runs", label: "Runs", value: "24" })]} />);

    expect(screen.getByTestId("stat-strip-provenance")).toHaveTextContent(/^Source: Data workspace$/);
    expect(screen.getByText("24")).toHaveClass("text-foreground");
  });
});
