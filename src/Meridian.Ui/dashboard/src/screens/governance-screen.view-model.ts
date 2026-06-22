import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  getCorporateActions,
  getReconciliationBreakQueue,
  getReconciliationCalibrationSummary,
  getRunTrialBalance,
  getSecurityConflicts,
  getSecurityIdentity,
  getTradingParameters,
  resolveReconciliationBreak,
  resolveSecurityConflict,
  reviewReconciliationBreak,
  searchSecurities
} from "@/lib/api";
import type {
  CorporateAction,
  GovernanceCashFlowSummary,
  GovernanceReportingProfile,
  GovernanceReportingSummary,
  GovernanceWorkspaceResponse,
  LedgerTrialBalanceLine,
  ReconciliationBreakQueueItem,
  ReconciliationCalibrationSummary,
  ReconciliationCalibrationStatus,
  ResolveConflictRequest,
  ResolveReconciliationBreakRequest,
  ReviewReconciliationBreakRequest,
  SecurityIdentityDrillIn,
  SecurityAliasEntry,
  SecurityIdentifierEntry,
  SecurityMasterConflict,
  SecurityMasterEntry,
  TradingParameters
} from "@/types";

export type GovernanceWorkstream = "ledger" | "reconciliation" | "security-master" | "reporting";
export type ReconciliationBreakCommand = "assign" | "resolve" | "dismiss";
export type SecurityConflictResolution = ResolveConflictRequest["resolution"];

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
  getCalibrationSummary: () => Promise<ReconciliationCalibrationSummary>;
}

export type CalibrationStatusTone = "success" | "warning" | "danger";

export interface CalibrationProfileRowViewModel {
  toleranceProfileId: string;
  exceptionRoute: string;
  highestSeverity: string;
  openBreakCount: number;
  resolvedBreakCount: number;
  pendingSignoffCount: number;
  lastUpdatedLabel: string;
  ariaLabel: string;
}

export interface CalibrationSummaryViewState {
  status: ReconciliationCalibrationStatus;
  statusLabel: string;
  statusTone: CalibrationStatusTone;
  summary: string;
  asOfLabel: string;
  totalBreakCount: number;
  openBreakCount: number;
  criticalOpenBreakCount: number;
  pendingSignoffCount: number;
  signedOffCount: number;
  missingMetadataCount: number;
  profileRows: CalibrationProfileRowViewModel[];
  hasProfiles: boolean;
  profilesLabel: string;
  errorText: string | null;
  loadingText: string | null;
  statusAnnouncement: string;
}

export interface CorporateActionRowViewModel extends CorporateAction {
  rowId: string;
  eventTypeLabel: string;
  exDateLabel: string;
  payDateLabel: string;
  amountLabel: string;
  ariaLabel: string;
}

export type TradingParametersField = { label: string; value: string; tone?: "default" | "warning" };

export interface TradingParametersViewState {
  securityId: string;
  asOfLabel: string;
  fields: TradingParametersField[];
  errorText: string | null;
  loadingText: string | null;
  statusAnnouncement: string;
}

export interface SecurityMasterDrillInServices {
  getCorporateActions: (securityId: string) => Promise<CorporateAction[]>;
  getTradingParameters: (securityId: string) => Promise<TradingParameters>;
}

