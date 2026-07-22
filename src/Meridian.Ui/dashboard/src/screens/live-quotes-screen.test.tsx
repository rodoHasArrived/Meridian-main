import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderHook, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { useNavigate } from "react-router-dom";

import { ApiError } from "@/lib/api-errors";
import { computeIntradayMetrics, LiveQuotesScreen } from "@/screens/live-quotes-screen";
import {
  LIVE_QUOTES_EMPTY_VALUE,
  buildLiveQuoteRefreshCommand,
  buildLiveQuoteSymbolLookupViewModel,
  buildLiveQuotesSessionStatsViewModel,
  buildLiveQuotesMarketViewModel,
  buildOrderRequest,
  buildPriceSparklineViewModel,
  buildQuickTradeTicketViewModel,
  formatMarketPrice,
  formatMarketSize,
  formatMarketTime,
  useQuickTradeTicket,
  validateQuickTicket
} from "@/screens/live-quotes-screen.view-model";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import * as api from "@/lib/api";
import type { OrderResult } from "@/types";

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
  timestamp: "2026-05-08T15:00:00.000Z"
};

const tradesFixture = {
  symbol: "AAPL",
  trades: [],
  count: 0,
  timestamp: "2026-05-08T15:00:00.000Z"
};

const tradesWithPrintsFixture = {
  ...tradesFixture,
  trades: [
    {
      symbol: "AAPL",
      timestamp: "2026-05-08T15:00:00.000Z",
      price: 188.06,
      size: 75,
      aggressor: "Buy",
      sequenceNumber: 10,
      streamId: "stream-1",
      venue: "NASDAQ"
    }
  ],
  count: 1
};

