import {
  PRICE_ALERT_MAX_TRIGGER_HISTORY,
  PRICE_ALERT_STORAGE_KEY,
  type PriceAlert,
  type PriceAlertStorageState,
  type PriceAlertTrigger
} from "./index";

export interface StorageLike {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

export type PriceAlertStorageWriteStatus = "saved" | "unavailable" | "failed";

function emptyState(): PriceAlertStorageState {
  return { version: 1, alerts: [], triggers: [] };
}

function resolveStorage(storage?: StorageLike | null): StorageLike | null {
  if (storage) {
    return storage;
  }
  if (typeof window === "undefined") {
    return null;
  }
  try {
    return window.localStorage;
  } catch {
    return null;
  }
}

export function loadPriceAlertState(storage?: StorageLike | null): PriceAlertStorageState {
  const target = resolveStorage(storage);
  if (!target) {
    return emptyState();
  }

  let raw: string | null = null;
  try {
    raw = target.getItem(PRICE_ALERT_STORAGE_KEY);
  } catch {
    return emptyState();
  }
  if (!raw) {
    return emptyState();
  }

  try {
    const parsed = JSON.parse(raw) as unknown;
    return normalizeState(parsed);
  } catch {
    return emptyState();
  }
}

export function savePriceAlertState(state: PriceAlertStorageState, storage?: StorageLike | null): PriceAlertStorageWriteStatus {
  const target = resolveStorage(storage);
  if (!target) {
    return "unavailable";
  }
  try {
    target.setItem(PRICE_ALERT_STORAGE_KEY, JSON.stringify(state));
    return "saved";
  } catch {
    // Quota exhausted or storage unavailable: keep in-memory state, but let callers warn the operator.
    return "failed";
  }
}

export function appendTrigger(state: PriceAlertStorageState, trigger: PriceAlertTrigger): PriceAlertStorageState {
  const triggers = [trigger, ...state.triggers].slice(0, PRICE_ALERT_MAX_TRIGGER_HISTORY);
  return { ...state, triggers };
}

export function updateAlert(state: PriceAlertStorageState, alertId: string, mutate: (alert: PriceAlert) => PriceAlert): PriceAlertStorageState {
  return {
    ...state,
    alerts: state.alerts.map((alert) => (alert.id === alertId ? mutate(alert) : alert))
  };
}

function normalizeState(value: unknown): PriceAlertStorageState {
  if (!isRecord(value)) {
    return emptyState();
  }

  const alerts = Array.isArray(value.alerts)
    ? value.alerts.map(normalizeAlert).filter((alert): alert is PriceAlert => alert !== null)
    : [];
  const triggers = Array.isArray(value.triggers)
    ? value.triggers.map(normalizeTrigger).filter((trigger): trigger is PriceAlertTrigger => trigger !== null)
    : [];

  return { version: 1, alerts, triggers: triggers.slice(0, PRICE_ALERT_MAX_TRIGGER_HISTORY) };
}

function normalizeAlert(raw: unknown): PriceAlert | null {
  if (!isRecord(raw)) {
    return null;
  }
  const id = asString(raw.id);
  const symbol = asString(raw.symbol)?.toUpperCase();
  const condition = asCondition(raw.condition);
  const field = asField(raw.field);
  const threshold = asNumber(raw.threshold);
  const createdAt = asString(raw.createdAt);
  if (!id || !symbol || !condition || !field || threshold === null || !createdAt) {
    return null;
  }
  return {
    id,
    symbol,
    condition,
    field,
    threshold,
    note: asString(raw.note) ?? null,
    createdAt,
    snoozedUntil: asString(raw.snoozedUntil) ?? null,
    enabled: typeof raw.enabled === "boolean" ? raw.enabled : true,
    triggeredAt: asString(raw.triggeredAt) ?? null,
    lastObservedPrice: asNumber(raw.lastObservedPrice),
    lastObservedAt: asString(raw.lastObservedAt) ?? null
  };
}

function normalizeTrigger(raw: unknown): PriceAlertTrigger | null {
  if (!isRecord(raw)) {
    return null;
  }
  const id = asString(raw.id);
  const alertId = asString(raw.alertId);
  const symbol = asString(raw.symbol)?.toUpperCase();
  const condition = asCondition(raw.condition);
  const field = asField(raw.field);
  const threshold = asNumber(raw.threshold);
  const triggeredPrice = asNumber(raw.triggeredPrice);
  const triggeredAt = asString(raw.triggeredAt);
  if (!id || !alertId || !symbol || !condition || !field || threshold === null || triggeredPrice === null || !triggeredAt) {
    return null;
  }
  return {
    id,
    alertId,
    symbol,
    condition,
    field,
    threshold,
    triggeredPrice,
    triggeredAt,
    acknowledged: typeof raw.acknowledged === "boolean" ? raw.acknowledged : false,
    note: asString(raw.note) ?? null
  };
}

function asString(value: unknown): string | null {
  return typeof value === "string" && value.length > 0 ? value : null;
}

function asNumber(value: unknown): number | null {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

function asCondition(value: unknown): PriceAlert["condition"] | null {
  return value === "above" || value === "below" || value === "crosses-up" || value === "crosses-down"
    ? value
    : null;
}

function asField(value: unknown): PriceAlert["field"] | null {
  return value === "last" || value === "bid" || value === "ask" || value === "mid" ? value : null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
