// Client-side read/dismissed state for the notification center. Price-alert
// acknowledgement is durable in its own store (meridian.priceAlerts.v1); this
// tracks read/dismissed ids for the inbox and system-event feeds only, following
// the meridian.workstation.<feature>.vN localStorage convention.
export const NOTIFICATION_READ_STATE_KEY = "meridian.workstation.notifications.v1";
export const NOTIFICATION_READ_STATE_LIMIT = 500;

export interface StorageLike {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

export interface NotificationReadState {
  version: 1;
  readIds: string[];
  dismissedIds: string[];
}

export type NotificationReadStateWriteStatus = "saved" | "unavailable" | "failed";

export function emptyNotificationReadState(): NotificationReadState {
  return { version: 1, readIds: [], dismissedIds: [] };
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

function normalizeIdList(value: unknown): string[] {
  if (!Array.isArray(value)) {
    return [];
  }
  const seen = new Set<string>();
  for (const entry of value) {
    if (typeof entry === "string" && entry) {
      seen.add(entry);
    }
  }
  // Keep the most recent NOTIFICATION_READ_STATE_LIMIT ids (newest at the tail).
  return [...seen].slice(-NOTIFICATION_READ_STATE_LIMIT);
}

function normalizeState(value: unknown): NotificationReadState {
  if (typeof value !== "object" || value === null) {
    return emptyNotificationReadState();
  }
  const source = value as Record<string, unknown>;
  return {
    version: 1,
    readIds: normalizeIdList(source.readIds),
    dismissedIds: normalizeIdList(source.dismissedIds)
  };
}

export function loadNotificationReadState(storage?: StorageLike | null): NotificationReadState {
  const target = resolveStorage(storage);
  if (!target) {
    return emptyNotificationReadState();
  }

  let raw: string | null;
  try {
    raw = target.getItem(NOTIFICATION_READ_STATE_KEY);
  } catch {
    return emptyNotificationReadState();
  }

  if (!raw) {
    return emptyNotificationReadState();
  }

  try {
    return normalizeState(JSON.parse(raw) as unknown);
  } catch {
    return emptyNotificationReadState();
  }
}

export function saveNotificationReadState(
  state: NotificationReadState,
  storage?: StorageLike | null
): NotificationReadStateWriteStatus {
  const target = resolveStorage(storage);
  if (!target) {
    return "unavailable";
  }

  try {
    target.setItem(NOTIFICATION_READ_STATE_KEY, JSON.stringify(normalizeState(state)));
    return "saved";
  } catch {
    return "failed";
  }
}

/** Appends an id to a bounded set, moving it to the newest position. Returns the
 *  same array reference when the id is already present as the sole newest entry. */
export function withId(ids: string[], id: string): string[] {
  const filtered = ids.filter((entry) => entry !== id);
  if (filtered.length === ids.length - 1 && ids[ids.length - 1] === id) {
    // Already present exactly once and already newest — no change.
    return ids;
  }
  const next = [...filtered, id];
  return next.length > NOTIFICATION_READ_STATE_LIMIT ? next.slice(-NOTIFICATION_READ_STATE_LIMIT) : next;
}

export function markNotificationRead(state: NotificationReadState, id: string): NotificationReadState {
  if (state.readIds.includes(id)) {
    return state;
  }
  return { ...state, readIds: withId(state.readIds, id) };
}

export function markNotificationsRead(state: NotificationReadState, ids: readonly string[]): NotificationReadState {
  // Only touch ids that aren't already read, so an all-read batch is a no-op.
  const missing = ids.filter((id) => !state.readIds.includes(id));
  if (missing.length === 0) {
    return state;
  }
  let readIds = state.readIds;
  for (const id of missing) {
    readIds = withId(readIds, id);
  }
  return { ...state, readIds };
}

export function dismissNotification(state: NotificationReadState, id: string): NotificationReadState {
  if (state.dismissedIds.includes(id)) {
    return state;
  }
  return {
    ...state,
    readIds: withId(state.readIds, id),
    dismissedIds: withId(state.dismissedIds, id)
  };
}
