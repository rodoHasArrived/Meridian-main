import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { LiveQuotesScreen, validateQuickTicket } from "@/screens/live-quotes-screen";
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
});

describe("LiveQuotesScreen quick trade", () => {
  beforeEach(() => {
    vi.spyOn(api, "getLiveQuote").mockResolvedValue(quoteFixture);
    vi.spyOn(api, "getLiveTrades").mockResolvedValue(tradesFixture);
    vi.spyOn(api, "getLiveOrderbook").mockResolvedValue(orderbookFixture);
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

  it("blocks submission with an invalid quantity", async () => {
    const submitSpy = vi.spyOn(api, "submitOrder");

    const user = userEvent.setup();
    renderWithRouter(<LiveQuotesScreen />, { initialEntries: ["/data/quotes?symbol=AAPL"] });

    await waitForAsyncEffects();

    await user.click(await screen.findByRole("button", { name: /Buy AAPL at ask/i }));
    await user.click(screen.getByRole("button", { name: /Submit buy order for AAPL/i }));

    expect(submitSpy).not.toHaveBeenCalled();
    expect(await screen.findByText(/Enter a quantity greater than zero/i)).toBeInTheDocument();
  });

  it("clears the limit-price requirement when switching to a market order", async () => {
    const user = userEvent.setup();
    renderWithRouter(<LiveQuotesScreen />, { initialEntries: ["/data/quotes?symbol=AAPL"] });

    await waitForAsyncEffects();

    await user.click(await screen.findByRole("button", { name: /Buy AAPL at ask/i }));
    const typeSelect = screen.getByLabelText("Order type");
    await user.selectOptions(typeSelect, "Market");

    const priceInput = screen.getByLabelText("Limit price") as HTMLInputElement;
    expect(priceInput.disabled).toBe(true);

    await user.type(screen.getByLabelText("Order quantity in shares"), "5");

    const submitSpy = vi.spyOn(api, "submitOrder").mockResolvedValue({
      success: true,
      orderId: "ORD-2",
      reason: null
    });

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
