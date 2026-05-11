import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  buildBulkAddFeedback,
  buildProviderSetupHandoff,
  buildQuoteRefreshCommand,
  buildQuoteStatus,
  buildStarterPackCommands,
  buildStarterPackFeedback,
  buildWatchlistSelectedDetail,
  buildWatchlistRows,
  buildWatchlistStats,
  formatRelative,
  parseWatchlistSymbols,
  useWatchlistScreenViewModel,
  validatePendingSymbol
} from "@/screens/watchlist-screen.view-model";
import type { QuotesSnapshotItem, SymbolRecord, SymbolStatistics } from "@/types";
import type { WatchlistApi } from "@/screens/watchlist-screen.view-model";

const symbols: SymbolRecord[] = [
  {
    symbol: "MSFT",
    status: "Archived",
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
  },
  {
    symbol: "TSLA",
    status: "Error",
    provider: "Databento",
    lastEventAt: "not-a-date",
    eventCount: 4,
    hasHistoricalData: false
  }
];

const quote: QuotesSnapshotItem = {
  symbol: "AAPL",
  timestamp: "2026-05-09T00:59:50.000Z",
  bidPrice: 188.05,
  bidSize: 200,
  askPrice: 188.07,
  askSize: 150,
  midPrice: 188.06,
  spread: 0.02,
  lastPrice: 188.5,
  lastSize: 100,
  lastTradeTimestamp: "2026-05-09T00:59:49.000Z",
  sequenceNumber: 42,
  streamId: "s1",
  venue: "NASDAQ"
};

