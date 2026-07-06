// Best-effort registry of which companion panes the operator has popped out this
// session. A `noopener` window.open returns null even on success, so the main
// window cannot observe live child-window state — this records pop-out *intent*
// instead, which is what saved layouts capture ("re-open these panes on restore").
// Entries are deduped and filtered to known panes; unknown ids are dropped on read.
import { COMPANION_PANES } from "@/lib/companion-pane/pane-window";
import { getLocalStorage } from "@/lib/theme";

export const OPEN_PANES_STORAGE_KEY = "meridian.workstation.openPanes.v1";

function isKnownPane(id: unknown): id is string {
  return typeof id === "string" && Object.prototype.hasOwnProperty.call(COMPANION_PANES, id);
}

/** Deduped, insertion-ordered list of known pane ids, defensively parsed. */
export function readOpenCompanionPaneIds(storage: Storage | null = getLocalStorage()): string[] {
  try {
    const raw = storage?.getItem(OPEN_PANES_STORAGE_KEY);
    if (!raw) {
      return [];
    }
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) {
      return [];
    }
    return dedupeKnownPanes(parsed);
  } catch {
    return [];
  }
}

export function setOpenCompanionPaneIds(ids: string[], storage: Storage | null = getLocalStorage()): string[] {
  const next = dedupeKnownPanes(ids);
  try {
    if (next.length === 0) {
      storage?.removeItem(OPEN_PANES_STORAGE_KEY);
    } else {
      storage?.setItem(OPEN_PANES_STORAGE_KEY, JSON.stringify(next));
    }
  } catch {
    // Registry persistence is best-effort when browser storage is blocked.
  }
  return next;
}

/** Record that a pane was popped out. No-op for unknown pane ids. */
export function recordCompanionPaneOpened(paneId: string, storage: Storage | null = getLocalStorage()): string[] {
  if (!isKnownPane(paneId)) {
    return readOpenCompanionPaneIds(storage);
  }
  const current = readOpenCompanionPaneIds(storage);
  return current.includes(paneId) ? current : setOpenCompanionPaneIds([...current, paneId], storage);
}

export function clearOpenCompanionPaneIds(storage: Storage | null = getLocalStorage()): void {
  try {
    storage?.removeItem(OPEN_PANES_STORAGE_KEY);
  } catch {
    // Best-effort.
  }
}

function dedupeKnownPanes(ids: unknown[]): string[] {
  const seen = new Set<string>();
  const result: string[] = [];
  for (const id of ids) {
    if (isKnownPane(id) && !seen.has(id)) {
      seen.add(id);
      result.push(id);
    }
  }
  return result;
}
