import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { MemoryRouter } from "react-router-dom";
import type { AppShellAttentionSignal } from "@/app-shell.attention";
import { OperationalTrustSummary } from "@/components/meridian/operational-trust-summary";
import { ShellStatusSignalsProvider } from "@/lib/shell-status-signals-context";

const readyFacts = {
  source: { value: "Interactive Brokers \u00b7 paper", detail: "Connected \u00b7 healthy", tone: "ready" },
  scope: { value: "Paper Index Mean Reversion", tone: "ready" },
  freshness: { value: "2s ago", tone: "ready" },
  completeness: { value: "12 positions", tone: "ready" }
} as const;

function shellSignal(overrides: Partial<AppShellAttentionSignal>): AppShellAttentionSignal {
  return {
    id: "providers",
    kind: "source",
    label: "Providers",
    value: "4/4 healthy",
    detail: "4 providers healthy in the loaded data posture.",
    tone: "ready",
    headline: "Providers: 4/4 healthy",
    href: null,
    actionLabel: null,
    ...overrides
  };
}

describe("OperationalTrustSummary", () => {
  it("says all sources are current in one line when every fact is ready", async () => {
    const { container } = render(<OperationalTrustSummary {...readyFacts} />);

    const region = screen.getByRole("region", { name: "Data confidence" });
    const summary = container.querySelector("summary")!;
    expect(region).toHaveAttribute("data-status-tone", "ready");
    expect(within(summary).getByTestId("status-row-clear")).toHaveTextContent("All sources current");
    // No row of "Ready" pills to learn to ignore: ready facts are behind Details.
    expect(summary).not.toHaveTextContent("Ready");
    expect(summary).not.toHaveTextContent("Paper Index Mean Reversion");
    expect(container.querySelector("details")).not.toHaveAttribute("open");
    expect((await axe(container)).violations).toEqual([]);
  });

  it("names only the facts that are not ready, worst first", () => {
    const { container } = render(
      <OperationalTrustSummary
        source={{ value: "Retained portfolio", detail: "Immutable source receipt", tone: "ready" }}
        scope={{ value: "none selected", tone: "unknown" }}
        freshness={{ value: "187d stale", tone: "review" }}
        completeness={{ value: "2 positions", tone: "ready" }}
        blocker={{ value: "Missing mark evidence", tone: "blocked" }}
      />
    );

    const line = within(container.querySelector("summary")!).getByTestId("status-row-issues");
    expect(line).toHaveTextContent("Blocked");
    expect(line.textContent).toMatch(/Blocker:Missing mark evidence.*Freshness:187d stale.*Scope:none selected/);
    expect(line).not.toHaveTextContent("Retained portfolio");
    expect(line).not.toHaveTextContent("2 positions");
    expect(line).not.toHaveTextContent("Immutable source receipt");
    expect(screen.getByRole("region", { name: "Data confidence" })).toHaveAttribute("data-status-tone", "blocked");
  });

  it("keeps the full per-fact breakdown and recovery actions behind Details", async () => {
    const user = userEvent.setup();
    const { container } = render(
      <OperationalTrustSummary
        source={{ value: "Retained portfolio", detail: "Immutable source receipt", tone: "ready" }}
        scope={{ value: "Book A", tone: "ready" }}
        freshness={{ value: "Stale snapshot", tone: "review" }}
        completeness={{ value: "2 positions", tone: "ready" }}
        blocker={{ value: "Missing mark evidence", tone: "blocked", action: <a href="/data/providers">Review marks</a> }}
      />
    );
    const details = container.querySelector("details")!;
    const summary = container.querySelector("summary")!;
    expect(details).not.toHaveAttribute("open");
    expect((await axe(container)).violations).toEqual([]);

    await user.click(summary);

    expect(details).toHaveAttribute("open");
    const breakdown = container.querySelector("dl")!;
    for (const label of ["Source", "Scope", "Freshness", "Completeness", "Blocker"]) {
      expect(within(breakdown).getByText(label)).toBeInTheDocument();
    }
    expect(within(breakdown).getAllByText("Ready")).toHaveLength(3);
    expect(within(breakdown).getByText("Needs review")).toBeInTheDocument();
    expect(within(breakdown).getByText("Immutable source receipt")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Review marks" })).toHaveAttribute("href", "/data/providers");
    expect((await axe(container)).violations).toEqual([]);
  });

  it("keeps the card action beside the row, outside the collapsed breakdown", () => {
    const { container } = render(
      <OperationalTrustSummary
        {...readyFacts}
        completeness={{ value: "Accounting basis missing", tone: "review" }}
        action={<button type="button">Synchronize</button>}
      />
    );

    const summary = container.querySelector("summary")!;
    const value = within(summary).getByText("Accounting basis missing");
    // Values are financially material: they wrap, they never truncate.
    expect(value).toHaveClass("break-words");
    expect(value).not.toHaveClass("truncate");
    expect(value.closest(".flex-wrap")).not.toBeNull();
    const action = screen.getByRole("button", { name: "Synchronize" });
    expect(container.querySelector("details")!.contains(action)).toBe(false);
  });

  it("names a fact with no stated tone as unknown instead of assuming it is fine", () => {
    const { container } = render(
      <OperationalTrustSummary
        source={{ value: "Not loaded" }}
        scope={{ value: "All portfolios" }}
        freshness={{ value: "Not available" }}
        completeness={{ value: "Not measured" }}
      />
    );

    const line = within(container.querySelector("summary")!).getByTestId("status-row-issues");
    expect(line).toHaveTextContent("Unknown");
    expect(line).toHaveTextContent("Source:Not loaded");
    const breakdown = container.querySelector("dl")!;
    expect(within(breakdown).getAllByText("Unknown")).toHaveLength(4);
    expect(breakdown).toHaveClass("grid-cols-[repeat(auto-fit,minmax(min(100%,12rem),1fr))]");
  });

  it("keeps remediation actions inside the fact that owns the warning", () => {
    const { container } = render(
      <OperationalTrustSummary
        source={{
          label: "Connectivity",
          value: "Provider degraded",
          tone: "blocked",
          action: <a href="/data/providers">Open provider posture</a>
        }}
        scope={{
          value: "No operating scope selected",
          tone: "review",
          action: <button type="button">Set operating scope</button>
        }}
        freshness={{
          value: "Stale update",
          tone: "review",
          action: <button type="button">Refresh evidence</button>
        }}
        completeness={{ value: "3 ranked items", tone: "ready" }}
      />
    );

    const breakdown = container.querySelector("dl")!;
    const connectivityCard = within(breakdown).getByText("Connectivity").parentElement as HTMLElement;
    const scopeCard = within(breakdown).getByText("Scope").parentElement as HTMLElement;
    const freshnessCard = within(breakdown).getByText("Freshness").parentElement as HTMLElement;

    expect(within(connectivityCard).getByRole("link", { name: "Open provider posture" }))
      .toHaveAttribute("href", "/data/providers");
    expect(within(scopeCard).getByRole("button", { name: "Set operating scope" })).toBeInTheDocument();
    expect(within(freshnessCard).getByRole("button", { name: "Refresh evidence" })).toBeInTheDocument();
  });

  it("gives a long status value its own line rather than breaking a word mid-character", () => {
    // Five tiles is the blocked case: the extra tile narrows each one to about 12rem, and the
    // status pill does not shrink. Without a flex basis the value is squeezed into ~100px and
    // "unavailable" splits mid-word.
    const { container } = render(
      <OperationalTrustSummary
        source={{ label: "Connectivity", value: "1 warning", tone: "review" }}
        scope={{ value: "No operating scope selected", tone: "review" }}
        freshness={{ value: "Timestamp unavailable", tone: "review" }}
        completeness={{ value: "6 ranked items", tone: "ready" }}
        blocker={{ value: "Cash variance over tolerance.", tone: "blocked" }}
      />
    );

    const breakdown = container.querySelector("dl")!;
    const value = within(breakdown).getByText("Timestamp unavailable");
    // Wraps to its own full-width line when the space beside the pill is too tight for it.
    expect(value).toHaveClass("basis-28");
    expect(value).toHaveClass("grow");
    expect(value.parentElement).toHaveClass("flex-wrap");

    // The pill must stay unshrunk -- it is the text status that carries meaning without colour.
    const pill = within(breakdown).getByText("Blocked");
    expect(pill).toHaveClass("shrink-0");
  });

  describe("shell source signals", () => {
    const renderInShell = (sourceSignals: AppShellAttentionSignal[]) =>
      render(
        <MemoryRouter>
          <ShellStatusSignalsProvider value={{ sourceSignals, operatingScope: null }}>
            <OperationalTrustSummary {...readyFacts} label="Trading data confidence" />
          </ShellStatusSignalsProvider>
        </MemoryRouter>
      );

    it("cannot read all sources current while the masthead warns about a provider", () => {
      // The Trading example: page freshness is "2s ago" and ready, the masthead says 1 warning.
      const { container } = renderInShell([
        shellSignal({
          value: "1 warning",
          detail: "1 provider warning; review provider trust before relying on fresh data.",
          tone: "review",
          headline: "Providers: 1 warning",
          href: "/data/providers",
          actionLabel: "Open provider posture"
        })
      ]);

      const summary = container.querySelector("summary")!;
      expect(within(summary).queryByTestId("status-row-clear")).toBeNull();
      const line = within(summary).getByTestId("status-row-issues");
      expect(line).toHaveTextContent("Needs review");
      expect(line).toHaveTextContent("Providers:1 warning");
      expect(line).not.toHaveTextContent("2s ago");
      expect(screen.getByRole("region", { name: "Trading data confidence" })).toHaveAttribute("data-status-tone", "review");
      expect(screen.getByRole("link", { name: "Open provider posture" })).toHaveAttribute("href", "/data/providers");
    });

    it("stays on all sources current when the shell signals are ready", () => {
      const { container } = renderInShell([shellSignal({})]);

      expect(within(container.querySelector("summary")!).getByTestId("status-row-clear"))
        .toHaveTextContent("All sources current");
      // A ready shell signal adds no tile either; the page breakdown stays the page's own facts.
      expect(within(container.querySelector("dl")!).queryByText("Providers")).toBeNull();
    });
  });
});
