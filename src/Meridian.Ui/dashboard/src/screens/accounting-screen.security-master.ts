import {
  getCorporateActions,
  getReferenceDataWorkbenchCoverage,
  getSecurityConflicts,
  getSecurityIdentity,
  getSecurityInstrumentPassport,
  getSecurityTrustSnapshot,
  getTradingParameters,
  resolveSecurityConflict,
  searchSecurities,
  type ReferenceDataWorkbenchCoverage
} from "@/lib/api";
import { describeApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import { formatCount as formatCountValue } from "@/lib/format";
import { type ReferenceDataWorkbenchEndpointSeed } from "@/lib/workstation-endpoints";
import type {
  CorporateAction,
  InstrumentPassport,
  ResolveConflictRequest,
  SecurityAliasEntry,
  SecurityIdentifierEntry,
  SecurityIdentityDrillIn,
  SecurityMasterConflict,
  SecurityMasterEntry,
  SecurityMasterOpenLotReadModel,
  SecurityMasterTrustSnapshot,
  TradingParameters
} from "@/types";
import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState
} from "react";
import { formatCount } from "./accounting-screen.formatting";
import {
  buildSecurityConflictAction,
  buildSecurityIdentityAliasRow,
  buildSecurityIdentityIdentifierRow,
  formatConflictDate,
  formatSecurityConflictField,
  formatSecurityDateRange,
  formatSecurityReferenceValue,
  statusBadgeVariantForSecurityIdentity,
  summarizeReferenceDataRoutes
} from "./accounting-screen.security-master-presenters";
import type { SecurityCashFlowScheduleEvent } from "./accounting-screen.security-projections";
import {
  buildCorporateActionRows,
  buildCorporateActionsViewState,
  buildInstrumentPassportViewState,
  buildReferenceDataWorkbenchViewState,
  buildSecurityOpenLotReadModelViewState,
  buildSecurityOpenLotRows,
  buildSecurityScheduleRows,
  buildSecuritySchedulesViewState,
  buildTradingParametersViewState,
  mapScheduleBookToCashFlowScheduleEvents,
  resolveSecurityScheduleEvents
} from "./accounting-screen.security-projections";
import { normalizeApiErrorDisplay } from "./accounting-screen.view-model.shared";

// Domain-specific projections and components shared by the screen composition.

export type SecurityConflictResolution = ResolveConflictRequest["resolution"];


export interface SecurityMasterServices {
  search: (query: string) => Promise<SecurityMasterEntry[]>;
  getIdentity: (securityId: string) => Promise<SecurityIdentityDrillIn>;
  getConflicts: () => Promise<SecurityMasterConflict[]>;
  resolveConflict: (request: ResolveConflictRequest) => Promise<SecurityMasterConflict>;
}


export interface SecurityMasterDrillInServices {
  getCorporateActions: (securityId: string) => Promise<CorporateAction[]>;
  getReferenceDataCoverage: (seed: ReferenceDataWorkbenchEndpointSeed) => Promise<ReferenceDataWorkbenchCoverage>;
  getInstrumentPassport: (securityId: string) => Promise<InstrumentPassport>;
  getTradingParameters: (securityId: string) => Promise<TradingParameters>;
  getTrustSnapshot: (securityId: string) => Promise<SecurityMasterTrustSnapshot>;
}


export interface SecuritySearchResultColumnViewModel {
  id: "name" | "assetClass" | "primaryId" | "currency" | "status";
  label: string;
}


export interface SecuritySearchResultRowViewModel extends SecurityMasterEntry {
  rowId: string;
  isSelected: boolean;
  detailPanelId: string;
  isExpanded: boolean;
  selectAriaLabel: string;
  primaryIdentifierLabel: string;
  statusTone: "success" | "warning";
  ariaLabel: string;
}


export interface SecuritySearchState {
  trimmedQuery: string;
  resultCount: number;
  hasResults: boolean;
  resultsTableLabel: string;
  resultColumns: SecuritySearchResultColumnViewModel[];
  resultRows: SecuritySearchResultRowViewModel[];
  searchStatusText: string | null;
  searchErrorText: string | null;
  searchErrorDetails: string[];
  statusAnnouncement: string;
}


export type SecurityMasterPageMetricTone = "default" | "success" | "warning";


export interface SecurityMasterPageMetricViewModel {
  id: "results" | "selected" | "conflicts" | "detail" | "reference" | "passport";
  label: string;
  value: string;
  detail: string;
  tone: SecurityMasterPageMetricTone;
}


export interface SecurityMasterCoveragePostureViewModel {
  label: "Ready" | "Review required" | "Verification pending" | "Select a record";
  detail: string;
  tone: SecurityMasterPageMetricTone;
}


export interface SecurityMasterDetailSectionViewModel {
  id: "overview" | "reference" | "schedules" | "lots" | "controls" | "audit";
  label: string;
  value: string;
  active?: boolean;
}


export interface SecurityMasterPageViewState {
  ariaLabel: string;
  eyebrow: string;
  title: string;
  description: string;
  coveragePosture: SecurityMasterCoveragePostureViewModel;
  metrics: SecurityMasterPageMetricViewModel[];
  detailEyebrow: string;
  detailTitle: string;
  detailSubtitle: string;
  detailDescription: string;
  detailStatusLabel: string;
  detailStatusBadgeVariant: "success" | "warning" | "outline";
  detailToolbarAriaLabel: string;
  detailSections: SecurityMasterDetailSectionViewModel[];
}

export interface SecurityIdentitySummaryFieldViewModel {
  label: string;
  value: string;
}


export interface SecurityIdentityIdentifierRowViewModel extends SecurityIdentifierEntry {
  rowId: string;
  providerLabel: string;
  primaryLabel: string;
  primaryBadgeVariant: "success" | "outline";
  validRangeLabel: string;
  ariaLabel: string;
}


export interface SecurityIdentityAliasRowViewModel extends SecurityAliasEntry {
  rowId: string;
  providerLabel: string;
  enabledLabel: string;
  enabledBadgeVariant: "success" | "warning";
  validRangeLabel: string;
  createdLabel: string;
  reasonText: string;
  ariaLabel: string;
}


export interface SecurityIdentityDrillInViewState {
  panelId: string;
  title: string;
  subtitle: string;
  description: string;
  ariaLabel: string;
  statusLabel: string;
  statusBadgeVariant: "success" | "warning" | "outline";
  summaryFields: SecurityIdentitySummaryFieldViewModel[];
  identifiersTitle: string;
  identifiersTableLabel: string;
  identifiers: SecurityIdentityIdentifierRowViewModel[];
  identifierEmptyText: string;
  aliasesTitle: string;
  aliasesTableLabel: string;
  aliases: SecurityIdentityAliasRowViewModel[];
  aliasEmptyText: string;
}


