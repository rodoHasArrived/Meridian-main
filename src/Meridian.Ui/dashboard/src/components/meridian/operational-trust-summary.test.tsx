import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { OperationalTrustSummary } from "@/components/meridian/operational-trust-summary";

describe("OperationalTrustSummary", () => {
  it("renders source, scope, freshness, completeness, blocker, and recovery action with text status", () => {
    render(
      <OperationalTrustSummary
        source={{ value: "Brokerage sync", detail: "Alpaca paper account", tone: "ready" }}
        scope={{ value: "Growth sleeve", tone: "ready" }}
        freshness={{ value: "2 minutes ago", tone: "review" }}
        completeness={{ value: "18 of 19 positions", tone: "review" }}
        blocker={{ value: "Security identity missing", tone: "blocked" }}
        action={<button type="button">Resolve identity</button>}
      />
    );

    const region = screen.getByRole("region", { name: "Data confidence" });
    expect(region).toHaveTextContent("Brokerage sync");
    expect(region).toHaveTextContent("Growth sleeve");
    expect(region).toHaveTextContent("2 minutes ago");
    expect(region).toHaveTextContent("18 of 19 positions");
    expect(region).toHaveTextContent("Security identity missing");
    expect(region).toHaveTextContent("Ready");
    expect(region).toHaveTextContent("Needs review");
    expect(region).toHaveTextContent("Blocked");
    expect(screen.getByRole("button", { name: "Resolve identity" })).toBeInTheDocument();
  });

  it("uses an explicit unknown status when a fact has no tone", () => {
    render(
      <OperationalTrustSummary
        source={{ value: "Not loaded" }}
        scope={{ value: "All portfolios" }}
        freshness={{ value: "Not available" }}
        completeness={{ value: "Not measured" }}
      />
    );

    const region = screen.getByRole("region", { name: "Data confidence" });
    expect(screen.getAllByText("Unknown")).toHaveLength(4);
    expect(region.querySelector("dl")).toHaveClass("grid-cols-[repeat(auto-fit,minmax(min(100%,12rem),1fr))]");
  });

  it("keeps remediation actions inside the fact that owns the warning", () => {
    render(
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

    const connectivityCard = screen.getByText("Connectivity").parentElement as HTMLElement;
    const scopeCard = screen.getByText("Scope").parentElement as HTMLElement;
    const freshnessCard = screen.getByText("Freshness").parentElement as HTMLElement;

    expect(within(connectivityCard).getByRole("link", { name: "Open provider posture" }))
      .toHaveAttribute("href", "/data/providers");
    expect(within(scopeCard).getByRole("button", { name: "Set operating scope" })).toBeInTheDocument();
    expect(within(freshnessCard).getByRole("button", { name: "Refresh evidence" })).toBeInTheDocument();
  });

  it("gives a long status value its own line rather than breaking a word mid-character", () => {
    // Five tiles is the blocked case: the extra tile narrows each one to about 12rem, and the
    // status pill does not shrink. Without a flex basis the value is squeezed into ~100px and
    // "unavailable" splits mid-word, which is what the daily control tower rendered.
    render(
      <OperationalTrustSummary
        source={{ label: "Connectivity", value: "1 warning", tone: "review" }}
        scope={{ value: "No operating scope selected", tone: "review" }}
        freshness={{ value: "Timestamp unavailable", tone: "review" }}
        completeness={{ value: "6 ranked items", tone: "ready" }}
        blocker={{ value: "Cash variance over tolerance.", tone: "blocked" }}
      />
    );

    const value = screen.getByText("Timestamp unavailable");
    // Wraps to its own full-width line when the space beside the pill is too tight for it.
    expect(value).toHaveClass("basis-28");
    expect(value).toHaveClass("grow");
    expect(value.parentElement).toHaveClass("flex-wrap");

    // The pill must stay unshrunk -- it is the text status that carries meaning without colour.
    const pill = screen.getByText("Blocked");
    expect(pill).toHaveClass("shrink-0");
  });

  it("wraps long trust facts instead of truncating financially material labels", () => {
    render(
      <OperationalTrustSummary
        source={{ value: "Trial balance", tone: "ready" }}
        scope={{ value: "Paper Index Mean Reversion", tone: "ready" }}
        freshness={{ value: "Current response", tone: "ready" }}
        completeness={{ value: "Accounting basis missing", tone: "review" }}
        blocker={{ value: "Open cash variance", tone: "blocked" }}
      />
    );

    for (const text of ["Paper Index Mean Reversion", "Accounting basis missing", "Open cash variance"]) {
      const element = screen.getByText(text);
      expect(element).toHaveClass("break-words");
      expect(element).not.toHaveClass("truncate");
    }
  });
});
