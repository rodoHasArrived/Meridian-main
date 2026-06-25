import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ApiError, createApiErrorFromResponseBody } from "@/lib/api-errors";
import {
  buildBulkAddFeedback,
  buildListRetryCommand,
  buildLiveQuoteHandoff,
  buildProviderSetupHandoff,
  buildQuoteRefreshCommand,
  buildQuoteStatus,
  buildStaleFilterCommand,
  buildStarterPackCommands,
  buildStarterPackFeedback,
  buildWatchlistAddSymbolField,
  buildWatchlistSelectedDetail,
  buildWatchlistRows,
  buildWatchlistStats,
  formatRelative,
  parseWatchlistSymbols,
  sortAndFilterWatchlistRows,
  toggleWatchlistSort,
  useWatchlistScreenViewModel,
  validatePendingSymbol,
  WATCHLIST_EMPTY_VALUE,
  WATCHLIST_NO_QUOTE_LABEL
} from "@/screens/watchlist-screen.view-model";
import type { QuotesSnapshotItem, SymbolRecord, SymbolStatistics } from "@/types";
import type { WatchlistApi, WatchlistSortState } from "@/screens/watchlist-screen.view-model";

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
  venue: "NASDAQ",
  session: null
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
      removeLabel: "Removing…",
      removeButtonVariant: "outline",
      removeStatusId: "watchlist-remove-aapl-status",
      removeStatusLabel: "Removing",
      removeStatusTone: "danger",
      removeDisabledReason: "AAPL removal is already running."
    });
    expect(rows[1]).toMatchObject({
      statusVariant: "outline",
      providerLabel: "No provider",
      historyLabel: "Missing"
    });
    expect(rows[2].ariaLabel).toContain("Status Error");
  });

  it("arms a row-owned confirmation state before symbol removal", () => {
    const rows = buildWatchlistRows(symbols, {}, {}, {}, Date.parse("2026-05-09T01:00:00.000Z"), "MSFT");

    expect(rows[1]).toMatchObject({
      symbol: "MSFT",
      removeLabel: "Confirm remove",
      removeAriaLabel: "Confirm remove MSFT from watchlist. This stops watchlist tracking for this row.",
      removeButtonVariant: "destructive",
      removeStatusId: "watchlist-remove-msft-status",
      removeStatusLabel: "Pending confirmation",
      removeStatusTone: "warning",
      removeDisabledReason: null
    });
    expect(rows[1].ariaLabel).toContain("Remove confirmation pending.");
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
      bidLabel: WATCHLIST_EMPTY_VALUE,
      askLabel: WATCHLIST_EMPTY_VALUE,
      hasQuote: false,
      quoteAgeLabel: WATCHLIST_NO_QUOTE_LABEL
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
    expect(buildBulkAddFeedback({ added: 3, skipped: 0, errors: [] }, 3, ["SPY", "DIA", "QQQ"])).toEqual({
      tone: "success",
      message: "Added 3 of 3 symbols.",
      nextActionHandoff: buildLiveQuoteHandoff(["SPY", "DIA", "QQQ"], "bulk-add")
    });

    expect(buildBulkAddFeedback({ added: 2, skipped: 1, errors: ["QQQ rejected"] }, 4, ["SPY", "DIA", "QQQ"])).toEqual({
      tone: "warning",
      message: "Added 2 of 4 symbols; 1 skipped; QQQ rejected.",
      providerSetupHandoff: buildProviderSetupHandoff("bulk-add-partial"),
      nextActionHandoff: buildLiveQuoteHandoff(["SPY", "DIA", "QQQ"], "bulk-add")
    });

    expect(buildBulkAddFeedback({ added: 0, skipped: 0, errors: ["Provider offline"] }, 2, ["SPY", "DIA"])).toEqual({
      tone: "danger",
      message: "Added 0 of 2 symbols; Provider offline.",
      providerSetupHandoff: buildProviderSetupHandoff("bulk-add-errors")
    });
  });

  it("builds add-symbol field accessibility metadata from feedback state", () => {
    expect(buildWatchlistAddSymbolField(null, false)).toEqual({
      id: "add-symbol-input",
      label: "Add symbol",
      placeholder: "Add symbols (e.g. MSFT, SPY)",
      helperId: "add-symbol-help",
      helperText: "Paste one or more symbols separated by spaces or commas. Meridian normalizes them to uppercase.",
      feedbackId: "add-symbol-feedback",
      feedbackRole: "alert",
      describedBy: "add-symbol-help",
      invalid: false,
      errorMessageId: undefined,
      disabled: false
    });

    expect(buildWatchlistAddSymbolField({ tone: "danger", message: "Provider offline" }, true)).toMatchObject({
      describedBy: "add-symbol-feedback add-symbol-help",
      feedbackRole: "alert",
      invalid: true,
      errorMessageId: "add-symbol-feedback",
      disabled: true
    });

    expect(buildWatchlistAddSymbolField({ tone: "success", message: "Added 1 of 1 symbols." }, false)).toMatchObject({
      describedBy: "add-symbol-feedback add-symbol-help",
      feedbackRole: "status",
      invalid: false,
      errorMessageId: undefined,
      disabled: false
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
      { label: "Bid x size", value: WATCHLIST_EMPTY_VALUE, tone: "muted" },
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
      busyLabel: "Adding US core…",
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

    expect(buildStarterPackFeedback("US core", { added: 3, skipped: 1, errors: ["MSFT already exists"] }, 4, ["SPY", "QQQ", "AAPL", "MSFT"])).toEqual({
      tone: "warning",
      message: "US core: added 3 of 4 symbols; 1 skipped; MSFT already exists.",
      providerSetupHandoff: buildProviderSetupHandoff("starter-pack-partial"),
      nextActionHandoff: buildLiveQuoteHandoff(["SPY", "QQQ", "AAPL", "MSFT"], "starter-pack")
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

  it("builds a live quote handoff after successful watchlist additions", () => {
    expect(buildLiveQuoteHandoff(["brk/b"], "single-symbol-add")).toEqual({
      href: "/data/quotes?symbol=BRK%2FB",
      label: "Review live quote",
      ariaLabel: "Open live quotes for BRK/B from watchlist single-symbol-add",
      detail: "Review the BRK/B live quote, chart, and quick-trade ticket."
    });
    expect(buildLiveQuoteHandoff([], "bulk-add")).toBeUndefined();
  });

  it("derives the list retry command for recoverable symbol-load failures", () => {
    expect(buildListRetryCommand(false)).toEqual({
      label: "Retry watchlist",
      ariaLabel: "Retry symbol watchlist load",
      disabled: false,
      disabledReason: null,
      busy: false
    });

    expect(buildListRetryCommand(true)).toEqual({
      label: "Retrying…",
      ariaLabel: "Retrying symbol watchlist load",
      disabled: true,
      disabledReason: "Watchlist refresh is already running.",
      busy: true
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
      label: "Live prices for 1 of 3 symbols; 1 stale; updated 0s ago.",
      details: []
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
      label: "Live prices for 2 symbols; updated 0s ago.",
      details: []
    });
  });

  it("keeps structured quote-refresh diagnostics separate from the status summary", () => {
    expect(buildQuoteStatus({
      listState: "ready",
      rowCount: 2,
      quoteCount: 0,
      staleCount: 0,
      quoteError: {
        summary: "collector offline",
        details: [
          "Meridian service returned 503. Open diagnostics for technical details.",
          "Service unavailable"
        ]
      },
      quoteFetchedAt: null
    })).toEqual({
      tone: "danger",
      label: "Live prices unavailable: collector offline",
      details: [
        "Meridian service returned 503. Open diagnostics for technical details.",
        "Service unavailable"
      ]
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
      label: "Refreshing prices…",
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
      expect.objectContaining({ symbol: "MSFT", hasQuote: false, quoteAgeLabel: WATCHLIST_NO_QUOTE_LABEL })
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

  it("requires a confirmation pass before removing a watchlist symbol", async () => {
    const api = createWatchlistApi();
    const { result } = renderHook(() => useWatchlistScreenViewModel(api));

    await waitFor(() => expect(result.current.rows.length).toBeGreaterThan(0));

    await act(async () => {
      await result.current.removeSymbol("MSFT");
    });

    expect(api.removeSymbol).not.toHaveBeenCalled();
    expect(result.current.selectedSymbol).toBe("MSFT");
    expect(result.current.rows.find((row) => row.symbol === "MSFT")).toMatchObject({
      removeLabel: "Confirm remove",
      removeAriaLabel: "Confirm remove MSFT from watchlist. This stops watchlist tracking for this row."
    });

    await act(async () => {
      await result.current.removeSymbol("MSFT");
    });

    expect(api.removeSymbol).toHaveBeenCalledWith("MSFT");
  });

  it("surfaces structured live-quote refresh failures from the shared api error contract", async () => {
    const api = createWatchlistApi();
    api.getLiveQuotesSnapshot = vi.fn().mockRejectedValueOnce(
      new ApiError({
        path: "/api/data/quotes-snapshot?symbols=AAPL",
        status: 503,
        title: "Service unavailable",
        detail: "collector offline"
      })
    );

    const { result } = renderHook(() => useWatchlistScreenViewModel(api));

    await waitFor(() => expect(result.current.quoteStatusTone).toBe("danger"));
    expect(result.current.quoteStatusLabel).toBe("Live prices unavailable: collector offline");
    expect(result.current.quoteStatusDetails).toEqual([
      "Meridian service returned 503. Open diagnostics for technical details.",
      "Service unavailable"
    ]);
  });

  it("surfaces structured symbol-load failures without losing detail lines", async () => {
    const api = createWatchlistApi();
    api.getSymbols = vi.fn().mockRejectedValueOnce(
      createApiErrorFromResponseBody(
        "/api/symbols",
        503,
        JSON.stringify({
          title: "Symbol service unavailable",
          detail: "The symbol catalog is temporarily offline.",
          errors: {
            provider: ["Reconnect the primary symbol source before retrying."]
          }
        })
      )
    );

    const { result } = renderHook(() => useWatchlistScreenViewModel(api));

    await waitFor(() => expect(result.current.listState).toBe("error"));
    expect(result.current.loadError).toEqual({
      summary: "The symbol catalog is temporarily offline.",
      details: [
        "Meridian service returned 503. Open diagnostics for technical details.",
        "Symbol service unavailable",
        "provider: Reconnect the primary symbol source before retrying."
      ]
    });
    expect(result.current.listDescription).toBe("The symbol catalog is temporarily offline.");
  });

  it("surfaces structured add-symbol failures with provider handoff details", async () => {
    const api = createWatchlistApi();
    api.addSymbol = vi.fn().mockRejectedValueOnce(
      createApiErrorFromResponseBody(
        "/api/symbols",
        422,
        JSON.stringify({
          title: "Symbol add rejected",
          detail: "The provider rejected the requested symbol.",
          errors: {
            symbol: ["Verify the symbol format or enable the provider before retrying."]
          }
        })
      )
    );

    const { result } = renderHook(() => useWatchlistScreenViewModel(api));
    await waitFor(() => expect(result.current.rows.length).toBeGreaterThan(0));

    act(() => {
      result.current.setPendingSymbol("SPY");
    });

    await act(async () => {
      await result.current.addPendingSymbol();
    });

    expect(result.current.submitFeedback).toEqual({
      tone: "danger",
      message: "The provider rejected the requested symbol.",
      details: [
        "Meridian service returned 422. Open diagnostics for technical details.",
        "Symbol add rejected",
        "symbol: Verify the symbol format or enable the provider before retrying."
      ],
      providerSetupHandoff: buildProviderSetupHandoff("symbol-add-exception")
    });
  });
});

describe("watchlist sort and filter", () => {
  const sortSamples: SymbolRecord[] = [
    { symbol: "AAA", status: "Active", provider: "Polygon", lastEventAt: null, eventCount: 0, hasHistoricalData: false },
    { symbol: "BBB", status: "Active", provider: "Polygon", lastEventAt: null, eventCount: 0, hasHistoricalData: false },
    { symbol: "CCC", status: "Active", provider: "Polygon", lastEventAt: null, eventCount: 0, hasHistoricalData: false },
    { symbol: "DDD", status: "Active", provider: "Polygon", lastEventAt: null, eventCount: 0, hasHistoricalData: false }
  ];

  const now = Date.parse("2026-05-09T01:00:00.000Z");

  function makeQuote(overrides: {
    symbol: string;
    timestamp?: string;
    lastPrice: number;
    spread: number;
    midPrice: number;
    change: number;
    changePercent: number | null;
    high: number;
    low: number;
  }): QuotesSnapshotItem {
    return {
      symbol: overrides.symbol,
      timestamp: overrides.timestamp ?? "2026-05-09T00:59:55.000Z",
      bidPrice: overrides.midPrice - overrides.spread / 2,
      bidSize: 10,
      askPrice: overrides.midPrice + overrides.spread / 2,
      askSize: 10,
      midPrice: overrides.midPrice,
      spread: overrides.spread,
      lastPrice: overrides.lastPrice,
      lastSize: 1,
      lastTradeTimestamp: overrides.timestamp ?? "2026-05-09T00:59:55.000Z",
      sequenceNumber: 1,
      streamId: null,
      venue: null,
      session: {
        sessionDate: "2026-05-09",
        open: overrides.lastPrice - overrides.change,
        high: overrides.high,
        low: overrides.low,
        last: overrides.lastPrice,
        volume: 1000,
        vwap: overrides.midPrice,
        tradeCount: 50,
        change: overrides.change,
        changePercent: overrides.changePercent,
        firstTradeAt: "2026-05-09T13:30:00.000Z",
        lastTradeAt: overrides.timestamp ?? "2026-05-09T00:59:55.000Z"
      }
    };
  }

  const baseRows = buildWatchlistRows(
    sortSamples,
    {},
    {
      AAA: makeQuote({ symbol: "AAA", lastPrice: 50, spread: 0.05, midPrice: 50, change: 1, changePercent: 2, high: 51, low: 48 }),
      BBB: makeQuote({ symbol: "BBB", lastPrice: 200, spread: 0.10, midPrice: 200, change: -3, changePercent: -1.5, high: 205, low: 199 }),
      CCC: makeQuote({
        symbol: "CCC",
        timestamp: "2026-05-09T00:59:00.000Z",
        lastPrice: 75,
        spread: 0.02,
        midPrice: 75,
        change: 5,
        changePercent: 7.1,
        high: 76,
        low: 70
      })
      // DDD intentionally has no quote
    },
    {},
    now
  );

  it("sorts by change percent descending (largest movers first) and pushes missing data to the bottom", () => {
    const sorted = sortAndFilterWatchlistRows(
      baseRows,
      { columnId: "change-percent", direction: "desc" } as WatchlistSortState,
      false
    );
    expect(sorted.map((row) => row.symbol)).toEqual(["CCC", "AAA", "BBB", "DDD"]);
  });

  it("sorts by spread ascending (tightest markets first)", () => {
    const sorted = sortAndFilterWatchlistRows(
      baseRows,
      { columnId: "spread", direction: "asc" } as WatchlistSortState,
      false
    );
    expect(sorted.map((row) => row.symbol)).toEqual(["CCC", "AAA", "BBB", "DDD"]);
  });

  it("hides stale rows when requested but keeps no-quote rows visible", () => {
    const sorted = sortAndFilterWatchlistRows(
      baseRows,
      { columnId: "symbol", direction: "asc" } as WatchlistSortState,
      true
    );
    // CCC's quote timestamp is 60s old; stale threshold is 15s, so CCC is stale.
    // DDD has no quote, so quoteStale is false — it remains visible.
    expect(sorted.map((row) => row.symbol)).toEqual(["AAA", "BBB", "DDD"]);
  });

  it("toggles sort direction: first click sets desc, second click sets asc, third resets to symbol", () => {
    const initial: WatchlistSortState = { columnId: "symbol", direction: "asc" };
    const afterFirst = toggleWatchlistSort(initial, "change-percent");
    expect(afterFirst).toEqual({ columnId: "change-percent", direction: "desc" });
    const afterSecond = toggleWatchlistSort(afterFirst, "change-percent");
    expect(afterSecond).toEqual({ columnId: "change-percent", direction: "asc" });
    const afterThird = toggleWatchlistSort(afterSecond, "change-percent");
    expect(afterThird).toEqual({ columnId: "symbol", direction: "asc" });
  });

  it("builds the stale filter command, disabling when no stale rows exist", () => {
    expect(buildStaleFilterCommand(0, 0, false)).toMatchObject({
      disabled: true,
      disabledReason: "Add a symbol before filtering stale quotes."
    });
    expect(buildStaleFilterCommand(5, 0, false)).toMatchObject({
      disabled: true,
      disabledReason: "No stale quotes to hide."
    });
    expect(buildStaleFilterCommand(5, 2, false)).toEqual({
      label: "Hide stale (2)",
      ariaLabel: "Hide 2 stale quotes.",
      pressed: false,
      disabled: false,
      disabledReason: null,
      hiddenCount: 0
    });
    expect(buildStaleFilterCommand(5, 2, true)).toEqual({
      label: "Showing fresh only (2 hidden)",
      ariaLabel: "Showing fresh quotes only. 2 stale rows hidden. Click to show all rows.",
      pressed: true,
      disabled: false,
      disabledReason: null,
      hiddenCount: 2
    });
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
