import { screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { PriceAlertsProvider } from "@/lib/price-alerts/service";
import type { StorageLike } from "@/lib/price-alerts/storage";
import { PRICE_ALERT_STORAGE_KEY, type PriceAlert, type PriceAlertStorageState } from "@/lib/price-alerts/types";
import { renderWithRouter } from "@/test/render";
import { PriceAlertsScreen } from "./price-alerts-screen";

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

function buildAlert(overrides: Partial<PriceAlert> = {}): PriceAlert {
  return {
    id: "alert-fixture",
    symbol: "AAPL",
    condition: "above",
    field: "last",
    threshold: 200,
    note: null,
    createdAt: new Date("2026-05-12T12:00:00.000Z").toISOString(),
    snoozedUntil: null,
    enabled: true,
    triggeredAt: null,
    lastObservedPrice: null,
    lastObservedAt: null,
    ...overrides
  };
}

function renderScreen(storage: StorageLike, initialEntries: string[] = ["/data/alerts"]) {
  const fetchSnapshot = vi.fn().mockResolvedValue({ timestamp: new Date().toISOString(), count: 0, quotes: [] });
  return renderWithRouter(
    <PriceAlertsProvider options={{ storage, fetchSnapshot, pollIntervalMs: 100_000 }}>
      <PriceAlertsScreen />
    </PriceAlertsProvider>,
    { initialEntries }
  );
}

function seedStorage(state: PriceAlertStorageState): MemoryStorage {
  const storage = new MemoryStorage();
  storage.setItem(PRICE_ALERT_STORAGE_KEY, JSON.stringify(state));
  return storage;
}

describe("PriceAlertsScreen", () => {
  beforeEach(() => {
    (globalThis as { Notification?: unknown }).Notification = undefined;
  });
  afterEach(() => {
    delete (globalThis as { Notification?: unknown }).Notification;
  });

  it("renders the empty state and form when no alerts exist", () => {
    renderScreen(new MemoryStorage());
    expect(screen.getByText(/Price alerts/i)).toBeInTheDocument();
    expect(screen.getByText(/evaluated against live quotes every 5s/i)).toBeInTheDocument();
    expect(screen.getByText(/No alerts set/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Create price alert/i })).toBeDisabled();
    expect(screen.getByText(/No alerts have triggered yet/i)).toBeInTheDocument();
  });

  it("renders persisted alerts and identifies the active count", () => {
    const storage = seedStorage({
      version: 1,
      alerts: [
        buildAlert({ id: "a-1", symbol: "AAPL", threshold: 200 }),
        buildAlert({ id: "a-2", symbol: "MSFT", threshold: 350, enabled: false, triggeredAt: new Date().toISOString() })
      ],
      triggers: []
    });
    renderScreen(storage);
    expect(screen.getByText("AAPL")).toBeInTheDocument();
    expect(screen.getByText("MSFT")).toBeInTheDocument();
    expect(screen.getByText(/2 alerts/i)).toBeInTheDocument();
    expect(screen.getAllByText(/Watching/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/Triggered/i).length).toBeGreaterThan(0);
  });

  it("seeds the symbol from ?symbol= query string", () => {
    renderScreen(new MemoryStorage(), ["/data/alerts?symbol=msft"]);
    expect((screen.getByLabelText("Symbol") as HTMLInputElement).value).toBe("MSFT");
  });

  it("shows the notification CTA when permission is default", () => {
    (globalThis as { Notification?: unknown }).Notification = class {
      static permission: NotificationPermission = "default";
      static requestPermission = vi.fn().mockResolvedValue("granted");
    };
    renderScreen(new MemoryStorage());
    expect(screen.getByRole("button", { name: /Enable notifications/i })).toBeInTheDocument();
  });
});
