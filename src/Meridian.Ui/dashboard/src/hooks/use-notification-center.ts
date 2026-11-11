import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { usePriceAlerts } from "@/lib/price-alerts/service";
import { getOperatorInbox } from "@/lib/api";
import {
  buildNotificationCenterModel,
  type NotificationCenterModel
} from "@/lib/notification-center/merge";
import {
  dismissNotification,
  emptyNotificationReadState,
  loadNotificationReadState,
  markNotificationRead,
  markNotificationsRead,
  saveNotificationReadState,
  type NotificationReadState,
  type StorageLike
} from "@/lib/notification-center/read-state";
import type { NotificationEnvelope } from "@/lib/notification-center/types";
import type { OperatorInbox, SystemOverviewResponse } from "@/types";

const DEFAULT_INBOX_POLL_INTERVAL_MS = 60_000;

export interface UseNotificationCenterOptions {
  overview: SystemOverviewResponse | null;
  fundAccountId?: string | null;
  actorKey?: string;
  fetchInbox?: (fundAccountId?: string) => Promise<OperatorInbox>;
  storage?: StorageLike | null;
  pollIntervalMs?: number;
}

export interface NotificationCenterApi extends NotificationCenterModel {
  inboxStatus: "loading" | "ready" | "stale" | "unavailable";
  lastCheckedAt: string | null;
  retry: () => void;
  markRead: (id: string) => void;
  markAllRead: () => void;
  dismiss: (id: string) => void;
}

/**
 * Shell-level notification center: fetches the operator inbox, reads system
 * events off the overview payload and price-alert triggers off the price-alerts
 * context, then merges them into one read-state-aware feed. Inbox fetch failures
 * are non-blocking — the center still surfaces the other feeds.
 */
export function useNotificationCenter(options: UseNotificationCenterOptions): NotificationCenterApi {
  const { overview, fundAccountId, actorKey = "", fetchInbox = getOperatorInbox, storage, pollIntervalMs = DEFAULT_INBOX_POLL_INTERVAL_MS } = options;
  const priceAlerts = usePriceAlerts();
  const contextKey = JSON.stringify([fundAccountId ?? null, actorKey]);
  const [inboxResult, setInboxResult] = useState<{
    contextKey: string; inbox: OperatorInbox | null;
    status: "loading" | "ready" | "stale" | "unavailable"; lastCheckedAt: string | null;
  } | null>(null);
  const [retryAttempt, setRetryAttempt] = useState(0);
  // Scope-gated during render too: a changed account cannot flash old inbox rows.
  const current = inboxResult?.contextKey === contextKey ? inboxResult : null;
  const inbox = current?.inbox ?? null;
  const scopedStorage = useMemo<StorageLike>(() => {
    const target = () => storage ?? window.localStorage;
    const key = (name: string) => `${name}:${encodeURIComponent(contextKey)}`;
    return { getItem: name => target().getItem(key(name)), setItem: (name, value) => target().setItem(key(name), value), removeItem: name => target().removeItem(key(name)) };
  }, [contextKey, storage]);
  const [readResult, setReadResult] = useState(() => ({ contextKey, state: loadNotificationReadState(scopedStorage) }));
  const readState = readResult.contextKey === contextKey ? readResult.state : emptyNotificationReadState();
  useEffect(() => { setReadResult({ contextKey, state: loadNotificationReadState(scopedStorage) }); }, [contextKey, scopedStorage]);

  const mountedRef = useRef(true);
  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  // Hold fetchInbox in a ref so an inline (unstable) fetchInbox prop cannot re-run
  // the poll effect every render — the effect only reacts to fund account + cadence.
  const fetchInboxRef = useRef(fetchInbox);
  fetchInboxRef.current = fetchInbox;

  const normalizedFundAccountId = fundAccountId ?? undefined;
  useEffect(() => {
    let cancelled = false;

    let loading = false;
    const load = async () => {
      if (loading) return;
      loading = true;
      setInboxResult(previous => previous?.contextKey === contextKey
        ? { ...previous, status: "loading" }
        : { contextKey, inbox: null, status: "loading", lastCheckedAt: null });
      try {
        const next = await fetchInboxRef.current(normalizedFundAccountId);
        if (!cancelled && mountedRef.current) {
          setInboxResult({ contextKey, inbox: next, status: "ready", lastCheckedAt: new Date().toISOString() });
        }
      } catch {
        if (!cancelled && mountedRef.current) {
          setInboxResult(previous => {
            const retained = previous?.contextKey === contextKey ? previous : null;
            return { contextKey, inbox: retained?.inbox ?? null,
              status: retained?.inbox ? "stale" : "unavailable", lastCheckedAt: retained?.lastCheckedAt ?? null };
          });
        }
      } finally { loading = false; }
    };

    void load();
    const interval = window.setInterval(() => void load(), pollIntervalMs);
    return () => {
      cancelled = true;
      window.clearInterval(interval);
    };
  }, [normalizedFundAccountId, contextKey, pollIntervalMs, retryAttempt]);
  const retry = useCallback(() => setRetryAttempt(value => value + 1), []);

  const model = useMemo(
    () =>
      buildNotificationCenterModel({
        inbox: inbox?.items ?? [],
        systemEvents: overview?.recentEvents ?? [],
        priceTriggers: priceAlerts.state.triggers,
        readState
      }),
    [inbox, overview, priceAlerts.state.triggers, readState]
  );

  const findEnvelope = useCallback(
    (id: string): NotificationEnvelope | undefined => model.notifications.find((entry) => entry.id === id),
    [model.notifications]
  );

  const commitReadState = useCallback(
    (next: NotificationReadState) => {
      saveNotificationReadState(next, scopedStorage);
      setReadResult({ contextKey, state: next });
    },
    [contextKey, scopedStorage]
  );

  const markRead = useCallback(
    (id: string) => {
      const envelope = findEnvelope(id);
      if (!envelope) {
        return;
      }
      if (envelope.source === "price-alert") {
        priceAlerts.acknowledgeTrigger(envelope.sourceId);
        return;
      }
      commitReadState(markNotificationRead(readState, id));
    },
    [commitReadState, findEnvelope, priceAlerts, readState]
  );

  const markAllRead = useCallback(() => {
    priceAlerts.acknowledgeAllTriggers();
    const ids = model.notifications
      .filter((entry) => entry.source !== "price-alert" && !entry.read)
      .map((entry) => entry.id);
    if (ids.length > 0) {
      commitReadState(markNotificationsRead(readState, ids));
    }
  }, [commitReadState, model.notifications, priceAlerts, readState]);

  const dismiss = useCallback(
    (id: string) => {
      const envelope = findEnvelope(id);
      if (!envelope || !envelope.dismissible) {
        return;
      }
      commitReadState(dismissNotification(readState, id));
    },
    [commitReadState, findEnvelope, readState]
  );

  return { ...model, inboxStatus: current?.status ?? "loading", lastCheckedAt: current?.lastCheckedAt ?? null, retry, markRead, markAllRead, dismiss };
}