export interface SecurityConflictActionViewModel {
  resolution: SecurityConflictResolution;
  label: string;
  ariaLabel: string;
  variant: "outline" | "ghost";
  disabled: boolean;
  disabledReason: string | null;
}


export interface SecurityConflictRefreshCommandViewModel {
  label: string;
  ariaLabel: string;
  disabled: boolean;
  disabledReason: string | null;
  busy: boolean;
  busyLabel: string | null;
  feedbackId: string;
  feedbackText: string | null;
}


export interface SecurityConflictRowViewModel extends SecurityMasterConflict {
  statusLabel: string;
  statusTone: "warning" | "neutral";
  isOpen: boolean;
  isResolving: boolean;
  fieldLabel: string;
  providerASummary: string;
  providerBSummary: string;
  detectedLabel: string;
  ariaLabel: string;
  resolutionStatusText: string | null;
  actions: SecurityConflictActionViewModel[];
}


export const defaultSecurityMasterServices: SecurityMasterServices = {
  search: (query) => searchSecurities(query),
  getIdentity: (securityId) => getSecurityIdentity(securityId),
  getConflicts: () => getSecurityConflicts(),
  resolveConflict: (request) => resolveSecurityConflict(request)
};


export const defaultSecurityMasterDrillInServices: SecurityMasterDrillInServices = {
  getCorporateActions: (securityId) => getCorporateActions(securityId),
  getReferenceDataCoverage: (seed) => getReferenceDataWorkbenchCoverage(seed),
  getInstrumentPassport: (securityId) => getSecurityInstrumentPassport(securityId),
  getTradingParameters: (securityId) => getTradingParameters(securityId),
  getTrustSnapshot: (securityId) => getSecurityTrustSnapshot(securityId)
};


