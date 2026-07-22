import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { MarketDataScreen } from "@/screens/market-data-screen";
import { renderWithRouter } from "@/test/render";

vi.mock("@/screens/live-quotes-screen", () => ({
  LiveQuotesScreen: () => <div>live-quotes-panel</div>
}));
vi.mock("@/screens/watchlist-screen", () => ({
  WatchlistScreen: () => <div>watchlist-panel</div>
}));
vi.mock("@/screens/price-alerts-screen", () => ({
  PriceAlertsScreen: () => <div>price-alerts-panel</div>
}));

describe("MarketDataScreen", () => {
  it("defaults to the live quotes view and mounts only that panel", () => {
    renderWithRouter(<MarketDataScreen />, { initialEntries: ["/data/quotes"] });

    expect(screen.getByRole("tab", { name: "Live quotes" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("live-quotes-panel")).toBeInTheDocument();
    expect(screen.queryByText("watchlist-panel")).not.toBeInTheDocument();
    expect(screen.queryByText("price-alerts-panel")).not.toBeInTheDocument();
  });

  it("mounts the watchlist view for ?view=watchlist deep links", () => {
    renderWithRouter(<MarketDataScreen />, { initialEntries: ["/data/quotes?view=watchlist"] });

    expect(screen.getByRole("tab", { name: "Watchlist" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("watchlist-panel")).toBeInTheDocument();
    expect(screen.queryByText("live-quotes-panel")).not.toBeInTheDocument();
  });

  it("mounts the alerts view for ?view=alerts deep links", () => {
    renderWithRouter(<MarketDataScreen />, { initialEntries: ["/data/quotes?view=alerts"] });

    expect(screen.getByRole("tab", { name: "Price alerts" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("price-alerts-panel")).toBeInTheDocument();
  });

  it("falls back to live quotes for unknown view values", () => {
    renderWithRouter(<MarketDataScreen />, { initialEntries: ["/data/quotes?view=bogus"] });

    expect(screen.getByRole("tab", { name: "Live quotes" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("live-quotes-panel")).toBeInTheDocument();
  });

  it("switches views from the tab strip while preserving other query scope", async () => {
    const user = userEvent.setup();
    renderWithRouter(<MarketDataScreen />, { initialEntries: ["/data/quotes?symbol=AAPL"] });

    await user.click(screen.getByRole("tab", { name: "Price alerts" }));

    expect(screen.getByRole("tab", { name: "Price alerts" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("price-alerts-panel")).toBeInTheDocument();
    expect(screen.queryByText("live-quotes-panel")).not.toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: "Live quotes" }));

    expect(screen.getByRole("tab", { name: "Live quotes" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("live-quotes-panel")).toBeInTheDocument();
  });
});
