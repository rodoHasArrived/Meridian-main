import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { renderWithRouter, TestMemoryRouter, waitForAsyncEffects } from "@/test/render";
import { PortfolioScreen } from "@/screens/portfolio-screen";
import * as api from "@/lib/api";
import type { TradingWorkspaceResponse } from "@/types";

function position(symbol: string, side: "Long" | "Short", current: boolean): TradingWorkspaceResponse["positions"][number] {
  return {
    symbol,
    side,
    quantity: "100",
    averagePrice: "188.10",
    markPrice: "189.00",
    dayPnl: "+$90",
    unrealizedPnl: "+$90",
    exposure: "$18,900",
    markFreshness: {
      symbol,
      status: current ? "Current" : "Stale",
      valuationDate: "2026-10-28",
      observedOn: "2026-10-27",
      ageDays: 1,
      policyVersion: "policy-1",
      blockReason: current ? null : "Recorded mark needs review."
    }
  };
}

const trading: TradingWorkspaceResponse = {
  metrics: [],
  positions: [
    position("MSFT", "Long", false),
    position("AAPL", "Short", true),
    position("MSFT", "Long", true),
    position("AAPL", "Long", false)
  ],
  openOrders: [],
  fills: [],
  risk: {
    state: "Healthy",
    summary: "Source risk posture.",
    netExposure: "$18,900",
    grossExposure: "$18,900",
    var95: "$900",
    maxDrawdown: "0%",
    buyingPowerUsed: "10%",
    activeGuardrails: []
  },
  brokerage: {
    provider: "Alpaca",
    account: "PA-DEMO",
    environment: "paper",
    connection: "Connected",
    lastHeartbeat: "1s ago",
    orderIngress: "healthy",
    fillFeed: "healthy",
    notes: ""
  }
};

function renderPortfolio(data = trading) {
  return renderWithRouter(<PortfolioScreen trading={data} strategy={null} accounting={null} />, { initialEntries: ["/portfolio"] });
}

function positionTable() {
  return screen.getByRole("treegrid", { name: "Open positions" });
}

function positionIds() {
  return Array.from(positionTable().querySelectorAll("tr[data-dense-row-id]")).map((row) => row.getAttribute("data-dense-row-id"));
}

