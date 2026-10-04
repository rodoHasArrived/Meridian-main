import { createContext, createElement, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { useQuotesStream } from "@/hooks/use-quotes-stream";
import { getLiveQuotesSnapshot } from "@/lib/api";
import { isAbortError, isApiError } from "@/lib/api-errors";
import type { QuotesSnapshotItem, QuotesSnapshotResponse } from "@/types";
import { describeCondition, shouldTrigger } from "./evaluator";
import {
  appendTrigger,
  loadPriceAlertState,
  savePriceAlertState,
  updateAlert,
  type StorageLike
} from "./storage";
import type { PriceAlert, PriceAlertDraft, PriceAlertField, PriceAlertStorageState, PriceAlertTrigger } from "./index";

export const PRICE_ALERTS_POLL_INTERVAL_MS = 5000;

/**
 * Shown once when the quote snapshot refuses the operator, in place of the transport error the
 * refusal would otherwise surface. It names the cause so the banner reads as a permission boundary
 * rather than an outage the operator should chase.
 */
export const quoteAccessDeniedMessage =
  "Price alerts need market-data permission, which this operator profile does not hold.";

/** 401 and 403 both mean this operator will not be served quotes; neither changes on retry. */
function isUnauthorizedQuoteAccess(error: unknown): boolean {
  return isApiError(error) && (error.status === 401 || error.status === 403);
}

export interface PriceAlertsApi {
  state: PriceAlertStorageState;
  enabledCount: number;
  unacknowledgedCount: number;
  lastPollAt: string | null;
  pollErrorMessage: string | null;
  persistenceErrorMessage: string | null;
  notificationPermission: NotificationPermission | "unsupported";
  requestNotificationPermission: () => Promise<NotificationPermission | "unsupported">;
  createAlert: (draft: PriceAlertDraft) => PriceAlert;
  updateAlertFields: (alertId: string, patch: Partial<Omit<PriceAlert, "id" | "createdAt">>) => void;
  deleteAlert: (alertId: string) => void;
  resetAlert: (alertId: string) => void;
  snoozeAlert: (alertId: string, until: Date | null) => void;
  acknowledgeTrigger: (triggerId: string) => void;
  acknowledgeAllTriggers: () => void;
  clearTriggers: () => void;
  evaluateSnapshot: (snapshot: QuotesSnapshotResponse) => void;
}

interface ServiceOptions {
  storage?: StorageLike | null;
  fetchSnapshot?: typeof getLiveQuotesSnapshot;
  pollIntervalMs?: number;
  notify?: (title: string, body: string) => void;
  now?: () => Date;
}

export function usePriceAlertsService(options: ServiceOptions = {}): PriceAlertsApi {
  const {
    storage,
    fetchSnapshot = getLiveQuotesSnapshot,
    pollIntervalMs = PRICE_ALERTS_POLL_INTERVAL_MS,
    notify,
    now = () => new Date()
  } = options;

  const [state, setState] = useState<PriceAlertStorageState>(() => loadPriceAlertState(storage));
  const [lastPollAt, setLastPollAt] = useState<string | null>(null);
  const [pollErrorMessage, setPollErrorMessage] = useState<string | null>(null);
  const [persistenceErrorMessage, setPersistenceErrorMessage] = useState<string | null>(null);
  const [notificationPermission, setNotificationPermission] = useState<NotificationPermission | "unsupported">(() => readPermission());

  const stateRef = useRef(state);
  stateRef.current = state;

  const persist = useCallback((next: PriceAlertStorageState) => {
    stateRef.current = next;
    const writeStatus = savePriceAlertState(next, storage);
    setPersistenceErrorMessage(buildPersistenceErrorMessage(writeStatus));
    setState(next);
  }, [storage]);

  const fireNotification = useCallback((title: string, body: string) => {
    if (notify) {
      notify(title, body);
      return;
    }
    if (typeof window === "undefined" || typeof Notification === "undefined") {
      return;
    }
    if (Notification.permission !== "granted") {
      return;
    }
    try {
      new Notification(title, { body, tag: title });
    } catch {
      // Browser refused notification — ignore.
    }
  }, [notify]);

  const evaluateSnapshot = useCallback((snapshot: QuotesSnapshotResponse) => {
    const current = stateRef.current;
    if (current.alerts.length === 0) {
      return;
    }

    const bySymbol = new Map<string, QuotesSnapshotItem>();
    for (const quote of snapshot.quotes) {
      if (quote.symbol) {
        bySymbol.set(quote.symbol.toUpperCase(), quote);
      }
    }

    const triggeredNow: PriceAlertTrigger[] = [];
    const nowIso = now().toISOString();
    let next: PriceAlertStorageState = current;

    for (const alert of current.alerts) {
      if (!alert.enabled) {
        continue;
      }
      if (alert.snoozedUntil && new Date(alert.snoozedUntil).getTime() > now().getTime()) {
        continue;
      }
      const quote = bySymbol.get(alert.symbol);
      if (!quote) {
        continue;
      }
      const price = readField(quote, alert.field);
      if (price === null) {
        continue;
      }

      const previousPrice = alert.lastObservedPrice;
      const fired = shouldTrigger({
        condition: alert.condition,
        threshold: alert.threshold,
        previousPrice,
        currentPrice: price
      });

      next = updateAlert(next, alert.id, (a) => ({
        ...a,
        lastObservedPrice: price,
        lastObservedAt: nowIso
      }));

      if (fired) {
        const trigger: PriceAlertTrigger = {
          id: `trigger-${alert.id}-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 6)}`,
          alertId: alert.id,
          symbol: alert.symbol,
          condition: alert.condition,
          field: alert.field,
          threshold: alert.threshold,
          triggeredPrice: price,
          triggeredAt: nowIso,
          acknowledged: false,
          note: alert.note
        };
        next = appendTrigger(next, trigger);
        next = updateAlert(next, alert.id, (a) => ({
          ...a,
          enabled: false,
          triggeredAt: nowIso
        }));
        triggeredNow.push(trigger);
      }
    }

    if (next !== current) {
      persist(next);
    }

    if (triggeredNow.length > 0) {
      for (const trigger of triggeredNow) {
        const title = `${trigger.symbol} — ${describeCondition(trigger.condition, trigger.threshold, trigger.field)}`;
        const body = `Triggered at ${formatPrice(trigger.triggeredPrice)} (${new Date(trigger.triggeredAt).toLocaleTimeString()})`;
        fireNotification(title, body);
      }
    }
  }, [fireNotification, now, persist]);

  const watchedSymbols = useMemo(() => {
    const symbols = new Set<string>();
    for (const alert of state.alerts) {
      if (!alert.enabled) {
        continue;
      }
      if (alert.snoozedUntil && new Date(alert.snoozedUntil).getTime() > Date.now()) {
        continue;
      }
      symbols.add(alert.symbol);
    }
    return Array.from(symbols).sort();
  }, [state.alerts]);

  const quotesStream = useQuotesStream(watchedSymbols);

  useEffect(() => {
    if (!quotesStream.snapshot || watchedSymbols.length === 0) {
      return;
    }

    evaluateSnapshot(quotesStream.snapshot);
    setLastPollAt(now().toISOString());
    setPollErrorMessage(null);
  }, [evaluateSnapshot, now, quotesStream.snapshot, watchedSymbols.length]);

  useEffect(() => {
    if (watchedSymbols.length === 0) {
      setPollErrorMessage(null);
      return;
    }

    let cancelled = false;
    let interval: number | null = null;
    const controller = new AbortController();

    const tick = async () => {
      try {
        const snapshot = await fetchSnapshot(watchedSymbols, { signal: controller.signal });
        if (cancelled) {
          return;
        }
        evaluateSnapshot(snapshot);
        setLastPollAt(now().toISOString());
        setPollErrorMessage(null);
      } catch (error) {
        if (cancelled || isAbortError(error)) {
          return;
        }
        // The alerts provider is mounted for the whole shell, so an operator without market-data
        // permission carries it on every screen. Authorization does not change between ticks:
        // retrying it every five seconds produces standing refused traffic and an error banner that
        // can never clear, so stand down and let the next mount or symbol change try again.
        if (isUnauthorizedQuoteAccess(error)) {
          setPollErrorMessage(quoteAccessDeniedMessage);
          if (interval !== null) {
            window.clearInterval(interval);
            interval = null;
          }
          return;
        }
        setPollErrorMessage(error instanceof Error ? error.message : "Failed to refresh quotes");
      }
    };

    void tick();
    if (quotesStream.healthy) {
      // Pushed snapshots drive alert evaluation; polling resumes if the stream degrades.
      return () => {
        cancelled = true;
        controller.abort();
      };
    }

    interval = window.setInterval(() => void tick(), pollIntervalMs);
    return () => {
      cancelled = true;
      controller.abort();
      if (interval !== null) {
        window.clearInterval(interval);
        interval = null;
      }
    };
  }, [evaluateSnapshot, fetchSnapshot, pollIntervalMs, quotesStream.healthy, watchedSymbols.join("|")]);

  const createAlert = useCallback((draft: PriceAlertDraft) => {
    const id = `alert-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
    const alert: PriceAlert = {
      id,
      symbol: draft.symbol.toUpperCase().trim(),
      condition: draft.condition,
      field: draft.field,
      threshold: draft.threshold,
      note: draft.note?.trim() ? draft.note.trim() : null,
      createdAt: now().toISOString(),
      snoozedUntil: null,
      enabled: true,
      triggeredAt: null,
      lastObservedPrice: null,
      lastObservedAt: null
    };
    const next: PriceAlertStorageState = {
      ...stateRef.current,
      alerts: [alert, ...stateRef.current.alerts]
    };
    persist(next);
    return alert;
  }, [now, persist]);

  const updateAlertFields = useCallback((alertId: string, patch: Partial<Omit<PriceAlert, "id" | "createdAt">>) => {
    const next = updateAlert(stateRef.current, alertId, (alert) => ({ ...alert, ...patch }));
    persist(next);
  }, [persist]);

  const deleteAlert = useCallback((alertId: string) => {
    const next: PriceAlertStorageState = {
      ...stateRef.current,
      alerts: stateRef.current.alerts.filter((alert) => alert.id !== alertId)
    };
    persist(next);
  }, [persist]);

  const resetAlert = useCallback((alertId: string) => {
    const next = updateAlert(stateRef.current, alertId, (alert) => ({
      ...alert,
      enabled: true,
      triggeredAt: null,
      lastObservedPrice: null,
      lastObservedAt: null,
      snoozedUntil: null
    }));
    persist(next);
  }, [persist]);

  const snoozeAlert = useCallback((alertId: string, until: Date | null) => {
    const next = updateAlert(stateRef.current, alertId, (alert) => ({
      ...alert,
      snoozedUntil: until ? until.toISOString() : null
    }));
    persist(next);
  }, [persist]);

  const acknowledgeTrigger = useCallback((triggerId: string) => {
    const next: PriceAlertStorageState = {
      ...stateRef.current,
      triggers: stateRef.current.triggers.map((trigger) =>
        trigger.id === triggerId ? { ...trigger, acknowledged: true } : trigger
      )
    };
    persist(next);
  }, [persist]);

  const acknowledgeAllTriggers = useCallback(() => {
    const next: PriceAlertStorageState = {
      ...stateRef.current,
      triggers: stateRef.current.triggers.map((trigger) => ({ ...trigger, acknowledged: true }))
    };
    persist(next);
  }, [persist]);

  const clearTriggers = useCallback(() => {
    const next: PriceAlertStorageState = { ...stateRef.current, triggers: [] };
    persist(next);
  }, [persist]);

  const requestNotificationPermission = useCallback(async (): Promise<NotificationPermission | "unsupported"> => {
    if (typeof window === "undefined" || typeof Notification === "undefined") {
      return "unsupported";
    }
    try {
      const result = await Notification.requestPermission();
      setNotificationPermission(result);
      return result;
    } catch {
      return Notification.permission;
    }
  }, []);

  const enabledCount = useMemo(
    () => state.alerts.filter((alert) => alert.enabled).length,
    [state.alerts]
  );

  const unacknowledgedCount = useMemo(
    () => state.triggers.filter((trigger) => !trigger.acknowledged).length,
    [state.triggers]
  );

  return {
    state,
    enabledCount,
    unacknowledgedCount,
    lastPollAt,
    pollErrorMessage,
    persistenceErrorMessage,
    notificationPermission,
    requestNotificationPermission,
    createAlert,
    updateAlertFields,
    deleteAlert,
    resetAlert,
    snoozeAlert,
    acknowledgeTrigger,
    acknowledgeAllTriggers,
    clearTriggers,
    evaluateSnapshot
  };
}

function buildPersistenceErrorMessage(status: ReturnType<typeof savePriceAlertState>): string | null {
  if (status === "saved") {
    return null;
  }

  if (status === "unavailable") {
    return "Price alert storage is unavailable. Alerts will only last for this browser tab.";
  }

  return "Price alert storage failed. Alerts may not survive a browser reload.";
}

function readField(quote: QuotesSnapshotItem, field: PriceAlertField): number | null {
  switch (field) {
    case "last":
      return numberOrNull(quote.lastPrice);
    case "bid":
      return numberOrNull(quote.bidPrice);
    case "ask":
      return numberOrNull(quote.askPrice);
    case "mid":
      return numberOrNull(quote.midPrice);
  }
}

function numberOrNull(value: number | null | undefined): number | null {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

function readPermission(): NotificationPermission | "unsupported" {
  if (typeof window === "undefined" || typeof Notification === "undefined") {
    return "unsupported";
  }
  return Notification.permission;
}

function formatPrice(value: number): string {
  return value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 4 });
}

const PriceAlertsContext = createContext<PriceAlertsApi | null>(null);

export interface PriceAlertsProviderProps {
  children: ReactNode;
  options?: ServiceOptions;
}

export function PriceAlertsProvider({ children, options }: PriceAlertsProviderProps) {
  const api = usePriceAlertsService(options);
  return createElement(PriceAlertsContext.Provider, { value: api }, children);
}

export function usePriceAlerts(): PriceAlertsApi {
  const ctx = useContext(PriceAlertsContext);
  if (!ctx) {
    throw new Error("usePriceAlerts must be used within <PriceAlertsProvider>.");
  }
  return ctx;
}
