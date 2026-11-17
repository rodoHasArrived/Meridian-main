import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { renderWithRouter } from "@/test/render";
import { PeriodComparisonPanel } from "./period-comparison-panel";
import { normalizePeriodComparison, type PeriodComparison } from "@/lib/api/period-comparison.api";
import { defaultMaterialityPolicyForClass } from "@/lib/reporting-materiality";
import { NavComparisonScreen } from "@/screens/nav-comparison-screen";
import { CashLadderPeriodComparison } from "@/screens/cash-ladder-screen.comparison";
import { RunAttributionPeriodComparison } from "@/screens/portfolio-screen.attribution-comparison";

vi.mock("@/lib/table-workbench", async (load) => ({ ...await load<typeof import("@/lib/table-workbench")>(), loadTableViews: vi.fn(async () => []), saveTableView: vi.fn() }));

function comparison(overrides: Partial<PeriodComparison> = {}): PeriodComparison {
  return normalizePeriodComparison({
    status: "Ready", reason: null, subject: "Alpha Fund", currency: "USD",
    current: { label: "As of Nov 17, 2026", start: null, end: "2026-11-17", isPartial: true },
    baseline: { label: "As of Oct 31, 2026", start: null, end: "2026-10-31", isPartial: false },
    lines: [
      { key: "total", label: "Total NAV", current: 1_450_000, prior: 1_100_000, change: 350_000, percentChange: 31.818181818, isTotal: true },
      { key: "asset-class:Equity", label: "Equity", current: "500.00", prior: "0", change: "500.00", percentChange: null, isTotal: false },
      { key: "breaches", label: "Buckets below the minimum", current: 2, prior: 1, change: 1, percentChange: 100, isTotal: false }
    ],
    notes: ["NAV is rebuilt from the posted ledger as of each date."],
    generatedAt: "2026-11-17T12:00:00Z",
    ...overrides
  } as Parameters<typeof normalizePeriodComparison>[0]);
}

describe("normalizePeriodComparison", () => {
  it("keeps amounts as decimal text and rejects malformed responses", () => {
    expect(comparison().lines[0]).toMatchObject({ current: "1450000", prior: "1100000", change: "350000", percentChange: "31.818181818", isTotal: true });
    expect(() => normalizePeriodComparison({ status: "Weird" } as never)).toThrow(/not a recognised/);
    expect(() => normalizePeriodComparison({ ...comparison(), lines: [{ key: "x", label: "X", current: "1,000", prior: 0, change: 0, percentChange: null }] } as never)).toThrow(/invalid current/);
  });
});

describe("PeriodComparisonPanel", () => {
  it("shows both windows, flags material variances, keeps counts whole and lists the notes", () => {
    renderWithRouter(<PeriodComparisonPanel title="NAV comparison" workbenchId="test-comparison" state={{ loading: false, error: null, result: comparison() }}
      policy={defaultMaterialityPolicyForClass("Portfolio")} currentLabel="Now" countKeys={["breaches"]} />);
    expect(screen.getByText(/Current: As of Nov 17, 2026\. Prior: As of Oct 31, 2026\./)).toBeInTheDocument();
    const table = screen.getByRole("table", { name: "NAV comparison" });
    const total = within(table).getByRole("row", { name: /Total NAV/ });
    expect(total).toHaveTextContent("1,450,000.00");
    expect(total).toHaveTextContent("31.82%");
    expect(total.querySelector("[data-variance-material]")).not.toBeNull();
    expect(within(table).getByRole("row", { name: /Equity/ })).toHaveTextContent("Not comparable · prior zero");
    const breaches = within(table).getByRole("row", { name: /Buckets below/ });
    expect(breaches).toHaveTextContent("2");
    expect(breaches).not.toHaveTextContent("2.00");
    expect(breaches.querySelector("[data-variance-material]")).toBeNull();
    expect(screen.getByText("NAV is rebuilt from the posted ledger as of each date.")).toBeInTheDocument();
  });

  it("says why when there is nothing to compare, and offers a retry on failure", async () => {
    const { rerender } = renderWithRouter(<PeriodComparisonPanel title="NAV comparison" workbenchId="t" state={{ loading: false, error: null, result: comparison({ status: "Unavailable", reason: "The ledger has no NAV components as of Dec 31, 2025.", lines: [] }) }} />);
    expect(screen.getByText("The ledger has no NAV components as of Dec 31, 2025.")).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
    const retry = vi.fn();
    rerender(<PeriodComparisonPanel title="NAV comparison" workbenchId="t" state={{ loading: false, error: "Server unavailable", result: null }} onRetry={retry} />);
    await userEvent.setup().click(screen.getByRole("button", { name: "Retry comparison" }));
    expect(retry).toHaveBeenCalledOnce();
  });
});

describe("prior-period surfaces", () => {
  it("NAV loads for the reporting fund and the chosen prior date", async () => {
    const service = vi.fn(async () => comparison());
    renderWithRouter(<NavComparisonScreen defaultFundProfileId="fund-alpha" service={service} />, { initialEntries: ["/portfolio/nav?baselineAsOf=2026-09-30"] });
    expect(await screen.findByRole("table", { name: "NAV comparison" })).toBeInTheDocument();
    expect(service).toHaveBeenCalledWith({ fundProfileId: "fund-alpha", baselineAsOf: "2026-09-30T23:59:59Z" }, expect.objectContaining({ signal: expect.any(AbortSignal) }));
  });

  it("NAV asks for a fund profile when none is known", () => {
    const service = vi.fn();
    renderWithRouter(<NavComparisonScreen service={service} />, { initialEntries: ["/portfolio/nav"] });
    expect(screen.getByText("Choose a fund profile to compare its NAV with a prior date.")).toBeInTheDocument();
    expect(service).not.toHaveBeenCalled();
  });

  it("the cash ladder comparison loads only when asked for", async () => {
    const service = vi.fn(async () => comparison({ subject: "Portfolio cash ladder" }));
    renderWithRouter(<CashLadderPeriodComparison scenario="base" minimumCash={250} fundAccountId="acct-1" service={service} />);
    expect(service).not.toHaveBeenCalled();
    await userEvent.setup().click(screen.getByRole("button", { name: "Vs end of last month" }));
    expect(await screen.findByRole("table", { name: "Cash ladder vs end of last month" })).toBeInTheDocument();
    expect(service).toHaveBeenCalledWith({ scenario: "base", minimumCash: 250, fundAccountId: "acct-1" }, expect.anything());
  });

  it("attribution compares the selected run's month with the month before", async () => {
    const service = vi.fn(async () => comparison({ subject: "Period Strategy" }));
    const { rerender } = renderWithRouter(<RunAttributionPeriodComparison runId={null} service={service} />);
    expect(service).not.toHaveBeenCalled();
    rerender(<RunAttributionPeriodComparison runId="run-7" service={service} />);
    expect(await screen.findByRole("table", { name: "P&L by symbol vs prior month" })).toBeInTheDocument();
    expect(service).toHaveBeenCalledWith("run-7", null, expect.anything());
  });
});
