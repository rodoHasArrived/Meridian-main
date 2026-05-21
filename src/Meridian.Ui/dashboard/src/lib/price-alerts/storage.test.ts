import { afterEach, describe, expect, it, vi } from "vitest";
import { appendTrigger, loadPriceAlertState, savePriceAlertState, updateAlert, type StorageLike } from "./storage";
import { PRICE_ALERT_MAX_TRIGGER_HISTORY, PRICE_ALERT_STORAGE_KEY, type PriceAlert, type PriceAlertStorageState, type PriceAlertTrigger } from "./types";

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

const alertSample: PriceAlert = {
  id: "alert-1",
  symbol: "AAPL",
  condition: "above",
  field: "last",
  threshold: 200,
  note: null,
  createdAt: "2026-05-12T12:00:00.000Z",
  snoozedUntil: null,
  enabled: true,
  triggeredAt: null,
  lastObservedPrice: null,
  lastObservedAt: null
};

const triggerSample: PriceAlertTrigger = {
  id: "trigger-1",
  alertId: "alert-1",
  symbol: "AAPL",
  condition: "above",
  field: "last",
  threshold: 200,
  triggeredPrice: 201.5,
  triggeredAt: "2026-05-12T12:30:00.000Z",
  acknowledged: false,
  note: null
};

describe("price alerts storage", () => {
  let storage: MemoryStorage;

  afterEach(() => {
    storage = new MemoryStorage();
    vi.unstubAllGlobals();
  });

  it("returns empty state when storage has no entry", () => {
    const result = loadPriceAlertState(new MemoryStorage());
    expect(result.version).toBe(1);
    expect(result.alerts).toEqual([]);
    expect(result.triggers).toEqual([]);
  });

  it("round-trips state through save/load", () => {
    storage = new MemoryStorage();
    const state: PriceAlertStorageState = { version: 1, alerts: [alertSample], triggers: [triggerSample] };
    expect(savePriceAlertState(state, storage)).toBe("saved");
    const round = loadPriceAlertState(storage);
    expect(round.alerts).toHaveLength(1);
    expect(round.alerts[0]?.symbol).toBe("AAPL");
    expect(round.triggers).toHaveLength(1);
  });

  it("ignores malformed stored payloads", () => {
    storage = new MemoryStorage();
    storage.setItem(PRICE_ALERT_STORAGE_KEY, "{ not json");
    const result = loadPriceAlertState(storage);
    expect(result.alerts).toEqual([]);
    expect(result.triggers).toEqual([]);
  });

  it("normalizes symbols to upper-case on load", () => {
    storage = new MemoryStorage();
    const payload = {
      version: 1,
      alerts: [{ ...alertSample, symbol: "aapl" }],
      triggers: []
    };
    storage.setItem(PRICE_ALERT_STORAGE_KEY, JSON.stringify(payload));
    const result = loadPriceAlertState(storage);
    expect(result.alerts[0]?.symbol).toBe("AAPL");
  });

  it("drops invalid alerts and keeps valid ones", () => {
    storage = new MemoryStorage();
    const payload = {
      version: 1,
      alerts: [
        alertSample,
        { id: "bogus" }, // missing required fields
        { ...alertSample, id: "alert-2", threshold: "not-a-number" }
      ],
      triggers: []
    };
    storage.setItem(PRICE_ALERT_STORAGE_KEY, JSON.stringify(payload));
    const result = loadPriceAlertState(storage);
    expect(result.alerts).toHaveLength(1);
    expect(result.alerts[0]?.id).toBe("alert-1");
  });

  it("appends a trigger to the head and caps history", () => {
    let state: PriceAlertStorageState = { version: 1, alerts: [alertSample], triggers: [] };
    for (let i = 0; i < PRICE_ALERT_MAX_TRIGGER_HISTORY + 5; i++) {
      state = appendTrigger(state, { ...triggerSample, id: `trigger-${i}` });
    }
    expect(state.triggers).toHaveLength(PRICE_ALERT_MAX_TRIGGER_HISTORY);
    expect(state.triggers[0]?.id).toBe(`trigger-${PRICE_ALERT_MAX_TRIGGER_HISTORY + 4}`);
  });

  it("updateAlert mutates only the matching alert", () => {
    const state: PriceAlertStorageState = {
      version: 1,
      alerts: [alertSample, { ...alertSample, id: "alert-2", symbol: "MSFT" }],
      triggers: []
    };
    const next = updateAlert(state, "alert-2", (alert) => ({ ...alert, enabled: false }));
    expect(next.alerts[0]?.enabled).toBe(true);
    expect(next.alerts[1]?.enabled).toBe(false);
  });

  it("handles save errors silently (no throw) when storage rejects", () => {
    const failing: StorageLike = {
      getItem: () => null,
      setItem: () => {
        throw new Error("quota");
      },
      removeItem: () => undefined
    };
    expect(savePriceAlertState({ version: 1, alerts: [alertSample], triggers: [] }, failing)).toBe("failed");
  });

  it("reports unavailable storage when no storage target exists", () => {
    vi.stubGlobal("window", undefined);
    expect(savePriceAlertState({ version: 1, alerts: [alertSample], triggers: [] }, null)).toBe("unavailable");
  });
});
