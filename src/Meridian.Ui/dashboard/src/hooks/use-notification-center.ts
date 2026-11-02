import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useVisibleInterval } from "@/hooks/use-visible-interval";
import { usePriceAlerts } from "@/lib/price-alerts/service";
import { getOperatorInbox } from "@/lib/api";
import {
  buildNotificationCenterModel,
  type NotificationCenterModel
} from "@/lib/notification-center/merge";
import {
  dismissNotification,
  loadNotificationReadState,
  setNotificationStatus,
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
  fetchInbox?: (fundAccountId?: string) => Promise<OperatorInbox>;
  storage?: StorageLike | null;
  pollIntervalMs?: number;
}

export interface NotificationCenterApi extends NotificationCenterModel {
  markRead: (id: string) => void;
  markAllRead: () => void;
  dismiss: (id: string) => void;
  /** Reverts one inbox/system item to a prior read state and clears its dismissal. */
  restoreStatus: (id: string, status: { read: boolean }) => void;
}

/**
 * Shell-level notification center: fetches the operator inbox, reads system
 * events off the overview payload and price-alert triggers off the price-alerts
 * context, then merges them into one read-state-aware feed. Inbox fetch failures
 * are non-blocking — the center still surfaces the other feeds.
 */
export function useNotificationCenter(options: UseNotificationCenterOptions): NotificationCenterApi {
  const { overview, fundAccountId, fetchInbox = getOperatorInbox, storage, pollIntervalMs = DEFAULT_INBOX_POLL_INTERVAL_MS } = options;
  const priceAlerts = usePriceAlerts();
  const [inbox, setInbox] = useState<OperatorInbox | null>(null);
  const [readState, setReadState] = useState<NotificationReadState>(() => loadNotificationReadState(storage));

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
  const fundAccountRef = useRef(normalizedFundAccountId);
  fundAccountRef.current = normalizedFundAccountId;

  // Only the newest request may commit, so a slow response for a previous fund
  // cannot overwrite the inbox for the current one.
  const requestSeqRef = useRef(0);
  const loadInbox = useCallback(async () => {
    const seq = ++requestSeqRef.current;
    try {
      const next = await fetchInboxRef.current(fundAccountRef.current);
      if (mountedRef.current && seq === requestSeqRef.current) {
        setInbox(next);
      }
    } catch {
      // Inbox is one of three feeds — a failed fetch must not blank the center.
    }
  }, []);

  useEffect(() => {
    void loadInbox();
  }, [loadInbox, normalizedFundAccountId]);

  // Paused while the tab is hidden; refreshes once on return.
  useVisibleInterval(loadInbox, pollIntervalMs);

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
      setReadState((current) => {
        if (next === current) {
          return current;
        }
        saveNotificationReadState(next, storage);
        return next;
      });
    },
    [storage]
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

  // Undo for a single read or dismiss. Uses a functional update so a restore that
  // fires after other changes cannot roll back unrelated read state.
  const restoreStatus = useCallback(
    (id: string, status: { read: boolean }) => {
      setReadState((current) => {
        const next = setNotificationStatus(current, id, { read: status.read, dismissed: false });
        if (next === current) {
          return current;
        }
        saveNotificationReadState(next, storage);
        return next;
      });
    },
    [storage]
  );

  return { ...model, markRead, markAllRead, dismiss, restoreStatus };
}