const tradesWithTwoPrintsFixture = {
  ...tradesFixture,
  trades: [
    {
      symbol: "AAPL",
      timestamp: "2026-05-08T15:00:01.000Z",
      price: 188.08,
      size: 125,
      aggressor: "Sell",
      sequenceNumber: 11,
      streamId: "stream-1",
      venue: "NASDAQ"
    },
    {
      symbol: "AAPL",
      timestamp: "2026-05-08T15:00:00.000Z",
      price: 188.06,
      size: 75,
      aggressor: "Buy",
      sequenceNumber: 10,
      streamId: "stream-1",
      venue: "NASDAQ"
    }
  ],
  count: 2
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

  it("keeps quick-ticket form fields and accessible copy in the view model", () => {
    const vm = buildQuickTradeTicketViewModel({
      activeSymbol: "AAPL",
      ticket: { side: "Buy", type: "Limit", quantity: "", limitPrice: "", phase: "idle", message: null, details: [], orderId: null, acknowledged: false },
      seedTicket: vi.fn(),
      updateField: vi.fn(),
      setReviewAcknowledged: vi.fn(),
      submitTicket: vi.fn(),
      resetTicket: vi.fn()
    });

    expect(vm.formLabel).toBe("Quick trade ticket for AAPL");
    expect(vm.quantityInvalid).toBe(false);
    expect(vm.priceInvalid).toBe(false);
    expect(vm.submitCommand).toMatchObject({
      disabled: true,
      disabledReason: "Enter a quantity greater than zero."
    });
    expect(vm.status).toMatchObject({
      role: "status",
      tone: "default",
      message: "Enter a quantity to enable order submission.",
      showErrorIcon: false,
      actions: []
    });
    expect(vm.fields.quantity).toMatchObject({
      id: "quick-ticket-quantity",
      label: "Quantity",
      ariaLabel: "Order quantity in shares",
      placeholder: "100",
      describedBy: vm.status.id,
      inputMode: "numeric",
      min: 1,
      step: 1
    });
    expect(vm.fields.limitPrice).toMatchObject({
      id: "quick-ticket-price",
      label: "Limit price",
      ariaLabel: "Limit price",
      placeholder: "0.00",
      describedBy: vm.status.id,
      inputMode: "decimal",
      min: 0,
      step: "0.01"
    });
    expect(vm.reviewAcknowledgement).toMatchObject({
      checked: false,
      disabled: true,
      disabledReason: "Enter a quantity greater than zero."
    });
  });

  it("requires quick-ticket review acknowledgement after fields are valid", () => {
    const vm = buildQuickTradeTicketViewModel({
      activeSymbol: "AAPL",
      ticket: { side: "Buy", type: "Limit", quantity: "10", limitPrice: "188.05", phase: "idle", message: null, details: [], orderId: null, acknowledged: false },
      seedTicket: vi.fn(),
      updateField: vi.fn(),
      setReviewAcknowledged: vi.fn(),
      submitTicket: vi.fn(),
      resetTicket: vi.fn()
    });

    expect(vm.reviewAcknowledgement).toMatchObject({
      label: "I reviewed this order ticket",
      description: "Buy 10 AAPL as a limit order at 188.05.",
      checked: false,
      disabled: false,
      disabledReason: null
    });
    expect(vm.submitCommand).toMatchObject({
      disabled: true,
      disabledReason: "Review and acknowledge the ticket before submitting."
    });
    expect(vm.status).toMatchObject({
      role: "status",
      message: "Review side, quantity, and price, then acknowledge before submitting."
    });

    const acknowledged = buildQuickTradeTicketViewModel({
      activeSymbol: "AAPL",
      ticket: { side: "Buy", type: "Limit", quantity: "10", limitPrice: "188.05", phase: "idle", message: null, details: [], orderId: null, acknowledged: true },
      seedTicket: vi.fn(),
      updateField: vi.fn(),
      setReviewAcknowledged: vi.fn(),
      submitTicket: vi.fn(),
      resetTicket: vi.fn()
    });

    expect(acknowledged.submitCommand).toMatchObject({
      disabled: false,
      disabledReason: null
    });
    expect(acknowledged.status.message).toBe("Orders route through Meridian's pre-trade risk and execution controls.");
    expect(acknowledged.status.actions).toEqual([]);
  });

  it("keeps seeded ticket confirmation in the quick-ticket view model", () => {
    const vm = buildQuickTradeTicketViewModel({
      activeSymbol: "AAPL",
      ticket: {
        side: "Sell",
        type: "Limit",
        quantity: "",
        limitPrice: "188.05",
        phase: "seeded",
        message: "Seeded sell AAPL limit ticket at 188.05. Enter quantity, then acknowledge before submitting.",
        details: [],
        orderId: null,
        acknowledged: false
      },
      seedTicket: vi.fn(),
      updateField: vi.fn(),
      setReviewAcknowledged: vi.fn(),
      submitTicket: vi.fn(),
      resetTicket: vi.fn()
    });

    expect(vm.status).toMatchObject({
      role: "status",
      tone: "success",
      message: "Seeded sell AAPL limit ticket at 188.05. Enter quantity, then acknowledge before submitting.",
      showSuccessIcon: true,
      actions: []
    });
    expect(vm.submitCommand.disabledReason).toBe("Enter a quantity greater than zero.");
  });

  it("surfaces Trading readiness handoffs after accepted and rejected submissions", () => {
    const accepted = buildQuickTradeTicketViewModel({
      activeSymbol: "AAPL",
      ticket: {
        side: "Buy",
        type: "Limit",
        quantity: "10",
        limitPrice: "188.05",
        phase: "submitted",
        message: "Order ORD-1 accepted.",
        details: [],
        orderId: "ORD-1",
        acknowledged: false
      },
      seedTicket: vi.fn(),
      updateField: vi.fn(),
      setReviewAcknowledged: vi.fn(),
      submitTicket: vi.fn(),
      resetTicket: vi.fn()
    });

    expect(accepted.status.actions).toEqual([
      {
        id: "trading-readiness",
        label: "Review readiness",
        href: "/trading/readiness",
        ariaLabel: "Open Trading readiness after order ORD-1 was accepted"
      }
    ]);

    const rejected = buildQuickTradeTicketViewModel({
      activeSymbol: "AAPL",
      ticket: {
        side: "Sell",
        type: "Market",
        quantity: "10",
        limitPrice: "",
        phase: "error",
        message: "Insufficient buying power",
        details: ["Meridian service returned 409. Open diagnostics for technical details."],
        orderId: null,
        acknowledged: false
      },
      seedTicket: vi.fn(),
      updateField: vi.fn(),
      setReviewAcknowledged: vi.fn(),
      submitTicket: vi.fn(),
      resetTicket: vi.fn()
    });

    expect(rejected.status.actions).toEqual([
      {
        id: "trading-readiness",
        label: "Review readiness",
        href: "/trading/readiness",
        ariaLabel: "Open Trading readiness after AAPL order submission failed"
      }
    ]);
  });

  it("switches quick-ticket price metadata for market orders", () => {
    const vm = buildQuickTradeTicketViewModel({
      activeSymbol: "AAPL",
      ticket: { side: "Buy", type: "Market", quantity: "10", limitPrice: "", phase: "idle", message: null, details: [], orderId: null, acknowledged: false },
      seedTicket: vi.fn(),
      updateField: vi.fn(),
      setReviewAcknowledged: vi.fn(),
      submitTicket: vi.fn(),
      resetTicket: vi.fn()
    });

    expect(vm.fields.limitPrice).toMatchObject({
      label: "Price (market)",
      ariaLabel: "Market order price",
      placeholder: "Best available",
      disabled: true,
      disabledReason: "Market orders route at the best available price."
    });
    expect(vm.priceDisabled).toBe(true);
  });

  it("locks quick-ticket fields while an order is submitting", () => {
    const vm = buildQuickTradeTicketViewModel({
      activeSymbol: "AAPL",
      ticket: {
        side: "Buy",
        type: "Limit",
        quantity: "10",
        limitPrice: "188.05",
        phase: "submitting",
        message: null,
        details: [],
        orderId: null,
        acknowledged: false
      },
      seedTicket: vi.fn(),
      updateField: vi.fn(),
      setReviewAcknowledged: vi.fn(),
      submitTicket: vi.fn(),
      resetTicket: vi.fn()
    });

    expect(vm.submitting).toBe(true);
    expect(vm.fields.side.disabledReason).toBe("Order submission is in progress; wait before editing the ticket.");
    expect(vm.fields.type.disabled).toBe(true);
    expect(vm.fields.quantity.disabled).toBe(true);
    expect(vm.fields.limitPrice.disabled).toBe(true);
    expect(vm.submitCommand).toMatchObject({
      disabled: true,
      disabledReason: "Order submission is already running.",
      busy: true
    });
  });
});