export function useSecurityMasterViewModel(
  active: boolean,
  services: SecurityMasterServices = defaultSecurityMasterServices,
  drillInServices: SecurityMasterDrillInServices = defaultSecurityMasterDrillInServices,
  searchDelayMs = 350
) {
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<SecurityMasterEntry[] | null>(null);
  const [searching, setSearching] = useState(false);
  const [searchError, setSearchError] = useState<ApiErrorDisplay | null>(null);
  const [selectedSecurityId, setSelectedSecurityId] = useState<string | null>(null);
  const [identity, setIdentity] = useState<SecurityIdentityDrillIn | null>(null);
  const [identityLoading, setIdentityLoading] = useState(false);
  const [identityError, setIdentityError] = useState<ApiErrorDisplay | null>(null);
  const [conflicts, setConflicts] = useState<SecurityMasterConflict[] | null>(null);
  const [conflictsLoading, setConflictsLoading] = useState(false);
  const [conflictsError, setConflictsError] = useState<ApiErrorDisplay | null>(null);
  const [conflictResolvingId, setConflictResolvingId] = useState<string | null>(null);
  const [conflictActionError, setConflictActionError] = useState<ApiErrorDisplay | null>(null);
  const [corporateActions, setCorporateActions] = useState<CorporateAction[] | null>(null);
  const [corporateActionsLoading, setCorporateActionsLoading] = useState(false);
  const [corporateActionsError, setCorporateActionsError] = useState<ApiErrorDisplay | null>(null);
  const [selectedCorporateActionId, setSelectedCorporateActionId] = useState<string | null>(null);
  const [referenceDataCoverage, setReferenceDataCoverage] = useState<ReferenceDataWorkbenchCoverage | null>(null);
  const [referenceDataCoverageLoading, setReferenceDataCoverageLoading] = useState(false);
  const [referenceDataCoverageError, setReferenceDataCoverageError] = useState<ApiErrorDisplay | null>(null);
  const [selectedReferenceDataEndpointId, setSelectedReferenceDataEndpointId] = useState<string | null>(null);
  const [selectedScheduleEventId, setSelectedScheduleEventId] = useState<string | null>(null);
  const [selectedOpenLotId, setSelectedOpenLotId] = useState<string | null>(null);
  const [trustSnapshot, setTrustSnapshot] = useState<SecurityMasterTrustSnapshot | null>(null);
  const [trustSnapshotLoading, setTrustSnapshotLoading] = useState(false);
  const [trustSnapshotError, setTrustSnapshotError] = useState<ApiErrorDisplay | null>(null);
  const [instrumentPassport, setInstrumentPassport] = useState<InstrumentPassport | null>(null);
  const [instrumentPassportLoading, setInstrumentPassportLoading] = useState(false);
  const [instrumentPassportError, setInstrumentPassportError] = useState<ApiErrorDisplay | null>(null);
  const [tradingParameters, setTradingParameters] = useState<TradingParameters | null>(null);
  const [tradingParametersLoading, setTradingParametersLoading] = useState(false);
  const [tradingParametersError, setTradingParametersError] = useState<ApiErrorDisplay | null>(null);
  const searchTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const searchGenerationRef = useRef(0);
  const identityGenerationRef = useRef(0);
  const conflictGenerationRef = useRef(0);
  const conflictResolvingIdRef = useRef<string | null>(null);
  const resetReferenceDataCoverage = useCallback(() => {
    setReferenceDataCoverage(null);
    setReferenceDataCoverageLoading(false);
    setReferenceDataCoverageError(null);
    setSelectedReferenceDataEndpointId(null);
  }, []);

  useEffect(() => () => {
    if (searchTimerRef.current) {
      clearTimeout(searchTimerRef.current);
    }
    searchGenerationRef.current += 1;
    identityGenerationRef.current += 1;
    conflictGenerationRef.current += 1;
    conflictResolvingIdRef.current = null;
  }, []);

  useEffect(() => {
    if (active) {
      return;
    }

    if (searchTimerRef.current) {
      clearTimeout(searchTimerRef.current);
      searchTimerRef.current = null;
    }

    searchGenerationRef.current += 1;
    identityGenerationRef.current += 1;
    conflictGenerationRef.current += 1;
    setSearching(false);
    setSelectedSecurityId(null);
    setIdentity(null);
    setIdentityLoading(false);
    setIdentityError(null);
    setConflictsLoading(false);
    setConflictsError(null);
    setConflictResolvingId(null);
    conflictResolvingIdRef.current = null;
    setConflictActionError(null);
    setCorporateActions(null);
    setCorporateActionsLoading(false);
    setCorporateActionsError(null);
    setSelectedCorporateActionId(null);
    resetReferenceDataCoverage();
    setSelectedScheduleEventId(null);
    setSelectedOpenLotId(null);
    setTrustSnapshot(null);
    setTrustSnapshotLoading(false);
    setTrustSnapshotError(null);
    setInstrumentPassport(null);
    setInstrumentPassportLoading(false);
    setInstrumentPassportError(null);
    setTradingParameters(null);
    setTradingParametersLoading(false);
    setTradingParametersError(null);
  }, [active, resetReferenceDataCoverage]);

  const refreshConflicts = useCallback(async () => {
    if (!active) {
      return;
    }

    if (conflictResolvingIdRef.current) {
      return;
    }

    const generation = conflictGenerationRef.current + 1;
    conflictGenerationRef.current = generation;
    setConflictsLoading(true);
    setConflictsError(null);

    try {
      const rows = await services.getConflicts();
      if (conflictGenerationRef.current === generation) {
        setConflicts(rows);
      }
    } catch (err) {
      if (conflictGenerationRef.current === generation) {
        setConflicts([]);
        setConflictsError(describeApiError(err, "Identifier conflicts failed to load."));
      }
    } finally {
      if (conflictGenerationRef.current === generation) {
        setConflictsLoading(false);
      }
    }
  }, [active, resetReferenceDataCoverage, services]);

  useEffect(() => {
    void refreshConflicts();
  }, [refreshConflicts]);

  useEffect(() => {
    if (!active || !selectedSecurityId) {
      setCorporateActions(null);
      setCorporateActionsLoading(false);
      setCorporateActionsError(null);
      setSelectedCorporateActionId(null);
      resetReferenceDataCoverage();
      setSelectedScheduleEventId(null);
      setSelectedOpenLotId(null);
      setTrustSnapshot(null);
      setTrustSnapshotLoading(false);
      setTrustSnapshotError(null);
      setTradingParameters(null);
      setTradingParametersLoading(false);
      setTradingParametersError(null);
      return;
    }

    let cancelled = false;
    setCorporateActionsLoading(true);
    setCorporateActionsError(null);
    setReferenceDataCoverage(null);
    setReferenceDataCoverageLoading(true);
    setReferenceDataCoverageError(null);
    setSelectedReferenceDataEndpointId(null);
    setTrustSnapshotLoading(true);
    setTrustSnapshotError(null);
    setInstrumentPassportLoading(true);
    setInstrumentPassportError(null);
    setTradingParametersLoading(true);
    setTradingParametersError(null);

    const selectedSearchResult = results?.find((entry) => entry.securityId === selectedSecurityId) ?? null;

    drillInServices.getReferenceDataCoverage(buildReferenceDataWorkbenchSeed(selectedSecurityId, selectedSearchResult))
      .then((coverage) => {
        if (!cancelled) {
          setReferenceDataCoverage(coverage);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setReferenceDataCoverage(null);
          setReferenceDataCoverageError(describeApiError(err, "Reference data workbench failed to load."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setReferenceDataCoverageLoading(false);
        }
      });
    drillInServices.getCorporateActions(selectedSecurityId)
      .then((rows) => {
        if (!cancelled) {
          setCorporateActions(rows);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setCorporateActions([]);
          setCorporateActionsError(describeApiError(err, "Corporate actions failed to load."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setCorporateActionsLoading(false);
        }
      });

    drillInServices.getTrustSnapshot(selectedSecurityId)
      .then((snapshot) => {
        if (!cancelled) {
          setTrustSnapshot(snapshot);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setTrustSnapshot(null);
          setTrustSnapshotError(describeApiError(err, "Trust snapshot failed to load."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setTrustSnapshotLoading(false);
        }
      });

    drillInServices.getInstrumentPassport(selectedSecurityId)
      .then((passport) => {
        if (!cancelled) {
          setInstrumentPassport(passport);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setInstrumentPassport(null);
          setInstrumentPassportError(describeApiError(err, "Instrument passport failed to load."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setInstrumentPassportLoading(false);
        }
      });

    drillInServices.getTradingParameters(selectedSecurityId)
      .then((params) => {
        if (!cancelled) {
          setTradingParameters(params);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setTradingParameters(null);
          setTradingParametersError(describeApiError(err, "Trading parameters failed to load."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setTradingParametersLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [active, selectedSecurityId, drillInServices, resetReferenceDataCoverage, results]);

  const updateQuery = useCallback((nextQuery: string) => {
    setQuery(nextQuery);
    setSelectedSecurityId(null);
    setIdentity(null);
    setIdentityError(null);
    setSearchError(null);
    setSelectedCorporateActionId(null);
    resetReferenceDataCoverage();
    setSelectedScheduleEventId(null);
    setSelectedOpenLotId(null);
    setTrustSnapshot(null);
    setTrustSnapshotLoading(false);
    setTrustSnapshotError(null);
    setInstrumentPassport(null);
    setInstrumentPassportLoading(false);
    setInstrumentPassportError(null);
    identityGenerationRef.current += 1;

    if (searchTimerRef.current) {
      clearTimeout(searchTimerRef.current);
      searchTimerRef.current = null;
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
            setSearchError(describeApiError(err, "Security search failed."));
          }
        })
        .finally(() => {
          if (searchGenerationRef.current === generation) {
            setSearching(false);
          }
        });
    }, searchDelayMs);
  }, [resetReferenceDataCoverage, searchDelayMs, services]);

  const selectSecurity = useCallback(async (securityId: string) => {
    if (!active) {
      return;
    }

    const generation = identityGenerationRef.current + 1;
    identityGenerationRef.current = generation;
    setSelectedSecurityId(securityId);
    setIdentity(null);
    setIdentityError(null);
    setIdentityLoading(true);
    setCorporateActions(null);
    setCorporateActionsError(null);
    setSelectedCorporateActionId(null);
    resetReferenceDataCoverage();
    setSelectedScheduleEventId(null);
    setSelectedOpenLotId(null);
    setTrustSnapshot(null);
    setTrustSnapshotLoading(false);
    setTrustSnapshotError(null);
    setInstrumentPassport(null);
    setInstrumentPassportLoading(false);
    setInstrumentPassportError(null);
    setTradingParameters(null);
    setTradingParametersError(null);

    try {
      const detail = await services.getIdentity(securityId);
      if (identityGenerationRef.current === generation) {
        setIdentity(detail);
      }
    } catch (err) {
      if (identityGenerationRef.current === generation) {
        setIdentityError(describeApiError(err, "Identity drill-in failed."));
      }
    } finally {
      if (identityGenerationRef.current === generation) {
        setIdentityLoading(false);
      }
    }
  }, [active, resetReferenceDataCoverage, services]);

  const resolveConflict = useCallback(async (
    conflictId: string,
    resolution: ResolveConflictRequest["resolution"]
  ) => {
    conflictResolvingIdRef.current = conflictId;
    setConflictResolvingId(conflictId);
    setConflictActionError(null);

    try {
      const updated = await services.resolveConflict({ conflictId, resolution, resolvedBy: "operator", reason: `${resolution} action from the shared conflict queue.` });
      setConflicts((current) => current?.map((conflict) => (
        conflict.conflictId === conflictId ? updated : conflict
      )) ?? current);
    } catch (err) {
      setConflictActionError(describeApiError(err, "Conflict resolution failed."));
    } finally {
      conflictResolvingIdRef.current = null;
      setConflictResolvingId(null);
    }
  }, [services]);

  const searchState = useMemo(
    () => buildSecuritySearchState({
      query,
      searching,
      results,
      selectedSecurityId,
      searchError,
      identityLoading,
      identityError
    }),
    [identityError, identityLoading, query, results, searchError, searching, selectedSecurityId]
  );
  const conflictRows = useMemo(
    () => buildSecurityConflictRows(conflicts, conflictResolvingId),
    [conflictResolvingId, conflicts]
  );
  const identityView = useMemo(
    () => buildSecurityIdentityDrillInState(identity),
    [identity]
  );
  const selectedSearchResult = useMemo(
    () => selectedSecurityId ? results?.find((entry) => entry.securityId === selectedSecurityId) ?? null : null,
    [results, selectedSecurityId]
  );
  const corporateActionRows = useMemo(
    () => buildCorporateActionRows(corporateActions, selectedCorporateActionId),
    [corporateActions, selectedCorporateActionId]
  );
  useEffect(() => {
    if (corporateActionRows.length === 0) {
      if (selectedCorporateActionId !== null) {
        setSelectedCorporateActionId(null);
      }
      return;
    }

    if (!selectedCorporateActionId || !corporateActionRows.some((row) => row.rowId === selectedCorporateActionId)) {
      setSelectedCorporateActionId(corporateActionRows[0].rowId);
    }
  }, [corporateActionRows, selectedCorporateActionId]);
  const corporateActionsView = useMemo(
    () => buildCorporateActionsViewState(
      selectedSecurityId,
      corporateActions,
      selectedCorporateActionId,
      corporateActionsLoading,
      corporateActionsError
    ),
    [corporateActions, corporateActionsError, corporateActionsLoading, selectedCorporateActionId, selectedSecurityId]
  );
  const securitySchedules = useMemo(() => {
    if (trustSnapshot?.scheduleBook) {
      return mapScheduleBookToCashFlowScheduleEvents(selectedSecurityId, trustSnapshot);
    }

    if (trustSnapshot || trustSnapshotLoading || trustSnapshotError) {
      return [];
    }

    return resolveSecurityScheduleEvents(selectedSecurityId);
  }, [selectedSecurityId, trustSnapshot, trustSnapshotError, trustSnapshotLoading]);
  const securityScheduleRows = useMemo(
    () => buildSecurityScheduleRows(securitySchedules, selectedScheduleEventId),
    [securitySchedules, selectedScheduleEventId]
  );
  useEffect(() => {
    if (securityScheduleRows.length === 0) {
      if (selectedScheduleEventId !== null) {
        setSelectedScheduleEventId(null);
      }
      return;
    }

    if (!selectedScheduleEventId || !securityScheduleRows.some((row) => row.rowId === selectedScheduleEventId)) {
      setSelectedScheduleEventId(securityScheduleRows[0].rowId);
    }
  }, [securityScheduleRows, selectedScheduleEventId]);
  const schedulesView = useMemo(
    () => buildSecuritySchedulesViewState({
      securityId: selectedSecurityId,
      displayName: identity?.displayName ?? selectedSearchResult?.displayName ?? null,
      assetClass: identity?.assetClass ?? selectedSearchResult?.classification.assetClass ?? null,
      schedules: securitySchedules,
      selectedRowId: selectedScheduleEventId,
      loading: trustSnapshotLoading,
      error: trustSnapshotError,
      factorHistoryCount: trustSnapshot?.scheduleBook?.factorHistory.length ?? 0,
      provenanceCount: trustSnapshot?.scheduleBook?.provenanceHistory.length ?? 0,
      sourceSummary: trustSnapshot?.scheduleBook?.sourceSummary ?? trustSnapshot?.scheduleSummary?.sourceSummary ?? null,
      declaredExercise: trustSnapshot?.scheduleBook?.declaredExercise ?? trustSnapshot?.scheduleSummary?.declaredExercise ?? null,
      governance: trustSnapshot?.governance ?? null,
      cashFlowBlocker: trustSnapshot?.cashFlowBlocker ?? null
    }),
    [
      identity?.assetClass,
      identity?.displayName,
      securitySchedules,
      selectedScheduleEventId,
      selectedSearchResult?.classification.assetClass,
      selectedSearchResult?.displayName,
      selectedSecurityId,
      trustSnapshot,
      trustSnapshotError,
      trustSnapshotLoading
    ]
  );
  const openLotRows = useMemo(
    () => buildSecurityOpenLotRows(trustSnapshot?.openLotReadModel ?? null, selectedOpenLotId),
    [selectedOpenLotId, trustSnapshot?.openLotReadModel]
  );
  useEffect(() => {
    if (openLotRows.length === 0) {
      if (selectedOpenLotId !== null) {
        setSelectedOpenLotId(null);
      }
      return;
    }

    if (!selectedOpenLotId || !openLotRows.some((row) => row.rowId === selectedOpenLotId)) {
      setSelectedOpenLotId(openLotRows[0].rowId);
    }
  }, [openLotRows, selectedOpenLotId]);
  const openLotReadModelView = useMemo(
    () => buildSecurityOpenLotReadModelViewState({
      securityId: selectedSecurityId,
      readModel: trustSnapshot?.openLotReadModel ?? null,
      selectedRowId: selectedOpenLotId,
      loading: trustSnapshotLoading,
      error: trustSnapshotError
    }),
    [
      selectedOpenLotId,
      selectedSecurityId,
      trustSnapshot?.openLotReadModel,
      trustSnapshotError,
      trustSnapshotLoading
    ]
  );
  const instrumentPassportView = useMemo(
    () => buildInstrumentPassportViewState({
      securityId: selectedSecurityId,
      passport: instrumentPassport,
      loading: instrumentPassportLoading,
      error: instrumentPassportError
    }),
    [instrumentPassport, instrumentPassportError, instrumentPassportLoading, selectedSecurityId]
  );
  const tradingParametersView = useMemo(
    () => buildTradingParametersViewState(tradingParameters, tradingParametersLoading, tradingParametersError),
    [tradingParameters, tradingParametersLoading, tradingParametersError]
  );
  const referenceDataWorkbenchView = useMemo(
    () => buildReferenceDataWorkbenchViewState({
      securityId: selectedSecurityId,
      coverage: referenceDataCoverage,
      loading: referenceDataCoverageLoading,
      error: referenceDataCoverageError,
      selectedRowId: selectedReferenceDataEndpointId
    }),
    [
      referenceDataCoverage,
      referenceDataCoverageError,
      referenceDataCoverageLoading,
      selectedReferenceDataEndpointId,
      selectedSecurityId
    ]
  );
  useEffect(() => {
    if (referenceDataWorkbenchView.rows.length === 0) {
      if (selectedReferenceDataEndpointId !== null) {
        setSelectedReferenceDataEndpointId(null);
      }
      return;
    }

    if (!selectedReferenceDataEndpointId || !referenceDataWorkbenchView.rows.some((row) => row.rowId === selectedReferenceDataEndpointId)) {
      setSelectedReferenceDataEndpointId(referenceDataWorkbenchView.rows[0].rowId);
    }
  }, [referenceDataWorkbenchView.rows, selectedReferenceDataEndpointId]);
  const openConflictCount = countOpenSecurityConflicts(conflicts);
  const conflictRefreshCommand = useMemo(
    () => buildSecurityConflictRefreshCommand(conflictsLoading, conflictsError, conflictResolvingId),
    [conflictResolvingId, conflictsError, conflictsLoading]
  );
  const pageView = useMemo(
    () => buildSecurityMasterPageViewState({
      query,
      results,
      selectedSecurityId,
      selectedDisplayName: identity?.displayName ?? selectedSearchResult?.displayName ?? null,
      selectedAssetClass: identity?.assetClass ?? selectedSearchResult?.classification.assetClass ?? null,
      selectedStatus: identity?.status ?? selectedSearchResult?.status ?? null,
      identity,
      identityLoading,
      conflicts,
      conflictsLoading,
      corporateActions,
      instrumentPassport,
      instrumentPassportLoading,
      instrumentPassportError,
      referenceDataCoverage,
      referenceDataLoading: referenceDataCoverageLoading,
      referenceDataError: referenceDataCoverageError,
      securitySchedules,
      openLotReadModel: trustSnapshot?.openLotReadModel ?? null,
      trustSnapshotLoading,
      trustSnapshotError,
      tradingParameters
    }),
    [
      conflicts,
      conflictsLoading,
      corporateActions,
      instrumentPassport,
      instrumentPassportError,
      instrumentPassportLoading,
      referenceDataCoverage,
      referenceDataCoverageError,
      referenceDataCoverageLoading,
      securitySchedules,
      trustSnapshot?.openLotReadModel,
      trustSnapshotError,
      trustSnapshotLoading,
      identity,
      identityLoading,
      query,
      results,
      selectedSearchResult,
      selectedSecurityId,
      tradingParameters
    ]
  );

  return {
    query,
    updateQuery,
    pageView,
    results,
    searching,
    selectedSecurityId,
    selectSecurity,
    identity,
    identityView,
    identityLoading,
    identityErrorText: identityError?.summary ?? null,
    identityErrorDetails: identityError?.details ?? [],
    conflicts,
    conflictRows,
    hasConflicts: conflictRows.length > 0,
    conflictEmptyText: "No identifier conflicts detected.",
    conflictSectionAriaLabel: "Security Master identifier conflict queue",
    conflictsLoading,
    conflictsErrorText: conflictsError?.summary ?? null,
    conflictsErrorDetails: conflictsError?.details ?? [],
    conflictResolvingId,
    conflictActionErrorText: conflictActionError?.summary ?? null,
    conflictActionErrorDetails: conflictActionError?.details ?? [],
    conflictRefreshCommand,
    refreshConflicts,
    resolveConflict,
    openConflictCount,
    conflictCountLabel: `${openConflictCount} open`,
    corporateActions,
    corporateActionRows,
    corporateActionsView,
    referenceDataCoverage,
    referenceDataWorkbenchView,
    selectReferenceDataEndpoint: setSelectedReferenceDataEndpointId,
    selectCorporateAction: setSelectedCorporateActionId,
    hasCorporateActions: (corporateActions?.length ?? 0) > 0,
    corporateActionsLoading,
    corporateActionsErrorText: corporateActionsError?.summary ?? null,
    securitySchedules,
    securityScheduleRows,
    schedulesView,
    selectScheduleEvent: setSelectedScheduleEventId,
    trustSnapshot,
    trustSnapshotLoading,
    trustSnapshotErrorText: trustSnapshotError?.summary ?? null,
    trustSnapshotErrorDetails: trustSnapshotError?.details ?? [],
    openLotRows,
    openLotReadModelView,
    selectOpenLot: setSelectedOpenLotId,
    instrumentPassport,
    instrumentPassportView,
    instrumentPassportLoading,
    instrumentPassportErrorText: instrumentPassportError?.summary ?? null,
    instrumentPassportErrorDetails: instrumentPassportError?.details ?? [],
    tradingParameters,
    tradingParametersView,
    tradingParametersLoading,
    tradingParametersErrorText: tradingParametersError?.summary ?? null,
    ...searchState
  };
}


export const securitySearchResultColumns: SecuritySearchResultColumnViewModel[] = [
  { id: "name", label: "Name" },
  { id: "assetClass", label: "Asset Class" },
  { id: "primaryId", label: "Primary ID" },
  { id: "currency", label: "Currency" },
  { id: "status", label: "Status" }
];


export const SECURITY_IDENTITY_DETAIL_PANEL_ID = "security-master-identity-detail";


export function buildSecurityMasterPageViewState({
  query,
  results,
  selectedSecurityId,
  selectedDisplayName,
  selectedAssetClass,
  selectedStatus,
  identity,
  identityLoading,
  conflicts,
  conflictsLoading,
  corporateActions,
  instrumentPassport,
  instrumentPassportLoading = false,
  instrumentPassportError = null,
  referenceDataCoverage = null,
  referenceDataLoading = false,
  referenceDataError = null,
  securitySchedules,
  openLotReadModel,
  trustSnapshotLoading = false,
  trustSnapshotError = null,
  tradingParameters
}: {
  query: string;
  results: SecurityMasterEntry[] | null;
  selectedSecurityId: string | null;
  selectedDisplayName: string | null;
  selectedAssetClass: string | null;
  selectedStatus: string | null;
  identity: SecurityIdentityDrillIn | null;
  identityLoading: boolean;
  conflicts: SecurityMasterConflict[] | null;
  conflictsLoading: boolean;
  corporateActions: CorporateAction[] | null;
  instrumentPassport?: InstrumentPassport | null;
  instrumentPassportLoading?: boolean;
  instrumentPassportError?: ApiErrorDisplay | string | null;
  referenceDataCoverage?: ReferenceDataWorkbenchCoverage | null;
  referenceDataLoading?: boolean;
  referenceDataError?: ApiErrorDisplay | string | null;
  securitySchedules?: SecurityCashFlowScheduleEvent[] | null;
  openLotReadModel?: SecurityMasterOpenLotReadModel | null;
  trustSnapshotLoading?: boolean;
  trustSnapshotError?: ApiErrorDisplay | string | null;
  tradingParameters: TradingParameters | null;
}): SecurityMasterPageViewState {
  const hasQuery = query.trim().length > 0;
  const resultCount = results?.length ?? 0;
  const openConflictCount = countOpenSecurityConflicts(conflicts);
  const selectedName = selectedDisplayName?.trim() || (selectedSecurityId ? "Selected security" : "None selected");
  const selectedClass = selectedAssetClass?.trim() || "Unclassified";
  const statusLabel = selectedStatus?.trim() || (selectedSecurityId ? "Pending" : "No selection");
  const identifiersLabel = identity
    ? formatCount(identity.identifiers?.length ?? 0, "identifier")
    : identityLoading
      ? "Loading identifiers"
      : "No identifiers loaded";
  const aliasesLabel = identity ? formatCount(identity.aliases?.length ?? 0, "alias") : "No aliases loaded";
  const corporateActionLabel = corporateActions
    ? formatCount(corporateActions.length, "corporate action")
    : selectedSecurityId
      ? "Loading schedules"
      : "No selection";
  const normalizedPassportError = normalizeApiErrorDisplay(instrumentPassportError);
  const referenceDataLabel = referenceDataError
    ? "Error"
    : referenceDataLoading
      ? "Loading"
      : referenceDataCoverage
        ? formatCount(referenceDataCoverage.endpoints?.length ?? 0, "route")
        : selectedSecurityId
          ? "Pending"
          : "No selection";
  const referenceRouteCounts = summarizeReferenceDataRoutes(referenceDataCoverage?.endpoints ?? []);
  const referenceDataDetail = referenceDataError
    ? "Reference coverage could not be loaded."
    : referenceDataLoading
      ? "Refreshing endpoint coverage for the selected record."
      : referenceDataCoverage
        ? `${formatCountValue(referenceRouteCounts.readyCount)} ready · ${formatCountValue(referenceRouteCounts.reviewCount)} need review · ${formatCountValue(referenceRouteCounts.deferredOrBlockedCount)} deferred or blocked · ${formatCountValue(referenceRouteCounts.totalCount)} total.`
        : selectedSecurityId
          ? "Reference coverage is queued for the selected record."
          : "Select a security to inspect source coverage.";
  const scheduleLabel = securitySchedules
    ? securitySchedules.length > 0
      ? formatCount(securitySchedules.length, "cash-flow event")
      : corporateActionLabel
    : corporateActionLabel;
  const openLotLabel = trustSnapshotError
    ? "Error"
    : trustSnapshotLoading
      ? "Loading"
      : openLotReadModel
        ? formatCount(openLotReadModel.lots?.length ?? 0, "lot")
        : selectedSecurityId
          ? "No lots"
          : "No selection";
  const operationsReadiness = instrumentPassport?.operationsWorkbench?.readiness ?? [];
  const readyOperationCount = operationsReadiness.filter((item) => item.status === "Ready" || item.status === "Complete").length;
  const totalOperationCount = operationsReadiness.length;
  const passportTrustSummary = instrumentPassport?.trustPosture?.summary?.trim() || "Passport evidence incomplete";
  const passportTrustTone = instrumentPassport?.trustPosture?.tone?.trim().toLowerCase() || "unknown";
  const passportControlLabel = normalizedPassportError
    ? "Error"
    : instrumentPassportLoading
      ? "Loading"
      : instrumentPassport
        ? passportTrustSummary
        : tradingParameters
          ? "Controls set"
          : selectedSecurityId
            ? "Pending"
            : "No selection";
  const passportControlDetail = normalizedPassportError
    ? "Passport readiness could not be loaded."
    : instrumentPassportLoading
      ? "Refreshing passport, provider confidence, and control readiness."
      : instrumentPassport
        ? totalOperationCount > 0
          ? `${readyOperationCount}/${totalOperationCount} operations checks ready; trading controls ${tradingParameters ? "loaded" : "pending"}.`
          : `Trust posture ${passportTrustSummary}; trading controls ${tradingParameters ? "loaded" : "pending"}.`
        : selectedSecurityId
          ? "Passport and control readiness are queued for the selected record."
          : "Select a security to inspect passport and control readiness.";
  const passportControlTone: SecurityMasterPageMetricTone = normalizedPassportError || instrumentPassportLoading
    ? "warning"
    : instrumentPassport
      ? passportTrustTone === "success" || passportTrustTone === "ready" || passportTrustTone === "trusted"
        ? "success"
        : "warning"
      : tradingParameters
        ? "success"
        : "default";
  const passportOperationsReady = operationsReadiness.length > 0
    && operationsReadiness.every((item) => item.status === "Ready" || item.status === "Complete");
  const passportEvidenceReady = Boolean(instrumentPassport)
    && (passportTrustTone === "success" || passportTrustTone === "ready" || passportTrustTone === "trusted")
    && passportOperationsReady;
  const coverageDetails: string[] = [];
  let coverageHasIssue = false;
  let coverageHasPendingCheck = false;

  if (conflictsLoading || conflicts === null) {
    coverageDetails.push("conflict checks are pending");
    coverageHasPendingCheck = true;
  } else if (openConflictCount > 0) {
    coverageDetails.push(formatCount(openConflictCount, "open conflict"));
    coverageHasIssue = true;
  }

  if (referenceDataError) {
    coverageDetails.push("reference-route coverage is unavailable");
    coverageHasIssue = true;
  } else if (referenceDataLoading || referenceDataCoverage === null) {
    coverageDetails.push("reference-route checks are pending");
    coverageHasPendingCheck = true;
  } else if (referenceRouteCounts.totalCount === 0) {
    coverageDetails.push("no reference routes returned coverage evidence");
    coverageHasIssue = true;
  } else {
    if (referenceRouteCounts.reviewCount > 0) {
      coverageDetails.push(`${formatCount(referenceRouteCounts.reviewCount, "route")} ${referenceRouteCounts.reviewCount === 1 ? "needs" : "need"} review`);
      coverageHasIssue = true;
    }
    if (referenceRouteCounts.deferredOrBlockedCount > 0) {
      coverageDetails.push(`${formatCount(referenceRouteCounts.deferredOrBlockedCount, "route")} ${referenceRouteCounts.deferredOrBlockedCount === 1 ? "is" : "are"} deferred or blocked`);
      coverageHasIssue ||= referenceRouteCounts.blockedCount > 0;
    }
  }

  if (normalizedPassportError) {
    coverageDetails.push("passport evidence is unavailable");
    coverageHasIssue = true;
  } else if (instrumentPassportLoading) {
    coverageDetails.push("passport checks are pending");
    coverageHasPendingCheck = true;
  } else if (!passportEvidenceReady) {
    coverageDetails.push(instrumentPassport ? "passport evidence is incomplete" : "passport evidence is missing");
    coverageHasIssue = true;
  }

  const coveragePosture: SecurityMasterCoveragePostureViewModel = !selectedSecurityId
    ? {
        label: "Select a record",
        detail: "Select a security before relying on conflicts, reference coverage, or passport evidence.",
        tone: "default"
      }
    : coverageHasIssue
      ? {
          label: "Review required",
          detail: `${coverageDetails.join("; ")}.`,
          tone: "warning"
        }
      : coverageHasPendingCheck
        ? {
            label: "Verification pending",
            detail: `${coverageDetails.join("; ")}.`,
            tone: "warning"
          }
        : {
            label: "Ready",
            detail: referenceRouteCounts.deferredCount > 0
              ? `No open conflicts; all probed reference routes are ready; passport evidence is trusted; ${formatCount(referenceRouteCounts.deferredCount, "write-capable route")} intentionally deferred.`
              : "No open conflicts; all reference routes are ready; passport evidence is trusted.",
            tone: "success"
          };

  return {
    ariaLabel: "Security Master command deck",
    eyebrow: "Security Master",
    title: "Security Master command deck",
    description: "Search, inspect, and reconcile trusted security reference records from one dense master-detail page.",
    coveragePosture,
    metrics: [
      {
        id: "selected",
        label: "Selected record",
        value: selectedName,
        detail: selectedSecurityId
          ? `${selectedClass} · ${statusLabel}.`
          : hasQuery
            ? `${formatCount(resultCount, "security")} returned. Select a row to open the record.`
            : "Search by ticker, ISIN, CUSIP, FIGI, or display name.",
        tone: selectedSecurityId ? "success" : "default"
      },
      {
        id: "conflicts",
        label: "Open conflicts",
        value: conflictsLoading ? "Loading" : formatCountValue(openConflictCount),
        detail: conflictsLoading
          ? "Refreshing provider conflict evidence."
          : openConflictCount > 0
            ? `${formatCount(openConflictCount, "open conflict")} requiring operator review.`
            : "No open conflicts need operator review.",
        tone: openConflictCount > 0 || conflictsLoading ? "warning" : "success"
      },
      {
        id: "reference",
        label: "Reference coverage",
        value: referenceDataLabel,
        detail: referenceDataDetail,
        tone: referenceDataError
          || referenceDataLoading
          || referenceRouteCounts.reviewCount > 0
          || referenceRouteCounts.blockedCount > 0
          || (referenceDataCoverage !== null && referenceRouteCounts.totalCount === 0)
          ? "warning"
          : referenceDataCoverage
            ? "success"
            : "default"
      },
      {
        id: "passport",
        label: "Passport controls",
        value: passportControlLabel,
        detail: passportControlDetail,
        tone: passportControlTone
      }
    ],
    detailEyebrow: "Security detail",
    detailTitle: "Security detail page",
    detailSubtitle: selectedSecurityId ? `${selectedClass} · ${statusLabel}` : "Select a security",
    detailDescription: selectedSecurityId
      ? `${selectedName} reference data, schedules, trading controls, lots, and audit evidence are grouped below the selected master row.`
      : "Select a security from the master table to inspect its reference record.",
    detailStatusLabel: statusLabel,
    detailStatusBadgeVariant: selectedSecurityId
      ? statusLabel.toLowerCase() === "active"
        ? "success"
        : "warning"
      : "outline",
    detailToolbarAriaLabel: selectedSecurityId ? `Security detail sections for ${selectedName}` : "Security detail sections",
    detailSections: [
      { id: "overview", label: "Overview", value: identifiersLabel, active: true },
      { id: "reference", label: "Reference", value: referenceDataLabel },
      { id: "schedules", label: "Schedules", value: scheduleLabel },
      { id: "lots", label: "Open lots", value: openLotLabel },
      { id: "controls", label: "Controls", value: tradingParameters ? "Trading set" : selectedSecurityId ? "Pending" : "No selection" },
      { id: "audit", label: "Audit", value: openConflictCount > 0 ? formatCount(openConflictCount, "conflict") : aliasesLabel }
    ]
  };
}


export function buildSecuritySearchState({
  query,
  searching,
  results,
  selectedSecurityId,
  searchError,
  identityLoading,
  identityError
}: {
  query: string;
  searching: boolean;
  results: SecurityMasterEntry[] | null;
  selectedSecurityId?: string | null;
  searchError: string | ApiErrorDisplay | null;
  identityLoading: boolean;
  identityError: string | ApiErrorDisplay | null;
}): SecuritySearchState {
  const trimmedQuery = query.trim();
  const resultCount = results?.length ?? 0;
  const hasResults = resultCount > 0;
  const resultRows = buildSecuritySearchResultRows(results, selectedSecurityId ?? null);
  const normalizedSearchError = normalizeApiErrorDisplay(searchError);
  const normalizedIdentityError = normalizeApiErrorDisplay(identityError);
  const searchErrorText = normalizedSearchError
    ? normalizedSearchError.summary.startsWith("Security search failed")
      ? normalizedSearchError.summary
      : `Security search failed: ${normalizedSearchError.summary}`
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
    resultsTableLabel: "Security search results",
    resultColumns: securitySearchResultColumns,
    resultRows,
    searchStatusText,
    searchErrorText,
    searchErrorDetails: normalizedSearchError?.details ?? [],
    statusAnnouncement: buildSecurityStatusAnnouncement({
      searching,
      trimmedQuery,
      resultCount,
      results,
      searchErrorText,
      identityLoading,
      identityError: normalizedIdentityError?.summary ?? null
    })
  };
}


export function countOpenSecurityConflicts(conflicts: SecurityMasterConflict[] | null): number {
  return conflicts?.filter((conflict) => conflict.status === "Open").length ?? 0;
}


export function buildSecurityConflictRefreshCommand(
  loading: boolean,
  errorText: string | ApiErrorDisplay | null,
  resolvingConflictId: string | null = null
): SecurityConflictRefreshCommandViewModel {
  const normalizedError = normalizeApiErrorDisplay(errorText);
  if (resolvingConflictId) {
    const disabledReason = `Wait until identifier conflict ${resolvingConflictId} finishes resolving before refreshing the conflict queue.`;

    return {
      label: "Refresh conflicts",
      ariaLabel: `Refresh disabled while identifier conflict ${resolvingConflictId} is resolving`,
      disabled: true,
      disabledReason,
      busy: false,
      busyLabel: null,
      feedbackId: "security-conflict-refresh-feedback",
      feedbackText: disabledReason
    };
  }

  return {
    label: loading ? "Refreshing..." : normalizedError ? "Retry conflicts" : "Refresh conflicts",
    ariaLabel: loading
      ? "Refreshing Security Master identifier conflicts"
      : normalizedError
        ? "Retry loading Security Master identifier conflicts"
        : "Refresh Security Master identifier conflicts",
    disabled: loading,
    disabledReason: loading ? "Identifier conflicts are already loading." : null,
    busy: loading,
    busyLabel: loading ? "Refreshing..." : null,
    feedbackId: "security-conflict-refresh-feedback",
    feedbackText: null
  };
}


export function buildSecuritySearchResultRows(
  results: SecurityMasterEntry[] | null,
  selectedSecurityId: string | null
): SecuritySearchResultRowViewModel[] {
  return (results ?? []).map((entry) => {
    const primaryIdentifierLabel = entry.classification.primaryIdentifierKind
      ? `${entry.classification.primaryIdentifierKind}: ${entry.classification.primaryIdentifierValue}`
      : "-";
    const isSelected = selectedSecurityId === entry.securityId;

    return {
      ...entry,
      rowId: `security-result-${entry.securityId}`,
      isSelected,
      detailPanelId: SECURITY_IDENTITY_DETAIL_PANEL_ID,
      isExpanded: isSelected,
      selectAriaLabel: `Open identity drill-in for ${entry.displayName}`,
      primaryIdentifierLabel,
      statusTone: entry.status === "Active" ? "success" : "warning",
      ariaLabel: `${entry.displayName}, ${entry.classification.assetClass}, primary identifier ${primaryIdentifierLabel}, currency ${entry.economicDefinition.currency}, status ${entry.status}${isSelected ? ", selected" : ""}.`
    };
  });
}


export function buildSecurityIdentityDrillInState(
  identity: SecurityIdentityDrillIn | null
): SecurityIdentityDrillInViewState | null {
  if (!identity) {
    return null;
  }

  const effectiveRange = formatSecurityDateRange(identity.effectiveFrom, identity.effectiveTo);
  const identifiers = identity.identifiers.map(buildSecurityIdentityIdentifierRow);
  const aliases = identity.aliases.map(buildSecurityIdentityAliasRow);

  return {
    panelId: SECURITY_IDENTITY_DETAIL_PANEL_ID,
    title: `Identity drill-in · ${identity.displayName}`,
    subtitle: `${identity.assetClass || "Unclassified"} · ${identity.status || "Status unavailable"}`,
    description: `${formatCount(identifiers.length, "identifier")} · ${formatCount(aliases.length, "alias")} · effective ${effectiveRange}`,
    ariaLabel: `Security identity detail for ${identity.displayName}`,
    statusLabel: identity.status || "Unknown",
    statusBadgeVariant: statusBadgeVariantForSecurityIdentity(identity.status),
    summaryFields: [
      { label: "Security ID", value: identity.securityId },
      { label: "Version", value: `v${identity.version}` },
      { label: "Asset class", value: identity.assetClass || "—" },
      { label: "Effective", value: effectiveRange }
    ],
    identifiersTitle: "Identifiers",
    identifiersTableLabel: `Identifiers for ${identity.displayName}`,
    identifiers,
    identifierEmptyText: "No identifiers found for this security.",
    aliasesTitle: "Aliases",
    aliasesTableLabel: `Aliases for ${identity.displayName}`,
    aliases,
    aliasEmptyText: "No aliases found for this security."
  };
}


export function buildSecurityConflictRows(
  conflicts: SecurityMasterConflict[] | null,
  resolvingConflictId: string | null
): SecurityConflictRowViewModel[] {
  return (conflicts ?? []).map((conflict) => {
    const isOpen = conflict.status === "Open";
    const isResolving = resolvingConflictId === conflict.conflictId;
    const canResolve = isOpen && !isResolving;
    const actionDisabledReason = isResolving
      ? `Resolution is already in progress for identifier conflict ${conflict.conflictId}.`
      : null;
    const providerASummary = `${conflict.providerA} -> security ${formatSecurityReferenceValue(conflict.valueA)}`;
    const providerBSummary = `${conflict.providerB} -> security ${formatSecurityReferenceValue(conflict.valueB)}`;

    return {
      ...conflict,
      statusLabel: conflict.status,
      statusTone: isOpen ? "warning" : "neutral",
      isOpen,
      isResolving,
      fieldLabel: formatSecurityConflictField(conflict.fieldPath),
      providerASummary,
      providerBSummary,
      detectedLabel: `Detected ${formatConflictDate(conflict.detectedAt)}`,
      ariaLabel: `Identifier conflict ${conflict.conflictId} on ${conflict.fieldPath}: ${conflict.status}. ${providerASummary}. ${providerBSummary}.`,
      resolutionStatusText: isResolving ? `Resolving identifier conflict ${conflict.conflictId}.` : null,
      actions: isOpen
        ? [
            buildSecurityConflictAction(conflict, "AcceptA", `Use ${conflict.providerA}`, canResolve, "outline", actionDisabledReason),
            buildSecurityConflictAction(conflict, "AcceptB", `Use ${conflict.providerB}`, canResolve, "outline", actionDisabledReason),
            buildSecurityConflictAction(conflict, "Dismiss", "Dismiss conflict", canResolve, "ghost", actionDisabledReason)
          ]
        : []
    };
  });
}


export function buildSecurityStatusAnnouncement({
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


export function buildReferenceDataWorkbenchSeed(
  securityId: string,
  entry: SecurityMasterEntry | null
): ReferenceDataWorkbenchEndpointSeed {
  const symbol = normalizeReferenceWorkbenchSymbol(
    entry?.classification.primaryIdentifierValue ?? entry?.displayName ?? securityId
  );
  const currency = entry?.economicDefinition.currency || "USD";
  const primaryKind = entry?.classification.primaryIdentifierKind ?? null;
  const primaryValue = entry?.classification.primaryIdentifierValue ?? null;
  const issuerName = entry?.displayName ?? symbol;

  return {
    securityId,
    displayName: entry?.displayName ?? null,
    primaryIdentifierKind: primaryKind,
    primaryIdentifierValue: primaryValue,
    currency,
    issuerName,
    optionContractSymbol: `${symbol}20261219C00150000`,
    optionChainId: `${symbol}-20261219`,
    optionExpiryDate: "2026-12-19",
    underlyingSymbol: symbol,
    exchangeCode: "XNYS",
    rootSymbol: symbol.slice(0, 3) || "ES",
    fxPairCode: currency === "USD" ? "EURUSD" : `${currency}USD`,
    swapType: entry?.economicDefinition.subType ?? "InterestRate",
    commodityType: entry?.economicDefinition.assetFamily ?? "Energy",
    network: "Ethereum",
    baseCurrency: currency === "USD" ? "BTC" : currency,
    institutionName: issuerName,
    fundFamily: issuerName,
    cik: primaryKind?.toLowerCase() === "cik" ? normalizeReferenceWorkbenchCik(primaryValue) : null,
    maturityFrom: "2026-01-01",
    maturityTo: "2036-12-31",
    beforeDate: "2031-12-31"
  };
}


export function normalizeReferenceWorkbenchSymbol(value: string): string {
  const match = value.toUpperCase().match(/[A-Z0-9]{1,8}/);
  return match?.[0] ?? "AAPL";
}


export function normalizeReferenceWorkbenchCik(value: string | null | undefined): string | null {
  const normalized = value?.replace(/\D/g, "");
  return normalized || null;
}
