import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { computeIntradayMetrics, LiveQuotesScreen } from "@/screens/live-quotes-screen";
import { buildOrderRequest, validateQuickTicket } from "@/screens/live-quotes-screen.view-model";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import * as api from "@/lib/api";

const quoteFixture = {
  symbol: "AAPL",
  quote: {
    symbol: "AAPL",
    timestamp: "2026-05-08T15:00:00.000Z",
    bidPrice: 188.05,
    bidSize: 200,
    askPrice: 188.07,
    askSize: 150,
    midPrice: 188.06,
    spread: 0.02,
    sequenceNumber: 42,
    streamId: "stream-1",
    venue: "NASDAQ"
  },
  timestamp: "2026-05-08T15:00:00.000Z"
};

const tradesFixture = {
  symbol: "AAPL",
  trades: [],
  count: 0,
  timestamp: "2026-05-08T15:00:00.000Z"
};

const orderbookFixture = {
  symbol: "AAPL",
  timestamp: "2026-05-08T15:00:00.000Z",
  bids: [{ side: "Bid", level: 1, price: 188.05, size: 200, marketMaker: null }],
  asks: [{ side: "Ask", level: 1, price: 188.07, size: 150, marketMaker: null }],
  midPrice: 188.06,
  imbalance: 0,
  marketState: "Open",
  sequenceNumber: 42,
  isStale: false,
  streamId: "stream-1",
  venue: "NASDAQ"
};

const msftQuoteFixture = {
  ...quoteFixture,
  symbol: "MSFT",
  quote: {
    ...quoteFixture.quote,
    symbol: "MSFT",
    bidPrice: 421.1,
    bidSize: 300,
    askPrice: 421.2,
    askSize: 250,
    midPrice: 421.15,
    spread: 0.1,
    sequenceNumber: 99,
    streamId: "stream-2",
    venue: "NYSE"
  }
};

const msftTradesFixture = {
  ...tradesFixture,
  symbol: "MSFT"
};

const msftOrderbookFixture = {
  ...orderbookFixture,
  symbol: "MSFT",
  bids: [{ side: "Bid", level: 1, price: 421.1, size: 300, marketMaker: null }],
  asks: [{ side: "Ask", level: 1, price: 421.2, size: 250, marketMaker: null }],
  midPrice: 421.15,
  spread: 0.1,
  sequenceNumber: 99,
  streamId: "stream-2",
  venue: "NYSE"
};

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });

  return { promise, resolve, reject };
}

describe("computeIntradayMetrics", () => {
  const trade = (overrides: Partial<{ price: number; size: number; timestamp: string; sequenceNumber: number }> = {}) => ({
    symbol: "AAPL",
    timestamp: overrides.timestamp ?? "2026-05-08T15:00:00.000Z",
    price: overrides.price ?? 100,
    size: overrides.size ?? 10,
    aggressor: "Buy",
    sequenceNumber: overrides.sequenceNumber ?? 1,
    streamId: null,
    venue: null
  });

  it("returns empty metrics when no trades", () => {
    const metrics = computeIntradayMetrics([]);
    expect(metrics.count).toBe(0);
    expect(metrics.last).toBeNull();
    expect(metrics.change).toBeNull();
    expect(metrics.series).toEqual([]);
  });

  it("computes open/high/low/last/vwap from chronological order (API returns newest first)", () => {
    // API returns most-recent first
    const trades = [
      trade({ price: 110, size: 10, timestamp: "2026-05-08T15:02:00.000Z", sequenceNumber: 3 }),
      trade({ price: 95,  size: 20, timestamp: "2026-05-08T15:01:00.000Z", sequenceNumber: 2 }),
      trade({ price: 100, size: 10, timestamp: "2026-05-08T15:00:00.000Z", sequenceNumber: 1 })
    ];
    const metrics = computeIntradayMetrics(trades);
    expect(metrics.count).toBe(3);
    expect(metrics.open).toBe(100);
    expect(metrics.last).toBe(110);
    expect(metrics.high).toBe(110);
    expect(metrics.low).toBe(95);
    expect(metrics.volume).toBe(40);
    // VWAP: (100*10 + 95*20 + 110*10) / 40 = (1000 + 1900 + 1100) / 40 = 4000/40 = 100
    expect(metrics.vwap).toBe(100);
    expect(metrics.change).toBe(10);
    expect(metrics.changePct).toBeCloseTo(10, 5);
    expect(metrics.series.map((p) => p.price)).toEqual([100, 95, 110]);
  });

  it("ignores invalid prices but keeps valid ones", () => {
    const trades = [
      trade({ price: 105, size: 5, timestamp: "2026-05-08T15:01:00.000Z", sequenceNumber: 2 }),
      trade({ price: 0, size: 5, timestamp: "2026-05-08T15:00:30.000Z", sequenceNumber: 1 }),
      trade({ price: 100, size: 5, timestamp: "2026-05-08T15:00:00.000Z", sequenceNumber: 0 })
    ];
    const metrics = computeIntradayMetrics(trades);
    expect(metrics.count).toBe(3); // count is total trades
    expect(metrics.series).toHaveLength(2); // but invalid prices excluded from chart
    expect(metrics.high).toBe(105);
    expect(metrics.low).toBe(100);
  });
});

