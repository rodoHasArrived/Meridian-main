import { describe, expect, it } from "vitest";
import {
  buildBulkAddFeedback,
  buildQuoteRefreshCommand,
  buildQuoteStatus,
  buildStarterPackCommands,
  buildStarterPackFeedback,
  buildWatchlistRows,
  buildWatchlistStats,
  formatRelative,
  parseWatchlistSymbols,
  validatePendingSymbol
} from "@/screens/watchlist-screen.view-model";
import type { QuotesSnapshotItem, SymbolRecord, SymbolStatistics } from "@/types";

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
      message: "Added 2 of 4 symbols; 1 skipped; QQQ rejected."
    });

    expect(buildBulkAddFeedback({ added: 0, skipped: 0, errors: ["Provider offline"] }, 2)).toEqual({
      tone: "danger",
      message: "Added 0 of 2 symbols; Provider offline."
    });
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
      message: "US core: added 3 of 4 symbols; 1 skipped; MSFT already exists."
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
});
