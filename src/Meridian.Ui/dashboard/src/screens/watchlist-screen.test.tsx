import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { WatchlistScreen } from "@/screens/watchlist-screen";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import * as api from "@/lib/api";
import type { QuotesSnapshotResponse, SymbolRecord, SymbolStatistics } from "@/types";

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
  }
];

const stats: SymbolStatistics = {
  totalSymbols: 2,
  monitoredSymbols: 1,
  archivedSymbols: 0,
  symbolsWithErrors: 0,
  totalEventsLast24h: 1200
};

const snapshot: QuotesSnapshotResponse = {
  timestamp: "2026-05-09T01:00:01.000Z",
  count: 2,
  quotes: [
    {
      symbol: "AAPL",
      timestamp: "2026-05-09T01:00:00.000Z",
      bidPrice: 188.05,
      bidSize: 200,
      askPrice: 188.07,
      askSize: 150,
      midPrice: 188.06,
      spread: 0.02,
      lastPrice: 188.06,
      lastSize: 100,
      lastTradeTimestamp: "2026-05-09T00:59:59.000Z",
      sequenceNumber: 42,
      streamId: "s1",
      venue: "NASDAQ"
    },
    {
      symbol: "MSFT",
      timestamp: "2026-05-09T01:00:00.000Z",
      bidPrice: 412.1,
      bidSize: 300,
      askPrice: 412.14,
      askSize: 250,
      midPrice: 412.12,
      spread: 0.04,
      lastPrice: 412.13,
      lastSize: 50,
      lastTradeTimestamp: "2026-05-09T00:59:58.000Z",
      sequenceNumber: 99,
      streamId: "s1",
      venue: "NASDAQ"
    }
  ]
};