describe("useQuickTradeTicket", () => {
  it("announces a seeded limit ticket and clears seed feedback when edited", () => {
    const submitOrder = vi.fn();
    const { result } = renderHook(() => useQuickTradeTicket("AAPL", { submitOrder }));

    act(() => {
      result.current.seedTicket("Sell", 188.05);
    });

    expect(result.current.ticket).toMatchObject({
      side: "Sell",
      type: "Limit",
      limitPrice: "188.05",
      phase: "seeded",
      message: "Seeded sell AAPL limit ticket at 188.05. Enter quantity, then acknowledge before submitting.",
      acknowledged: false
    });
    expect(result.current.status.message).toBe(
      "Seeded sell AAPL limit ticket at 188.05. Enter quantity, then acknowledge before submitting."
    );

    act(() => {
      result.current.updateField("quantity", "25");
    });

    expect(result.current.ticket.phase).toBe("idle");
    expect(result.current.ticket.message).toBeNull();
    expect(result.current.status.message).toBe("Review side, quantity, and price, then acknowledge before submitting.");
  });

  it("ignores in-flight submit results after the active symbol changes", async () => {
    const order = deferred<OrderResult>();
    const submitOrder = vi.fn(() => order.promise);
    const { result, rerender } = renderHook(
      ({ symbol }: { symbol: string | null }) => useQuickTradeTicket(symbol, { submitOrder }),
      { initialProps: { symbol: "AAPL" } }
    );

    act(() => {
      result.current.updateField("quantity", "10");
      result.current.updateField("limitPrice", "188.05");
      result.current.setReviewAcknowledged(true);
    });

    let submitPromise!: Promise<void>;
    act(() => {
      submitPromise = result.current.submitTicket({
        preventDefault: vi.fn()
      } as unknown as Parameters<typeof result.current.submitTicket>[0]);
    });

    await waitFor(() => expect(result.current.ticket.phase).toBe("submitting"));
    expect(submitOrder).toHaveBeenCalledWith({
      symbol: "AAPL",
      side: "Buy",
      type: "Limit",
      quantity: 10,
      limitPrice: 188.05
    });

    rerender({ symbol: "MSFT" });
    await waitFor(() => expect(result.current.ticket.phase).toBe("idle"));

    await act(async () => {
      order.resolve({
        success: true,
        orderId: "ORD-AAPL",
        reason: null
      });
      await submitPromise;
    });

    expect(result.current.ticket.phase).toBe("idle");
    expect(result.current.ticket.message).toBeNull();
    expect(result.current.ticket.details).toEqual([]);
    expect(result.current.ticket.orderId).toBeNull();
  });

  it("keeps structured submit failure details on the ticket state", async () => {
    const submitOrder = vi.fn(async () => {
      throw new ApiError({
        path: "/api/orders",
        status: 422,
        title: "Order validation failed",
        detail: "Quantity exceeds configured order limit.",
        validationIssues: [
          {
            field: "quantity",
            label: "quantity",
            messages: ["Reduce the share count before resubmitting."]
          }
        ]
      });
    });
    const { result } = renderHook(() => useQuickTradeTicket("AAPL", { submitOrder }));

    act(() => {
      result.current.updateField("quantity", "1000");
      result.current.updateField("limitPrice", "188.05");
      result.current.setReviewAcknowledged(true);
    });

    await act(async () => {
      await result.current.submitTicket({
        preventDefault: vi.fn()
      } as unknown as Parameters<typeof result.current.submitTicket>[0]);
    });

    expect(result.current.ticket.phase).toBe("error");
    expect(result.current.ticket.message).toBe("Quantity exceeds configured order limit.");
    expect(result.current.ticket.details).toEqual([
      "Meridian service returned 422. Open diagnostics for technical details.",
      "Order validation failed",
      "quantity: Reduce the share count before resubmitting."
    ]);
  });
});

