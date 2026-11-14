// The operator's display time zone (UX consolidation P3). Workstation timestamps render in UTC by
// default; an operator can pick an IANA zone in Settings > Preferences, and every zone-aware
// formatter in `@/lib/format` (`formatTimestamp`, `<TimeText>`) reads it from here. The preference
// is per browser and persisted in localStorage; storage that is blocked or holds an unknown zone
// leaves the default in place. No React: `<TimeText>` subscribes through `subscribeDisplayTimeZone`.

export const DISPLAY_TIME_ZONE_STORAGE_KEY = "meridian.workstation.display-time-zone.v1";
export const DEFAULT_DISPLAY_TIME_ZONE = "UTC";

/** UTC plus the desk zones an operator most often reads in, for the Settings picker. */
export const COMMON_DISPLAY_TIME_ZONES: readonly string[] = [
  "UTC",
  "America/New_York",
  "America/Chicago",
  "America/Los_Angeles",
  "America/Sao_Paulo",
  "Europe/London",
  "Europe/Paris",
  "Europe/Zurich",
  "Asia/Dubai",
  "Asia/Hong_Kong",
  "Asia/Singapore",
  "Asia/Tokyo",
  "Australia/Sydney"
];

/**
 * The runtime's canonical spelling of an IANA zone ("america/new_york" -> "America/New_York"), or
 * null when the zone is unknown ("Mars/Base") or blank.
 */
export function canonicalTimeZone(zone: string | null | undefined): string | null {
  const candidate = zone?.trim();
  if (!candidate) {
    return null;
  }
  try {
    // Validation only: the formatter is never used to render text.
    return new Intl.DateTimeFormat("en-US", { timeZone: candidate }).resolvedOptions().timeZone;
  } catch {
    return null;
  }
}

/** Whether the runtime knows `zone` as an IANA time zone, e.g. "Europe/London". */
export function isValidTimeZone(zone: string | null | undefined): zone is string {
  return canonicalTimeZone(zone) !== null;
}

function storage(): Storage | null {
  try {
    return typeof localStorage === "undefined" ? null : localStorage;
  } catch {
    return null;
  }
}

function readStoredTimeZone(): string {
  try {
    return canonicalTimeZone(storage()?.getItem(DISPLAY_TIME_ZONE_STORAGE_KEY)) ?? DEFAULT_DISPLAY_TIME_ZONE;
  } catch {
    return DEFAULT_DISPLAY_TIME_ZONE;
  }
}

let currentTimeZone: string | null = null;
const listeners = new Set<() => void>();

function notify() {
  for (const listener of listeners) {
    listener();
  }
}

let storageListenerAttached = false;

// One window-level listener for the life of the page, attached on first use. Formatter-only
// consumers (`formatTimestamp` on Trading, Portfolio, ...) mount no subscriber, so tying the
// listener to subscribers would leave their cached zone stale after another tab changes it.
function ensureStorageListener(): void {
  if (storageListenerAttached || typeof window === "undefined") {
    return;
  }
  storageListenerAttached = true;
  window.addEventListener("storage", onStorage);
}

/** The zone timestamps render in: the stored operator preference, else "UTC". */
export function getDisplayTimeZone(): string {
  ensureStorageListener();
  if (currentTimeZone === null) {
    currentTimeZone = readStoredTimeZone();
  }
  return currentTimeZone;
}

/**
 * Pick the display zone and persist it for this browser. Returns false, changing nothing, when the
 * zone is not a known IANA zone. Choosing "UTC" removes the stored preference.
 */
export function setDisplayTimeZone(zone: string): boolean {
  const next = canonicalTimeZone(zone);
  if (next === null) {
    return false;
  }
  try {
    if (next === DEFAULT_DISPLAY_TIME_ZONE) {
      storage()?.removeItem(DISPLAY_TIME_ZONE_STORAGE_KEY);
    } else {
      storage()?.setItem(DISPLAY_TIME_ZONE_STORAGE_KEY, next);
    }
  } catch {
    // Persistence is best-effort when browser storage is blocked; the choice still applies now.
  }
  if (next !== getDisplayTimeZone()) {
    currentTimeZone = next;
    notify();
  }
  return true;
}

/** Forget the in-memory choice so the next read comes from storage again (tests, other tabs). */
export function reloadDisplayTimeZone(): void {
  const previous = currentTimeZone;
  currentTimeZone = null;
  if (previous !== null && previous !== getDisplayTimeZone()) {
    notify();
  }
}

function onStorage(event: StorageEvent): void {
  if (event.key === null || event.key === DISPLAY_TIME_ZONE_STORAGE_KEY) {
    reloadDisplayTimeZone();
  }
}

/** Call `listener` whenever the display zone changes, including from another tab. Returns the unsubscribe. */
export function subscribeDisplayTimeZone(listener: () => void): () => void {
  ensureStorageListener();
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}
