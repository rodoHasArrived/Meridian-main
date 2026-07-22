import { render, screen } from "@testing-library/react";
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