describe("WatchlistScreen", () => {
  beforeEach(() => {
    vi.spyOn(api, "getSymbols").mockResolvedValue(symbols);
    vi.spyOn(api, "getSymbolsStatistics").mockResolvedValue(stats);
    vi.spyOn(api, "getLiveQuotesSnapshot").mockResolvedValue(snapshot);
    vi.spyOn(api, "addSymbol").mockResolvedValue({ success: true, symbol: "SPY" });
    vi.spyOn(api, "bulkAddSymbols").mockResolvedValue({ added: 2, skipped: 0, errors: [] });
    vi.spyOn(api, "removeSymbol").mockResolvedValue({ success: true, symbol: "MSFT" });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders sorted rows through the dense table with live quote labels", async () => {
    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });

    const table = await screen.findByRole("table", { name: /subscribed symbol watchlist/i });
    const rows = within(table).getAllByRole("row");

    await waitFor(() => expect(within(rows[1]).getByText("188.05 x 200")).toBeInTheDocument());
    expect(rows[1]).toHaveAccessibleName(/AAPL. Status Active/i);
    expect(rows[2]).toHaveAccessibleName(/MSFT. Status Monitored/i);
    expect(within(rows[1]).getByText("188.07 x 150")).toBeInTheDocument();
    expect(within(rows[2]).getByText("412.10 x 300")).toBeInTheDocument();
    expect(screen.getByRole("group", { name: /Total metric\. 2\. Status neutral/i })).toBeInTheDocument();
    expect(screen.getByRole("toolbar", { name: /symbol watchlist status/i })).toHaveTextContent("24h events");
    expect(within(rows[1]).getByRole("link", { name: /View live quotes for AAPL/i })).toHaveAttribute("href", "/data/quotes?symbol=AAPL");
  });

  it("requests live quotes only for the subscribed symbols", async () => {
    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });

    await waitForAsyncEffects();

    await waitFor(() => {
      expect(api.getLiveQuotesSnapshot).toHaveBeenCalled();
    });
    expect(vi.mocked(api.getLiveQuotesSnapshot).mock.calls[0]?.[0]).toEqual(["MSFT", "AAPL"]);
  });

  it("lets operators manually refresh live quotes", async () => {
    const user = userEvent.setup();
    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });

    await screen.findByText(/Live prices for 2 symbols/i);
    vi.mocked(api.getLiveQuotesSnapshot).mockClear();

    await user.click(screen.getByRole("button", { name: /Refresh live prices/i }));

    await waitFor(() => {
      expect(api.getLiveQuotesSnapshot).toHaveBeenCalledWith(["MSFT", "AAPL"]);
    });
  });

  it("surfaces an inline warning when the live-quote feed errors", async () => {
    vi.mocked(api.getLiveQuotesSnapshot).mockRejectedValueOnce(new Error("collector offline"));

    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });

    await waitForAsyncEffects();

    await waitFor(() => {
      expect(screen.getByRole("alert")).toHaveTextContent(/collector offline/i);
    });
    expect(screen.getByRole("link", { name: /Open provider setup from watchlist live-quotes/i })).toHaveAttribute(
      "href",
      "/settings#alpaca-provider-setup"
    );
  });

  it("warns when the live-quote snapshot only covers part of the watchlist", async () => {
    vi.mocked(api.getLiveQuotesSnapshot).mockResolvedValueOnce({
      ...snapshot,
      count: 1,
      quotes: [snapshot.quotes[0]]
    });

    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });

    expect(await screen.findByText(/Live prices for 1 of 2 symbols/i)).toBeInTheDocument();
    const table = screen.getByRole("table", { name: /subscribed symbol watchlist/i });
    expect(within(table).getByText("188.05 x 200")).toBeInTheDocument();
    expect(screen.getByRole("row", { name: /MSFT. Status Monitored/i })).toHaveTextContent("Never");
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
    expect(screen.getByRole("link", { name: /Open provider setup from watchlist bulk-add-partial/i })).toHaveAttribute(
      "href",
      "/settings#alpaca-provider-setup"
    );
    expect(api.getSymbols).toHaveBeenCalledTimes(2);
  });

  it("quick-adds a starter pack through the bulk endpoint", async () => {
    vi.mocked(api.bulkAddSymbols).mockResolvedValueOnce({ added: 4, skipped: 0, errors: [] });
    const user = userEvent.setup();
    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });

    await screen.findByRole("table", { name: /subscribed symbol watchlist/i });
    await user.click(screen.getByRole("button", { name: /Add US core starter pack: SPY, QQQ, AAPL, MSFT/i }));

    await waitFor(() => expect(api.bulkAddSymbols).toHaveBeenCalledWith(["SPY", "QQQ", "AAPL", "MSFT"]));
    expect(await screen.findByText("US core: added 4 of 4 symbols.")).toBeInTheDocument();
    expect(api.getSymbols).toHaveBeenCalledTimes(2);
  });

  it("keeps starter pack symbols visible when quick-add fails", async () => {
    vi.mocked(api.bulkAddSymbols).mockRejectedValueOnce(new Error("Provider offline"));
    const user = userEvent.setup();
    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });

    await screen.findByRole("table", { name: /subscribed symbol watchlist/i });
    await user.click(screen.getByRole("button", { name: /Add Risk pulse starter pack: TLT, GLD, USO, VIXY/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Provider offline");
    expect(screen.getByRole("link", { name: /Open provider setup from watchlist starter-pack-exception/i })).toHaveAttribute(
      "href",
      "/settings#alpaca-provider-setup"
    );
    expect(screen.getByLabelText("Add symbol")).toHaveValue("TLT, GLD, USO, VIXY");
    expect(api.getSymbols).toHaveBeenCalledTimes(1);
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

  it("selects watchlist rows with the shared dense-table keyboard command", async () => {
    const user = userEvent.setup();
    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });

    await screen.findByRole("table", { name: /subscribed symbol watchlist/i });
    const msftRow = screen.getByRole("row", { name: /Select MSFT watchlist row/i });

    msftRow.focus();
    await user.keyboard("{Enter}");

    await waitFor(() => expect(msftRow).toHaveAttribute("aria-selected", "true"));

    const aaplRow = screen.getByRole("row", { name: /Select AAPL watchlist row/i });
    aaplRow.focus();
    await user.keyboard(" ");

    await waitFor(() => expect(aaplRow).toHaveAttribute("aria-selected", "true"));
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
  });
});