describe("Portfolio Open positions controls", () => {
  beforeEach(() => {
    vi.spyOn(api, "getFinancialRecordExplorer").mockRejectedValue(new Error("Explorer not configured in this fixture."));
  });
  afterEach(() => vi.restoreAllMocks());

  it("combines symbol, side and server mark readiness filters and resets controls", async () => {
    const user = userEvent.setup();
    renderPortfolio();
    await waitForAsyncEffects();
    expect(screen.getByText("Showing 4 of 4 loaded positions")).toBeInTheDocument();
    await user.type(screen.getByRole("searchbox", { name: "Search positions by symbol" }), " mSf ");
    await user.selectOptions(screen.getByRole("combobox", { name: "Side" }), "Long");
    await user.selectOptions(screen.getByRole("combobox", { name: "Mark readiness" }), "current");
    expect(positionIds()).toEqual(["msft-long-2"]);
    expect(screen.getByText("Showing 1 of 4 loaded positions")).toBeInTheDocument();
    await user.selectOptions(screen.getByRole("combobox", { name: "Side" }), "Short");
    expect(screen.getByText("No positions match these controls.")).toBeInTheDocument();
    expect(screen.getByText("Showing 0 of 4 loaded positions")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Reset controls" }));
    expect(positionIds()).toEqual(["msft-long-0", "aapl-short-1", "msft-long-2", "aapl-long-3"]);
  });

  it("sorts supported columns in both directions while retaining row IDs and source-order ties", async () => {
    const user = userEvent.setup();
    renderPortfolio();
    await waitForAsyncEffects();
    await user.click(within(positionTable()).getByRole("button", { name: "Sort by Symbol" }));
    expect(positionIds()).toEqual(["aapl-short-1", "aapl-long-3", "msft-long-0", "msft-long-2"]);
    expect(within(positionTable()).getByRole("columnheader", { name: /Symbol/ })).toHaveAttribute("aria-sort", "ascending");
    await user.click(within(positionTable()).getByRole("button", { name: /Symbol sorted ascending/ }));
    expect(positionIds()).toEqual(["msft-long-0", "msft-long-2", "aapl-short-1", "aapl-long-3"]);
    expect(within(positionTable()).getByRole("columnheader", { name: /Symbol/ })).toHaveAttribute("aria-sort", "descending");
    await user.click(within(positionTable()).getByRole("button", { name: "Sort by Mark readiness" }));
    expect(positionIds()).toEqual(["aapl-short-1", "msft-long-2", "msft-long-0", "aapl-long-3"]);
    expect(within(positionTable()).queryByRole("button", { name: /Sort by.*P&L|Sort by Exposure/ })).not.toBeInTheDocument();
  });

  it("keeps selected holding evidence intact when filters hide it, then reveals the same row", async () => {
    const user = userEvent.setup();
    renderPortfolio();
    await waitForAsyncEffects();
    await user.click(within(positionTable()).getByRole("row", { name: "Inspect AAPL Short holding" }));
    const evidence = screen.getByRole("link", { name: "Open asset detail for AAPL" });
    const href = evidence.getAttribute("href");
    expect(positionTable().querySelector('[data-dense-row-id="aapl-short-1"]')).toHaveAttribute("aria-selected", "true");
    await user.type(screen.getByRole("searchbox", { name: "Search positions by symbol" }), "MSFT");
    expect(screen.getByText("Selected holding is outside the current results.")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "AAPL holding detail" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open asset detail for AAPL" })).toHaveAttribute("href", href);
    expect(positionIds()).toEqual(["msft-long-0", "msft-long-2"]);
    await user.click(screen.getByRole("button", { name: "Clear filters" }));
    expect(positionTable().querySelector('[data-dense-row-id="aapl-short-1"]')).toHaveAttribute("aria-selected", "true");
    expect(screen.queryByText("Selected holding is outside the current results.")).not.toBeInTheDocument();
  });

  it("distinguishes an empty source from a filter with no matches and keeps controls available", async () => {
    renderPortfolio({ ...trading, positions: [] });
    await waitForAsyncEffects();
    expect(screen.getByText("No open holdings")).toBeInTheDocument();
    expect(screen.queryByText("No positions match these controls.")).not.toBeInTheDocument();
    expect(screen.getByText("Showing 0 of 0 loaded positions")).toBeInTheDocument();
    expect(screen.getByRole("searchbox", { name: "Search positions by symbol" })).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: "Side" })).toBeInTheDocument();
  });

  it("retains controls and selection when refreshed source rows change", async () => {
    const user = userEvent.setup();
    const { rerender } = renderPortfolio();
    await waitForAsyncEffects();
    await user.type(screen.getByRole("searchbox", { name: "Search positions by symbol" }), "MSFT");
    await user.selectOptions(screen.getByRole("combobox", { name: "Mark readiness" }), "current");
    const refreshed = { ...trading, positions: [...trading.positions, position("MSFT", "Short", true)] };
    rerender(<TestMemoryRouter initialEntries={["/portfolio"]}><PortfolioScreen trading={refreshed} strategy={null} accounting={null} /></TestMemoryRouter>);
    expect(screen.getByRole("searchbox", { name: "Search positions by symbol" })).toHaveValue("MSFT");
    expect(screen.getByRole("combobox", { name: "Mark readiness" })).toHaveValue("current");
    expect(positionIds()).toEqual(["msft-long-2", "msft-short-4"]);
    expect(screen.getByText("Showing 2 of 5 loaded positions")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "MSFT holding detail" })).toBeInTheDocument();
  });

  it("has no basic accessibility violations for filtered and no-match position states", async () => {
    const user = userEvent.setup();
    const { container } = renderPortfolio();
    await waitForAsyncEffects();
    await user.type(screen.getByRole("searchbox", { name: "Search positions by symbol" }), "MSFT");
    expect((await axe(container)).violations).toEqual([]);
    await user.clear(screen.getByRole("searchbox", { name: "Search positions by symbol" }));
    await user.type(screen.getByRole("searchbox", { name: "Search positions by symbol" }), "missing");
    expect(screen.getByText("No positions match these controls.")).toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
  });
});