describe("buildLiveQuotesMarketViewModel", () => {
  it("separates initial loading from empty quote, depth, and trades states", () => {
    const vm = buildLiveQuotesMarketViewModel({
      activeSymbol: "AAPL",
      quote: { data: null, error: null },
      trades: { data: null, error: null },
      orderbook: { data: null, error: null },
      refreshing: true,
      tradeTableLimit: 25
    });

    expect(vm.quoteState).toMatchObject({
      status: "loading",
      role: "status",
      message: "Loading quote data for AAPL…",
      showData: false
    });
    expect(vm.orderbookState).toMatchObject({
      status: "loading",
      role: "status",
      message: "Loading depth for AAPL…",
      showData: false
    });
    expect(vm.tradesState).toMatchObject({
      status: "loading",
      role: "status",
      message: "Loading recent trades for AAPL…",
      showData: false
    });
  });

  it("uses the design-system unavailable-value marker for missing market data", () => {
    expect(formatMarketPrice(null)).toBe(LIVE_QUOTES_EMPTY_VALUE);
    expect(formatMarketSize(undefined)).toBe(LIVE_QUOTES_EMPTY_VALUE);
    expect(formatMarketTime("not-a-date")).toBe(LIVE_QUOTES_EMPTY_VALUE);
    expect(formatMarketTime("2026-05-08T15:00:00.123Z")).toBe("15:00:00.123 UTC");

    const vm = buildLiveQuotesMarketViewModel({
      activeSymbol: "AAPL",
      quote: {
        data: {
          ...quoteFixture,
          quote: {
            ...quoteFixture.quote,
            midPrice: null,
            spread: null,
            sequenceNumber: Number.NaN,
            streamId: null
          }
        },
        error: null
      },
      trades: { data: tradesFixture, error: null },
      orderbook: { data: { ...orderbookFixture, bids: [], asks: [] }, error: null },
      refreshing: false,
      tradeTableLimit: 25
    });

    expect(vm.quoteMetrics.map((metric) => [metric.label, metric.value])).toEqual([
      ["Mid", LIVE_QUOTES_EMPTY_VALUE],
      ["Spread", LIVE_QUOTES_EMPTY_VALUE],
      ["Sequence", LIVE_QUOTES_EMPTY_VALUE],
      ["Stream", LIVE_QUOTES_EMPTY_VALUE]
    ]);
    expect(vm.priceChart).toMatchObject({
      lastPriceLabel: LIVE_QUOTES_EMPTY_VALUE,
      changeLabel: `${LIVE_QUOTES_EMPTY_VALUE} (${LIVE_QUOTES_EMPTY_VALUE})`,
      statusMessage: "No recent prints available for AAPL."
    });
  });

  it("keeps session-stat presentation state in the market view model", () => {
    const sessionStats = buildLiveQuotesSessionStatsViewModel("AAPL", quoteFixture.quote.session);

    expect(sessionStats).toMatchObject({
      id: "live-quotes-session-stats",
      ariaLabel: "AAPL session statistics",
      descriptionId: "live-quotes-session-stats-description",
      periodLabel: "Today",
      dateLabel: "Session 2026-05-08",
      changeLabel: "+1.06 (+0.57%)",
      changeAriaLabel: "Day change +1.06 (+0.57%)",
      changeTone: "positive",
      description: "Session 2026-05-08 quote evidence from 13:30:00.000 UTC to 14:59:59.000 UTC."
    });
    expect(sessionStats?.stats.map((stat) => [stat.label, stat.value])).toEqual([
      ["Open", "187.00"],
      ["High", "188.50"],
      ["Low", "186.80"],
      ["VWAP", "187.74"],
      ["Volume", "1.25M"]
    ]);

    const flatSessionStats = buildLiveQuotesSessionStatsViewModel("MSFT", {
      ...quoteFixture.quote.session,
      change: 0,
      changePercent: null,
      volume: 950
    });

    expect(flatSessionStats).toMatchObject({
      changeLabel: "0.00 (—)",
      changeTone: "default"
    });
    expect(flatSessionStats?.stats.find((stat) => stat.id === "volume")?.value).toBe("950");
    expect(buildLiveQuotesSessionStatsViewModel("AAPL", null)).toBeNull();
  });

  it("models empty market-data panels after a completed fetch returns no rows", () => {
    const vm = buildLiveQuotesMarketViewModel({
      activeSymbol: "AAPL",
      quote: { data: null, error: null },
      trades: { data: tradesFixture, error: null },
      orderbook: { data: { ...orderbookFixture, bids: [], asks: [] }, error: null },
      refreshing: false,
      tradeTableLimit: 25
    });

    expect(vm.quoteState.status).toBe("empty");
    expect(vm.quoteState.message).toBe("No quote data available for AAPL.");
    expect(vm.quoteState.showData).toBe(false);
    expect(vm.orderbookState.status).toBe("empty");
    expect(vm.tradesState.status).toBe("empty");
    expect(vm.tradesDescription).toBe("Recent prints");
  });

  it("models ready quote, depth, and trades evidence with bounded trade rows", () => {
    const trade = {
      symbol: "AAPL",
      timestamp: "2026-05-08T15:00:00.000Z",
      price: 188.07,
      size: 100,
      aggressor: "Buy",
      sequenceNumber: 1,
      streamId: null,
      venue: "NASDAQ"
    };

    const vm = buildLiveQuotesMarketViewModel({
      activeSymbol: "AAPL",
      quote: { data: quoteFixture, error: null },
      trades: { data: { ...tradesFixture, trades: [trade, { ...trade, sequenceNumber: 2 }] }, error: null },
      orderbook: { data: orderbookFixture, error: null },
      refreshing: false,
      tradeTableLimit: 1
    });

    expect(vm.quoteState.status).toBe("ready");
    expect(vm.quoteState.showData).toBe(true);
    expect(vm.orderbookState.status).toBe("ready");
    expect(vm.tradesState.status).toBe("ready");
    expect(vm.tradeRows).toHaveLength(1);
    expect(vm.tradeDisplayRows[0]).toMatchObject({
      priceLabel: "188.07",
      sizeLabel: "100",
      aggressorTone: "positive",
      venueLabel: "NASDAQ"
    });
    expect(vm.venueLabel).toBe("NASDAQ");
    expect(vm.lastUpdateLabel).toBe("15:00:00.000 UTC");
  });

  it("keeps recent-trade row selection and detail state in the view model", () => {
    const selectTrade = vi.fn();
    const selectedId = "11-2026-05-08T15:00:01.000Z";

    const vm = buildLiveQuotesMarketViewModel({
      activeSymbol: "AAPL",
      quote: { data: quoteFixture, error: null },
      trades: { data: tradesWithTwoPrintsFixture, error: null },
      orderbook: { data: orderbookFixture, error: null },
      refreshing: false,
      selectedTradeId: selectedId,
      selectTrade,
      tradeTableLimit: 25
    });

    expect(vm.selectedTradeId).toBe(selectedId);
    expect(vm.selectTrade).toBe(selectTrade);
    expect(vm.tradeDisplayRows[0]).toMatchObject({
      id: selectedId,
      expanded: true,
      detailPanelId: "live-quotes-trade-detail",
      selectAriaLabel: "Inspect AAPL trade 11 at 188.08"
    });
    expect(vm.tradeDisplayRows[1]).toMatchObject({
      expanded: false,
      detailPanelId: "live-quotes-trade-detail"
    });
    expect(vm.selectedTradeDetail).toMatchObject({
      title: "AAPL print 11",
      statusLabel: "Sell",
      statusBadgeVariant: "warning",
      ariaLabel: "AAPL trade 11 detail"
    });
    expect(vm.selectedTradeDetail?.fields.map((field) => field.label)).toEqual([
      "Price",
      "Size",
      "Sequence",
      "Stream",
      "Venue",
      "Timestamp"
    ]);
    expect(vm.selectedTradeDetail?.fields.find((field) => field.label === "Timestamp")?.value)
      .toBe("15:00:01.000 UTC");
  });

  it("derives BBO, depth, quote metrics, and chart labels for the view", () => {
    const trade = {
      symbol: "AAPL",
      timestamp: "2026-05-08T15:00:00.000Z",
      price: 188.07,
      size: 100,
      aggressor: "Sell",
      sequenceNumber: 1,
      streamId: null,
      venue: "NASDAQ"
    };

    const vm = buildLiveQuotesMarketViewModel({
      activeSymbol: "AAPL",
      quote: { data: quoteFixture, error: null },
      trades: { data: { ...tradesFixture, trades: [trade] }, error: null },
      orderbook: { data: orderbookFixture, error: null },
      refreshing: false,
      tradeTableLimit: 25
    });

    expect(vm.bboPanels).toEqual([
      expect.objectContaining({
        id: "bid",
        priceLabel: "188.05",
        sizeLabel: "200 shares",
        seedSide: "Sell",
        seedLabel: "Sell AAPL at bid 188.05"
      }),
      expect.objectContaining({
        id: "ask",
        priceLabel: "188.07",
        sizeLabel: "150 shares",
        seedSide: "Buy",
        seedLabel: "Buy AAPL at ask 188.07"
      })
    ]);
    expect(vm.quoteMetrics.map((metric) => [metric.label, metric.value])).toEqual([
      ["Mid", "188.06"],
      ["Spread", "0.02"],
      ["Sequence", "42"],
      ["Stream", "stream-1"]
    ]);
    expect(vm.depthLadder.bids[0]).toMatchObject({
      side: "bid",
      priceLabel: "188.05",
      sizeLabel: "200",
      barWidth: "100%",
      seedLabel: "Sell AAPL at 188.05",
      selectLabel: "Inspect AAPL bid level 1 at 188.05; Sell AAPL at 188.05",
      detailPanelId: "live-quotes-depth-level-detail",
      expanded: true
    });
    expect(vm.depthLadder.selectedDetail).toMatchObject({
      title: "Bid level 1 @ 188.05",
      statusLabel: "Bid",
      ariaLabel: "Bid level 1 detail",
      fields: expect.arrayContaining([
        { label: "Venue", value: "NASDAQ" },
        { label: "Sequence", value: "42" },
        { label: "Timestamp", value: "15:00:00.000 UTC" }
      ])
    });
    expect(vm.tradeDisplayRows[0]).toMatchObject({
      aggressorLabel: "Sell",
      aggressorTone: "negative",
      timeLabel: "15:00:00.000 UTC"
    });
    expect(vm.priceChart).toMatchObject({
      title: "AAPL prints over 1s",
      lastPriceLabel: "188.07",
      changeLabel: "0.00 (0.00%)",
      changeTone: "default",
      strokeToken: "var(--chart-bench)",
      statusMessage: null
    });
    expect(vm.priceChart.sparkline).toMatchObject({
      viewBox: "0 0 800 180",
      strokeToken: "var(--chart-bench)",
      highLabel: "188.07",
      lowLabel: "188.07",
      ariaLabel: "Recent AAPL trade prices, ranging from 188.07 to 188.07."
    });
    expect(vm.priceChart.sparkline?.points).toMatch(/^\d+\.\d{2},\d+\.\d{2}$/);
    expect(vm.sessionStats).toMatchObject({
      ariaLabel: "AAPL session statistics",
      changeLabel: "+1.06 (+0.57%)",
      changeTone: "positive",
      stats: expect.arrayContaining([
        { id: "open", label: "Open", value: "187.00" },
        { id: "volume", label: "Volume", value: "1.25M" }
      ])
    });
  });

  it("keeps sparkline projection in the view model and returns null without chartable prints", () => {
    const empty = computeIntradayMetrics([]);

    expect(buildPriceSparklineViewModel(empty, "var(--chart-bench)", "Empty chart")).toBeNull();

    const metrics = computeIntradayMetrics([
      {
        symbol: "MSFT",
        timestamp: "2026-05-08T15:01:00.000Z",
        price: 421.5,
        size: 20,
        aggressor: "Buy",
        sequenceNumber: 2,
        streamId: null,
        venue: "NYSE"
      },
      {
        symbol: "MSFT",
        timestamp: "2026-05-08T15:00:00.000Z",
        price: 420,
        size: 10,
        aggressor: "Sell",
        sequenceNumber: 1,
        streamId: null,
        venue: "NYSE"
      }
    ]);

    expect(buildPriceSparklineViewModel(metrics, "var(--chart-up)", "MSFT chart")).toMatchObject({
      viewBox: "0 0 800 180",
      guideStartX: "8.00",
      guideEndX: "792.00",
      labelX: "792.00",
      highLabel: "421.50",
      lowLabel: "420.00",
      strokeToken: "var(--chart-up)",
      ariaLabel: "MSFT chart"
    });
  });

  it("keeps stale market data usable while surfacing refresh errors", () => {
    const vm = buildLiveQuotesMarketViewModel({
      activeSymbol: "AAPL",
      quote: { data: quoteFixture, error: "quote feed offline" },
      trades: { data: tradesWithPrintsFixture, error: "trade tape offline" },
      orderbook: { data: orderbookFixture, error: "depth feed offline" },
      refreshing: false,
      tradeTableLimit: 25
    });

    expect(vm.quoteState).toMatchObject({
      status: "warning",
      role: "alert",
      message: "quote feed offline",
      showData: true
    });
    expect(vm.orderbookState).toMatchObject({
      status: "warning",
      role: "alert",
      message: "depth feed offline",
      showData: true
    });
    expect(vm.tradesState).toMatchObject({
      status: "warning",
      role: "alert",
      message: "trade tape offline",
      showData: true
    });
  });

  it("keeps selected depth-level detail in the market view model", () => {
    const selectDepthLevel = vi.fn();
    const vm = buildLiveQuotesMarketViewModel({
      activeSymbol: "AAPL",
      quote: { data: quoteFixture, error: null },
      trades: { data: tradesFixture, error: null },
      orderbook: { data: orderbookFixture, error: null },
      refreshing: false,
      selectedDepthLevelId: "ask-1",
      selectDepthLevel,
      tradeTableLimit: 25
    });

    expect(vm.depthLadder.selectedLevelId).toBe("ask-1");
    expect(vm.depthLadder.asks[0]).toMatchObject({
      id: "ask-1",
      expanded: true,
      tone: "negative",
      selectLabel: "Inspect AAPL ask level 1 at 188.07; Buy AAPL at 188.07"
    });
    expect(vm.depthLadder.bids[0]).toMatchObject({ id: "bid-1", expanded: false });
    expect(vm.depthLadder.selectedDetail).toMatchObject({
      title: "Ask level 1 @ 188.07",
      statusLabel: "Ask",
      statusBadgeVariant: "warning",
      description: "150 shares are visible at 188.07. Selecting this level seeds a buy limit ticket.",
      fields: expect.arrayContaining([
        { label: "Price", value: "188.07" },
        { label: "Size", value: "150" },
        { label: "Mid", value: "188.06" }
      ])
    });
    vm.depthLadder.selectLevel("bid-1");
    expect(selectDepthLevel).toHaveBeenCalledWith("bid-1");
  });
});

