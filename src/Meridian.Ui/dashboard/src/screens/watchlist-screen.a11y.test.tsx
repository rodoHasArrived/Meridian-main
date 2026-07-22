import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import { axe } from "jest-axe";
import { WatchlistScreen } from "@/screens/watchlist-screen";
import { renderWithRouter } from "@/test/render";
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

describe("WatchlistScreen accessibility", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.getSymbols).mockResolvedValue(symbols);
    vi.mocked(api.getSymbolsStatistics).mockResolvedValue(stats);
    vi.mocked(api.getLiveQuotesSnapshot).mockResolvedValue(snapshot);
  });

  it("has no basic accessibility violations in the populated watchlist", async () => {
    const { container } = renderWithRouter(<WatchlistScreen />, { initialEntries: ["/data/watchlist"] });

    const table = await screen.findByRole("treegrid", { name: /subscribed symbol watchlist/i });
    await waitFor(() => expect(within(table).getByText("188.05 x 200")).toBeInTheDocument());

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });
});
