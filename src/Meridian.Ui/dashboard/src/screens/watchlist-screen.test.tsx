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
