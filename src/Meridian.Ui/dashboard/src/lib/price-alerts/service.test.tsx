import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { QuotesSnapshotResponse } from "@/types";
import { usePriceAlertsService } from "./service";
import type { StorageLike } from "./storage";

class MemoryStorage implements StorageLike {
  private readonly map = new Map<string, string>();
  getItem(key: string): string | null {
    return this.map.has(key) ? this.map.get(key)! : null;
  }
  setItem(key: string, value: string): void {
    this.map.set(key, value);
  }
  removeItem(key: string): void {
    this.map.delete(key);
  }
}

function snapshot(quotes: Partial<QuotesSnapshotResponse["quotes"][number]>[]): QuotesSnapshotResponse {
  return {
    timestamp: new Date().toISOString(),
    count: quotes.length,
    quotes: quotes.map((q) => ({
      symbol: q.symbol ?? "AAPL",
      timestamp: q.timestamp ?? new Date().toISOString(),
      bidPrice: q.bidPrice ?? 0,
      bidSize: q.bidSize ?? 0,
      askPrice: q.askPrice ?? 0,
      askSize: q.askSize ?? 0,
      midPrice: q.midPrice ?? null,
      spread: q.spread ?? null,
      lastPrice: q.lastPrice ?? null,
      lastSize: q.lastSize ?? null,
      lastTradeTimestamp: q.lastTradeTimestamp ?? null,
      sequenceNumber: q.sequenceNumber ?? 0,
      streamId: q.streamId ?? null,
      venue: q.venue ?? null,
      session: q.session ?? null
    }))
  };
}

