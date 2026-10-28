import { useCallback, useEffect, useRef } from "react";
import { useLocation, useNavigate } from "react-router-dom";

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
  queueSort: "reconciliationSort"
} as const;
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
    scopeKey: JSON.stringify(scopeKeys.map((key) => params.get(key)))
  };
}

/** URL state contains view choices only: server authority and approval state are never cached. */
export function useReconciliationViewState(enabled = true) {
  const location = useLocation();
  const navigate = useNavigate();
  const view = readReconciliationViewState(location.search);
  const previous = useRef(view);
  const scopeChangedWithRetainedSelection = previous.current.scopeKey !== view.scopeKey
    && previous.current.selectedRunId === view.selectedRunId
    && previous.current.selectedBreakId === view.selectedBreakId;
  const update = useCallback((patch: Partial<Omit<ReturnType<typeof readReconciliationViewState>, "scopeKey">>, replace = false) => {
    if (!enabled) return;
    const params = new URLSearchParams(location.search);
    Object.entries(patch).forEach(([key, value]) => {
      const queryKey = queryKeys[key as keyof typeof queryKeys];
      if (value === null || value === "" || value === "all" || value === "default") params.delete(queryKey);
      else params.set(queryKey, value);
    });
    // The queue records scroll on the current history entry without rerendering Router.
    // Preserve that state when navigating from this entry; MemoryRouter keeps its own state.
    const currentEntry = window.history.state as { key?: string; usr?: unknown } | null;
    const navigationState = currentEntry?.key === location.key ? currentEntry.usr : location.state;
    navigate({ pathname: location.pathname, search: params.toString(), hash: location.hash }, { replace, state: navigationState });
  }, [enabled, location.hash, location.key, location.pathname, location.search, location.state, navigate]);

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
    setQueueSort: (queueSort: ReconciliationQueueSort) => update({ queueSort })
  };
}