describe("validateQuickTicket", () => {
  it("rejects empty quantity", () => {
    expect(validateQuickTicket({ side: "Buy", type: "Limit", quantity: "", limitPrice: "10" })).toMatch(/quantity/i);
  });

  it("rejects zero or negative quantity", () => {
    expect(validateQuickTicket({ side: "Buy", type: "Limit", quantity: "0", limitPrice: "10" })).toMatch(/quantity/i);
    expect(validateQuickTicket({ side: "Buy", type: "Limit", quantity: "-5", limitPrice: "10" })).toMatch(/quantity/i);
  });

  it("rejects fractional quantity", () => {
    expect(validateQuickTicket({ side: "Buy", type: "Limit", quantity: "1.5", limitPrice: "10" })).toMatch(/whole/i);
  });

  it("requires limit price for limit orders", () => {
    expect(validateQuickTicket({ side: "Buy", type: "Limit", quantity: "10", limitPrice: "" })).toMatch(/limit/i);
    expect(validateQuickTicket({ side: "Buy", type: "Limit", quantity: "10", limitPrice: "0" })).toMatch(/limit/i);
  });

  it("does not require limit price for market orders", () => {
    expect(validateQuickTicket({ side: "Buy", type: "Market", quantity: "10", limitPrice: "" })).toBeNull();
  });

  it("accepts a valid limit ticket", () => {
    expect(validateQuickTicket({ side: "Sell", type: "Limit", quantity: "10", limitPrice: "188.05" })).toBeNull();
  });

  it("builds market and limit order requests from ticket state", () => {
    expect(buildOrderRequest("AAPL", { side: "Buy", type: "Market", quantity: "5", limitPrice: "" })).toEqual({
      symbol: "AAPL",
      side: "Buy",
      type: "Market",
      quantity: 5,
      limitPrice: null
    });
    expect(buildOrderRequest("AAPL", { side: "Sell", type: "Limit", quantity: "10", limitPrice: "188.05" })).toEqual({
      symbol: "AAPL",
      side: "Sell",
      type: "Limit",
      quantity: 10,
      limitPrice: 188.05
    });
  });
});