describe("buildLiveQuoteSymbolLookupViewModel", () => {
  it("normalizes symbol input and exposes a route-ready command", () => {
    const vm = buildLiveQuoteSymbolLookupViewModel({
      inputValue: " msft ",
      activeSymbol: "AAPL",
      submittedEmpty: false
    });

    expect(vm.normalizedSymbol).toBe("MSFT");
    expect(vm.command).toMatchObject({
      disabled: false,
      disabledReason: null,
      ariaLabel: "View live quote for MSFT"
    });
    expect(vm.status).toMatchObject({
      role: "status",
      message: "Ready to load MSFT."
    });
  });

  it("models empty symbol input as a disabled lookup command", () => {
    const vm = buildLiveQuoteSymbolLookupViewModel({
      inputValue: " ",
      activeSymbol: null,
      submittedEmpty: true
    });

    expect(vm.normalizedSymbol).toBe("");
    expect(vm.inputInvalid).toBe(true);
    expect(vm.command).toMatchObject({
      disabled: true,
      disabledReason: "Enter a symbol before loading live market data."
    });
    expect(vm.status).toMatchObject({
      role: "alert",
      message: "Enter a symbol before loading live market data."
    });
  });
});

describe("buildLiveQuoteRefreshCommand", () => {
  it("hides refresh until a symbol is active and disables during refresh", () => {
    expect(buildLiveQuoteRefreshCommand(null, false)).toBeNull();
    expect(buildLiveQuoteRefreshCommand("AAPL", false)).toMatchObject({
      label: "Refresh",
      ariaLabel: "Refresh live data for AAPL",
      disabled: false,
      disabledReason: null,
      busy: false
    });
    expect(buildLiveQuoteRefreshCommand("AAPL", true)).toMatchObject({
      label: "Refreshing",
      ariaLabel: "Refreshing live data for AAPL",
      disabled: true,
      disabledReason: "Live market data refresh is already running.",
      busy: true
    });
  });
});

