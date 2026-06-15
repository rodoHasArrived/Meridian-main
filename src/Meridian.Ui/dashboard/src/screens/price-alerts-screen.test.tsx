import { act, cleanup, fireEvent, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { PriceAlertsProvider } from "@/lib/price-alerts/service";
import type { StorageLike } from "@/lib/price-alerts/storage";
import { PRICE_ALERT_STORAGE_KEY, type PriceAlert, type PriceAlertStorageState, type PriceAlertTrigger } from "@/lib/price-alerts/types";
import { renderWithRouter } from "@/test/render";
import { expectNoAxeViolations } from "@/test/axe";
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

class FailingStorage implements StorageLike {
  getItem(): string | null {
    return null;
  }
  setItem(): void {
    throw new Error("quota");
  }
  removeItem(): void {
    return undefined;
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

function buildTrigger(overrides: Partial<PriceAlertTrigger> = {}): PriceAlertTrigger {
  return {
    id: "trigger-fixture",
    alertId: "alert-fixture",
    symbol: "AAPL",
    condition: "above",
    field: "last",
    threshold: 200,
    triggeredPrice: 201.25,
    triggeredAt: "2026-05-12T14:02:37.000Z",
    acknowledged: false,
    note: null,
    ...overrides
  };
}

function renderScreen(
  storage: StorageLike,
  initialEntries: string[] = ["/data/alerts"],
  fetchSnapshot = vi.fn().mockResolvedValue({ timestamp: new Date().toISOString(), count: 0, quotes: [] })
) {
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
    cleanup();
    delete (globalThis as { Notification?: unknown }).Notification;
  });

  it("has no basic accessibility violations", async () => {
    const storage = seedStorage({
      version: 1,
      alerts: [
        buildAlert({ id: "a-1", symbol: "AAPL", threshold: 200, enabled: false }),
        buildAlert({ id: "a-2", symbol: "MSFT", threshold: 350, enabled: true })
      ],
      triggers: []
    });
    const { container } = renderScreen(storage);

    await expectNoAxeViolations(container);
  });

  it("renders the empty state and form when no alerts exist", () => {
    renderScreen(new MemoryStorage());
    expect(screen.getByText(/Price alerts/i)).toBeInTheDocument();
    expect(screen.getByText(/evaluated against live quotes every 5s/i)).toBeInTheDocument();
    expect(screen.getByText(/No alerts set/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Create price alert/i })).toBeDisabled();
    expect(screen.getByText(/No alerts have triggered yet/i)).toBeInTheDocument();
  });

  it("renders VM-owned condition and field guidance in the alert form", () => {
    renderScreen(new MemoryStorage());

    const condition = screen.getByLabelText("Condition");
    const priceField = screen.getByLabelText("Price field");
    const note = screen.getByLabelText("Note (optional)");

    expect(condition).toHaveAttribute("aria-describedby", "price-alert-condition-help");
    expect(priceField).toHaveAttribute("aria-describedby", "price-alert-field-help");
    expect(note).toHaveAttribute("aria-describedby", "price-alert-note-help");
    expect(note).toHaveAttribute("maxlength", "120");
    expect(screen.getByText("Fires whenever price is at or above the threshold.")).toBeInTheDocument();
    expect(screen.getByText("Field: last trade price.")).toBeInTheDocument();
    expect(screen.getByText("Optional operator context, up to 120 characters.")).toBeInTheDocument();

    fireEvent.change(condition, { target: { value: "crosses-up" } });
    fireEvent.change(priceField, { target: { value: "mid" } });

    expect(screen.getByText("Fires once when price rises through the threshold.")).toBeInTheDocument();
    expect(screen.getByText("Field: bid-ask midpoint.")).toBeInTheDocument();
  });

  it("links symbol and threshold validation feedback to the invalid inputs", () => {
    renderScreen(new MemoryStorage());

    const symbol = screen.getByLabelText("Symbol");
    const threshold = screen.getByLabelText("Threshold");

    expect(symbol).toHaveAttribute("aria-describedby", "price-alert-symbol-help");
    expect(symbol).not.toHaveAttribute("aria-errormessage");
    expect(threshold).toHaveAttribute("aria-describedby", "price-alert-threshold-help");
    expect(threshold).not.toHaveAttribute("aria-errormessage");

    fireEvent.change(symbol, { target: { value: "bad symbol!" } });
    fireEvent.change(threshold, { target: { value: "-1" } });

    expect(symbol).toHaveAttribute("aria-invalid", "true");
    expect(symbol).toHaveAttribute("aria-describedby", "price-alert-symbol-help price-alert-symbol-error");
    expect(symbol).toHaveAttribute("aria-errormessage", "price-alert-symbol-error");
    expect(screen.getByText("Use 1-16 letters, digits, or . / : _ -")).toHaveAttribute("id", "price-alert-symbol-error");

    expect(threshold).toHaveAttribute("aria-invalid", "true");
    expect(threshold).toHaveAttribute("aria-describedby", "price-alert-threshold-help price-alert-threshold-error");
    expect(threshold).toHaveAttribute("aria-errormessage", "price-alert-threshold-error");
    expect(screen.getByText("Threshold must be greater than 0.")).toHaveAttribute("id", "price-alert-threshold-error");
  });

  it("renders persisted alerts and identifies disabled and triggered states", () => {
    const storage = seedStorage({
      version: 1,
      alerts: [
        buildAlert({ id: "a-1", symbol: "AAPL", threshold: 200, enabled: false }),
        buildAlert({ id: "a-2", symbol: "MSFT", threshold: 350, enabled: false, triggeredAt: new Date().toISOString() })
      ],
      triggers: []
    });
    renderScreen(storage);
    expect(screen.getAllByText("AAPL").length).toBeGreaterThan(0);
    expect(screen.getAllByText("MSFT").length).toBeGreaterThan(0);
    expect(screen.getByText(/2 alerts/i)).toBeInTheDocument();
    expect(screen.getAllByText(/Disabled/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/Triggered/i).length).toBeGreaterThan(0);
  });

  it("links alert and trigger rows to visible detail panels", () => {
    const storage = seedStorage({
      version: 1,
      alerts: [
        buildAlert({ id: "a-1", symbol: "AAPL", threshold: 200, note: "Earnings watch", enabled: false }),
        buildAlert({ id: "a-2", symbol: "MSFT", threshold: 350, enabled: false, triggeredAt: "2026-05-12T15:00:00.000Z" })
      ],
      triggers: [
        buildTrigger({ id: "t-1", alertId: "a-1", symbol: "AAPL", acknowledged: false, note: "Gap through threshold" }),
        buildTrigger({ id: "t-2", alertId: "a-2", symbol: "MSFT", acknowledged: true })
      ]
    });

    renderScreen(storage);

    const triggeredRow = screen.getByRole("row", { name: "Inspect triggered alert AAPL" });
    expect(triggeredRow).toHaveAttribute("aria-selected", "true");
    expect(triggeredRow).toHaveAttribute("aria-expanded", "true");
    expect(triggeredRow).toHaveAttribute("aria-controls", "price-alert-trigger-detail-panel");
    expect(screen.getByRole("region", { name: "Triggered price alert detail for AAPL" }))
      .toHaveTextContent("Gap through threshold");

    const configuredRow = screen.getByRole("row", { name: "Inspect configured alert AAPL" });
    expect(configuredRow).toHaveAttribute("aria-selected", "true");
    expect(configuredRow).toHaveAttribute("aria-controls", "price-alert-list-detail-panel");
    expect(screen.getByRole("region", { name: "Configured price alert detail for AAPL" }))
      .toHaveTextContent("Earnings watch");

    const msftRow = screen.getByRole("row", { name: "Inspect configured alert MSFT" });
    act(() => {
      fireEvent.click(msftRow);
    });

    expect(msftRow).toHaveAttribute("aria-selected", "true");
    expect(msftRow).toHaveAttribute("aria-expanded", "true");
    expect(configuredRow).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByRole("region", { name: "Configured price alert detail for MSFT" }))
      .toHaveTextContent("Alert is not watching live quotes");
  });

  it("supports keyboard selection for configured alert detail", () => {
    const storage = seedStorage({
      version: 1,
      alerts: [
        buildAlert({ id: "a-1", symbol: "AAPL", enabled: false }),
        buildAlert({ id: "a-2", symbol: "MSFT", note: "Review after open", enabled: false })
      ],
      triggers: []
    });

    renderScreen(storage);

    const msftRow = screen.getByRole("row", { name: "Inspect configured alert MSFT" });
    msftRow.focus();

    act(() => {
      fireEvent.keyDown(msftRow, { key: "Enter" });
    });

    expect(msftRow).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("region", { name: "Configured price alert detail for MSFT" }))
      .toHaveTextContent("Review after open");
  });

  it("requires confirmation before deleting a configured alert", () => {
    const storage = seedStorage({
      version: 1,
      alerts: [
        buildAlert({ id: "a-1", symbol: "AAPL" })
      ],
      triggers: []
    });

    renderScreen(storage);

    fireEvent.click(screen.getByRole("button", { name: "Delete AAPL alert" }));

    expect(screen.getAllByText("AAPL").length).toBeGreaterThan(0);
    const confirmDelete = screen.getByRole("button", { name: "Confirm delete AAPL alert. This removes it from this browser." });
    expect(confirmDelete).toBeInTheDocument();
    expect(confirmDelete).toHaveAttribute("aria-describedby", "price-alert-delete-a-1-status");
    expect(screen.getByText("Delete confirmation pending for AAPL. Confirm delete removes it from this browser."))
      .toHaveAttribute("id", "price-alert-delete-a-1-status");
    expect(screen.getByRole("row", { name: "Inspect configured alert AAPL" }))
      .toHaveAttribute("aria-selected", "true");

    fireEvent.click(confirmDelete);

    expect(screen.queryByRole("row", { name: "Inspect configured alert AAPL" })).not.toBeInTheDocument();
    expect(screen.getByText("No alerts set.")).toBeInTheDocument();
  });

  it("seeds the symbol from ?symbol= query string", () => {
    renderScreen(new MemoryStorage(), ["/data/alerts?symbol=msft"]);
    expect((screen.getByLabelText("Symbol") as HTMLInputElement).value).toBe("MSFT");
  });

  it("links successful alert creation to live quote validation", () => {
    const fetchSnapshot = vi.fn().mockImplementation(() => new Promise<never>(() => {}));
    renderScreen(new MemoryStorage(), ["/data/alerts"], fetchSnapshot);

    fireEvent.change(screen.getByLabelText("Symbol"), { target: { value: "brk/b" } });
    fireEvent.change(screen.getByLabelText("Threshold"), { target: { value: "300" } });
    fireEvent.click(screen.getByRole("button", { name: "Create price alert" }));

    expect(screen.getByRole("status", { name: "Price alert created for BRK/B" }))
      .toHaveTextContent("Alert set: BRK/B last ≥ 300");
    expect(screen.getByRole("link", { name: "Open live quotes for BRK/B after creating price alert" }))
      .toHaveAttribute("href", "/data/quotes?symbol=BRK%2FB");
  });

  it("warns when alert changes cannot be persisted", () => {
    const fetchSnapshot = vi.fn().mockImplementation(() => new Promise<never>(() => {}));
    renderScreen(new FailingStorage(), ["/data/alerts"], fetchSnapshot);

    fireEvent.change(screen.getByLabelText("Symbol"), { target: { value: "AAPL" } });
    fireEvent.change(screen.getByLabelText("Threshold"), { target: { value: "200" } });
    fireEvent.click(screen.getByRole("button", { name: "Create price alert" }));

    expect(screen.getByText("Price alert storage failed. Alerts may not survive a browser reload."))
      .toHaveAttribute("id", "price-alert-storage-warning");
  });

  it("shows the notification CTA when permission is default", () => {
    (globalThis as { Notification?: unknown }).Notification = class {
      static permission: NotificationPermission = "default";
      static requestPermission = vi.fn().mockResolvedValue("granted");
    };
    renderScreen(new MemoryStorage());
    expect(screen.getByRole("button", { name: /Enable browser notifications for price alerts/i })).toBeInTheDocument();
  });
});
