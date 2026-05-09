import { describe, expect, it } from "vitest";
import {
  buildBulkAddFeedback,
  buildWatchlistRows,
  buildWatchlistStats,
  formatRelative,
  parseWatchlistSymbols,
  validatePendingSymbol
} from "@/screens/watchlist-screen.view-model";
import type { SymbolRecord, SymbolStatistics } from "@/types";

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

  it("derives stat cards with danger tone for symbol errors", () => {
    const stats: SymbolStatistics = {
      totalSymbols: 3,
      monitoredSymbols: 2,
      archivedSymbols: 1,
      symbolsWithErrors: 1,
      totalEventsLast24h: 1200
    };

    expect(buildWatchlistStats(stats)).toEqual([
      { id: "total", label: "Total", value: "3", tone: "default", ariaLabel: "Total: 3" },
      { id: "monitored", label: "Monitored", value: "2", tone: "default", ariaLabel: "Monitored: 2" },
      { id: "archived", label: "Archived", value: "1", tone: "default", ariaLabel: "Archived: 1" },
      { id: "errors", label: "Errors", value: "1", tone: "danger", ariaLabel: "Errors: 1" }
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
});