describe("LiveQuotesScreen quick trade", () => {
  beforeEach(() => {
    vi.spyOn(api, "getLiveQuote").mockResolvedValue(quoteFixture);
    vi.spyOn(api, "getLiveTrades").mockResolvedValue(tradesFixture);
    vi.spyOn(api, "getLiveOrderbook").mockResolvedValue(orderbookFixture);
    vi.spyOn(api, "getLiveQuotesSnapshot").mockResolvedValue({
      timestamp: quoteFixture.timestamp,
      count: 1,
      quotes: [
        {
          symbol: "AAPL",
          timestamp: quoteFixture.quote.timestamp,
          bidPrice: quoteFixture.quote.bidPrice,
          bidSize: quoteFixture.quote.bidSize,
          askPrice: quoteFixture.quote.askPrice,
          askSize: quoteFixture.quote.askSize,
          midPrice: quoteFixture.quote.midPrice,
          spread: quoteFixture.quote.spread,
          lastPrice: quoteFixture.quote.session.last,
          lastSize: null,
          lastTradeTimestamp: quoteFixture.quote.session.lastTradeAt,
          sequenceNumber: quoteFixture.quote.sequenceNumber,
          streamId: quoteFixture.quote.streamId,
          venue: quoteFixture.quote.venue,
          session: quoteFixture.quote.session
        }
      ]
    });
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

  it("shows freshness chips for the market panels and quote matrix after data loads", async () => {
    renderWithRouter(<LiveQuotesScreen />, { initialEntries: ["/data/quotes?symbol=AAPL"] });

    await waitForAsyncEffects();

    expect(screen.getByLabelText(/Market panels updated/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Quote matrix updated/)).toBeInTheDocument();
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

    expect(screen.getByText("Seeded buy AAPL limit ticket at 188.07. Enter quantity, then acknowledge before submitting.")).toBeInTheDocument();

    const sideSelect = screen.getByLabelText("Order side") as HTMLSelectElement;
    expect(sideSelect.value).toBe("Buy");

    const priceInput = screen.getByLabelText("Limit price") as HTMLInputElement;
    expect(priceInput.value).toBe("188.07");

    await user.type(screen.getByLabelText("Order quantity in shares"), "100");
    const submitButton = screen.getByRole("button", { name: /Submit buy order for AAPL/i });
    expect(submitButton).toBeDisabled();
    expect(submitButton).toHaveAttribute("title", "Review and acknowledge the ticket before submitting.");
    await user.click(screen.getByRole("checkbox", { name: /I reviewed this order ticket/i }));

    await user.click(submitButton);

    await waitFor(() => expect(submitSpy).toHaveBeenCalledTimes(1));
    expect(submitSpy.mock.calls[0]?.[0]).toEqual({
      symbol: "AAPL",
      side: "Buy",
      type: "Limit",
      quantity: 100,
      limitPrice: 188.07
    });

    expect(await screen.findByText(/Order ORD-1 accepted/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open Trading readiness after order ORD-1 was accepted" })).toHaveAttribute(
      "href",
      "/trading/readiness"
    );
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
    await user.click(screen.getByRole("checkbox", { name: /I reviewed this order ticket/i }));
    await user.click(screen.getByRole("button", { name: /Submit sell order for AAPL/i }));

    expect(await screen.findByText(/Insufficient buying power/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open Trading readiness after AAPL order submission failed" })).toHaveAttribute(
      "href",
      "/trading/readiness"
    );
  });

  it("renders structured backend details when order submission throws", async () => {
    vi.spyOn(api, "submitOrder").mockRejectedValue(new ApiError({
      path: "/api/orders",
      status: 503,
      title: "Execution service unavailable",
      detail: "Order router is offline.",
      validationIssues: [
        {
          field: "routing",
          label: "routing",
          messages: ["Reconnect the execution provider before retrying this order."]
        }
      ]
    }));

    const user = userEvent.setup();
    renderWithRouter(<LiveQuotesScreen />, { initialEntries: ["/data/quotes?symbol=AAPL"] });

    await waitForAsyncEffects();

    await user.click(await screen.findByRole("button", { name: /Buy AAPL at ask/i }));
    await user.type(screen.getByLabelText("Order quantity in shares"), "10");
    await user.click(screen.getByRole("checkbox", { name: /I reviewed this order ticket/i }));
    await user.click(screen.getByRole("button", { name: /Submit buy order for AAPL/i }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Order router is offline.");
    expect(within(alert).getByText("Meridian service returned 503. Open diagnostics for technical details.")).toBeInTheDocument();
    expect(within(alert).getByText("Execution service unavailable")).toBeInTheDocument();
    expect(within(alert).getByText("routing: Reconnect the execution provider before retrying this order.")).toBeInTheDocument();
  });

  it("confirms seeded quick-ticket state and escalates invalid edited fields", async () => {
    const submitSpy = vi.spyOn(api, "submitOrder");

    const user = userEvent.setup();
    renderWithRouter(<LiveQuotesScreen />, { initialEntries: ["/data/quotes?symbol=AAPL"] });

    await waitForAsyncEffects();

    await user.click(await screen.findByRole("button", { name: /Buy AAPL at ask/i }));
    const submitButton = screen.getByRole("button", { name: /Submit buy order for AAPL/i });

    expect(submitButton).toBeDisabled();
    expect(submitButton).toHaveAttribute("title", "Enter a quantity greater than zero.");
    expect(screen.getByText("Seeded buy AAPL limit ticket at 188.07. Enter quantity, then acknowledge before submitting.")).toBeInTheDocument();
    expect(screen.getByText("Enter a quantity greater than zero.")).toBeInTheDocument();

    const quantityInput = screen.getByLabelText("Order quantity in shares");
    await user.type(quantityInput, "0");

    expect(quantityInput).toHaveAttribute("aria-invalid", "true");
    expect(submitSpy).not.toHaveBeenCalled();
    expect(screen.getAllByText("Enter a quantity greater than zero.").length).toBeGreaterThan(0);
  });

  it("syncs the active symbol when the symbol query parameter changes", async () => {
    const user = userEvent.setup();

    function Harness() {
      const navigate = useNavigate();
      return (
        <>
          <button type="button" onClick={() => navigate("/data/quotes?symbol=MSFT")}>Route to MSFT</button>
          <LiveQuotesScreen />
        </>
      );
    }

    renderWithRouter(<Harness />, { initialEntries: ["/data/quotes?symbol=AAPL"] });

    await waitFor(() => expect(api.getLiveQuote).toHaveBeenCalledWith("AAPL", expect.objectContaining({ signal: expect.any(Object) })));

    await user.click(screen.getByRole("button", { name: "Route to MSFT" }));

    await waitFor(() => expect(api.getLiveQuote).toHaveBeenCalledWith("MSFT", expect.objectContaining({ signal: expect.any(Object) })));
    expect(screen.getByDisplayValue("MSFT")).toBeInTheDocument();
  });

  it("disables empty symbol lookup and describes the required input", () => {
    renderWithRouter(<LiveQuotesScreen />, { initialEntries: ["/data/quotes"] });

    const submitButton = screen.getByRole("button", { name: "View live quote" });
    expect(submitButton).toBeDisabled();
    expect(submitButton).toHaveAttribute("title", "Enter a symbol before loading live market data.");
    expect(screen.getByText("Enter a symbol to load live BBO, recent trades, and L2 depth.")).toBeInTheDocument();
    expect(screen.getByText("Start a quote list")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Add starter symbols" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Import symbols from watchlist" })).toHaveAttribute("href", "/data/watchlist");
    expect(screen.getByRole("button", { name: "Search symbol" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Selected symbol detail" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Open replay workflow" })).not.toBeInTheDocument();
    expect(api.getLiveQuote).not.toHaveBeenCalled();
  });

  it("has no basic accessibility violations in the no-symbol state", async () => {
    const { container } = renderWithRouter(<LiveQuotesScreen />, { initialEntries: ["/data/quotes"] });

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });

  it("renders loading states while initial market data is pending", async () => {
    const quote = deferred<typeof quoteFixture>();
    const trades = deferred<typeof tradesFixture>();
    const orderbook = deferred<typeof orderbookFixture>();

    vi.spyOn(api, "getLiveQuote").mockReturnValue(quote.promise);
    vi.spyOn(api, "getLiveTrades").mockReturnValue(trades.promise);
    vi.spyOn(api, "getLiveOrderbook").mockReturnValue(orderbook.promise);

    renderWithRouter(<LiveQuotesScreen />, { initialEntries: ["/data/quotes?symbol=AAPL"] });

    await waitFor(() => expect(api.getLiveQuote).toHaveBeenCalledWith("AAPL", expect.objectContaining({ signal: expect.any(Object) })));

    expect(screen.getByText(/Loading quote data for AAPL/i)).toBeInTheDocument();
    expect(screen.getByText(/Loading depth for AAPL/i)).toBeInTheDocument();
    expect(screen.getByText(/Loading recent trades for AAPL/i)).toBeInTheDocument();

    await act(async () => {
      quote.resolve(quoteFixture);
      trades.resolve(tradesFixture);
      orderbook.resolve(orderbookFixture);
      await Promise.resolve();
    });
  });

  it("renders the session-stat banner from VM-owned labels and ARIA copy", async () => {
    renderWithRouter(<LiveQuotesScreen />, { initialEntries: ["/data/quotes?symbol=AAPL"] });

    const sessionRegion = await screen.findByRole("region", { name: "AAPL session statistics" });

    expect(sessionRegion).toHaveAttribute("aria-describedby", "live-quotes-session-stats-description");
    expect(within(sessionRegion).getByLabelText("Day change +1.06 (+0.57%)")).toHaveTextContent("+1.06 (+0.57%)");
    expect(within(sessionRegion).getByText("Session 2026-05-08")).toBeInTheDocument();
    expect(within(sessionRegion).getByText("Open")).toBeInTheDocument();
    expect(within(sessionRegion).getByText("187.00")).toBeInTheDocument();
    expect(within(sessionRegion).getByText("Volume")).toBeInTheDocument();
    expect(within(sessionRegion).getByText("1.25M")).toBeInTheDocument();
  });

  it("keeps the last market snapshot visible when a manual refresh fails", async () => {
    vi.spyOn(window, "setInterval").mockReturnValue(0 as unknown as ReturnType<typeof setInterval>);
    vi.mocked(api.getLiveQuote)
      .mockResolvedValueOnce(quoteFixture)
      .mockRejectedValueOnce(new Error("quote feed offline"));
    vi.mocked(api.getLiveTrades)
      .mockResolvedValueOnce(tradesWithPrintsFixture)
      .mockRejectedValueOnce(new Error("trade tape offline"));
    vi.mocked(api.getLiveOrderbook)
      .mockResolvedValueOnce(orderbookFixture)
      .mockRejectedValueOnce(new Error("depth feed offline"));

    const user = userEvent.setup();
    renderWithRouter(<LiveQuotesScreen />, { initialEntries: ["/data/quotes?symbol=AAPL"] });

    expect(await screen.findByRole("button", { name: /Buy AAPL at ask 188\.07/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Sell AAPL at 188\.05/i })).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Refresh live data for AAPL" }));

    expect(await screen.findByText("quote feed offline")).toBeInTheDocument();
    expect(screen.getByText("trade tape offline")).toBeInTheDocument();
    expect(screen.getByText("depth feed offline")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Buy AAPL at ask 188\.07/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Sell AAPL at 188\.05/i })).toBeInTheDocument();
    expect(screen.getAllByText("188.06").length).toBeGreaterThan(0);
  });

  it("renders recent trades as selectable rows with a linked detail inspector", async () => {
    vi.spyOn(api, "getLiveTrades").mockResolvedValue(tradesWithTwoPrintsFixture);

    const user = userEvent.setup();
    renderWithRouter(<LiveQuotesScreen />, { initialEntries: ["/data/quotes?symbol=AAPL"] });

    const firstTrade = await screen.findByRole("row", { name: /Inspect AAPL trade 11 at 188\.08/i });
    const secondTrade = await screen.findByRole("row", { name: /Inspect AAPL trade 10 at 188\.06/i });

    expect(firstTrade).toHaveAttribute("aria-controls", "live-quotes-trade-detail");
    expect(firstTrade).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("region", { name: "AAPL trade 11 detail" })).toBeInTheDocument();
    expect(screen.getByText("AAPL print 11")).toBeInTheDocument();

    await user.click(secondTrade);

    expect(secondTrade).toHaveAttribute("aria-expanded", "true");
    expect(firstTrade).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByRole("region", { name: "AAPL trade 10 detail" })).toBeInTheDocument();
    expect(screen.getByText("AAPL print 10")).toBeInTheDocument();
  });

  it("links order book levels to a persistent selected-depth detail panel", async () => {
    const user = userEvent.setup();
    renderWithRouter(<LiveQuotesScreen />, { initialEntries: ["/data/quotes?symbol=AAPL"] });

    const bidLevel = await screen.findByRole("button", {
      name: "Inspect AAPL bid level 1 at 188.05; Sell AAPL at 188.05"
    });
    const askLevel = await screen.findByRole("button", {
      name: "Inspect AAPL ask level 1 at 188.07; Buy AAPL at 188.07"
    });

    expect(bidLevel).toHaveAttribute("aria-controls", "live-quotes-depth-level-detail");
    expect(bidLevel).toHaveAttribute("aria-expanded", "true");
    expect(askLevel).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByRole("region", { name: "Bid level 1 detail" })).toBeInTheDocument();
    expect(screen.getByText("Bid level 1 @ 188.05")).toBeInTheDocument();

    await user.click(askLevel);

    expect(askLevel).toHaveAttribute("aria-expanded", "true");
    expect(bidLevel).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByRole("region", { name: "Ask level 1 detail" })).toBeInTheDocument();
    expect(screen.getByText("Ask level 1 @ 188.07")).toBeInTheDocument();
    expect(screen.getByText("Seeded buy AAPL limit ticket at 188.07. Enter quantity, then acknowledge before submitting.")).toBeInTheDocument();
    expect((screen.getByLabelText("Limit price") as HTMLInputElement).value).toBe("188.07");
  });

  it("ignores stale quote responses after switching symbols", async () => {
    const aaplQuote = deferred<typeof quoteFixture>();
    const aaplTrades = deferred<typeof tradesFixture>();
    const aaplOrderbook = deferred<typeof orderbookFixture>();
    const msftQuote = deferred<typeof msftQuoteFixture>();
    const msftTrades = deferred<typeof msftTradesFixture>();
    const msftOrderbook = deferred<typeof msftOrderbookFixture>();
    const quoteSignals: AbortSignal[] = [];
    const tradeSignals: AbortSignal[] = [];
    const orderbookSignals: AbortSignal[] = [];

    vi.spyOn(api, "getLiveQuote").mockImplementation((symbol, options) => {
      quoteSignals.push(options?.signal as AbortSignal);
      return symbol === "AAPL" ? aaplQuote.promise : msftQuote.promise;
    });
    vi.spyOn(api, "getLiveTrades").mockImplementation((symbol, _limit, options) => {
      tradeSignals.push(options?.signal as AbortSignal);
      return symbol === "AAPL" ? aaplTrades.promise : msftTrades.promise;
    });
    vi.spyOn(api, "getLiveOrderbook").mockImplementation((symbol, _depth, options) => {
      orderbookSignals.push(options?.signal as AbortSignal);
      return symbol === "AAPL" ? aaplOrderbook.promise : msftOrderbook.promise;
    });

    const user = userEvent.setup();
    renderWithRouter(<LiveQuotesScreen />, { initialEntries: ["/data/quotes?symbol=AAPL"] });

    await waitFor(() => expect(api.getLiveQuote).toHaveBeenCalledWith("AAPL", expect.objectContaining({ signal: expect.any(Object) })));
    expect(quoteSignals[0]?.aborted).toBe(false);

    await user.clear(screen.getByLabelText("Symbol"));
    await user.type(screen.getByLabelText("Symbol"), "MSFT");
    await user.click(screen.getByRole("button", { name: /View live quote for MSFT/i }));

    await waitFor(() => expect(api.getLiveQuote).toHaveBeenCalledWith("MSFT", expect.objectContaining({ signal: expect.any(Object) })));
    expect(quoteSignals[0]?.aborted).toBe(true);
    expect(tradeSignals[0]?.aborted).toBe(true);
    expect(orderbookSignals[0]?.aborted).toBe(true);
    expect(quoteSignals.at(-1)?.aborted).toBe(false);

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

    const priceInput = screen.getByLabelText("Market order price") as HTMLInputElement;
    expect(priceInput.disabled).toBe(true);

    await user.type(screen.getByLabelText("Order quantity in shares"), "5");
    await user.click(screen.getByRole("checkbox", { name: /I reviewed this order ticket/i }));

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
