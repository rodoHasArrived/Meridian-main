import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  getReconciliationBreakQueue,
  getRunTrialBalance,
  getSecurityConflicts,
  getSecurityIdentity,
  resolveReconciliationBreak,
  resolveSecurityConflict,
  reviewReconciliationBreak,
  searchSecurities
} from "@/lib/api";
import type {
  GovernanceWorkspaceResponse,
  LedgerTrialBalanceLine,
  ReconciliationBreakQueueItem,
  ResolveConflictRequest,
  ResolveReconciliationBreakRequest,
  ReviewReconciliationBreakRequest,
  SecurityIdentityDrillIn,
  SecurityMasterConflict,
  SecurityMasterEntry
} from "@/types";

export type GovernanceWorkstream = "ledger" | "reconciliation" | "security-master" | "reporting";
export type ReconciliationBreakCommand = "assign" | "resolve" | "dismiss";

export interface SecurityMasterServices {
  search: (query: string) => Promise<SecurityMasterEntry[]>;
  getIdentity: (securityId: string) => Promise<SecurityIdentityDrillIn>;
  getConflicts: () => Promise<SecurityMasterConflict[]>;
  resolveConflict: (request: ResolveConflictRequest) => Promise<SecurityMasterConflict>;
}

export interface GovernanceReconciliationServices {
  getBreakQueue: () => Promise<ReconciliationBreakQueueItem[]>;
  reviewBreak: (request: ReviewReconciliationBreakRequest) => Promise<ReconciliationBreakQueueItem>;
  resolveBreak: (request: ResolveReconciliationBreakRequest) => Promise<ReconciliationBreakQueueItem>;
  getTrialBalance: (runId: string) => Promise<LedgerTrialBalanceLine[]>;
}

export interface SecuritySearchState {
  trimmedQuery: string;
  resultCount: number;
  hasResults: boolean;
  searchStatusText: string | null;
  searchErrorText: string | null;
  statusAnnouncement: string;
}

export interface ReconciliationBreakAction {
  breakId: string;
  command: ReconciliationBreakCommand;
}

export interface ReconciliationBreakRowViewModel extends ReconciliationBreakQueueItem {
  actionBusy: boolean;
  assignLabel: string;
  resolveLabel: string;
  dismissLabel: string;
  assignAriaLabel: string;
  resolveAriaLabel: string;
  dismissAriaLabel: string;
  canAssign: boolean;
  canResolve: boolean;
  canDismiss: boolean;
}

export interface ReconciliationBreakQueueState {
  rows: ReconciliationBreakRowViewModel[];
  hasBreaks: boolean;
  loadingText: string | null;
  emptyText: string;
  errorText: string | null;
  actionErrorText: string | null;
  statusAnnouncement: string;
}

const defaultSecurityMasterServices: SecurityMasterServices = {
  search: (query) => searchSecurities(query),
  getIdentity: (securityId) => getSecurityIdentity(securityId),
  getConflicts: () => getSecurityConflicts(),
  resolveConflict: (request) => resolveSecurityConflict(request)
};

const defaultGovernanceReconciliationServices: GovernanceReconciliationServices = {
  getBreakQueue: () => getReconciliationBreakQueue(),
  reviewBreak: (request) => reviewReconciliationBreak(request),
  resolveBreak: (request) => resolveReconciliationBreak(request),
  getTrialBalance: (runId) => getRunTrialBalance(runId)
};

