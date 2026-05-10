import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
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
  }
];

const stats: SymbolStatistics = {
  totalSymbols: 2,
  monitoredSymbols: 1,
  archivedSymbols: 0,
  symbolsWithErrors: 0,
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

describe("WatchlistScreen live prices", () => {
  beforeEach(() => {
    vi.spyOn(api, "getSymbols").mockResolvedValue(symbols);
    vi.spyOn(api, "getSymbolsStatistics").mockResolvedValue(stats);
    vi.spyOn(api, "getLiveQuotesSnapshot").mockResolvedValue(snapshot);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

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
    (api.getLiveQuotesSnapshot as unknown as ReturnType<typeof vi.fn>).mockRejectedValueOnce(
      new Error("collector offline")
    );

    renderWithRouter(<WatchlistScreen />);

    await waitForAsyncEffects();

    await waitFor(() => {
      expect(screen.getByText(/collector offline/i)).toBeInTheDocument();
    });
  });
});
