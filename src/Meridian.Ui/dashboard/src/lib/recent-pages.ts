// Recent and pinned pages for the command palette (UX Phase D). A per-viewer convenience kept in
// this browser's storage: every read and write is guarded, and an unavailable store simply means
// the palette shows no recent or pinned pages. Nothing here is shared state or business data.
import { useSyncExternalStore } from "react";
import { getLocalStorage } from "@/lib/theme";

export interface SavedPage {
  /** Path and query, e.g. "/accounting/ledger?ledgerBookId=book-a". */
  route: string;
  /** The page name the operator saw, e.g. "Ledger Explorer". */
  label: string;
}

const RECENT_KEY = "meridian.workstation.recent-pages.v1";
const PINNED_KEY = "meridian.workstation.pinned-pages.v1";
export const RECENT_PAGE_LIMIT = 8;
export const PINNED_PAGE_LIMIT = 12;

type Listener = () => void;
const listeners = new Set<Listener>();
let cache: { recent: SavedPage[]; pinned: SavedPage[] } | null = null;

function read(key: string): SavedPage[] {
  try {
    const raw = getLocalStorage()?.getItem(key);
    if (!raw) return [];
    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed)
      ? parsed.filter((page): page is SavedPage => typeof page?.route === "string" && typeof page?.label === "string" && page.route.startsWith("/"))
      : [];
  } catch {
    return [];
  }
}

function write(key: string, pages: readonly SavedPage[]) {
  try {
    getLocalStorage()?.setItem(key, JSON.stringify(pages));
  } catch {
    // Storage full, blocked or private: the palette keeps working without it.
  }
}

function snapshot() {
  cache ??= { recent: read(RECENT_KEY), pinned: read(PINNED_KEY) };
  return cache;
}

function publish(next: { recent: SavedPage[]; pinned: SavedPage[] }) {
  cache = next;
  for (const listener of listeners) listener();
}

/** Records a visit: newest first, one entry per route, at most RECENT_PAGE_LIMIT. */
export function recordRecentPage(page: SavedPage) {
  if (!page.route.startsWith("/") || !page.label.trim()) return;
  const current = snapshot();
  if (current.recent[0]?.route === page.route && current.recent[0]?.label === page.label) return;
  const recent = [page, ...current.recent.filter((entry) => entry.route !== page.route)].slice(0, RECENT_PAGE_LIMIT);
  write(RECENT_KEY, recent);
  publish({ ...current, recent });
}

export function isPinnedPage(route: string): boolean {
  return snapshot().pinned.some((page) => page.route === route);
}

/** Pins or unpins a page; returns whether it is pinned afterwards. */
export function togglePinnedPage(page: SavedPage): boolean {
  const current = snapshot();
  const pinnedNow = current.pinned.some((entry) => entry.route === page.route);
  const pinned = pinnedNow
    ? current.pinned.filter((entry) => entry.route !== page.route)
    : [page, ...current.pinned].slice(0, PINNED_PAGE_LIMIT);
  write(PINNED_KEY, pinned);
  publish({ ...current, pinned });
  return !pinnedNow;
}

function subscribe(listener: Listener) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function useSavedPages() {
  return useSyncExternalStore(subscribe, snapshot, snapshot);
}

/** Test seam: forget the in-memory copy so the next read goes back to storage. */
export function resetSavedPagesCache() {
  cache = null;
}