describe("watchlist-screen view model", () => {
  it("sorts rows and derives table-facing symbol state", () => {
    const rows = buildWatchlistRows(symbols, { AAPL: true });

    expect(rows.map((row) => row.symbol)).toEqual(["AAPL", "MSFT", "TSLA"]);
    expect(rows[0]).toMatchObject({
      symbol: "AAPL",
      statusVariant: "success",
      providerLabel: "Polygon",
      eventCountLabel: "1,200",
      historyLabel: "Available",
      quoteHref: "/data/quotes?symbol=AAPL",
      inspectLabel: "Inspect",
      inspectAriaLabel: "Inspect AAPL watchlist detail",
      rowSelectAriaLabel: "Select AAPL watchlist row. AAPL. Status Active.",
      removeLabel: "Removing...",
      removeDisabledReason: "AAPL removal is already running."
    });
    expect(rows[1]).toMatchObject({
      statusVariant: "outline",
      providerLabel: "No provider",
      historyLabel: "Missing"
    });
    expect(rows[2].ariaLabel).toContain("Status Error");
  });

  it("derives live quote coverage, stale age, and price movement state", () => {
    const now = Date.parse("2026-05-09T01:00:00.000Z");
    const rows = buildWatchlistRows(symbols, {}, { AAPL: quote }, { AAPL: 187 }, now);

    expect(rows[0]).toMatchObject({
      symbol: "AAPL",
      bidLabel: "188.05 x 200",
      askLabel: "188.07 x 150",
      lastPriceLabel: "188.50",
      spreadLabel: "0.02 (1.1 bps)",
      quoteAgeLabel: "10s ago",
      hasQuote: true,
      quoteStale: false,
      lastTone: "success"
    });
    expect(rows[1]).toMatchObject({
      symbol: "MSFT",
      bidLabel: "-",
      askLabel: "-",
      hasQuote: false,
      quoteAgeLabel: "Never"
    });
  });

  it("derives stat cards with danger tone for symbol errors", () => {
    const stats: SymbolStatistics = {
      totalSymbols: 3,
      monitoredSymbols: 2,
      archivedSymbols: 1,
      symbolsWithErrors: 1,
      totalEventsLast24h: 1200
    };

    expect(buildWatchlistStats(stats)).toEqual([
      { id: "total", label: "Total", value: "3", delta: "", tone: "default" },
      { id: "monitored", label: "Monitored", value: "2", delta: "", tone: "default" },
      { id: "archived", label: "Archived", value: "1", delta: "", tone: "default" },
      { id: "errors", label: "Errors", value: "1", delta: "", tone: "danger" }
    ]);
  });

  it("normalizes relative timestamps and symbol validation", () => {
    expect(formatRelative(null)).toBe("Never");
    expect(formatRelative("bad")).toBe("Never");
    expect(formatRelative("2026-05-09T00:59:30.000Z", Date.parse("2026-05-09T01:00:00.000Z"))).toBe("30s ago");
    expect(formatRelative("2026-05-09T00:40:00.000Z", Date.parse("2026-05-09T01:00:00.000Z"))).toBe("20m ago");
    expect(validatePendingSymbol(" ")).toBe("Enter at least one symbol before adding it.");
    expect(validatePendingSymbol(" aapl ")).toBeNull();
    expect(parseWatchlistSymbols(" aapl, MSFT spy AAPL ")).toEqual(["AAPL", "MSFT", "SPY"]);
  });

  it("summarizes bulk-add outcomes for operator feedback", () => {
    expect(buildBulkAddFeedback({ added: 3, skipped: 0, errors: [] }, 3)).toEqual({
      tone: "success",
      message: "Added 3 of 3 symbols."
    });

    expect(buildBulkAddFeedback({ added: 2, skipped: 1, errors: ["QQQ rejected"] }, 4)).toEqual({
      tone: "warning",
      message: "Added 2 of 4 symbols; 1 skipped; QQQ rejected.",
      providerSetupHandoff: buildProviderSetupHandoff("bulk-add-partial")
    });

    expect(buildBulkAddFeedback({ added: 0, skipped: 0, errors: ["Provider offline"] }, 2)).toEqual({
      tone: "danger",
      message: "Added 0 of 2 symbols; Provider offline.",
      providerSetupHandoff: buildProviderSetupHandoff("bulk-add-errors")
    });
  });

  it("builds a selected-symbol detail panel from the active watchlist row", () => {
    const now = Date.parse("2026-05-09T01:00:00.000Z");
    const [row] = buildWatchlistRows(symbols, {}, { AAPL: quote }, { AAPL: 187 }, now);
    const detail = buildWatchlistSelectedDetail(row);

    expect(detail).toMatchObject({
      symbol: "AAPL",
      title: "AAPL",
      statusLabel: "Active",
      statusVariant: "success",
      quoteActionHref: "/data/quotes?symbol=AAPL",
      regionLabel: "AAPL watchlist detail"
    });
    expect(detail?.description).toContain("ready for operator review");
    expect(detail?.fields).toEqual(expect.arrayContaining([
      { label: "Bid x size", value: "188.05 x 200", tone: "default" },
      { label: "Last", value: "188.50", tone: "success" },
      { label: "Quote age", value: "10s ago", tone: "success" },
      { label: "History", value: "Available", tone: "success" }
    ]));
  });

  it("marks selected-symbol detail as recoverable when live quotes are missing", () => {
    const rows = buildWatchlistRows(symbols);
    const detail = buildWatchlistSelectedDetail(rows[1]);

    expect(detail?.symbol).toBe("MSFT");
    expect(detail?.description).toContain("No live quote has been returned");
    expect(detail?.fields).toEqual(expect.arrayContaining([
      { label: "Bid x size", value: "-", tone: "muted" },
      { label: "Provider", value: "No provider", tone: "warning" },
      { label: "History", value: "Missing", tone: "warning" }
    ]));
  });

  it("builds starter pack commands and pack-specific feedback", () => {
    expect(buildStarterPackCommands(false, null)[0]).toEqual({
      id: "us-core",
      label: "US core",
      symbols: ["SPY", "QQQ", "AAPL", "MSFT"],
      symbolsLabel: "SPY, QQQ, AAPL, MSFT",
      ariaLabel: "Add US core starter pack: SPY, QQQ, AAPL, MSFT",
      disabled: false,
      disabledReason: null,
      busy: false
    });

    expect(buildStarterPackCommands(true, "us-core")[0]).toMatchObject({
      ariaLabel: "Adding US core starter pack",
      disabled: true,
      disabledReason: "Wait for the current symbol add request to finish.",
      busy: true
    });

    expect(buildStarterPackFeedback("US core", { added: 3, skipped: 1, errors: ["MSFT already exists"] }, 4)).toEqual({
      tone: "warning",
      message: "US core: added 3 of 4 symbols; 1 skipped; MSFT already exists.",
      providerSetupHandoff: buildProviderSetupHandoff("starter-pack-partial")
    });
  });

  it("builds a provider setup handoff for watchlist recovery paths", () => {
    expect(buildProviderSetupHandoff("live-quotes")).toEqual({
      href: "/settings#alpaca-provider-setup",
      label: "Fix provider setup",
      ariaLabel: "Open provider setup from watchlist live-quotes",
      detail: "Review provider credentials and connection status in Settings."
    });
  });

  it("warns when live quote coverage is partial or stale", () => {
    const now = Date.parse("2026-05-09T01:00:00.000Z");

    expect(buildQuoteStatus({
      listState: "ready",
      rowCount: 3,
      quoteCount: 1,
      staleCount: 1,
      quoteError: null,
      quoteFetchedAt: now,
      now
    })).toEqual({
      tone: "warning",
      label: "Live prices for 1 of 3 symbols; 1 stale; updated 0s ago."
    });

    expect(buildQuoteStatus({
      listState: "ready",
      rowCount: 2,
      quoteCount: 2,
      staleCount: 0,
      quoteError: null,
      quoteFetchedAt: now,
      now
    })).toEqual({
      tone: "default",
      label: "Live prices for 2 symbols; updated 0s ago."
    });
  });

  it("derives the live-price refresh command state", () => {
    expect(buildQuoteRefreshCommand("ready", 2, false)).toEqual({
      label: "Refresh prices",
      ariaLabel: "Refresh live prices",
      disabled: false,
      disabledReason: null,
      busy: false
    });

    expect(buildQuoteRefreshCommand("empty", 0, false)).toMatchObject({
      disabled: true,
      disabledReason: "Add a symbol before refreshing live prices."
    });

    expect(buildQuoteRefreshCommand("ready", 2, true)).toEqual({
      label: "Refreshing prices...",
      ariaLabel: "Refreshing live prices",
      disabled: true,
      disabledReason: "Live price refresh is already running.",
      busy: true
    });
  });

  it("keeps the latest symbol refresh when an earlier load finishes later", async () => {
    const slowSymbols = deferred<SymbolRecord[]>();
    const api = createWatchlistApi();
    api.getSymbols = vi.fn()
      .mockReturnValueOnce(slowSymbols.promise)
      .mockResolvedValueOnce([{ ...symbols[0], symbol: "MSFT" }]);

    const { result } = renderHook(() => useWatchlistScreenViewModel(api));
    await waitFor(() => expect(api.getSymbols).toHaveBeenCalledTimes(1));

    await act(async () => {
      await result.current.refresh();
    });
    expect(result.current.rows.map((row) => row.symbol)).toEqual(["MSFT"]);

    await act(async () => {
      slowSymbols.resolve([{ ...symbols[1], symbol: "AAPL" }]);
      await slowSymbols.promise;
    });

    expect(result.current.rows.map((row) => row.symbol)).toEqual(["MSFT"]);
  });

  it("ignores stale quote snapshots after the subscribed symbol set changes", async () => {
    const slowQuote = deferred<{ quotes: QuotesSnapshotItem[] }>();
    const api = createWatchlistApi();
    api.getSymbols = vi.fn()
      .mockResolvedValueOnce([{ ...symbols[1], symbol: "AAPL" }])
      .mockResolvedValueOnce([{ ...symbols[0], symbol: "MSFT" }]);
    api.getLiveQuotesSnapshot = vi.fn().mockReturnValue(slowQuote.promise);

    const { result } = renderHook(() => useWatchlistScreenViewModel(api));
    await waitFor(() => expect(api.getLiveQuotesSnapshot).toHaveBeenCalledWith(["AAPL"], expect.objectContaining({
      signal: expect.any(AbortSignal)
    })));

    await act(async () => {
      await result.current.refresh();
    });
    expect(result.current.rows.map((row) => row.symbol)).toEqual(["MSFT"]);

    await act(async () => {
      slowQuote.resolve({ quotes: [quote] });
      await slowQuote.promise;
    });

    expect(result.current.rows).toEqual([
      expect.objectContaining({ symbol: "MSFT", hasQuote: false, quoteAgeLabel: "Never" })
    ]);
  });

  it("queues the latest quote snapshot when symbols change during an in-flight refresh", async () => {
    const slowQuote = deferred<{ quotes: QuotesSnapshotItem[] }>();
    const latestQuote = deferred<{ quotes: QuotesSnapshotItem[] }>();
    const api = createWatchlistApi();
    api.getSymbols = vi.fn()
      .mockResolvedValueOnce([{ ...symbols[1], symbol: "AAPL" }])
      .mockResolvedValueOnce([{ ...symbols[0], symbol: "MSFT" }]);
    api.getLiveQuotesSnapshot = vi.fn()
      .mockReturnValueOnce(slowQuote.promise)
      .mockReturnValueOnce(latestQuote.promise);

    const { result } = renderHook(() => useWatchlistScreenViewModel(api));
    await waitFor(() => expect(api.getLiveQuotesSnapshot).toHaveBeenCalledWith(["AAPL"], expect.objectContaining({
      signal: expect.any(AbortSignal)
    })));

    await act(async () => {
      await result.current.refresh();
    });
    expect(result.current.rows.map((row) => row.symbol)).toEqual(["MSFT"]);

    await act(async () => {
      slowQuote.resolve({ quotes: [quote] });
      await slowQuote.promise;
    });

    await waitFor(() => expect(api.getLiveQuotesSnapshot).toHaveBeenCalledWith(["MSFT"], expect.objectContaining({
      signal: expect.any(AbortSignal)
    })));

    await act(async () => {
      latestQuote.resolve({ quotes: [{ ...quote, symbol: "MSFT" }] });
      await latestQuote.promise;
    });

    await waitFor(() => expect(result.current.rows[0]).toEqual(
      expect.objectContaining({ symbol: "MSFT", hasQuote: true })
    ));
  });

  it("keeps a selected watchlist row and falls back when the row disappears", async () => {
    const api = createWatchlistApi();
    api.getSymbols = vi.fn()
      .mockResolvedValueOnce(symbols)
      .mockResolvedValueOnce([{ ...symbols[0], symbol: "MSFT" }]);

    const { result } = renderHook(() => useWatchlistScreenViewModel(api));

    await waitFor(() => expect(result.current.selectedSymbol).toBe("AAPL"));
    act(() => result.current.selectSymbol("TSLA"));
    expect(result.current.selectedSymbol).toBe("TSLA");
    expect(result.current.selectedDetail?.title).toBe("TSLA");

    await act(async () => {
      await result.current.refresh();
    });

    await waitFor(() => expect(result.current.selectedSymbol).toBe("MSFT"));
    expect(result.current.selectedRowId).toBe("MSFT");
    expect(result.current.selectedDetail?.title).toBe("MSFT");
  });
});

function createWatchlistApi(): WatchlistApi {
  return {
    getSymbols: vi.fn().mockResolvedValue(symbols),
    getSymbolsStatistics: vi.fn().mockResolvedValue({
      totalSymbols: symbols.length,
      monitoredSymbols: 1,
      archivedSymbols: 1,
      symbolsWithErrors: 1,
      totalEventsLast24h: 1200
    }),
    getLiveQuotesSnapshot: vi.fn().mockResolvedValue({ quotes: [] }),
    addSymbol: vi.fn().mockResolvedValue({ success: true, symbol: "AAPL" }),
    bulkAddSymbols: vi.fn().mockResolvedValue({ added: 0, skipped: 0, errors: [] }),
    removeSymbol: vi.fn().mockResolvedValue({ success: true, symbol: "AAPL" })
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}