export function useSecurityMasterViewModel(
  active: boolean,
  services: SecurityMasterServices = defaultSecurityMasterServices,
  searchDelayMs = 350
) {
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<SecurityMasterEntry[] | null>(null);
  const [searching, setSearching] = useState(false);
  const [searchError, setSearchError] = useState<string | null>(null);
  const [selectedSecurityId, setSelectedSecurityId] = useState<string | null>(null);
  const [identity, setIdentity] = useState<SecurityIdentityDrillIn | null>(null);
  const [identityLoading, setIdentityLoading] = useState(false);
  const [identityError, setIdentityError] = useState<string | null>(null);
  const [conflicts, setConflicts] = useState<SecurityMasterConflict[] | null>(null);
  const [conflictsLoading, setConflictsLoading] = useState(false);
  const [conflictsError, setConflictsError] = useState<string | null>(null);
  const [conflictResolvingId, setConflictResolvingId] = useState<string | null>(null);
  const [conflictActionError, setConflictActionError] = useState<string | null>(null);
  const searchTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const searchGenerationRef = useRef(0);

  useEffect(() => () => {
    if (searchTimerRef.current) {
      clearTimeout(searchTimerRef.current);
    }
  }, []);

  useEffect(() => {
    if (!active) {
      return;
    }

    let cancelled = false;
    setConflictsLoading(true);
    setConflictsError(null);

    services.getConflicts()
      .then((rows) => {
        if (!cancelled) {
          setConflicts(rows);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setConflicts([]);
          setConflictsError(toErrorMessage(err, "Identifier conflicts failed to load."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setConflictsLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [active, services]);

  const updateQuery = useCallback((nextQuery: string) => {
    setQuery(nextQuery);
    setSelectedSecurityId(null);
    setIdentity(null);
    setIdentityError(null);
    setSearchError(null);

    if (searchTimerRef.current) {
      clearTimeout(searchTimerRef.current);
    }

    const trimmed = nextQuery.trim();
    searchGenerationRef.current += 1;

    if (!trimmed) {
      setSearching(false);
      setResults(null);
      return;
    }

    searchTimerRef.current = setTimeout(() => {
      const generation = searchGenerationRef.current;
      setSearching(true);

      services.search(trimmed)
        .then((rows) => {
          if (searchGenerationRef.current === generation) {
            setResults(rows);
          }
        })
        .catch((err) => {
          if (searchGenerationRef.current === generation) {
            setResults([]);
            setSearchError(toErrorMessage(err, "Security search failed."));
          }
        })
        .finally(() => {
          if (searchGenerationRef.current === generation) {
            setSearching(false);
          }
        });
    }, searchDelayMs);
  }, [searchDelayMs, services]);

  const selectSecurity = useCallback(async (securityId: string) => {
    setSelectedSecurityId(securityId);
    setIdentity(null);
    setIdentityError(null);
    setIdentityLoading(true);

    try {
      const detail = await services.getIdentity(securityId);
      setIdentity(detail);
    } catch (err) {
      setIdentityError(toErrorMessage(err, "Identity drill-in failed."));
    } finally {
      setIdentityLoading(false);
    }
  }, [services]);

  const resolveConflict = useCallback(async (
    conflictId: string,
    resolution: ResolveConflictRequest["resolution"]
  ) => {
    setConflictResolvingId(conflictId);
    setConflictActionError(null);

    try {
      const updated = await services.resolveConflict({ conflictId, resolution, resolvedBy: "operator" });
      setConflicts((current) => current?.map((conflict) => (
        conflict.conflictId === conflictId ? updated : conflict
      )) ?? current);
    } catch (err) {
      setConflictActionError(toErrorMessage(err, "Conflict resolution failed."));
    } finally {
      setConflictResolvingId(null);
    }
  }, [services]);

  const searchState = useMemo(
    () => buildSecuritySearchState({
      query,
      searching,
      results,
      searchError,
      identityLoading,
      identityError
    }),
    [identityError, identityLoading, query, results, searchError, searching]
  );

  return {
    query,
    updateQuery,
    results,
    searching,
    selectedSecurityId,
    selectSecurity,
    identity,
    identityLoading,
    identityErrorText: identityError,
    conflicts,
    conflictsLoading,
    conflictsErrorText: conflictsError,
    conflictResolvingId,
    conflictActionErrorText: conflictActionError,
    resolveConflict,
    openConflictCount: countOpenSecurityConflicts(conflicts),
    ...searchState
  };
}

export function useGovernanceReconciliationViewModel(
  data: GovernanceWorkspaceResponse | null,
  workstream: GovernanceWorkstream,
  services: GovernanceReconciliationServices = defaultGovernanceReconciliationServices
) {
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null);
  const [breakQueue, setBreakQueue] = useState<ReconciliationBreakQueueItem[]>(data?.breakQueue ?? []);
  const [breakQueueLoading, setBreakQueueLoading] = useState(false);
  const [breakQueueError, setBreakQueueError] = useState<string | null>(null);
  const [breakAction, setBreakAction] = useState<ReconciliationBreakAction | null>(null);
  const [breakActionError, setBreakActionError] = useState<string | null>(null);
  const [trialBalance, setTrialBalance] = useState<LedgerTrialBalanceLine[]>([]);
  const [trialBalanceLoading, setTrialBalanceLoading] = useState(false);
  const [trialBalanceError, setTrialBalanceError] = useState<string | null>(null);

  const reconciliationQueue = data?.reconciliationQueue ?? [];
  const selectedReconciliation = useMemo(
    () => resolveSelectedReconciliation(reconciliationQueue, selectedRunId),
    [reconciliationQueue, selectedRunId]
  );

  useEffect(() => {
    if (reconciliationQueue.length === 0) {
      setSelectedRunId(null);
      return;
    }

    if (!selectedRunId || !reconciliationQueue.some((item) => item.runId === selectedRunId)) {
      setSelectedRunId(reconciliationQueue[0].runId);
    }
  }, [reconciliationQueue, selectedRunId]);

  useEffect(() => {
    setBreakQueue(data?.breakQueue ?? []);
  }, [data?.breakQueue]);

  useEffect(() => {
    if (workstream !== "reconciliation") {
      return;
    }

    let cancelled = false;
    setBreakQueueLoading(true);
    setBreakQueueError(null);

    services.getBreakQueue()
      .then((rows) => {
        if (!cancelled) {
          setBreakQueue(rows);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setBreakQueue(data?.breakQueue ?? []);
          setBreakQueueError(toErrorMessage(err, "Reconciliation break queue failed to load."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setBreakQueueLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [data?.breakQueue, services, workstream]);

  useEffect(() => {
    if (!selectedReconciliation || workstream !== "ledger") {
      setTrialBalance([]);
      setTrialBalanceError(null);
      setTrialBalanceLoading(false);
      return;
    }

    let cancelled = false;
    setTrialBalanceLoading(true);
    setTrialBalanceError(null);

    services.getTrialBalance(selectedReconciliation.runId)
      .then((rows) => {
        if (!cancelled) {
          setTrialBalance(rows);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setTrialBalance([]);
          setTrialBalanceError(toErrorMessage(err, "Trial balance failed to load."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setTrialBalanceLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [selectedReconciliation, services, workstream]);

  const assignBreak = useCallback(async (breakId: string) => {
    setBreakAction({ breakId, command: "assign" });
    setBreakActionError(null);

    try {
      const updated = await services.reviewBreak({ breakId, assignedTo: "ops.gov", reviewedBy: "ops.gov" });
      setBreakQueue((current) => replaceBreakQueueItem(current, updated));
    } catch (err) {
      setBreakActionError(toErrorMessage(err, "Break assignment failed."));
    } finally {
      setBreakAction(null);
    }
  }, [services]);

  const resolveBreak = useCallback(async (
    breakId: string,
    status: ResolveReconciliationBreakRequest["status"]
  ) => {
    const command: ReconciliationBreakCommand = status === "Resolved" ? "resolve" : "dismiss";
    setBreakAction({ breakId, command });
    setBreakActionError(null);

    try {
      const updated = await services.resolveBreak({
        breakId,
        status,
        resolvedBy: "ops.gov",
        resolutionNote: "Reviewed in governance panel."
      });
      setBreakQueue((current) => replaceBreakQueueItem(current, updated));
    } catch (err) {
      setBreakActionError(toErrorMessage(err, "Break resolution failed."));
    } finally {
      setBreakAction(null);
    }
  }, [services]);

  const breakQueueState = useMemo(
    () => buildReconciliationBreakQueueState({
      breakQueue,
      loading: breakQueueLoading,
      loadError: breakQueueError,
      action: breakAction,
      actionError: breakActionError
    }),
    [breakAction, breakActionError, breakQueue, breakQueueError, breakQueueLoading]
  );

  return {
    reconciliationQueue,
    selectedRunId,
    selectedReconciliation,
    selectRun: setSelectedRunId,
    trialBalance,
    trialBalanceLoading,
    trialBalanceErrorText: trialBalanceError,
    breakAction,
    assignBreak,
    resolveBreak,
    ...breakQueueState
  };
}

export function resolveGovernanceWorkstream(pathname: string): GovernanceWorkstream {
  if (pathname.startsWith("/reporting")) {
    return "reporting";
  }

  if (pathname.includes("/reconciliation")) {
    return "reconciliation";
  }

  if (pathname.includes("/security-master")) {
    return "security-master";
  }

  return "ledger";
}

export function resolveSelectedReconciliation(
  queue: GovernanceWorkspaceResponse["reconciliationQueue"],
  selectedRunId: string | null
) {
  return queue.find((item) => item.runId === selectedRunId) ?? queue[0] ?? null;
}

export function buildSecuritySearchState({
  query,
  searching,
  results,
  searchError,
  identityLoading,
  identityError
}: {
  query: string;
  searching: boolean;
  results: SecurityMasterEntry[] | null;
  searchError: string | null;
  identityLoading: boolean;
  identityError: string | null;
}): SecuritySearchState {
  const trimmedQuery = query.trim();
  const resultCount = results?.length ?? 0;
  const hasResults = resultCount > 0;
  const searchErrorText = searchError
    ? searchError.startsWith("Security search failed")
      ? searchError
      : `Security search failed: ${searchError}`
    : null;

  let searchStatusText: string | null = null;
  if (!trimmedQuery) {
    searchStatusText = "Enter a ticker, ISIN, CUSIP, FIGI, or display name.";
  } else if (searching) {
    searchStatusText = `Searching Security Master for "${trimmedQuery}"...`;
  } else if (results === null) {
    searchStatusText = `Security Master search queued for "${trimmedQuery}".`;
  } else if (searchErrorText) {
    searchStatusText = searchErrorText;
  } else if (results !== null && resultCount === 0) {
    searchStatusText = `No securities found for "${trimmedQuery}".`;
  } else if (hasResults) {
    searchStatusText = `${resultCount} securities found for "${trimmedQuery}".`;
  }

  return {
    trimmedQuery,
    resultCount,
    hasResults,
    searchStatusText,
    searchErrorText,
    statusAnnouncement: buildSecurityStatusAnnouncement({
      searching,
      trimmedQuery,
      resultCount,
      results,
      searchErrorText,
      identityLoading,
      identityError
    })
  };
}

export function countOpenSecurityConflicts(conflicts: SecurityMasterConflict[] | null): number {
  return conflicts?.filter((conflict) => conflict.status === "Open").length ?? 0;
}

export function buildReconciliationBreakQueueState({
  breakQueue,
  loading,
  loadError,
  action,
  actionError
}: {
  breakQueue: ReconciliationBreakQueueItem[];
  loading: boolean;
  loadError: string | null;
  action: ReconciliationBreakAction | null;
  actionError: string | null;
}): ReconciliationBreakQueueState {
  const rows = buildReconciliationBreakRows(breakQueue, action);
  const loadingText = loading ? "Loading reconciliation break queue..." : null;
  const errorText = loadError
    ? loadError.startsWith("Reconciliation break queue failed")
      ? loadError
      : `Reconciliation break queue failed: ${loadError}`
    : null;
  const actionErrorText = actionError
    ? actionError.startsWith("Break ")
      ? actionError
      : `Break action failed: ${actionError}`
    : null;

  return {
    rows,
    hasBreaks: rows.length > 0,
    loadingText,
    emptyText: "No reconciliation breaks in the current queue.",
    errorText,
    actionErrorText,
    statusAnnouncement: buildReconciliationBreakStatusAnnouncement({
      loading,
      action,
      loadError: errorText,
      actionError: actionErrorText,
      breakCount: rows.length
    })
  };
}

export function buildReconciliationBreakRows(
  breakQueue: ReconciliationBreakQueueItem[],
  action: ReconciliationBreakAction | null
): ReconciliationBreakRowViewModel[] {
  return breakQueue.map((item) => {
    const actionBusy = action?.breakId === item.breakId;
    const assignBusy = actionBusy && action?.command === "assign";
    const resolveBusy = actionBusy && action?.command === "resolve";
    const dismissBusy = actionBusy && action?.command === "dismiss";

    return {
      ...item,
      actionBusy,
      assignLabel: assignBusy ? "Assigning..." : "Assign",
      resolveLabel: resolveBusy ? "Resolving..." : "Resolve",
      dismissLabel: dismissBusy ? "Dismissing..." : "Dismiss",
      assignAriaLabel: `Assign reconciliation break ${item.breakId}`,
      resolveAriaLabel: `Resolve reconciliation break ${item.breakId}`,
      dismissAriaLabel: `Dismiss reconciliation break ${item.breakId}`,
      canAssign: !action && item.status === "Open",
      canResolve: !action && item.status !== "Resolved",
      canDismiss: !action && item.status !== "Dismissed"
    };
  });
}

export function buildReconciliationNarrative(item: GovernanceWorkspaceResponse["reconciliationQueue"][number]) {
  if (item.reconciliationStatus === "Balanced") {
    return "This run is currently balanced. Audit review should focus on evidence completeness and timing freshness rather than open break remediation.";
  }

  if (item.reconciliationStatus === "SecurityCoverageOpen") {
    return "Break counts are secondary here. The main task is resolving Security Master coverage so downstream ledger and reporting workflows are trustworthy.";
  }

  if (item.reconciliationStatus === "Resolved") {
    return "Historical breaks have been worked through, but the run still needs operator review before it can be treated as fully balanced.";
  }

  if (item.reconciliationStatus === "NotStarted") {
    return "No reconciliation pass has been recorded yet. This run should be queued behind currently active governance review work.";
  }

  return "Open reconciliation breaks remain on this run. Prioritize amount mismatches, timing drift, and unresolved references before moving on.";
}

function buildSecurityStatusAnnouncement({
  searching,
  trimmedQuery,
  resultCount,
  results,
  searchErrorText,
  identityLoading,
  identityError
}: {
  searching: boolean;
  trimmedQuery: string;
  resultCount: number;
  results: SecurityMasterEntry[] | null;
  searchErrorText: string | null;
  identityLoading: boolean;
  identityError: string | null;
}): string {
  if (identityLoading) {
    return "Loading Security Master identity drill-in.";
  }

  if (identityError) {
    return identityError;
  }

  if (!trimmedQuery) {
    return "";
  }

  if (searching) {
    return `Searching Security Master for ${trimmedQuery}.`;
  }

  if (results === null) {
    return `Security Master search queued for ${trimmedQuery}.`;
  }

  if (searchErrorText) {
    return searchErrorText;
  }

  if (results !== null && resultCount === 0) {
    return `No securities found for ${trimmedQuery}.`;
  }

  if (resultCount > 0) {
    return `${resultCount} securities found for ${trimmedQuery}.`;
  }

  return "";
}

function buildReconciliationBreakStatusAnnouncement({
  loading,
  action,
  loadError,
  actionError,
  breakCount
}: {
  loading: boolean;
  action: ReconciliationBreakAction | null;
  loadError: string | null;
  actionError: string | null;
  breakCount: number;
}): string {
  if (loading) {
    return "Loading reconciliation break queue.";
  }

  if (action?.command === "assign") {
    return `Assigning reconciliation break ${action.breakId}.`;
  }

  if (action?.command === "resolve") {
    return `Resolving reconciliation break ${action.breakId}.`;
  }

  if (action?.command === "dismiss") {
    return `Dismissing reconciliation break ${action.breakId}.`;
  }

  if (actionError) {
    return actionError;
  }

  if (loadError) {
    return loadError;
  }

  if (breakCount === 0) {
    return "No reconciliation breaks in the current queue.";
  }

  return `${breakCount} reconciliation ${breakCount === 1 ? "break" : "breaks"} loaded.`;
}

function replaceBreakQueueItem(
  current: ReconciliationBreakQueueItem[],
  updated: ReconciliationBreakQueueItem
): ReconciliationBreakQueueItem[] {
  if (!current.some((item) => item.breakId === updated.breakId)) {
    return [updated, ...current];
  }

  return current.map((item) => (item.breakId === updated.breakId ? updated : item));
}

function toErrorMessage(err: unknown, fallback: string): string {
  if (err instanceof Error && err.message.trim()) {
    return err.message;
  }

  return fallback;
}