describe("usePriceAlertsService", () => {
  it("creates an alert that watches its symbol and persists it", () => {
    const storage = new MemoryStorage();
    const { result } = renderHook(() =>
      usePriceAlertsService({
        storage,
        fetchSnapshot: vi.fn().mockResolvedValue(snapshot([]))
      })
    );

    act(() => {
      result.current.createAlert({
        symbol: "aapl",
        condition: "above",
        field: "last",
        threshold: 200
      });
    });

    expect(result.current.state.alerts).toHaveLength(1);
    expect(result.current.state.alerts[0]?.symbol).toBe("AAPL");
    expect(result.current.enabledCount).toBe(1);
    const persisted = JSON.parse(storage.getItem("meridian.priceAlerts.v1")!);
    expect(persisted.alerts).toHaveLength(1);
  });

  it("triggers and disables the alert when the snapshot crosses the threshold", async () => {
    const notify = vi.fn();
    const storage = new MemoryStorage();
    const { result } = renderHook(() =>
      usePriceAlertsService({
        storage,
        fetchSnapshot: vi.fn().mockResolvedValue(snapshot([])),
        notify
      })
    );

    act(() => {
      result.current.createAlert({ symbol: "AAPL", condition: "above", field: "last", threshold: 200 });
    });

    act(() => {
      result.current.evaluateSnapshot(snapshot([{ symbol: "AAPL", lastPrice: 199 }]));
    });

    await waitFor(() => expect(result.current.state.alerts[0]?.lastObservedPrice).toBe(199));
    expect(result.current.state.triggers).toHaveLength(0);
    expect(notify).not.toHaveBeenCalled();

    act(() => {
      result.current.evaluateSnapshot(snapshot([{ symbol: "AAPL", lastPrice: 201 }]));
    });

    await waitFor(() => expect(result.current.state.triggers).toHaveLength(1));
    expect(result.current.state.alerts[0]?.enabled).toBe(false);
    expect(result.current.state.alerts[0]?.triggeredAt).not.toBeNull();
    expect(result.current.unacknowledgedCount).toBe(1);
    expect(notify).toHaveBeenCalledTimes(1);
  });

  it("respects snooze and does not fire while snoozed", () => {
    const storage = new MemoryStorage();
    const future = new Date(Date.now() + 60_000);
    const { result } = renderHook(() =>
      usePriceAlertsService({
        storage,
        fetchSnapshot: vi.fn().mockResolvedValue(snapshot([]))
      })
    );

    let alertId = "";
    act(() => {
      alertId = result.current.createAlert({ symbol: "MSFT", condition: "above", field: "last", threshold: 100 }).id;
    });
    act(() => {
      result.current.snoozeAlert(alertId, future);
    });
    act(() => {
      result.current.evaluateSnapshot(snapshot([{ symbol: "MSFT", lastPrice: 105 }]));
    });

    expect(result.current.state.triggers).toHaveLength(0);
    expect(result.current.state.alerts[0]?.enabled).toBe(true);
  });

  it("acknowledgeAllTriggers marks every trigger as acknowledged", () => {
    const storage = new MemoryStorage();
    const { result } = renderHook(() =>
      usePriceAlertsService({
        storage,
        fetchSnapshot: vi.fn().mockResolvedValue(snapshot([]))
      })
    );

    act(() => {
      result.current.createAlert({ symbol: "TSLA", condition: "below", field: "last", threshold: 500 });
    });
    act(() => {
      result.current.evaluateSnapshot(snapshot([{ symbol: "TSLA", lastPrice: 400 }]));
    });
    expect(result.current.unacknowledgedCount).toBe(1);

    act(() => {
      result.current.acknowledgeAllTriggers();
    });
    expect(result.current.unacknowledgedCount).toBe(0);
  });

  it("resetAlert re-enables a triggered alert", () => {
    const storage = new MemoryStorage();
    const { result } = renderHook(() =>
      usePriceAlertsService({
        storage,
        fetchSnapshot: vi.fn().mockResolvedValue(snapshot([]))
      })
    );

    let alertId = "";
    act(() => {
      alertId = result.current.createAlert({ symbol: "NVDA", condition: "above", field: "last", threshold: 100 }).id;
    });
    act(() => {
      result.current.evaluateSnapshot(snapshot([{ symbol: "NVDA", lastPrice: 105 }]));
    });
    expect(result.current.state.alerts[0]?.enabled).toBe(false);

    act(() => {
      result.current.resetAlert(alertId);
    });
    expect(result.current.state.alerts[0]?.enabled).toBe(true);
    expect(result.current.state.alerts[0]?.triggeredAt).toBeNull();
    expect(result.current.state.alerts[0]?.lastObservedPrice).toBeNull();
  });

  it("polls the snapshot endpoint for enabled-alert symbols", async () => {
    const fetchSnapshot = vi.fn().mockResolvedValue(snapshot([{ symbol: "AAPL", lastPrice: 150 }]));
    const storage = new MemoryStorage();
    const { result } = renderHook(() =>
      usePriceAlertsService({
        storage,
        fetchSnapshot,
        pollIntervalMs: 30_000
      })
    );

    act(() => {
      result.current.createAlert({ symbol: "AAPL", condition: "above", field: "last", threshold: 200 });
    });

    await waitFor(() => expect(fetchSnapshot).toHaveBeenCalled());
    const calledWith = fetchSnapshot.mock.calls[0]?.[0];
    expect(calledWith).toEqual(["AAPL"]);
  });

  it("does not poll when no alerts are enabled", async () => {
    const fetchSnapshot = vi.fn().mockResolvedValue(snapshot([]));
    renderHook(() =>
      usePriceAlertsService({
        storage: new MemoryStorage(),
        fetchSnapshot,
        pollIntervalMs: 30_000
      })
    );
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(fetchSnapshot).not.toHaveBeenCalled();
  });

  it("rehydrates persisted state on next mount", () => {
    const storage = new MemoryStorage();
    const { result: first, unmount } = renderHook(() =>
      usePriceAlertsService({
        storage,
        fetchSnapshot: vi.fn().mockResolvedValue(snapshot([]))
      })
    );
    act(() => {
      first.current.createAlert({ symbol: "META", condition: "below", field: "bid", threshold: 300 });
    });
    unmount();

    const { result: second } = renderHook(() =>
      usePriceAlertsService({
        storage,
        fetchSnapshot: vi.fn().mockResolvedValue(snapshot([]))
      })
    );
    expect(second.current.state.alerts).toHaveLength(1);
    expect(second.current.state.alerts[0]?.symbol).toBe("META");
  });
});
