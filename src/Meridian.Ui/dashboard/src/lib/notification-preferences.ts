// The operator's pop-up notification choices (Settings > Preferences > Notifications): a master
// switch, five notification types, and quiet hours. The same set is synced through the operator's
// host preferences, where the WPF desktop applies it to its toasts. In this browser the master
// switch and quiet hours govern the price-alert system pop-ups; the five types name desktop
// notifications the browser does not raise. Pop-ups only: the notification center, alert history and
// every record are unaffected. Quiet hours are wall-clock HH:mm times in the operator's display time
// zone; a window whose start is later than its end runs past midnight, the end minute is not quiet,
// and an equal start and end is an empty window. Persisted per browser in localStorage as a cache.

import { getDisplayTimeZone } from "@/lib/display-time-zone";
import { notifyOperatorPreferenceChanged } from "@/lib/operator-preference-events";

export const NOTIFICATION_PREFERENCES_STORAGE_KEY = "meridian.workstation.notification-preferences.v1";

export interface NotificationPreferences {
  enabled: boolean;
  connectionStatus: boolean;
  errors: boolean;
  backfillComplete: boolean;
  dataGaps: boolean;
  storageWarnings: boolean;
  quietHoursEnabled: boolean;
  /** HH:mm, 24-hour, in the display time zone. */
  quietHoursStart: string;
  /** HH:mm, 24-hour, in the display time zone; this minute is no longer quiet. */
  quietHoursEnd: string;
}

export type NotificationTypeKey = "connectionStatus" | "errors" | "backfillComplete" | "dataGaps" | "storageWarnings";

/** The five notification types, in the order the Settings card lists them. */
export const NOTIFICATION_TYPES: ReadonlyArray<{ key: NotificationTypeKey; label: string }> = [
  { key: "connectionStatus", label: "Connection status changes" },
  { key: "errors", label: "Errors and warnings" },
  { key: "backfillComplete", label: "Backfill completion" },
  { key: "dataGaps", label: "Data gaps detected" },
  { key: "storageWarnings", label: "Storage warnings (low space)" }
];

export const DEFAULT_NOTIFICATION_PREFERENCES: Readonly<NotificationPreferences> = Object.freeze({
  enabled: true,
  connectionStatus: true,
  errors: true,
  backfillComplete: true,
  dataGaps: true,
  storageWarnings: true,
  quietHoursEnabled: false,
  quietHoursStart: "22:00",
  quietHoursEnd: "07:00"
});

const CLOCK_PATTERN = /^([01]\d|2[0-3]):([0-5]\d)$/;

/** The trimmed time when `value` is a 24-hour HH:mm time such as "22:00", otherwise null. */
export function parseClockTime(value: unknown): string | null {
  if (typeof value !== "string") {
    return null;
  }
  const trimmed = value.trim();
  return CLOCK_PATTERN.test(trimmed) ? trimmed : null;
}

function minutesOf(clock: string): number {
  const [hours, minutes] = clock.split(":").map(Number);
  return hours! * 60 + minutes!;
}

/**
 * A full set from any partial or untrusted value: each field this browser can use, otherwise the
 * default. Used for stored values and for values read from the host.
 */
export function normalizeNotificationPreferences(value: unknown): NotificationPreferences {
  const source = value && typeof value === "object" ? (value as Record<string, unknown>) : {};
  const flag = (key: keyof NotificationPreferences) =>
    typeof source[key] === "boolean" ? (source[key] as boolean) : (DEFAULT_NOTIFICATION_PREFERENCES[key] as boolean);
  return {
    enabled: flag("enabled"),
    connectionStatus: flag("connectionStatus"),
    errors: flag("errors"),
    backfillComplete: flag("backfillComplete"),
    dataGaps: flag("dataGaps"),
    storageWarnings: flag("storageWarnings"),
    quietHoursEnabled: flag("quietHoursEnabled"),
    quietHoursStart: parseClockTime(source.quietHoursStart) ?? DEFAULT_NOTIFICATION_PREFERENCES.quietHoursStart,
    quietHoursEnd: parseClockTime(source.quietHoursEnd) ?? DEFAULT_NOTIFICATION_PREFERENCES.quietHoursEnd
  };
}

/** The wall-clock minute of `instant` in `timeZone`, from midnight. */
function minuteOfDay(instant: Date, timeZone: string): number {
  const parts = new Intl.DateTimeFormat("en-GB", { timeZone, hour: "2-digit", minute: "2-digit", hourCycle: "h23" }).formatToParts(instant);
  const hour = Number(parts.find((part) => part.type === "hour")?.value ?? 0);
  const minute = Number(parts.find((part) => part.type === "minute")?.value ?? 0);
  return (hour % 24) * 60 + minute;
}

/** True when quiet hours are on and `instant`, read in `timeZone`, falls inside the window. */
export function isQuietHoursActive(
  preferences: NotificationPreferences,
  instant: Date = new Date(),
  timeZone: string = getDisplayTimeZone()
): boolean {
  if (!preferences.quietHoursEnabled) {
    return false;
  }
  const start = minutesOf(preferences.quietHoursStart);
  const end = minutesOf(preferences.quietHoursEnd);
  if (start === end) {
    return false;
  }
  const now = minuteOfDay(instant, timeZone);
  return start < end ? now >= start && now < end : now >= start || now < end;
}

/** Whether a pop-up may show now: the master switch is on and quiet hours are not holding pop-ups. */
export function popUpsAllowed(instant: Date = new Date(), preferences: NotificationPreferences = getNotificationPreferences()): boolean {
  return preferences.enabled && !isQuietHoursActive(preferences, instant);
}

function storage(): Storage | null {
  try {
    return typeof localStorage === "undefined" ? null : localStorage;
  } catch {
    return null;
  }
}

function readStored(): NotificationPreferences {
  try {
    const raw = storage()?.getItem(NOTIFICATION_PREFERENCES_STORAGE_KEY);
    return normalizeNotificationPreferences(raw ? JSON.parse(raw) : null);
  } catch {
    return { ...DEFAULT_NOTIFICATION_PREFERENCES };
  }
}

let current: NotificationPreferences | null = null;
const listeners = new Set<() => void>();

export function getNotificationPreferences(): NotificationPreferences {
  current ??= readStored();
  return current;
}

/**
 * Replaces this browser's set. `announce` (the default) reports an operator change so the host sync
 * saves it; the sync itself passes false when writing values it read from the host.
 */
export function setNotificationPreferences(next: NotificationPreferences, announce = true): void {
  const normalized = normalizeNotificationPreferences(next);
  current = normalized;
  try {
    storage()?.setItem(NOTIFICATION_PREFERENCES_STORAGE_KEY, JSON.stringify(normalized));
  } catch {
    // Storage blocked: the choice still applies for this page.
  }
  for (const listener of [...listeners]) {
    listener();
  }
  if (announce) {
    notifyOperatorPreferenceChanged();
  }
}

export function subscribeNotificationPreferences(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

/** Re-reads storage; for tests that clear localStorage between cases. */
export function reloadNotificationPreferences(): void {
  current = readStored();
  for (const listener of [...listeners]) {
    listener();
  }
}
