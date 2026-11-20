import { useCallback, useEffect, useRef } from "react";
import { useHref, useLocation, useNavigate } from "react-router-dom";

export type ReconciliationQueueStatus = "all" | "Open" | "InReview" | "Resolved" | "Dismissed" | "SignedOff";
export type ReconciliationQueueSort = "default" | "oldest" | "newest" | "severity";

export interface ReconciliationSelectionState {
  selectedRunId: string | null;
  selectedBreakId: string | null;
  scopeKey: string;
  selectRun: (runId: string | null) => void;
  replaceSelection?: (runId: string | null, breakId: string | null) => void;
  selectBreak: (breakId: string | null, runId?: string | null) => void;
}

const queryKeys = {
  selectedRunId: "reconciliationRun",
  selectedBreakId: "reconciliationBreak",
  queueSearch: "reconciliationSearch",
  queueStatus: "reconciliationStatus",
  queueSort: "reconciliationSort",
  hiddenColumns: "reconciliationColumns"
} as const;
/** The break queue's hideable columns (C4); hidden ones travel in the link so saved views keep them. */
export const RECONCILIATION_QUEUE_HIDEABLE_COLUMNS = ["category", "owner", "updated", "source"] as const;
const scopeKeys = ["fundAccountId", "symbol", "runId", "provider", "from", "to", "date", "asOf"];

export function readReconciliationViewState(search: string) {
  const params = new URLSearchParams(search);
  const status = params.get(queryKeys.queueStatus);
  const sort = params.get(queryKeys.queueSort);
  return {
    selectedRunId: params.get(queryKeys.selectedRunId)?.trim() || null,
    selectedBreakId: params.get(queryKeys.selectedBreakId)?.trim() || null,
    queueSearch: params.get(queryKeys.queueSearch) ?? "",
    queueStatus: (["Open", "InReview", "Resolved", "Dismissed", "SignedOff"].includes(status ?? "") ? status : "all") as ReconciliationQueueStatus,
    queueSort: (["oldest", "newest", "severity"].includes(sort ?? "") ? sort : "default") as ReconciliationQueueSort,
    hiddenColumns: RECONCILIATION_QUEUE_HIDEABLE_COLUMNS.filter((id) => (params.get(queryKeys.hiddenColumns) ?? "").split(",").includes(id)) as string[],
    scopeKey: JSON.stringify(scopeKeys.map((key) => params.get(key)))
  };
}

/** URL state contains view choices only: server authority and approval state are never cached. */
export function useReconciliationViewState(enabled = true) {
  const location = useLocation();
  const navigate = useNavigate();
  const pathnameHref = useHref(location.pathname);
  const view = readReconciliationViewState(location.search);
  const latest = useRef({ location, enabled, pathnameHref });
  latest.current = { location, enabled, pathnameHref };
  const ownsBrowserHistory = useRef(false);
  const browserEntry = typeof window === "undefined" ? null : window.history.state as { key?: string; idx?: number } | null;
  if (typeof browserEntry?.idx === "number" && (browserEntry.key ?? "default") === location.key
    && window.location.pathname === new URL(pathnameHref, window.location.href).pathname) {
    ownsBrowserHistory.current = true;
  }
  const previous = useRef(view);
  const scopeChangedWithRetainedSelection = previous.current.scopeKey !== view.scopeKey
    && previous.current.selectedRunId === view.selectedRunId
    && previous.current.selectedBreakId === view.selectedBreakId;
  const update = useCallback((patch: Partial<Omit<ReturnType<typeof readReconciliationViewState>, "scopeKey">>, replace = false) => {
    const current = latest.current;
    if (!current.enabled || current.location.pathname !== location.pathname) return;
    const currentEntry = window.history.state as { key?: string; usr?: unknown } | null;
    // Browser navigation updates history before React commits the next location. A delayed
    // reconciliation default must patch that entry, preserving another panel's book/run choice.
    // MemoryRouter has its own history, so it continues to use the latest rendered location.
    if (ownsBrowserHistory.current && window.location.pathname !== new URL(current.pathnameHref, window.location.href).pathname) return;
    const search = ownsBrowserHistory.current ? window.location.search : current.location.search;
    const hash = ownsBrowserHistory.current ? window.location.hash : current.location.hash;
    if (readReconciliationViewState(search).scopeKey !== view.scopeKey) return;
    const params = new URLSearchParams(search);
    Object.entries(patch).forEach(([key, value]) => {
      const queryKey = queryKeys[key as keyof typeof queryKeys];
      const text = Array.isArray(value) ? value.join(",") : value;
      if (text === null || text === undefined || text === "" || text === "all" || text === "default") params.delete(queryKey);
      else params.set(queryKey, text);
    });
    // The queue records scroll on the current history entry without rerendering Router.
    // Preserve that state when navigating from this entry; MemoryRouter keeps its own state.
    const navigationState = ownsBrowserHistory.current || currentEntry?.key === current.location.key ? currentEntry?.usr : current.location.state;
    navigate({ pathname: current.location.pathname, search: params.toString(), hash }, { replace, state: navigationState });
  }, [location.pathname, navigate, view.scopeKey]);

  useEffect(() => {
    previous.current = view;
    if (enabled && scopeChangedWithRetainedSelection && (view.selectedRunId || view.selectedBreakId)) {
      update({ selectedRunId: null, selectedBreakId: null }, true);
    }
  }, [enabled, scopeChangedWithRetainedSelection, update, view]);

  return {
    ...view,
    selectedRunId: scopeChangedWithRetainedSelection ? null : view.selectedRunId,
    selectedBreakId: scopeChangedWithRetainedSelection ? null : view.selectedBreakId,
    replaceSelection: (selectedRunId: string | null, selectedBreakId: string | null) => update({ selectedRunId, selectedBreakId }, true),
    selectRun: (selectedRunId: string | null) => update({ selectedRunId, selectedBreakId: null }),
    selectBreak: (selectedBreakId: string | null, selectedRunId?: string | null) => update({ selectedBreakId, ...(selectedRunId ? { selectedRunId } : {}) }),
    clearQueueFilters: () => update({ queueSearch: "", queueStatus: "all" }),
    setQueueSearch: (queueSearch: string) => update({ queueSearch }, true),
    setQueueStatus: (queueStatus: ReconciliationQueueStatus) => update({ queueStatus }),
    setQueueSort: (queueSort: ReconciliationQueueSort) => update({ queueSort }),
    setHiddenColumns: (hiddenColumns: string[]) => update({ hiddenColumns }, true)
  };
}
