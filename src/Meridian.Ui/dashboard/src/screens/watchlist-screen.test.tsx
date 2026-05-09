import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
<<<<<<< Updated upstream
import { screen, waitFor, within } from "@testing-library/react";

import { WatchlistScreen } from "@/screens/watchlist-screen";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import * as api from "@/lib/api";
import type { QuotesSnapshotResponse, SymbolRecord, SymbolStatistics } from "@/types";

const symbols: SymbolRecord[] = [
  {
    symbol: "AAPL",
    status: "Active",
    provider: "alpaca",
    lastEventAt: "2026-05-08T15:00:00.000Z",
    eventCount: 1234,
    hasHistoricalData: true
  },
  {
    symbol: "MSFT",
    status: "Monitored",
    provider: "alpaca",
    lastEventAt: "2026-05-08T14:59:00.000Z",
    eventCount: 845,
    hasHistoricalData: false
=======
import { cleanup, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { WatchlistScreen } from "@/screens/watchlist-screen";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import * as api from "@/lib/api";
import type { SymbolRecord, SymbolStatistics } from "@/types";

const symbols: SymbolRecord[] = [
  {
    symbol: "MSFT",
    status: "Monitored",
    provider: null,
    lastEventAt: null,
    eventCount: 0,
    hasHistoricalData: false
  },
  {
    symbol: "AAPL",
    status: "Active",
    provider: "Polygon",
    lastEventAt: "2026-05-09T01:00:00.000Z",
    eventCount: 1200,
    hasHistoricalData: true
>>>>>>> Stashed changes
  }
];

const stats: SymbolStatistics = {
  totalSymbols: 2,
  monitoredSymbols: 1,
  archivedSymbols: 0,
  symbolsWithErrors: 0,
<<<<<<< Updated upstream
  totalEventsLast24h: 2079
};

const snapshot: QuotesSnapshotResponse = {
  timestamp: "2026-05-08T15:00:01.000Z",
  count: 2,
  quotes: [
    {
      symbol: "AAPL",
      timestamp: "2026-05-08T15:00:00.000Z",
      bidPrice: 188.05,
      bidSize: 200,
      askPrice: 188.07,
      askSize: 150,
      midPrice: 188.06,
      spread: 0.02,
      lastPrice: 188.06,
      lastSize: 100,
      lastTradeTimestamp: "2026-05-08T14:59:59.000Z",
      sequenceNumber: 42,
      streamId: "s1",
      venue: "NASDAQ"
    },
    {
      symbol: "MSFT",
      timestamp: "2026-05-08T15:00:00.000Z",
      bidPrice: 412.10,
      bidSize: 300,
      askPrice: 412.14,
      askSize: 250,
      midPrice: 412.12,
      spread: 0.04,
      lastPrice: 412.13,
      lastSize: 50,
      lastTradeTimestamp: "2026-05-08T14:59:58.000Z",
      sequenceNumber: 99,
      streamId: "s1",
      venue: "NASDAQ"
    }
  ]
};

describe("WatchlistScreen live prices", () => {
  beforeEach(() => {
    vi.spyOn(api, "getSymbols").mockResolvedValue(symbols);
    vi.spyOn(api, "getSymbolsStatistics").mockResolvedValue(stats);
    vi.spyOn(api, "getLiveQuotesSnapshot").mockResolvedValue(snapshot);
=======
  totalEventsLast24h: 1200
};

describe("WatchlistScreen", () => {
  beforeEach(() => {
    vi.spyOn(api, "getSymbols").mockResolvedValue(symbols);
    vi.spyOn(api, "getSymbolsStatistics").mockResolvedValue(stats);
    vi.spyOn(api, "addSymbol").mockResolvedValue({ success: true, symbol: "SPY" });
    vi.spyOn(api, "bulkAddSymbols").mockResolvedValue({ added: 2, skipped: 0, errors: [] });
    vi.spyOn(api, "removeSymbol").mockResolvedValue({ success: true, symbol: "MSFT" });
>>>>>>> Stashed changes
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

<<<<<<< Updated upstream
  it("renders bid, ask, last and spread for each subscribed symbol", async () => {
    renderWithRouter(<WatchlistScreen />);

    await waitForAsyncEffects();

    const aaplRow = await screen.findByRole("row", { name: /AAPL/i });
    const msftRow = await screen.findByRole("row", { name: /MSFT/i });

    await waitFor(() => {
      expect(within(aaplRow).getByText(/188\.05/)).toBeInTheDocument();
      expect(within(aaplRow).getByText(/188\.07/)).toBeInTheDocument();
      expect(within(aaplRow).getByText(/188\.06/)).toBeInTheDocument();
      expect(within(msftRow).getByText(/412\.10/)).toBeInTheDocument();
      expect(within(msftRow).getByText(/412\.14/)).toBeInTheDocument();
    });
  });

  it("requests live quotes only for the subscribed symbols", async () => {
    renderWithRouter(<WatchlistScreen />);

    await waitForAsyncEffects();

    await waitFor(() => {
      expect(api.getLiveQuotesSnapshot).toHaveBeenCalled();
    });
    const args = (api.getLiveQuotesSnapshot as unknown as ReturnType<typeof vi.fn>).mock.calls[0]?.[0];
    expect(args).toEqual(["AAPL", "MSFT"]);
  });

  it("surfaces an inline warning when the live-quote feed errors", async () => {
    (api.getLiveQuotesSnapshot as unknown as ReturnType<typeof vi.fn>).mockRejectedValueOnce(
      new Error("collector offline")
    );

    renderWithRouter(<WatchlistScreen />);

    await waitForAsyncEffects();

    await waitFor(() => {
      expect(screen.getByText(/collector offline/i)).toBeInTheDocument();
    });
=======
  it("renders sorted rows through the dense table with derived labels", async () => {
    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });

    const table = await screen.findByRole("table", { name: /subscribed symbol watchlist/i });
    const rows = within(table).getAllByRole("row");

    expect(rows[1]).toHaveAccessibleName(/AAPL. Status Active/i);
    expect(rows[2]).toHaveAccessibleName(/MSFT. Status Monitored/i);
    expect(screen.getByRole("group", { name: "Total: 2" })).toBeInTheDocument();
    expect(screen.getByRole("toolbar", { name: /symbol watchlist status/i })).toHaveTextContent("24h events");
    expect(screen.getByRole("link", { name: /View live quotes for AAPL/i })).toHaveAttribute("href", "/data/quotes?symbol=AAPL");
  });

  it("normalizes symbols before add and refreshes after success", async () => {
    const user = userEvent.setup();
    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });

    await screen.findByRole("table", { name: /subscribed symbol watchlist/i });
    await user.type(screen.getByLabelText("Add symbol"), " spy ");
    await user.click(screen.getByRole("button", { name: /Add SPY to watchlist/i }));

    await waitFor(() => expect(api.addSymbol).toHaveBeenCalledWith("SPY"));
    expect(api.getSymbols).toHaveBeenCalledTimes(2);
  });

  it("bulk adds pasted symbol lists through the bulk endpoint", async () => {
    vi.mocked(api.bulkAddSymbols).mockResolvedValueOnce({ added: 2, skipped: 1, errors: ["QQQ rejected"] });
    const user = userEvent.setup();
    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });

    await screen.findByRole("table", { name: /subscribed symbol watchlist/i });
    await user.type(screen.getByLabelText("Add symbol"), " spy, dia qqq spy ");
    await user.click(screen.getByRole("button", { name: /Add 3 symbols to watchlist: SPY, DIA, QQQ/i }));

    await waitFor(() => expect(api.bulkAddSymbols).toHaveBeenCalledWith(["SPY", "DIA", "QQQ"]));
    expect(api.addSymbol).not.toHaveBeenCalled();
    expect(await screen.findByRole("alert")).toHaveTextContent("Added 2 of 3 symbols; 1 skipped; QQQ rejected.");
    expect(api.getSymbols).toHaveBeenCalledTimes(2);
  });

  it("keeps the visible rows and reports remove failures", async () => {
    vi.mocked(api.removeSymbol).mockRejectedValueOnce(new Error("Symbol remove failed"));
    const user = userEvent.setup();
    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });

    await screen.findByRole("table", { name: /subscribed symbol watchlist/i });
    await user.click(screen.getByRole("button", { name: /Remove MSFT from watchlist/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Symbol remove failed");
    expect(screen.getByRole("row", { name: /MSFT. Status Monitored/i })).toBeInTheDocument();
    expect(api.getSymbols).toHaveBeenCalledTimes(1);
  });

  it("shows loading, empty, and initial error states", async () => {
    vi.mocked(api.getSymbols).mockReturnValue(new Promise(() => {}));
    const loadingRender = renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });
    expect(screen.getByRole("status")).toHaveTextContent("Loading symbols...");
    loadingRender.unmount();

    vi.mocked(api.getSymbols).mockResolvedValueOnce([]);
    const emptyRender = renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });
    expect(await screen.findByRole("table", { name: /subscribed symbol watchlist/i })).toHaveTextContent(/No symbols configured/i);
    emptyRender.unmount();
    cleanup();

    vi.mocked(api.getSymbols).mockRejectedValueOnce(new Error("Symbol API offline"));
    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });
    await waitForAsyncEffects();
    expect(await screen.findByRole("alert")).toHaveTextContent("Symbol API offline");
>>>>>>> Stashed changes
  });
});
