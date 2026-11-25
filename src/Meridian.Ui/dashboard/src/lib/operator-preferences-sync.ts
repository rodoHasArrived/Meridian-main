// Keeps the operator's presentation preferences (appearance, density, display time zone, page
// descriptions, browser shortcut overrides, pop-up notification choices) on the host so they follow the operator to another
// browser or machine. Each preference keeps its own browser store, which stays a local cache:
//
// 1. Once a session is known, read the operator's host preferences. A value the host holds wins
//    and is written to this browser. A value the host lacks keeps this browser's value, and the
//    merged set is saved back, so the first session on a host migrates existing local choices.
// 2. After that, every operator change in this browser saves the whole set (debounced).
//
// A failed read (no session, an older host without the endpoint, or no network) leaves the
// browser-only behaviour in place and saves nothing. A failed save keeps the local change and is
// retried with the next change. Either failure shows in Settings, through the sync status in
// operator-preference-events, with "Retry now". The values are display-only and never change
// stored data.

import { getOperatorPreferences, saveOperatorPreferences } from "@/lib/api/operator-workstation.api";
import { readSectionDescriptionsHidden, setSectionDescriptionsHidden } from "@/components/ui/section-description";
import { applyDensity, isDensity, readStoredDensity, writeStoredDensity, type Density } from "@/lib/density";
import { canonicalTimeZone, getDisplayTimeZone, setDisplayTimeZone } from "@/lib/display-time-zone";
import { applyKeyBindingOverrides, getKeyBindingOverrides } from "@/lib/keymap";
import {
  DEFAULT_NOTIFICATION_PREFERENCES,
  getNotificationPreferences,
  normalizeNotificationPreferences,
  setNotificationPreferences,
  type NotificationPreferences
} from "@/lib/notification-preferences";
import {
  getOperatorPreferenceSyncStatus,
  notifyOperatorPreferencesApplied,
  onOperatorPreferenceChanged,
  registerOperatorPreferenceSyncRetry,
  resetOperatorPreferenceSyncStatus,
  setOperatorPreferenceSyncStatus,
  type OperatorPreferenceSyncStatus
} from "@/lib/operator-preference-events";
import { applyAppearance, isAppearance, readStoredAppearance, writeStoredAppearance, type Appearance } from "@/lib/theme";
import type { OperatorPreferences, OperatorPreferenceValues } from "@/types/operator-preferences";

/** The full set as this browser holds it; every field explicit so sets compare reliably. */
export interface LocalOperatorPreferences {
  appearance: Appearance;
  density: Density;
  displayTimeZone: string;
  pageDescriptionsHidden: boolean;
  keyBindings: Record<string, string> | null;
  notifications: NotificationPreferences;
}

export function readLocalOperatorPreferences(operatorKey: string | null): LocalOperatorPreferences {
  const keyBindings = { ...getKeyBindingOverrides() } as Record<string, string>;
  return {
    appearance: readStoredAppearance(),
    density: readStoredDensity(),
    displayTimeZone: getDisplayTimeZone(),
    pageDescriptionsHidden: readSectionDescriptionsHidden(operatorKey),
    keyBindings: Object.keys(keyBindings).length > 0 ? sortRecord(keyBindings) : null,
    notifications: { ...getNotificationPreferences() }
  };
}

/** The host's notification set as this browser reads it, or null when the host holds none. */
function hostNotifications(values: OperatorPreferenceValues | undefined): NotificationPreferences | null {
  return values?.notifications && typeof values.notifications === "object" ? normalizeNotificationPreferences(values.notifications) : null;
}

function sortRecord(record: Record<string, string>): Record<string, string> {
  return Object.fromEntries(Object.entries(record).sort(([left], [right]) => (left < right ? -1 : left > right ? 1 : 0)));
}

function sameSet(left: LocalOperatorPreferences, right: LocalOperatorPreferences): boolean {
  return JSON.stringify(left) === JSON.stringify(right);
}

/**
 * The set to keep after reading the host: each value the host holds and this browser can use,
 * otherwise this browser's value. Unknown or invalid host values are ignored, never applied.
 * Shortcut overrides are a whole set: once the host holds a record (`updatedAt` is set), its
 * missing overrides mean "none", not "unknown", so a reset in another browser is honoured.
 */
export function mergeOperatorPreferences(
  host: OperatorPreferences | null | undefined,
  local: LocalOperatorPreferences
): LocalOperatorPreferences {
  const values = host?.preferences;
  const hostKeyBindings = values?.keyBindings && typeof values.keyBindings === "object" ? sortRecord(values.keyBindings) : null;
  const hostHasRecord = Boolean(host?.updatedAt);
  return {
    appearance: isAppearance(values?.appearance) ? values.appearance : local.appearance,
    density: isDensity(values?.density) ? values.density : local.density,
    displayTimeZone: canonicalTimeZone(values?.displayTimeZone) ?? local.displayTimeZone,
    pageDescriptionsHidden: typeof values?.pageDescriptionsHidden === "boolean" ? values.pageDescriptionsHidden : local.pageDescriptionsHidden,
    keyBindings: hostKeyBindings ?? (hostHasRecord ? null : local.keyBindings),
    notifications: hostNotifications(values) ?? local.notifications
  };
}

/** True when the host already holds exactly this set, so nothing needs saving back. */
function hostHolds(host: OperatorPreferences | null | undefined, set: LocalOperatorPreferences): boolean {
  const values = host?.preferences;
  if (!host?.updatedAt || !values) {
    return false;
  }
  return (
    values.appearance === set.appearance
    && values.density === set.density
    && canonicalTimeZone(values.displayTimeZone) === set.displayTimeZone
    && values.pageDescriptionsHidden === set.pageDescriptionsHidden
    && JSON.stringify(values.keyBindings ? sortRecord(values.keyBindings) : null) === JSON.stringify(set.keyBindings)
    // A host without a notification set holds the defaults, so a browser on the defaults saves nothing.
    && JSON.stringify(hostNotifications(values) ?? DEFAULT_NOTIFICATION_PREFERENCES) === JSON.stringify(set.notifications)
  );
}

