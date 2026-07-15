// Durable, bounded persistence for the action-history drawer, following the
// meridian.workstation.<feature>.vN localStorage convention. Undo thunks are
// in-memory only and never persisted; a reloaded reversible-but-idle entry is
// downgraded to `expired` (history only) on load.
import type { ActivityEntry, ActivityTone, ActivityUndoStatus } from "./types";

export const ACTIVITY_LOG_STORAGE_KEY = "meridian.workstation.activity.v1";
export const ACTIVITY_LOG_LIMIT = 200;

export interface StorageLike {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

interface PersistedState {
  version: 1;
  entries: ActivityEntry[];
}

const TONES: ActivityTone[] = ["default", "success", "warning", "danger"];
const UNDO_STATUSES: ActivityUndoStatus[] = ["none", "idle", "running", "undone", "failed", "expired"];

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

function readString(value: unknown): string | undefined {
  return typeof value === "string" && value ? value : undefined;
}

/**
 * A thunk-less status is what survives a reload: `idle`/`running` become `expired`
 * (the in-memory undo handler is gone), every other status is preserved.
 */
function settledUndoStatus(value: unknown): ActivityUndoStatus {
  const status = typeof value === "string" && (UNDO_STATUSES as string[]).includes(value)
    ? (value as ActivityUndoStatus)
    : "none";
  return status === "idle" || status === "running" ? "expired" : status;
}

function normalizeEntry(value: unknown): ActivityEntry | null {
  if (typeof value !== "object" || value === null) {
    return null;
  }
  const source = value as Record<string, unknown>;
  const id = readString(source.id);
  const kind = readString(source.kind);
  const title = readString(source.title);
  const timestamp = readString(source.timestamp);
  if (!id || !kind || !title || !timestamp) {
    return null;
  }
  const tone = typeof source.tone === "string" && (TONES as string[]).includes(source.tone)
    ? (source.tone as ActivityTone)
    : "default";
  return {
    id,
    kind,
    title,
    detail: readString(source.detail),
    route: readString(source.route),
    routeLabel: readString(source.routeLabel),
    tone,
    timestamp,
    undoStatus: settledUndoStatus(source.undoStatus),
    undoError: readString(source.undoError)
  };
}

/** The serializable snapshot to persist (the entry is already a typed ActivityEntry). */
function toPersistedEntry(entry: ActivityEntry): ActivityEntry {
  return {
    id: entry.id,
    kind: entry.kind,
    title: entry.title,
    detail: entry.detail,
    route: entry.route,
    routeLabel: entry.routeLabel,
    tone: entry.tone,
    timestamp: entry.timestamp,
    undoStatus: entry.undoStatus,
    undoError: entry.undoError
  };
}

export function loadActivityLog(storage?: StorageLike | null): ActivityEntry[] {
  const target = resolveStorage(storage);
  if (!target) {
    return [];
  }

  let raw: string | null;
  try {
    raw = target.getItem(ACTIVITY_LOG_STORAGE_KEY);
  } catch {
    return [];
  }
  if (!raw) {
    return [];
  }

  try {
    const parsed = JSON.parse(raw) as unknown;
    const entries = (parsed as Partial<PersistedState>)?.entries;
    if (!Array.isArray(entries)) {
      return [];
    }
    return entries
      .map(normalizeEntry)
      .filter((entry): entry is ActivityEntry => entry !== null)
      .slice(0, ACTIVITY_LOG_LIMIT);
  } catch {
    return [];
  }
}

export function saveActivityLog(entries: ActivityEntry[], storage?: StorageLike | null): "saved" | "unavailable" | "failed" {
  const target = resolveStorage(storage);
  if (!target) {
    return "unavailable";
  }
  const payload: PersistedState = {
    version: 1,
    entries: entries.slice(0, ACTIVITY_LOG_LIMIT).map(toPersistedEntry)
  };
  try {
    target.setItem(ACTIVITY_LOG_STORAGE_KEY, JSON.stringify(payload));
    return "saved";
  } catch {
    return "failed";
  }
}