describe("LiveQuotesScreen quick trade", () => {
  beforeEach(() => {
    vi.spyOn(api, "getLiveQuote").mockResolvedValue(quoteFixture);
    vi.spyOn(api, "getLiveTrades").mockResolvedValue(tradesFixture);
    vi.spyOn(api, "getLiveOrderbook").mockResolvedValue(orderbookFixture);
    vi.spyOn(api, "getHistoricalBars").mockResolvedValue({
      success: true,
      message: null,
      symbol: "AAPL",
      intervalMinutes: 5,
      from: null,
      to: null,
      totalBars: 0,
      filesProcessed: 0,
      totalFiles: 0,
      queryTimeMs: 0,
      bars: []
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("seeds a buy ticket at the ask price when ask is clicked and submits", async () => {
    const submitSpy = vi.spyOn(api, "submitOrder").mockResolvedValue({
      success: true,
      orderId: "ORD-1",
      reason: null
    });

    const user = userEvent.setup();
    renderWithRouter(<LiveQuotesScreen />, { initialEntries: ["/data/quotes?symbol=AAPL"] });

    await waitForAsyncEffects();

    const ask = await screen.findByRole("button", { name: /Buy AAPL at ask/i });
    await user.click(ask);

    const sideSelect = screen.getByLabelText("Order side") as HTMLSelectElement;
    expect(sideSelect.value).toBe("Buy");

    const priceInput = screen.getByLabelText("Limit price") as HTMLInputElement;
    expect(priceInput.value).toBe("188.07");

    await user.type(screen.getByLabelText("Order quantity in shares"), "100");

    await user.click(screen.getByRole("button", { name: /Submit buy order for AAPL/i }));

    await waitFor(() => expect(submitSpy).toHaveBeenCalledTimes(1));
    expect(submitSpy.mock.calls[0]?.[0]).toEqual({
      symbol: "AAPL",
      side: "Buy",
      type: "Limit",
      quantity: 100,
      limitPrice: 188.07
    });

    expect(await screen.findByText(/Order ORD-1 accepted/i)).toBeInTheDocument();
  });

  it("surfaces server-side rejection reason", async () => {
    vi.spyOn(api, "submitOrder").mockResolvedValue({
      success: false,
      orderId: null,
      reason: "Insufficient buying power"
    });

    const user = userEvent.setup();
    renderWithRouter(<LiveQuotesScreen />, { initialEntries: ["/data/quotes?symbol=AAPL"] });

    await waitForAsyncEffects();

    await user.click(await screen.findByRole("button", { name: /Sell AAPL at bid/i }));
    await user.type(screen.getByLabelText("Order quantity in shares"), "10");
    await user.click(screen.getByRole("button", { name: /Submit sell order for AAPL/i }));

    expect(await screen.findByText(/Insufficient buying power/i)).toBeInTheDocument();
  });

  it("blocks submission with an invalid quantity before the order command runs", async () => {
    const submitSpy = vi.spyOn(api, "submitOrder");

    const user = userEvent.setup();
    renderWithRouter(<LiveQuotesScreen />, { initialEntries: ["/data/quotes?symbol=AAPL"] });

    await waitForAsyncEffects();

    await user.click(await screen.findByRole("button", { name: /Buy AAPL at ask/i }));
    const submitButton = screen.getByRole("button", { name: /Submit buy order for AAPL/i });

    expect(submitButton).toBeDisabled();
    expect(submitButton).toHaveAttribute("title", "Enter a quantity greater than zero.");
    expect(submitSpy).not.toHaveBeenCalled();
    expect(await screen.findByText(/Enter a quantity greater than zero/i)).toBeInTheDocument();
  });

  it("ignores stale quote responses after switching symbols", async () => {
    const aaplQuote = deferred<typeof quoteFixture>();
    const aaplTrades = deferred<typeof tradesFixture>();
    const aaplOrderbook = deferred<typeof orderbookFixture>();
    const msftQuote = deferred<typeof msftQuoteFixture>();
    const msftTrades = deferred<typeof msftTradesFixture>();
    const msftOrderbook = deferred<typeof msftOrderbookFixture>();

    vi.spyOn(api, "getLiveQuote").mockImplementation((symbol) => (
      symbol === "AAPL" ? aaplQuote.promise : msftQuote.promise
    ));
    vi.spyOn(api, "getLiveTrades").mockImplementation((symbol) => (
      symbol === "AAPL" ? aaplTrades.promise : msftTrades.promise
    ));
    vi.spyOn(api, "getLiveOrderbook").mockImplementation((symbol) => (
      symbol === "AAPL" ? aaplOrderbook.promise : msftOrderbook.promise
    ));

    const user = userEvent.setup();
    renderWithRouter(<LiveQuotesScreen />, { initialEntries: ["/data/quotes?symbol=AAPL"] });

    await waitFor(() => expect(api.getLiveQuote).toHaveBeenCalledWith("AAPL"));

    await user.clear(screen.getByLabelText("Symbol"));
    await user.type(screen.getByLabelText("Symbol"), "MSFT");
    await user.click(screen.getByRole("button", { name: /View quote/i }));

    await waitFor(() => expect(api.getLiveQuote).toHaveBeenCalledWith("MSFT"));

    await act(async () => {
      msftQuote.resolve(msftQuoteFixture);
      msftTrades.resolve(msftTradesFixture);
      msftOrderbook.resolve(msftOrderbookFixture);
      await Promise.resolve();
    });

    expect(await screen.findByRole("button", { name: /Buy MSFT at ask 421\.20/i })).toBeInTheDocument();

    await act(async () => {
      aaplQuote.resolve(quoteFixture);
      aaplTrades.resolve(tradesFixture);
      aaplOrderbook.resolve(orderbookFixture);
      await Promise.resolve();
    });

    expect(screen.getByRole("button", { name: /Buy MSFT at ask 421\.20/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Buy MSFT at ask 188\.07/i })).not.toBeInTheDocument();
  });

  it("clears the limit-price requirement when switching to a market order", async () => {
    const submitSpy = vi.spyOn(api, "submitOrder").mockResolvedValue({
      success: true,
      orderId: "ORD-2",
      reason: null
    });

    const user = userEvent.setup();
    renderWithRouter(<LiveQuotesScreen />, { initialEntries: ["/data/quotes?symbol=AAPL"] });

    await waitForAsyncEffects();

    await user.click(await screen.findByRole("button", { name: /Buy AAPL at ask/i }));
    const typeSelect = screen.getByLabelText("Order type");
    await user.selectOptions(typeSelect, "Market");

    const priceInput = screen.getByLabelText("Limit price") as HTMLInputElement;
    expect(priceInput.disabled).toBe(true);

    await user.type(screen.getByLabelText("Order quantity in shares"), "5");

    await user.click(screen.getByRole("button", { name: /Submit buy order for AAPL/i }));

    await waitFor(() => expect(submitSpy).toHaveBeenCalled());
    expect(submitSpy.mock.calls[0]?.[0]).toEqual({
      symbol: "AAPL",
      side: "Buy",
      type: "Market",
      quantity: 5,
      limitPrice: null
    });
  });
});

afterEach(() => {
  // Avoid open intervals leaking into subsequent test files.
  act(() => {
    vi.useRealTimers();
  });
});
