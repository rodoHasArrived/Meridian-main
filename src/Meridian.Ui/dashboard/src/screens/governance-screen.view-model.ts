import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  getSecurityConflicts,
  getSecurityIdentity,
  resolveSecurityConflict,
  searchSecurities
} from "@/lib/api";
import type {
  GovernanceWorkspaceResponse,
  ResolveConflictRequest,
  SecurityIdentityDrillIn,
  SecurityMasterConflict,
  SecurityMasterEntry
} from "@/types";

export type GovernanceWorkstream = "ledger" | "reconciliation" | "security-master";

export interface SecurityMasterServices {
  search: (query: string) => Promise<SecurityMasterEntry[]>;
  getIdentity: (securityId: string) => Promise<SecurityIdentityDrillIn>;
  getConflicts: () => Promise<SecurityMasterConflict[]>;
  resolveConflict: (request: ResolveConflictRequest) => Promise<SecurityMasterConflict>;
}

export interface SecuritySearchState {
  trimmedQuery: string;
  resultCount: number;
  hasResults: boolean;
  searchStatusText: string | null;
  searchErrorText: string | null;
  statusAnnouncement: string;
}

const defaultSecurityMasterServices: SecurityMasterServices = {
  search: (query) => searchSecurities(query),
  getIdentity: (securityId) => getSecurityIdentity(securityId),
  getConflicts: () => getSecurityConflicts(),
  resolveConflict: (request) => resolveSecurityConflict(request)
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

export function resolveGovernanceWorkstream(pathname: string): GovernanceWorkstream {
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

function toErrorMessage(err: unknown, fallback: string): string {
  if (err instanceof Error && err.message.trim()) {
    return err.message;
  }

  return fallback;
}
