import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useLiveQuotesScreenViewModel, type LiveQuotesApi, type LiveQuotesRouteBinding } from "@/screens/live-quotes-screen.view-model";

// Scheduled retries are gated on operator intent: they must never refresh a
// paused tape or a symbol that is no longer subscribed, while an unchanged
// subscription retries as advertised.
describe("live quotes retry gating", () => {
  const failure = () => Promise.reject(new Error("provider outage"));

  function buildApi(): LiveQuotesApi {
    return {
      getLiveQuote: vi.fn(failure),
      getLiveTrades: vi.fn(failure),
      getLiveOrderbook: vi.fn(failure),
      getLiveQuotesSnapshot: vi.fn(failure),
      submitOrder: vi.fn(failure)
    };
  }

  // Mirrors the shell's URL binding: route setters immediately update the
  // values the hook reads back, exactly as the router does.
  function buildRoute(): LiveQuotesRouteBinding {
    const route: LiveQuotesRouteBinding = {
      routeSymbol: "AAPL",
      routeSymbols: "AAPL",
      setRouteSymbol: (symbol) => {
        route.routeSymbol = symbol;
        route.routeSymbols = symbol;
      },
      setRouteSymbols: (symbols, selectedSymbol) => {
        route.routeSymbols = symbols.join(",");
        route.routeSymbol = selectedSymbol ?? "";
      }
    };
    return route;
  }

  async function flushMicrotasks() {
    await act(async () => {
      await Promise.resolve();
      await Promise.resolve();
    });
  }

  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("retries a failed refresh while the subscription is unchanged", async () => {
    const api = buildApi();
    const route = buildRoute();
    renderHook(() => useLiveQuotesScreenViewModel(api, route));
    await flushMicrotasks();
    expect(api.getLiveQuote).toHaveBeenCalledTimes(1);
    expect(api.getLiveQuotesSnapshot).toHaveBeenCalledTimes(1);

    // First retry fires at the 500ms base backoff, well before the 2s poll.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(600);
    });
    expect(api.getLiveQuote).toHaveBeenCalledTimes(2);
    expect(api.getLiveQuotesSnapshot).toHaveBeenCalledTimes(2);
  });

  it("never fires a pending retry into a paused tape", async () => {
    const api = buildApi();
    const route = buildRoute();
    const { result } = renderHook(() => useLiveQuotesScreenViewModel(api, route));
    await flushMicrotasks();
    expect(api.getLiveQuote).toHaveBeenCalledTimes(1);

    // The mount refresh failed and armed a retry; pausing inside the backoff
    // window must cancel it so nothing mutates the frozen panels.
    act(() => {
      result.current.workspace.togglePaused();
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10_000);
    });
    expect(api.getLiveQuote).toHaveBeenCalledTimes(1);
    expect(api.getLiveTrades).toHaveBeenCalledTimes(1);
    expect(api.getLiveOrderbook).toHaveBeenCalledTimes(1);
    expect(api.getLiveQuotesSnapshot).toHaveBeenCalledTimes(1);
  });

  it("never fires a pending retry for a symbol the operator just removed", async () => {
    const api = buildApi();
    const route = buildRoute();
    const { result } = renderHook(() => useLiveQuotesScreenViewModel(api, route));
    await flushMicrotasks();
    expect(api.getLiveQuotesSnapshot).toHaveBeenCalledTimes(1);

    // Removing the only symbol empties the subscription; the armed retry must
    // not refetch (or republish status for) the removed symbol.
    act(() => {
      result.current.workspace.removeSelectedSymbol();
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10_000);
    });
    expect(api.getLiveQuote).toHaveBeenCalledTimes(1);
    expect(api.getLiveQuotesSnapshot).toHaveBeenCalledTimes(1);
  });
});
