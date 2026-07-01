import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApiError } from "@/lib/api-errors";
import { WatchlistScreen } from "@/screens/watchlist-screen";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import * as api from "@/lib/api";
import type { QuotesSnapshotResponse, SymbolRecord, SymbolStatistics } from "@/types";

vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  return {
    ...actual,
    getSymbols: vi.fn(),
    getSymbolsStatistics: vi.fn(),
    getLiveQuotesSnapshot: vi.fn(),
    addSymbol: vi.fn(),
    bulkAddSymbols: vi.fn(),
    removeSymbol: vi.fn()
  };
});

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
      venue: "NASDAQ",
      session: {
        sessionDate: "2026-05-08",
        open: 187.00,
        high: 188.50,
        low: 186.80,
        last: 188.06,
        volume: 1_250_000,
        vwap: 187.74,
        tradeCount: 4321,
        change: 1.06,
        changePercent: 0.5668,
        firstTradeAt: "2026-05-08T13:30:00.000Z",
        lastTradeAt: "2026-05-08T14:59:59.000Z"
      }
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
      venue: "NASDAQ",
      session: {
        sessionDate: "2026-05-08",
        open: 415.00,
        high: 415.30,
        low: 411.10,
        last: 412.13,
        volume: 850_000,
        vwap: 412.55,
        tradeCount: 3120,
        change: -2.87,
        changePercent: -0.6916,
        firstTradeAt: "2026-05-08T13:30:00.000Z",
        lastTradeAt: "2026-05-08T14:59:58.000Z"
      }
    }
  ]
};

const fullSuiteTimeout = { timeout: 5000 };

