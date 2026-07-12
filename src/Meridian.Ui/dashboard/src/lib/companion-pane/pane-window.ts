// Companion panes: chrome-less pop-out windows that render a single workspace
// surface (e.g. the watchlist or live quotes) in their own browser window,
// synchronized with the main workstation over a same-origin channel.

export const COMPANION_PANE_ROUTE_PREFIX = "/panes/";

export interface CompanionPaneDefinition {
  id: string;
  title: string;
  /** Accessible label for the chrome-less window region. */
  description: string;
}

export const COMPANION_PANES: Record<string, CompanionPaneDefinition> = {
  watchlist: { id: "watchlist", title: "Watchlist", description: "Watchlist companion pane" },
  "live-quotes": { id: "live-quotes", title: "Live quotes", description: "Live quotes companion pane" }
};

export function companionPaneHref(paneId: string): string {
  return `${COMPANION_PANE_ROUTE_PREFIX}${paneId}`;
}

/** True when the path targets a companion pane (used for the chrome-less app branch). */
export function isCompanionPaneRoute(pathname: string): boolean {
  return pathname.startsWith(COMPANION_PANE_ROUTE_PREFIX);
}

/** Resolve the pane definition for a path, or null when it is not a known pane. */
export function resolveCompanionPane(pathname: string): CompanionPaneDefinition | null {
  if (!isCompanionPaneRoute(pathname)) {
    return null;
  }
  const id = pathname.slice(COMPANION_PANE_ROUTE_PREFIX.length).replace(/\/+$/, "").split("/")[0];
  return COMPANION_PANES[id] ?? null;
}

export interface PaneOpener {
  open: Window["open"];
}

/**
 * Open a companion pane in a new window. Uses `noopener,noreferrer` so the child
 * cannot reach back into the opener. Because `window.open` with `noopener` returns
 * `null` even on success, callers must NOT infer popup-blocker state from the
 * return value — always keep a visible fallback link so the user has a reliable
 * way in.
 */
export function openCompanionPane(paneId: string, opener: PaneOpener = window): void {
  opener.open(companionPaneHref(paneId), "_blank", "noopener,noreferrer");
}