/** Writes a set to this browser's stores and the page without announcing an operator change. */
export function applyOperatorPreferences(next: LocalOperatorPreferences, operatorKey: string | null): void {
  const current = readLocalOperatorPreferences(operatorKey);
  if (next.appearance !== current.appearance) {
    writeStoredAppearance(next.appearance, undefined, false);
    applyAppearance(next.appearance);
  }
  if (next.density !== current.density) {
    writeStoredDensity(next.density, undefined, undefined, false);
    applyDensity(next.density);
  }
  if (next.displayTimeZone !== current.displayTimeZone) {
    setDisplayTimeZone(next.displayTimeZone, false);
  }
  if (next.pageDescriptionsHidden !== current.pageDescriptionsHidden) {
    setSectionDescriptionsHidden(operatorKey, next.pageDescriptionsHidden, false);
  }
  if (JSON.stringify(next.keyBindings) !== JSON.stringify(current.keyBindings)) {
    applyKeyBindingOverrides(next.keyBindings);
  }
  if (JSON.stringify(next.notifications) !== JSON.stringify(current.notifications)) {
    setNotificationPreferences(next.notifications, false);
  }
  notifyOperatorPreferencesApplied();
}

function toWire(set: LocalOperatorPreferences): OperatorPreferenceValues {
  return { ...set, notifications: { ...set.notifications } };
}

export interface OperatorPreferenceSyncOptions {
  /** The session's `environment:name:role` key, which scopes the page-descriptions setting. */
  operatorKey: string | null;
  load?: (signal: AbortSignal) => Promise<OperatorPreferences>;
  save?: (preferences: OperatorPreferenceValues, signal: AbortSignal) => Promise<OperatorPreferences>;
  /** Delay before saving after a change, so a burst of changes saves once. */
  debounceMs?: number;
}

/** Starts syncing for one signed-in operator. Returns a stop function that cancels pending work. */
export function startOperatorPreferenceSync({
  operatorKey,
  load = (signal) => getOperatorPreferences({ signal }),
  save = (preferences, signal) => saveOperatorPreferences(preferences, { signal }),
  debounceMs = 500
}: OperatorPreferenceSyncOptions): () => void {
  const controller = new AbortController();
  let lastSaved: LocalOperatorPreferences | null = null;
  let lastSavedAt: string | null = null;
  let timer: ReturnType<typeof setTimeout> | null = null;
  let saving: Promise<void> = Promise.resolve();
  let unsubscribe: (() => void) | null = null;
  let loaded = false;

  const setStatus = (status: OperatorPreferenceSyncStatus) => {
    if (!controller.signal.aborted) {
      setOperatorPreferenceSyncStatus(status);
    }
  };

  const persist = (set: LocalOperatorPreferences) => {
    saving = saving.then(async () => {
      if (controller.signal.aborted) {
        return;
      }
      if (lastSaved && sameSet(lastSaved, set)) {
        if (getOperatorPreferenceSyncStatus().state === "saving") {
          setStatus({ state: "synced", lastSavedAt });
        }
        return;
      }
      setStatus({ state: "saving", lastSavedAt });
      try {
        const saved = await save(toWire(set), controller.signal);
        lastSaved = set;
        lastSavedAt = saved?.updatedAt ?? new Date().toISOString();
        setStatus({ state: "synced", lastSavedAt });
      } catch {
        // Keep the local change; the next change, or "Retry now" in Settings, retries the whole set.
        setStatus({ state: "failed", lastSavedAt, reason: "save" });
      }
    });
  };

  const scheduleSave = () => {
    if (controller.signal.aborted) {
      return;
    }
    if (timer !== null) {
      clearTimeout(timer);
    }
    timer = setTimeout(() => {
      timer = null;
      persist(readLocalOperatorPreferences(operatorKey));
    }, debounceMs);
  };

  const loadAndApply = () => {
    setStatus({ state: "loading" });
    void load(controller.signal)
      .then((host) => {
        if (controller.signal.aborted || loaded) {
          return;
        }
        loaded = true;
        const merged = mergeOperatorPreferences(host, readLocalOperatorPreferences(operatorKey));
        applyOperatorPreferences(merged, operatorKey);
        if (hostHolds(host, merged)) {
          lastSaved = merged;
          lastSavedAt = host.updatedAt ?? null;
          setStatus({ state: "synced", lastSavedAt });
        } else {
          lastSaved = null;
        }
        persist(merged);
        unsubscribe = onOperatorPreferenceChanged(scheduleSave);
      })
      .catch(() => {
        // No session, an older host or no network: preferences stay browser-only until a retry.
        setStatus({ state: "failed", lastSavedAt, reason: "load" });
      });
  };

  const retry = () => {
    if (controller.signal.aborted) {
      return;
    }
    if (!loaded) {
      loadAndApply();
      return;
    }
    if (timer !== null) {
      clearTimeout(timer);
      timer = null;
    }
    lastSaved = null;
    persist(readLocalOperatorPreferences(operatorKey));
  };

  const unregisterRetry = registerOperatorPreferenceSyncRetry(retry);
  loadAndApply();

  return () => {
    controller.abort();
    if (timer !== null) {
      clearTimeout(timer);
    }
    unsubscribe?.();
    unregisterRetry();
    resetOperatorPreferenceSyncStatus();
  };
}