describe("WatchlistScreen", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.getSymbols).mockResolvedValue(symbols);
    vi.mocked(api.getSymbolsStatistics).mockResolvedValue(stats);
    vi.mocked(api.getLiveQuotesSnapshot).mockResolvedValue(snapshot);
    vi.mocked(api.addSymbol).mockResolvedValue({ success: true, symbol: "SPY" });
    vi.mocked(api.bulkAddSymbols).mockResolvedValue({ added: 2, skipped: 0, errors: [] });
    vi.mocked(api.removeSymbol).mockResolvedValue({ success: true, symbol: "MSFT" });
  });

  afterEach(() => {
    cleanup();
    vi.clearAllMocks();
  });

  it("renders sorted rows through the dense table with live quote labels", async () => {
    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });

    const table = await screen.findByRole("treegrid", { name: /subscribed symbol watchlist/i });
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

    await screen.findByText(/Live prices for 2 symbols/i, undefined, fullSuiteTimeout);
    vi.mocked(api.getLiveQuotesSnapshot).mockClear();

    await user.click(screen.getByRole("button", { name: /Refresh live prices/i }));

    await waitFor(() => {
      expect(api.getLiveQuotesSnapshot).toHaveBeenCalledWith(["MSFT", "AAPL"], expect.objectContaining({
        signal: expect.any(AbortSignal)
      }));
    });
  });

  it("renders the day change and percent change for each symbol", async () => {
    renderWithRouter(<WatchlistScreen />);

    await waitForAsyncEffects();

    const aaplRow = await screen.findByRole("row", { name: /AAPL/i });
    const msftRow = await screen.findByRole("row", { name: /MSFT/i });

    await waitFor(() => {
      expect(within(aaplRow).getByText(/\+1\.06/)).toBeInTheDocument();
      expect(within(aaplRow).getByText(/\+0\.57%/)).toBeInTheDocument();
      expect(within(msftRow).getByText(/-2\.87/)).toBeInTheDocument();
      expect(within(msftRow).getByText(/-0\.69%/)).toBeInTheDocument();
    });
  });

  it("surfaces an inline warning when the live-quote feed errors", async () => {
    vi.mocked(api.getLiveQuotesSnapshot).mockRejectedValueOnce(new ApiError({
      path: "/api/data/quotes-snapshot?symbols=MSFT,AAPL",
      status: 503,
      title: "Service unavailable",
      detail: "collector offline"
    }));

    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });

    await waitForAsyncEffects();

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(/collector offline/i);
    expect(within(alert).getByText("Meridian service returned 503. Open diagnostics for technical details.")).toBeInTheDocument();
    expect(within(alert).getByText("Service unavailable")).toBeInTheDocument();
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

    expect(await screen.findByText(/Live prices for 1 of 2 symbols/i, undefined, fullSuiteTimeout)).toBeInTheDocument();
    const table = screen.getByRole("treegrid", { name: /subscribed symbol watchlist/i });
    expect(within(table).getByText("188.05 x 200")).toBeInTheDocument();
    expect(screen.getByRole("row", { name: /MSFT. Status Monitored/i })).toHaveTextContent("No quote");
  });

  it("normalizes symbols before add and refreshes after success", async () => {
    const user = userEvent.setup();
    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });

    await screen.findByRole("treegrid", { name: /subscribed symbol watchlist/i });
    await user.type(screen.getByLabelText("Add symbol"), " spy ");
    await user.click(screen.getByRole("button", { name: /Add SPY to watchlist/i }));

    await waitFor(() => expect(api.addSymbol).toHaveBeenCalledWith("SPY"));
    expect(await screen.findByRole("link", { name: /Open live quotes for SPY from watchlist single-symbol-add/i })).toHaveAttribute(
      "href",
      "/data/quotes?symbol=SPY"
    );
    expect(api.getSymbols).toHaveBeenCalledTimes(2);
  });

  it("bulk adds pasted symbol lists through the bulk endpoint", async () => {
    vi.mocked(api.bulkAddSymbols).mockResolvedValueOnce({ added: 2, skipped: 1, errors: ["QQQ rejected"] });
    const user = userEvent.setup();
    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });

    await screen.findByRole("treegrid", { name: /subscribed symbol watchlist/i });
    await user.type(screen.getByLabelText("Add symbol"), " spy, dia qqq spy ");
    await user.click(screen.getByRole("button", { name: /Add 3 symbols to watchlist: SPY, DIA, QQQ/i }));

    await waitFor(() => expect(api.bulkAddSymbols).toHaveBeenCalledWith(["SPY", "DIA", "QQQ"]));
    expect(api.addSymbol).not.toHaveBeenCalled();
    expect(await screen.findByRole("alert")).toHaveTextContent("Added 2 of 3 symbols; 1 skipped; QQQ rejected.");
    expect(screen.getByRole("link", { name: /Open provider setup from watchlist bulk-add-partial/i })).toHaveAttribute(
      "href",
      "/settings#alpaca-provider-setup"
    );
    expect(screen.getByRole("link", { name: /Open live quotes for SPY from watchlist bulk-add/i })).toHaveAttribute(
      "href",
      "/data/quotes?symbol=SPY"
    );
    expect(api.getSymbols).toHaveBeenCalledTimes(2);
  });

  it("quick-adds a starter pack through the bulk endpoint", async () => {
    vi.mocked(api.bulkAddSymbols).mockResolvedValueOnce({ added: 4, skipped: 0, errors: [] });
    const user = userEvent.setup();
    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });

    await screen.findByRole("treegrid", { name: /subscribed symbol watchlist/i });
    await user.click(screen.getByRole("button", { name: /Add US core starter pack: SPY, QQQ, AAPL, MSFT/i }));

    await waitFor(() => expect(api.bulkAddSymbols).toHaveBeenCalledWith(["SPY", "QQQ", "AAPL", "MSFT"]));
    expect(await screen.findByText("US core: added 4 of 4 symbols.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Open live quotes for SPY from watchlist starter-pack/i })).toHaveAttribute(
      "href",
      "/data/quotes?symbol=SPY"
    );
    expect(api.getSymbols).toHaveBeenCalledTimes(2);
  });

  it("keeps starter pack symbols visible when quick-add fails", async () => {
    vi.mocked(api.bulkAddSymbols).mockRejectedValueOnce(new ApiError({
      path: "/api/symbols/bulk",
      status: 503,
      title: "Provider offline",
      detail: "Starter pack request could not reach the configured provider.",
      validationIssues: [
        {
          field: "provider",
          label: "provider",
          messages: ["Reconnect provider credentials before retrying the starter pack."]
        }
      ]
    }));
    const user = userEvent.setup();
    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });

    await screen.findByRole("treegrid", { name: /subscribed symbol watchlist/i });
    await user.click(screen.getByRole("button", { name: /Add Risk pulse starter pack: TLT, GLD, USO, VIXY/i }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Starter pack request could not reach the configured provider.");
    expect(within(alert).getByText("Meridian service returned 503. Open diagnostics for technical details.")).toBeInTheDocument();
    expect(within(alert).getByText("Provider offline")).toBeInTheDocument();
    expect(within(alert).getByText("provider: Reconnect provider credentials before retrying the starter pack.")).toBeInTheDocument();
    expect(screen.getByLabelText("Add symbol")).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByLabelText("Add symbol")).toHaveAttribute("aria-errormessage", "add-symbol-feedback");
    expect(screen.getByLabelText("Add symbol")).toHaveAttribute(
      "aria-describedby",
      "add-symbol-feedback add-symbol-help"
    );
    expect(screen.getByRole("link", { name: /Open provider setup from watchlist starter-pack-exception/i })).toHaveAttribute(
      "href",
      "/settings#alpaca-provider-setup"
    );
    expect(screen.getByLabelText("Add symbol")).toHaveValue("TLT, GLD, USO, VIXY");
    expect(api.getSymbols).toHaveBeenCalledTimes(1);
  });

  it("keeps the visible rows and reports remove failures", async () => {
    vi.mocked(api.removeSymbol).mockRejectedValueOnce(new ApiError({
      path: "/api/symbols/MSFT",
      status: 409,
      title: "Watchlist removal blocked",
      detail: "MSFT cannot be removed while reconciliation is still pending.",
      validationIssues: [
        {
          field: "symbol",
          label: "symbol",
          messages: ["Resolve the pending reconciliation break before removing this symbol."]
        }
      ]
    }));
    const user = userEvent.setup();
    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });

    await screen.findByRole("treegrid", { name: /subscribed symbol watchlist/i });
    await user.click(screen.getByRole("button", { name: /Remove MSFT from watchlist/i }));
    expect(api.removeSymbol).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: /Confirm remove MSFT from watchlist/i }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("MSFT cannot be removed while reconciliation is still pending.");
    expect(within(alert).getByText("Meridian service returned 409. Open diagnostics for technical details.")).toBeInTheDocument();
    expect(within(alert).getByText("Watchlist removal blocked")).toBeInTheDocument();
    expect(within(alert).getByText("symbol: Resolve the pending reconciliation break before removing this symbol.")).toBeInTheDocument();
    expect(screen.getByRole("row", { name: /MSFT. Status Monitored/i })).toBeInTheDocument();
    expect(api.getSymbols).toHaveBeenCalledTimes(1);
  });

  it("treats unsuccessful remove responses as failed mutations", async () => {
    vi.mocked(api.removeSymbol).mockResolvedValueOnce({ success: false, symbol: "MSFT" });
    const user = userEvent.setup();
    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });

    await screen.findByRole("treegrid", { name: /subscribed symbol watchlist/i });
    await user.click(screen.getByRole("button", { name: /Remove MSFT from watchlist/i }));
    await user.click(screen.getByRole("button", { name: /Confirm remove MSFT from watchlist/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Could not remove MSFT.");
    expect(screen.getByRole("row", { name: /MSFT. Status Monitored/i })).toBeInTheDocument();
    expect(api.getSymbols).toHaveBeenCalledTimes(1);
  });

  it("requires two clicks before removing a configured watchlist symbol", async () => {
    const user = userEvent.setup();
    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });

    await screen.findByRole("treegrid", { name: /subscribed symbol watchlist/i });
    await user.click(screen.getByRole("button", { name: /Remove MSFT from watchlist/i }));

    expect(api.removeSymbol).not.toHaveBeenCalled();
    const confirmButton = screen.getByRole("button", { name: /Confirm remove MSFT from watchlist/i });
    expect(confirmButton).toBeInTheDocument();
    expect(confirmButton).toHaveAttribute("aria-describedby", "watchlist-remove-msft-status");
    expect(screen.getByText("Pending confirmation")).toHaveAttribute("role", "status");
    expect(screen.getByRole("row", { name: /MSFT. Status Monitored/i })).toHaveAccessibleName(/Remove confirmation pending/i);

    await user.click(confirmButton);

    await waitFor(() => expect(api.removeSymbol).toHaveBeenCalledWith("MSFT"));
  });

  it("selects watchlist rows with the shared dense-table keyboard command", async () => {
    const user = userEvent.setup();
    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });

    await screen.findByRole("treegrid", { name: /subscribed symbol watchlist/i });
    const msftRow = screen.getByRole("row", { name: /Select MSFT watchlist row/i });
    const detail = screen.getByRole("complementary", { name: /selected watchlist symbol detail/i });
    expect(msftRow).toHaveAttribute("aria-controls", "watchlist-selected-symbol-detail");
    expect(detail).toHaveAttribute("id", "watchlist-selected-symbol-detail");
    expect(msftRow).toHaveAttribute("aria-expanded", "false");

    msftRow.focus();
    await user.keyboard("{Enter}");

    await waitFor(() => expect(msftRow).toHaveAttribute("aria-selected", "true"));
    expect(msftRow).toHaveAttribute("aria-expanded", "true");
    expect(within(msftRow).getByRole("button", { name: /Inspect MSFT watchlist detail/i })).toHaveAttribute("aria-expanded", "true");

    const aaplRow = screen.getByRole("row", { name: /Select AAPL watchlist row/i });
    aaplRow.focus();
    await user.keyboard(" ");

    await waitFor(() => expect(aaplRow).toHaveAttribute("aria-selected", "true"));
    expect(aaplRow).toHaveAttribute("aria-expanded", "true");
    expect(msftRow).toHaveAttribute("aria-expanded", "false");
  });

  it("shows loading, empty, and retryable initial error states", async () => {
    vi.mocked(api.getSymbols).mockReturnValue(new Promise(() => {}));
    const loadingRender = renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });
    expect(screen.getByRole("status")).toHaveTextContent("Loading symbols…");
    loadingRender.unmount();

    vi.mocked(api.getSymbols).mockResolvedValueOnce([]);
    const emptyRender = renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });
    expect(await screen.findByRole("treegrid", { name: /subscribed symbol watchlist/i })).toHaveTextContent(/No symbols configured/i);
    emptyRender.unmount();
    cleanup();
    vi.mocked(api.getSymbols).mockClear();

    vi.mocked(api.getSymbols).mockRejectedValueOnce(new Error("Symbol API offline"));
    vi.mocked(api.getSymbols).mockResolvedValueOnce(symbols);
    renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });
    await waitForAsyncEffects();
    expect(await screen.findByRole("alert")).toHaveTextContent("Symbol API offline");

    await userEvent.setup().click(screen.getByRole("button", { name: /Retry symbol watchlist load/i }));

    expect(await screen.findByRole("treegrid", { name: /subscribed symbol watchlist/i })).toBeInTheDocument();
    expect(api.getSymbols).toHaveBeenCalledTimes(2);
  });
});