export interface SecuritySearchState {
  trimmedQuery: string;
  resultCount: number;
  hasResults: boolean;
  searchStatusText: string | null;
  searchErrorText: string | null;
  statusAnnouncement: string;
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

export type CashFlowEvidenceTone = "default" | "success" | "warning" | "danger";

export interface GovernanceCashFlowRowViewModel {
  id: string;
  label: string;
  value: string;
  tone: CashFlowEvidenceTone;
  ariaLabel: string;
}

export interface GovernanceCashFlowViewState {
  eyebrow: string;
  title: string;
  description: string;
  routePath: string;
  statusLabel: string;
  statusTone: CashFlowEvidenceTone;
  statusAriaLabel: string;
  ariaLabel: string;
  rowGroupLabel: string;
  rows: GovernanceCashFlowRowViewModel[];
  statusAnnouncement: string;
}

export interface ReportingProfileBadgeViewModel {
  label: string;
  tone: "primary" | "success" | "warning" | "muted";
}

export interface ReportingProfileRowViewModel extends GovernanceReportingProfile {
  formatLabel: string;
  targetLabel: string;
  recommendationLabel: string | null;
  badges: ReportingProfileBadgeViewModel[];
  isSelected: boolean;
  selectAriaLabel: string;
  detailId: string;
}

export interface ReportingProfileDetailField {
  label: string;
  value: string;
  tone?: "success" | "warning" | "muted";
}

export interface ReportingProfileDetailViewModel {
  id: string;
  title: string;
  subtitle: string;
  description: string;
  fields: ReportingProfileDetailField[];
}

export interface GovernanceReportingViewState {
  title: string;
  description: string;
  countLabel: string;
  visibleCountLabel: string;
  targetSummary: string;
  listLabel: string;
  detailId: string;
  rows: ReportingProfileRowViewModel[];
  hasRows: boolean;
  emptyText: string;
  selectedProfile: ReportingProfileDetailViewModel | null;
  statusTitle: string;
  statusDetail: string;
  nextAction: string;
}

export type GovernanceTrialBalanceState = "ready" | "loading" | "empty" | "error";

export interface GovernanceTrialBalanceRowViewModel extends LedgerTrialBalanceLine {
  rowId: string;
  accountLabel: string;
  accountTypeLabel: string;
  balanceLabel: string;
  balanceTone: "default" | "success" | "danger";
  entryCountLabel: string;
  ariaLabel: string;
}

export interface GovernanceTrialBalanceViewState {
  title: string;
  description: string;
  tableLabel: string;
  state: GovernanceTrialBalanceState;
  rows: GovernanceTrialBalanceRowViewModel[];
  hasRows: boolean;
  loadingText: string | null;
  emptyTitle: string;
  emptyDetail: string;
  errorText: string | null;
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
  getTrialBalance: (runId) => getRunTrialBalance(runId),
  getCalibrationSummary: () => getReconciliationCalibrationSummary()
};

const defaultSecurityMasterDrillInServices: SecurityMasterDrillInServices = {
  getCorporateActions: (securityId) => getCorporateActions(securityId),
  getTradingParameters: (securityId) => getTradingParameters(securityId)
};

export function useGovernanceCashFlowViewModel(
  cashFlow: GovernanceCashFlowSummary | null,
  pathname: string,
  workstream: GovernanceWorkstream
) {
  return useMemo(
    () => buildGovernanceCashFlowViewState(cashFlow, pathname, workstream),
    [cashFlow, pathname, workstream]
  );
}

export function useGovernanceReportingViewModel(
  reporting: GovernanceReportingSummary | null
) {
  const [selectedProfileId, setSelectedProfileId] = useState<string | null>(null);
  const viewState = useMemo(
    () => buildGovernanceReportingViewState(reporting, selectedProfileId),
    [reporting, selectedProfileId]
  );
  const selectProfile = useCallback((profileId: string) => setSelectedProfileId(profileId), []);

  return {
    ...viewState,
    selectProfile
  };
}

export function useSecurityMasterViewModel(
  active: boolean,
  services: SecurityMasterServices = defaultSecurityMasterServices,
  drillInServices: SecurityMasterDrillInServices = defaultSecurityMasterDrillInServices,
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
  const [corporateActions, setCorporateActions] = useState<CorporateAction[] | null>(null);
  const [corporateActionsLoading, setCorporateActionsLoading] = useState(false);
  const [corporateActionsError, setCorporateActionsError] = useState<string | null>(null);
  const [tradingParameters, setTradingParameters] = useState<TradingParameters | null>(null);
  const [tradingParametersLoading, setTradingParametersLoading] = useState(false);
  const [tradingParametersError, setTradingParametersError] = useState<string | null>(null);
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

  useEffect(() => {
    if (!selectedSecurityId) {
      setCorporateActions(null);
      setCorporateActionsError(null);
      setTradingParameters(null);
      setTradingParametersError(null);
      return;
    }

    let cancelled = false;
    setCorporateActionsLoading(true);
    setCorporateActionsError(null);
    setTradingParametersLoading(true);
    setTradingParametersError(null);

    drillInServices.getCorporateActions(selectedSecurityId)
      .then((rows) => {
        if (!cancelled) {
          setCorporateActions(rows);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setCorporateActions([]);
          setCorporateActionsError(toErrorMessage(err, "Corporate actions failed to load."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setCorporateActionsLoading(false);
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
          setTradingParametersError(toErrorMessage(err, "Trading parameters failed to load."));
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
  }, [selectedSecurityId, drillInServices]);

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
    const isReselect = securityId === selectedSecurityId;
    setSelectedSecurityId(securityId);
    setIdentity(null);
    setIdentityError(null);
    setIdentityLoading(true);

    if (!isReselect) {
      setCorporateActions(null);
      setCorporateActionsError(null);
      setTradingParameters(null);
      setTradingParametersError(null);
    }

    try {
      const detail = await services.getIdentity(securityId);
      setIdentity(detail);
    } catch (err) {
      setIdentityError(toErrorMessage(err, "Identity drill-in failed."));
    } finally {
      setIdentityLoading(false);
    }
  }, [selectedSecurityId, services]);

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
  const conflictRows = useMemo(
    () => buildSecurityConflictRows(conflicts, conflictResolvingId),
    [conflictResolvingId, conflicts]
  );
  const identityView = useMemo(
    () => buildSecurityIdentityDrillInState(identity),
    [identity]
  );
  const corporateActionRows = useMemo(
    () => buildCorporateActionRows(corporateActions),
    [corporateActions]
  );
  const tradingParametersView = useMemo(
    () => buildTradingParametersViewState(tradingParameters, tradingParametersLoading, tradingParametersError),
    [tradingParameters, tradingParametersLoading, tradingParametersError]
  );
  const openConflictCount = countOpenSecurityConflicts(conflicts);

  return {
    query,
    updateQuery,
    results,
    searching,
    selectedSecurityId,
    selectSecurity,
    identity,
    identityView,
    identityLoading,
    identityErrorText: identityError,
    conflicts,
    conflictRows,
    hasConflicts: conflictRows.length > 0,
    conflictEmptyText: "No identifier conflicts detected.",
    conflictSectionAriaLabel: "Security Master identifier conflict queue",
    conflictsLoading,
    conflictsErrorText: conflictsError,
    conflictResolvingId,
    conflictActionErrorText: conflictActionError,
    resolveConflict,
    openConflictCount,
    conflictCountLabel: `${openConflictCount} open`,
    corporateActions,
    corporateActionRows,
    hasCorporateActions: (corporateActions?.length ?? 0) > 0,
    corporateActionsLoading,
    corporateActionsErrorText: corporateActionsError,
    tradingParameters,
    tradingParametersView,
    tradingParametersLoading,
    tradingParametersErrorText: tradingParametersError,
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
  const [calibrationSummary, setCalibrationSummary] = useState<ReconciliationCalibrationSummary | null>(null);
  const [calibrationLoading, setCalibrationLoading] = useState(false);
  const [calibrationError, setCalibrationError] = useState<string | null>(null);

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
    if (workstream !== "reconciliation") {
      return;
    }

    let cancelled = false;
    setCalibrationLoading(true);
    setCalibrationError(null);

    services.getCalibrationSummary()
      .then((summary) => {
        if (!cancelled) {
          setCalibrationSummary(summary);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setCalibrationError(toErrorMessage(err, "Calibration summary failed to load."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setCalibrationLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [services, workstream]);

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
        resolutionNote: "Reviewed in governance panel.",
        operatorRationale: "Reviewed and actioned via governance panel."
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
  const trialBalanceView = useMemo(
    () => buildGovernanceTrialBalanceViewState({
      runId: selectedReconciliation?.runId ?? null,
      rows: trialBalance,
      loading: trialBalanceLoading,
      errorText: trialBalanceError
    }),
    [selectedReconciliation?.runId, trialBalance, trialBalanceError, trialBalanceLoading]
  );
  const calibrationView = useMemo(
    () => buildCalibrationSummaryViewState(calibrationSummary, calibrationLoading, calibrationError),
    [calibrationSummary, calibrationLoading, calibrationError]
  );

  return {
    reconciliationQueue,
    selectedRunId,
    selectedReconciliation,
    selectRun: setSelectedRunId,
    trialBalance,
    trialBalanceLoading,
    trialBalanceErrorText: trialBalanceError,
    trialBalanceView,
    breakAction,
    assignBreak,
    resolveBreak,
    calibrationSummary,
    calibrationLoading,
    calibrationErrorText: calibrationError,
    calibrationView,
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
    title: `Identity drill-in · ${identity.displayName}`,
    subtitle: `${identity.securityId} · v${identity.version} · ${identity.assetClass || "—"}`,
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
    const providerASummary = `${conflict.providerA} -> security ${formatSecurityReferenceValue(conflict.valueA)}`;
    const providerBSummary = `${conflict.providerB} -> security ${formatSecurityReferenceValue(conflict.valueB)}`;

    return {
      ...conflict,
      statusLabel: conflict.status,
      statusTone: isOpen ? "warning" : "neutral",
      isOpen,
      isResolving,
      fieldLabel: conflict.fieldPath,
      providerASummary,
      providerBSummary,
      detectedLabel: `Detected ${formatConflictDate(conflict.detectedAt)}`,
      ariaLabel: `Identifier conflict ${conflict.conflictId} on ${conflict.fieldPath}: ${conflict.status}. ${providerASummary}. ${providerBSummary}.`,
      resolutionStatusText: isResolving ? `Resolving identifier conflict ${conflict.conflictId}.` : null,
      actions: isOpen
        ? [
            buildSecurityConflictAction(conflict, "AcceptA", `Use ${conflict.providerA}`, canResolve, "outline"),
            buildSecurityConflictAction(conflict, "AcceptB", `Use ${conflict.providerB}`, canResolve, "outline"),
            buildSecurityConflictAction(conflict, "Dismiss", "Dismiss conflict", canResolve, "ghost")
          ]
        : []
    };
  });
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

export function buildGovernanceCashFlowViewState(
  cashFlow: GovernanceCashFlowSummary | null,
  pathname: string,
  workstream: GovernanceWorkstream
): GovernanceCashFlowViewState {
  const routePath = pathname || "/accounting";
  const contextLabel = cashFlowContextLabel(workstream);

  if (!cashFlow) {
    return {
      eyebrow: "Cash Flow",
      title: "Cash-flow evidence loading",
      description: `${contextLabel} is waiting for the shared accounting cash-flow payload.`,
      routePath,
      statusLabel: "Pending",
      statusTone: "warning",
      statusAriaLabel: "Cash-flow status pending",
      ariaLabel: `Cash-flow evidence for ${contextLabel} at ${routePath}`,
      rowGroupLabel: "Cash-flow evidence rows",
      rows: [],
      statusAnnouncement: "Cash-flow evidence is loading."
    };
  }

  const statusTone = normalizeCashFlowTone(cashFlow.tone, cashFlow.netVariance);
  const statusLabel = cashFlow.netVariance === 0
    ? "Balanced"
    : cashFlow.runsWithCashVariance > 0
      ? "Variance review"
      : "Observe";
  const rows: GovernanceCashFlowRowViewModel[] = [
    buildCashFlowRow("portfolio-cash", "Portfolio cash", cashFlow.totalCash, "default"),
    buildCashFlowRow("ledger-cash", "Ledger cash", cashFlow.totalLedgerCash, "default"),
    buildCashFlowRow("net-variance", "Net variance", cashFlow.netVariance, statusTone),
    buildCashFlowRow("financing", "Financing", cashFlow.totalFinancing, "default"),
    buildCashFlowCountRow("cash-signal-runs", "Runs with cash signals", cashFlow.runsWithCashSignals, "default"),
    buildCashFlowCountRow("variance-runs", "Runs with variance", cashFlow.runsWithCashVariance, statusTone)
  ];

  return {
    eyebrow: "Cash Flow",
    title: cashFlow.summary,
    description: `${contextLabel} at ${routePath} reuses the shared accounting/reporting cash-flow summary payload.`,
    routePath,
    statusLabel,
    statusTone,
    statusAriaLabel: `Cash-flow status ${statusLabel}. Net variance ${formatCurrency(cashFlow.netVariance)}.`,
    ariaLabel: `Cash-flow evidence for ${contextLabel} at ${routePath}`,
    rowGroupLabel: "Cash-flow evidence rows",
    rows,
    statusAnnouncement: `${statusLabel}: ${cashFlow.summary}`
  };
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

export function buildGovernanceReportingViewState(
  reporting: GovernanceReportingSummary | null,
  selectedProfileId: string | null
): GovernanceReportingViewState {
  const profileCount = reporting?.profileCount ?? 0;
  const profiles = reporting?.profiles ?? [];
  const visibleProfiles = profiles.slice(0, 4);
  const recommendedProfiles = new Set((reporting?.recommendedProfiles ?? []).map((value) => value.toLowerCase()));
  const selectedId = selectedProfileId && visibleProfiles.some((profile) => profile.id === selectedProfileId)
    ? selectedProfileId
    : visibleProfiles[0]?.id ?? null;
  const rows = visibleProfiles.map((profile) => buildReportingProfileRow(profile, recommendedProfiles, profile.id === selectedId));
  const selectedRow = rows.find((profile) => profile.id === selectedId) ?? null;
  const selectedProfile = selectedRow ? buildReportingProfileDetail(selectedRow) : null;
  const targetSummary = formatReportPackTargets(reporting?.reportPackTargets ?? []);
  const hiddenProfileCount = Math.max(profileCount - rows.length, 0);
  const visibleCountLabel = hiddenProfileCount > 0
    ? `Showing ${rows.length} of ${profileCount} profiles.`
    : `${formatCount(rows.length, "profile")} loaded.`;

  return {
    title: "Reporting profiles",
    description: reporting?.summary ?? "Reporting profile metadata has not loaded yet.",
    countLabel: formatCount(profileCount, "profile"),
    visibleCountLabel,
    targetSummary,
    listLabel: "Reporting profile selector",
    detailId: "reporting-profile-detail",
    rows,
    hasRows: rows.length > 0,
    emptyText: "No reporting profiles available. Sync report-pack metadata before export review.",
    selectedProfile,
    statusTitle: "Report packet posture",
    statusDetail: profileCount > 0
      ? `${formatCount(profileCount, "profile")} configured. ${targetSummary}`
      : "No reporting profiles are configured for packet generation.",
    nextAction: selectedRow
      ? `Inspect ${selectedRow.name} before packet generation.`
      : "Sync reporting profile metadata before packet generation."
  };
}

function buildReportingProfileRow(
  profile: GovernanceReportingProfile,
  recommendedProfiles: Set<string>,
  isSelected: boolean
): ReportingProfileRowViewModel {
  const isRecommended = recommendedProfiles.has(profile.id.toLowerCase()) || recommendedProfiles.has(profile.name.toLowerCase());
  const badges: ReportingProfileBadgeViewModel[] = [
    { label: profile.dataDictionary ? "Data dictionary" : "Dictionary missing", tone: profile.dataDictionary ? "success" : "warning" },
    { label: profile.loaderScript ? "Loader script" : "No loader", tone: profile.loaderScript ? "primary" : "muted" }
  ];

  if (isRecommended) {
    badges.unshift({ label: "Recommended", tone: "primary" });
  }

  return {
    ...profile,
    formatLabel: profile.format.toUpperCase(),
    targetLabel: `Target - ${profile.targetTool}`,
    recommendationLabel: isRecommended ? "Recommended for current packet flow" : null,
    badges,
    isSelected,
    selectAriaLabel: `Inspect reporting profile ${profile.name} for ${profile.targetTool} ${profile.format}`,
    detailId: `reporting-profile-${toDomId(profile.id)}`
  };
}

function buildReportingProfileDetail(profile: ReportingProfileRowViewModel): ReportingProfileDetailViewModel {
  return {
    id: profile.detailId,
    title: `Selected reporting profile - ${profile.name}`,
    subtitle: `${profile.formatLabel} - ${profile.targetTool}`,
    description: profile.description,
    fields: [
      { label: "Profile ID", value: profile.id },
      { label: "Format", value: profile.formatLabel },
      { label: "Target", value: profile.targetTool },
      { label: "Data dictionary", value: profile.dataDictionary ? "Included" : "Missing", tone: profile.dataDictionary ? "success" : "warning" },
      { label: "Loader script", value: profile.loaderScript ? "Available" : "Not configured", tone: profile.loaderScript ? "success" : "muted" },
      { label: "Recommendation", value: profile.recommendationLabel ?? "Not recommended for current packet flow", tone: profile.recommendationLabel ? "success" : "muted" }
    ]
  };
}

export function buildGovernanceTrialBalanceViewState({
  runId,
  rows,
  loading,
  errorText
}: {
  runId: string | null;
  rows: LedgerTrialBalanceLine[];
  loading: boolean;
  errorText: string | null;
}): GovernanceTrialBalanceViewState {
  const runLabel = runId ?? "selected run";
  const viewRows = rows.map(buildTrialBalanceRow);
  const hasRows = viewRows.length > 0;
  const state: GovernanceTrialBalanceState = errorText
    ? "error"
    : loading && !hasRows
      ? "loading"
      : hasRows
        ? "ready"
        : "empty";
  const loadingText = loading
    ? hasRows
      ? `Refreshing trial balance for ${runLabel}.`
      : `Loading trial balance for ${runLabel}.`
    : null;

  return {
    title: "Multi-ledger trial balance",
    description: `Baseline ledger balances for ${runLabel} grouped by account type.`,
    tableLabel: `Trial balance lines for ${runLabel}`,
    state,
    rows: viewRows,
    hasRows,
    loadingText,
    emptyTitle: "No trial balance lines",
    emptyDetail: `Meridian did not return account-balance rows for ${runLabel}. Select another reconciliation run or refresh ledger evidence before report handoff.`,
    errorText,
    statusAnnouncement: buildTrialBalanceAnnouncement({ runLabel, state, rowCount: viewRows.length, loading, errorText })
  };
}

function buildTrialBalanceRow(line: LedgerTrialBalanceLine): GovernanceTrialBalanceRowViewModel {
  const accountLabel = line.accountName.trim() || "Unnamed account";
  const accountTypeLabel = line.accountType.trim() || "Unclassified";
  const balanceLabel = formatCurrency(line.balance);
  const entryCountLabel = line.entryCount.toLocaleString();
  const securityLabel = line.security?.primaryIdentifier?.trim() || line.symbol?.trim() || line.security?.displayName.trim() || null;
  const rowId = [
    accountLabel,
    accountTypeLabel,
    line.financialAccountId,
    securityLabel
  ].filter(Boolean).join("-");

  return {
    ...line,
    rowId,
    accountLabel,
    accountTypeLabel,
    balanceLabel,
    balanceTone: line.balance < 0 ? "danger" : line.balance > 0 ? "success" : "default",
    entryCountLabel,
    ariaLabel: [
      `${accountLabel} ${accountTypeLabel}`,
      `Balance ${balanceLabel}`,
      `${entryCountLabel} entries`,
      securityLabel ? `Security ${securityLabel}` : null
    ].filter(Boolean).join(". ")
  };
}

function buildTrialBalanceAnnouncement({
  runLabel,
  state,
  rowCount,
  loading,
  errorText
}: {
  runLabel: string;
  state: GovernanceTrialBalanceState;
  rowCount: number;
  loading: boolean;
  errorText: string | null;
}): string {
  if (errorText) {
    return `Trial balance failed for ${runLabel}: ${errorText}`;
  }

  if (loading) {
    return `Loading trial balance for ${runLabel}.`;
  }

  if (state === "empty") {
    return `No trial balance lines returned for ${runLabel}.`;
  }

  return rowCount === 1
    ? `1 trial balance line loaded for ${runLabel}.`
    : `${rowCount} trial balance lines loaded for ${runLabel}.`;
}

function formatReportPackTargets(targets: string[]): string {
  if (targets.length === 0) {
    return "No report-pack targets configured.";
  }

  return `Targets: ${targets.join(", ")}.`;
}

function cashFlowContextLabel(workstream: GovernanceWorkstream): string {
  if (workstream === "reporting") {
    return "Reporting packet context";
  }

  if (workstream === "reconciliation") {
    return "Reconciliation context";
  }

  if (workstream === "security-master") {
    return "Security coverage context";
  }

  return "Ledger context";
}

function buildCashFlowRow(
  id: string,
  label: string,
  value: number,
  tone: CashFlowEvidenceTone
): GovernanceCashFlowRowViewModel {
  const formattedValue = formatCurrency(value);
  return {
    id,
    label,
    value: formattedValue,
    tone,
    ariaLabel: `${label}: ${formattedValue}`
  };
}

function buildCashFlowCountRow(
  id: string,
  label: string,
  value: number,
  tone: CashFlowEvidenceTone
): GovernanceCashFlowRowViewModel {
  const formattedValue = String(value);
  return {
    id,
    label,
    value: formattedValue,
    tone,
    ariaLabel: `${label}: ${formattedValue}`
  };
}

function normalizeCashFlowTone(
  tone: GovernanceCashFlowSummary["tone"],
  netVariance: number
): CashFlowEvidenceTone {
  if (netVariance === 0) {
    return "success";
  }

  if (tone === "danger") {
    return "danger";
  }

  if (tone === "success" || tone === "warning") {
    return tone;
  }

  return "warning";
}

function formatCount(count: number, singular: string): string {
  return `${count} ${singular}${count === 1 ? "" : "s"}`;
}

function formatCurrency(value: number) {
  const prefix = value >= 0 ? "$" : "-$";
  return `${prefix}${Math.abs(value).toLocaleString(undefined, { maximumFractionDigits: 2 })}`;
}

function toDomId(value: string): string {
  const normalized = value.trim().toLowerCase().replace(/[^a-z0-9_-]+/g, "-").replace(/^-+|-+$/g, "");
  return normalized || "profile";
}

function buildSecurityConflictAction(
  conflict: SecurityMasterConflict,
  resolution: SecurityConflictResolution,
  label: string,
  enabled: boolean,
  variant: "outline" | "ghost"
): SecurityConflictActionViewModel {
  const choice =
    resolution === "AcceptA"
      ? `${conflict.providerA} value ${formatSecurityReferenceValue(conflict.valueA)}`
      : resolution === "AcceptB"
        ? `${conflict.providerB} value ${formatSecurityReferenceValue(conflict.valueB)}`
        : "no provider value";

  return {
    resolution,
    label,
    ariaLabel: resolution === "Dismiss"
      ? `Dismiss identifier conflict ${conflict.conflictId} on ${conflict.fieldPath}`
      : `Resolve identifier conflict ${conflict.conflictId} on ${conflict.fieldPath} with ${choice}`,
    variant,
    disabled: !enabled
  };
}

function buildSecurityIdentityIdentifierRow(
  identifier: SecurityIdentifierEntry
): SecurityIdentityIdentifierRowViewModel {
  const providerLabel = valueOrDash(identifier.provider);
  const primaryLabel = identifier.isPrimary ? "Primary" : "Secondary";
  const validRangeLabel = formatSecurityDateRange(identifier.validFrom, identifier.validTo);

  return {
    ...identifier,
    rowId: `identifier-${toDomId(`${identifier.kind}-${identifier.value}`)}`,
    providerLabel,
    primaryLabel,
    primaryBadgeVariant: identifier.isPrimary ? "success" : "outline",
    validRangeLabel,
    ariaLabel: `${identifier.kind} ${identifier.value}, ${primaryLabel}, provider ${providerLabel}, valid ${validRangeLabel}`
  };
}

function buildSecurityIdentityAliasRow(alias: SecurityAliasEntry): SecurityIdentityAliasRowViewModel {
  const providerLabel = valueOrDash(alias.provider);
  const enabledLabel = alias.isEnabled ? "Enabled" : "Disabled";
  const validRangeLabel = formatSecurityDateRange(alias.validFrom, alias.validTo);

  return {
    ...alias,
    rowId: `alias-${toDomId(alias.aliasId)}`,
    providerLabel,
    enabledLabel,
    enabledBadgeVariant: alias.isEnabled ? "success" : "warning",
    validRangeLabel,
    createdLabel: formatSecurityDate(alias.createdAt),
    reasonText: alias.reason?.trim() || "No alias reason recorded.",
    ariaLabel: `${alias.aliasKind} ${alias.aliasValue}, ${enabledLabel}, scope ${alias.scope}, provider ${providerLabel}, valid ${validRangeLabel}`
  };
}

function statusBadgeVariantForSecurityIdentity(
  status: string | null | undefined
): SecurityIdentityDrillInViewState["statusBadgeVariant"] {
  const normalized = status?.trim().toLowerCase();
  if (normalized === "active") {
    return "success";
  }

  if (normalized === "pending" || normalized === "inactive" || normalized === "deactivated") {
    return "warning";
  }

  return "outline";
}

function formatSecurityReferenceValue(value: string): string {
  return value.length > 8 ? `${value.substring(0, 8)}...` : value;
}

function formatSecurityDate(value: string | null | undefined): string {
  if (!value) {
    return "—";
  }

  const match = /^\d{4}-\d{2}-\d{2}/.exec(value);
  return match?.[0] ?? value;
}

function formatSecurityDateRange(from: string | null | undefined, to: string | null | undefined): string {
  return `${formatSecurityDate(from)} -> ${to ? formatSecurityDate(to) : "active"}`;
}

function formatConflictDate(value: string): string {
  const match = /^\d{4}-\d{2}-\d{2}/.exec(value);
  return match?.[0] ?? value;
}

function valueOrDash(value: string | null | undefined): string {
  return value?.trim() || "—";
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

export function buildCalibrationSummaryViewState(
  summary: ReconciliationCalibrationSummary | null,
  loading: boolean,
  errorText: string | null
): CalibrationSummaryViewState {
  const statusTone = calibrationStatusTone(summary?.status ?? null);
  const profileRows = (summary?.profiles ?? []).map(buildCalibrationProfileRow);

  return {
    status: summary?.status ?? "Ready",
    statusLabel: calibrationStatusLabel(summary?.status ?? null, loading),
    statusTone,
    summary: summary?.summary ?? "",
    asOfLabel: summary?.asOf ? formatSecurityDate(summary.asOf) : "—",
    totalBreakCount: summary?.totalBreakCount ?? 0,
    openBreakCount: summary?.openBreakCount ?? 0,
    criticalOpenBreakCount: summary?.criticalOpenBreakCount ?? 0,
    pendingSignoffCount: summary?.pendingSignoffCount ?? 0,
    signedOffCount: summary?.signedOffCount ?? 0,
    missingMetadataCount: summary?.missingCalibrationMetadataCount ?? 0,
    profileRows,
    hasProfiles: profileRows.length > 0,
    profilesLabel: profileRows.length === 1 ? "1 tolerance profile" : `${profileRows.length} tolerance profiles`,
    errorText,
    loadingText: loading ? "Loading calibration summary..." : null,
    statusAnnouncement: errorText
      ? `Calibration summary error: ${errorText}`
      : loading
        ? "Loading calibration summary."
        : summary
          ? `Calibration status: ${calibrationStatusLabel(summary.status, false)}. ${summary.summary}`
          : ""
  };
}

function buildCalibrationProfileRow(
  profile: ReconciliationCalibrationSummary["profiles"][number]
): CalibrationProfileRowViewModel {
  return {
    toleranceProfileId: profile.toleranceProfileId,
    exceptionRoute: profile.exceptionRoute,
    highestSeverity: profile.highestSeverity,
    openBreakCount: profile.openBreakCount,
    resolvedBreakCount: profile.resolvedBreakCount,
    pendingSignoffCount: profile.pendingSignoffCount,
    lastUpdatedLabel: formatSecurityDate(profile.lastUpdatedAt),
    ariaLabel: `Profile ${profile.toleranceProfileId}: ${profile.openBreakCount} open, ${profile.pendingSignoffCount} pending sign-off, severity ${profile.highestSeverity}`
  };
}

function calibrationStatusTone(status: ReconciliationCalibrationStatus | null): CalibrationStatusTone {
  if (status === "Ready") {
    return "success";
  }

  if (status === "Blocked") {
    return "danger";
  }

  return "warning";
}

function calibrationStatusLabel(status: ReconciliationCalibrationStatus | null, loading: boolean): string {
  if (loading) {
    return "Loading...";
  }

  if (status === "Ready") {
    return "Ready";
  }

  if (status === "Blocked") {
    return "Blocked";
  }

  if (status === "ReviewRequired") {
    return "Review required";
  }

  return "Unknown";
}

export function buildCorporateActionRows(
  actions: CorporateAction[] | null
): CorporateActionRowViewModel[] {
  return (actions ?? []).map((action) => {
    const amountLabel = formatCorpActAmount(action);

    return {
      ...action,
      rowId: action.corpActId,
      eventTypeLabel: formatCorpActEventType(action.eventType),
      exDateLabel: formatSecurityDate(action.exDate),
      payDateLabel: action.payDate ? formatSecurityDate(action.payDate) : "—",
      amountLabel,
      ariaLabel: `${formatCorpActEventType(action.eventType)} for ${action.securityId}, ex-date ${formatSecurityDate(action.exDate)}, ${amountLabel}`
    };
  });
}

export function buildTradingParametersViewState(
  params: TradingParameters | null,
  loading: boolean,
  errorText: string | null
): TradingParametersViewState {
  const fields: TradingParametersField[] = params
    ? [
        { label: "Lot size", value: params.lotSize !== null ? String(params.lotSize) : "—" },
        { label: "Tick size", value: params.tickSize !== null ? String(params.tickSize) : "—" },
        { label: "Contract multiplier", value: params.contractMultiplier !== null ? String(params.contractMultiplier) : "—" },
        {
          label: "Margin requirement",
          value: params.marginRequirementPct !== null ? `${params.marginRequirementPct}%` : "—",
          tone: params.marginRequirementPct !== null && params.marginRequirementPct > 50 ? "warning" : "default"
        },
        { label: "Trading hours (UTC)", value: params.tradingHoursUtc ?? "—" },
        {
          label: "Circuit breaker",
          value: params.circuitBreakerThresholdPct !== null ? `${params.circuitBreakerThresholdPct}%` : "—",
          tone: params.circuitBreakerThresholdPct !== null ? "warning" : "default"
        }
      ]
    : [];

  return {
    securityId: params?.securityId ?? "",
    asOfLabel: params?.asOf ? formatSecurityDate(params.asOf) : "—",
    fields,
    errorText,
    loadingText: loading ? "Loading trading parameters..." : null,
    statusAnnouncement: errorText
      ? `Trading parameters error: ${errorText}`
      : loading
        ? "Loading trading parameters."
        : params
          ? `Trading parameters loaded for ${params.securityId}.`
          : ""
  };
}

function formatCorpActEventType(eventType: string): string {
  const labels: Record<string, string> = {
    Dividend: "Dividend",
    StockSplit: "Stock split",
    SpinOff: "Spin-off",
    Merger: "Merger",
    RightsIssue: "Rights issue"
  };

  return labels[eventType] ?? eventType;
}

function formatCorpActAmount(action: CorporateAction): string {
  if (action.dividendPerShare !== null) {
    const currency = action.currency ? ` ${action.currency}` : "";
    return `${action.dividendPerShare}${currency} / share`;
  }

  if (action.splitRatio !== null) {
    return `${action.splitRatio}:1 split`;
  }

  if (action.exchangeRatio !== null) {
    return `${action.exchangeRatio}:1 exchange`;
  }

  if (action.distributionRatio !== null) {
    return `${action.distributionRatio}:1 distribution`;
  }

  if (action.rightsPerShare !== null) {
    const price = action.subscriptionPricePerShare !== null
      ? ` @ ${action.subscriptionPricePerShare}${action.currency ? ` ${action.currency}` : ""}`
      : "";
    return `${action.rightsPerShare} rights/share${price}`;
  }

  return "—";
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
