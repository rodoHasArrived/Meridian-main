import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { acceptCorporateActionInboxProposal, getCorporateActionInbox } from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import type {
  CorporateActionCaseProjection,
  CorporateActionCaseScope,
  CorporateActionCaseStatus,
  CorporateActionInboxAcceptRequest,
  CorporateActionInboxAcceptResult,
  CorporateActionInboxResponse,
  CorporateActionProcessingCaseDto,
  CorporateActionProposalEntry
} from "@/types";

export type CorporateActionInboxFetcher = typeof getCorporateActionInbox;
export type CorporateActionInboxAccepter = typeof acceptCorporateActionInboxProposal;
export type CorporateActionIdempotencyKeyFactory = () => string;

export type InboxRowTone = "warning" | "neutral";

export interface CorporateActionInboxRowModel {
  rowId: string;
  caseIdLabel: string;
  proposalIdLabel: string;
  versionLabel: string;
  statusLabel: string;
  assignmentLabel: string;
  conflictLabel: string;
  permissionLabel: string;
  expectedVersion: number | null;
  acceptanceScope: CorporateActionCaseScope | null;
  canAcceptCanonicalFact: boolean;
  acceptCanonicalFactDisabledReason: string | null;
  securityId: string;
  ticker: string;
  actionType: string;
  exDateLabel: string;
  /** Days until the ex-date; negative when it already passed. */
  daysUntilEx: number | null;
  countdownLabel: string;
  valueLabel: string;
  sourceEventLabel: string;
  sourceObservedAtLabel: string;
  sourceEvidenceReference: string | null;
  /** e.g. "2/3 sources agree" — dissent is what the operator reviews. */
  consensusLabel: string;
  winningSource: string;
  agreeingSources: string[];
  dissentingSources: string[];
  recordDateLabel: string;
  payableDateLabel: string;
  tone: InboxRowTone;
  durableCase: CorporateActionCaseProjection | null;
  compactCase: CorporateActionProcessingCaseDto | null;
  proposal: CorporateActionProposalEntry | null;
}

export interface CorporateActionInboxModel {
  lastIngestLabel: string;
  stagedCount: number;
  appliedLastRun: number;
  duplicatesSkippedLastRun: number;
  rows: CorporateActionInboxRowModel[];
  errors: string[];
  hasPartialProviderFailure: boolean;
  summary: string;
}

export interface CorporateActionInboxFilters {
  search: string;
  status: string;
  assignment: string;
  conflict: string;
}

export const DEFAULT_CORPORATE_ACTION_INBOX_FILTERS: CorporateActionInboxFilters = {
  search: "",
  status: "All",
  assignment: "All",
  conflict: "All"
};

export interface CorporateActionAcceptanceReceipt {
  row: CorporateActionInboxRowModel;
  result: CorporateActionInboxAcceptResult;
  queueRefreshWarning: string | null;
}

function formatValue(row: Pick<CorporateActionProposalEntry, "amount" | "currency" | "splitFromFactor" | "splitToFactor">): string {
  if (row.splitFromFactor != null && row.splitToFactor != null) {
    return `${row.splitToFactor}:${row.splitFromFactor} split`;
  }
  if (row.amount != null) {
    return `${row.amount} ${row.currency ?? ""}`.trim();
  }
  return "—";
}

function formatCountdown(daysUntilEx: number): string {
  if (daysUntilEx > 0) return `in ${daysUntilEx} day${daysUntilEx === 1 ? "" : "s"}`;
  if (daysUntilEx === 0) return "today";
  return `${-daysUntilEx} day${daysUntilEx === -1 ? "" : "s"} ago`;
}

const CORPORATE_ACTION_CASE_STATUS_BY_WIRE_VALUE: Readonly<Record<string, CorporateActionCaseStatus>> = {
  Detected: "Detected",
  NeedsTerms: "NeedsTerms",
  Disputed: "Disputed",
  TermsConfirmed: "TermsConfirmed",
  ElectionPending: "ElectionPending",
  ElectionSubmitted: "ElectionSubmitted",
  AllocationPending: "AllocationPending",
  AccountingReview: "AccountingReview",
  ReadyForApproval: "ReadyForApproval",
  Approved: "Approved",
  Scheduled: "Scheduled",
  Posted: "Posted",
  Reconciled: "Reconciled",
  Reported: "Reported",
  Closed: "Closed",
  Blocked: "Blocked",
  Cancelled: "Cancelled",
  Superseded: "Superseded",
  RestatementRequired: "RestatementRequired"
};

/**
 * Adapts the compact durable-case wire contract into the additive workspace projection. Fields
 * absent from the wire contract stay empty or disabled; the browser does not infer conflict,
 * accounting, election, or posting state.
 */
export function adaptCorporateActionProcessingCase(
  processingCase: CorporateActionProcessingCaseDto
): CorporateActionCaseProjection | null {
  const status = CORPORATE_ACTION_CASE_STATUS_BY_WIRE_VALUE[processingCase.state];
  if (!status) return null;

  const genericBlockers = processingCase.actionAvailability?.blockers ?? [];
  const unavailableReason = genericBlockers[0]
    ?? processingCase.blockedReason
    ?? "The compact case response does not supply this workspace command.";

  return {
    caseId: processingCase.caseId,
    proposalId: processingCase.proposalId,
    version: processingCase.version,
    status,
    assignedTo: processingCase.assignedTo,
    conflictState: null,
    permissionState: null,
    scope: processingCase.scope,
    receivedAt: processingCase.createdAtUtc,
    dueAt: null,
    sourceFacts: [],
    entitlement: null,
    elections: [],
    basisComparisons: [],
    lotPreview: [],
    journalPreview: [],
    reconciliation: [],
    history: [],
    proofReferences: [],
    actionAvailability: {
      canAcceptCanonicalFact: false,
      acceptCanonicalFactDisabledReason: "The canonical source proposal has already been accepted into this durable case.",
      canSubmitElection: false,
      submitElectionDisabledReason: unavailableReason,
      canApproveTreatment: false,
      approveTreatmentDisabledReason: unavailableReason,
      canPost: false,
      postDisabledReason: unavailableReason
    }
  };
}

function deriveCaseFields(
  proposal: CorporateActionProposalEntry,
  compactCase: CorporateActionProcessingCaseDto | null
) {
  const durableCase = compactCase ? adaptCorporateActionProcessingCase(compactCase) : null;
  const hasDissent = proposal.dissentingSources.length > 0;
  const permissionState = durableCase?.permissionState ?? null;
  const proposalAvailability = proposal.actionAvailability ?? null;
  const proposalId = proposal.proposalId ?? null;
  const expectedVersion = proposal.version ?? null;
  const acceptanceScope = proposal.acceptanceScope ?? null;
  const hasScope = Boolean(acceptanceScope?.tenantId.trim() && acceptanceScope.companyId.trim());
  const serverBlocker = proposalAvailability?.blockers[0]
    ?? null;
  const explicitlyAllowed = proposalAvailability?.canAccept === true && serverBlocker === null;
  const contractComplete = Boolean(proposalId && expectedVersion !== null && expectedVersion > 0 && hasScope);
  const missingContractReason = !proposalId
    ? "Server did not supply a durable proposal ID."
    : expectedVersion === null
      ? "Server did not supply the proposal version required for concurrency control."
      : expectedVersion <= 0
        ? "Server supplied an invalid proposal version for concurrency control."
      : !hasScope
        ? "Server did not supply an exact tenant and company scope for case creation."
        : null;
  const permissionLabel = proposalAvailability
    ? explicitlyAllowed
      ? "Allowed by server policy"
      : "Denied by server policy"
    : permissionState === "Allowed"
      ? "Allowed by server policy"
      : permissionState === "Denied"
        ? "Denied by server policy"
        : permissionState === "ServerChecked"
          ? "Checked by server on submit"
          : "Authorization not supplied";
  const authorizationDisabledReason = serverBlocker
    ?? (proposalAvailability ? "Server policy does not allow acceptance." : "Server did not supply action authorization.");

  return {
    durableCase,
    caseIdLabel: durableCase?.caseId ?? compactCase?.caseId ?? "Not supplied",
    proposalIdLabel: proposalId ?? "Not supplied",
    versionLabel: expectedVersion !== null ? `v${expectedVersion}` : "Not supplied",
    statusLabel: durableCase?.status ?? compactCase?.state ?? proposal.proposalState ?? "Not supplied",
    assignmentLabel: durableCase?.assignedTo?.trim() || compactCase?.assignedTo?.trim() || "Unassigned",
    conflictLabel: durableCase?.conflictState ?? (hasDissent ? "Source dissent" : "Not supplied"),
    permissionLabel,
    expectedVersion,
    acceptanceScope,
    canAcceptCanonicalFact: explicitlyAllowed && contractComplete,
    acceptCanonicalFactDisabledReason: explicitlyAllowed
      ? missingContractReason
      : authorizationDisabledReason,
    compactCase
  };
}

function buildProposalRow(
  proposal: CorporateActionProposalEntry,
  compactCase: CorporateActionProcessingCaseDto | null,
  todayUtc: number
): CorporateActionInboxRowModel {
  const exDate = new Date(`${proposal.exDate}T00:00:00Z`);
  const daysUntilEx = Math.round((exDate.getTime() - todayUtc) / 86_400_000);
  const totalSources = proposal.agreeingSources.length + proposal.dissentingSources.length;
  const caseFields = deriveCaseFields(proposal, compactCase);
  return {
    rowId: caseFields.proposalIdLabel !== "Not supplied"
      ? caseFields.proposalIdLabel
      : `${proposal.securityId}:${proposal.actionType}:${proposal.exDate}`,
    ...caseFields,
    securityId: proposal.securityId,
    ticker: proposal.ticker,
    actionType: proposal.actionType,
    exDateLabel: proposal.exDate,
    daysUntilEx,
    countdownLabel: formatCountdown(daysUntilEx),
    valueLabel: formatValue(proposal),
    sourceEventLabel: "Not supplied",
    sourceObservedAtLabel: "Not supplied",
    sourceEvidenceReference: null,
    consensusLabel: `${proposal.agreeingSources.length}/${totalSources} source${totalSources === 1 ? "" : "s"} agree`,
    winningSource: proposal.winningSource,
    agreeingSources: proposal.agreeingSources,
    dissentingSources: proposal.dissentingSources,
    recordDateLabel: proposal.recordDate ?? "Not supplied",
    payableDateLabel: proposal.payableDate ?? "Not supplied",
    tone: proposal.dissentingSources.length > 0 ? "warning" : "neutral",
    proposal
  };
}

function buildCaseOnlyRow(
  processingCase: CorporateActionProcessingCaseDto,
  todayUtc: number,
  retainedSourceRow: CorporateActionInboxRowModel | null = null
): CorporateActionInboxRowModel {
  const durableCase = adaptCorporateActionProcessingCase(processingCase);
  const snapshot = processingCase.sourceSnapshot ?? null;
  const action = snapshot?.proposedAction ?? null;
  const display = snapshot?.displayMetadata ?? null;
  const providerId = snapshot?.providerIdentity.providerId.trim() || null;
  const winningSource = display?.winningSource.trim() || providerId || retainedSourceRow?.winningSource || "Not supplied";
  const agreeingSources = display?.agreeingSources.length
    ? display.agreeingSources
    : providerId
      ? [providerId]
      : retainedSourceRow?.agreeingSources ?? [];
  const dissentingSources = display?.dissentingSources ?? retainedSourceRow?.dissentingSources ?? [];
  const totalSources = agreeingSources.length + dissentingSources.length;
  const exDateLabel = action?.exDate ?? retainedSourceRow?.exDateLabel ?? "Not supplied";
  const daysUntilEx = action
    ? Math.round((new Date(`${action.exDate}T00:00:00Z`).getTime() - todayUtc) / 86_400_000)
    : retainedSourceRow?.daysUntilEx ?? null;
  const valueLabel = action
    ? formatValue({
        amount: action.dividendPerShare,
        currency: action.currency,
        splitFromFactor: action.splitRatio == null ? null : 1,
        splitToFactor: action.splitRatio
      })
    : retainedSourceRow?.valueLabel ?? "Not supplied";
  const isWarning = processingCase.state === "Disputed"
    || processingCase.state === "Blocked"
    || dissentingSources.length > 0;
  return {
    rowId: processingCase.proposalId,
    caseIdLabel: processingCase.caseId,
    proposalIdLabel: processingCase.proposalId,
    versionLabel: `case v${processingCase.version}`,
    statusLabel: processingCase.state,
    assignmentLabel: processingCase.assignedTo?.trim() || "Unassigned",
    conflictLabel: "Not supplied",
    permissionLabel: "Case actions checked by server",
    expectedVersion: null,
    acceptanceScope: processingCase.scope,
    canAcceptCanonicalFact: false,
    acceptCanonicalFactDisabledReason: "The canonical source proposal has already been accepted into this durable case.",
    securityId: processingCase.securityId,
    ticker: display?.ticker.trim() || retainedSourceRow?.ticker || (snapshot ? processingCase.securityId : "Not supplied"),
    actionType: action?.eventType ?? retainedSourceRow?.actionType ?? "Not supplied",
    exDateLabel,
    daysUntilEx,
    countdownLabel: daysUntilEx === null ? retainedSourceRow?.countdownLabel ?? "Not supplied" : formatCountdown(daysUntilEx),
    valueLabel,
    sourceEventLabel: snapshot
      ? `${snapshot.providerIdentity.sourceEventId} · ${snapshot.providerIdentity.sourceEventVersion}`
      : retainedSourceRow?.sourceEventLabel ?? "Not supplied",
    sourceObservedAtLabel: snapshot?.providerIdentity.observedAtUtc
      ?? retainedSourceRow?.sourceObservedAtLabel
      ?? "Not supplied",
    sourceEvidenceReference: snapshot?.providerIdentity.evidenceReference
      ?? retainedSourceRow?.sourceEvidenceReference
      ?? null,
    consensusLabel: totalSources > 0
      ? `${agreeingSources.length}/${totalSources} source${totalSources === 1 ? "" : "s"} agree`
      : retainedSourceRow?.consensusLabel ?? "Source consensus not supplied",
    winningSource,
    agreeingSources,
    dissentingSources,
    recordDateLabel: action?.recordDate ?? retainedSourceRow?.recordDateLabel ?? "Not supplied",
    payableDateLabel: action?.payDate ?? retainedSourceRow?.payableDateLabel ?? "Not supplied",
    tone: isWarning ? "warning" : retainedSourceRow?.tone ?? "neutral",
    durableCase,
    compactCase: processingCase,
    proposal: null
  };
}

/**
 * Pure projection of the corporate-action inbox snapshot into durable-case-oriented presentation
 * state. Missing case fields remain visibly "Not supplied"; the browser never invents case status,
 * version, accounting results, or policy authorization.
 */
export function buildCorporateActionInboxModel(
  response: CorporateActionInboxResponse,
  today: Date = new Date(),
  retainedRows: readonly CorporateActionInboxRowModel[] = []
): CorporateActionInboxModel {
  const todayUtc = Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), today.getUTCDate());
  const compactCases = response.cases ?? [];
  const compactCaseByProposalId = new Map(compactCases.map((processingCase) => [processingCase.proposalId, processingCase]));
  const retainedRowByProposalId = new Map(retainedRows.map((row) => [row.proposalIdLabel, row]));
  const stagedProposalIds = new Set(response.staged.flatMap((proposal) => proposal.proposalId ? [proposal.proposalId] : []));
  const rows = [
    ...response.staged.map((proposal) => buildProposalRow(
      proposal,
      proposal.proposalId ? compactCaseByProposalId.get(proposal.proposalId) ?? null : null,
      todayUtc
    )),
    ...compactCases
      .filter((processingCase) => !stagedProposalIds.has(processingCase.proposalId))
      .map((processingCase) => buildCaseOnlyRow(
        processingCase,
        todayUtc,
        retainedRowByProposalId.get(processingCase.proposalId) ?? null
      ))
  ];
  rows.sort((a, b) => {
    if (a.daysUntilEx === null && b.daysUntilEx !== null) return 1;
    if (a.daysUntilEx !== null && b.daysUntilEx === null) return -1;
    if (a.daysUntilEx !== null && b.daysUntilEx !== null && a.daysUntilEx !== b.daysUntilEx) {
      return a.daysUntilEx - b.daysUntilEx;
    }
    return a.ticker.localeCompare(b.ticker) || a.caseIdLabel.localeCompare(b.caseIdLabel);
  });

  return {
    lastIngestLabel: response.lastIngestAt ? new Date(response.lastIngestAt).toLocaleString() : "never",
    stagedCount: response.stagedCount,
    appliedLastRun: response.appliedLastRun,
    duplicatesSkippedLastRun: response.duplicatesSkippedLastRun,
    rows,
    errors: response.errors,
    hasPartialProviderFailure: response.errors.length > 0 && (response.stagedCount > 0 || response.appliedLastRun > 0),
    summary:
      response.stagedCount === 0
        ? compactCases.length > 0
          ? `${compactCases.length} durable processing case${compactCases.length === 1 ? "" : "s"}; no staged corporate actions awaiting review.`
          : "No staged corporate actions awaiting review."
        : `${response.stagedCount} staged proposal${response.stagedCount === 1 ? "" : "s"} awaiting review; ` +
          `${response.appliedLastRun} auto-applied last run.`
  };
}

export function filterCorporateActionInboxRows(
  rows: CorporateActionInboxRowModel[],
  filters: CorporateActionInboxFilters
): CorporateActionInboxRowModel[] {
  const query = filters.search.trim().toLocaleLowerCase();
  return rows.filter((row) => {
    const matchesSearch = !query || [
      row.ticker,
      row.securityId,
      row.actionType,
      row.caseIdLabel,
      row.proposalIdLabel,
      row.winningSource
    ].some((value) => value.toLocaleLowerCase().includes(query));
    return matchesSearch
      && (filters.status === "All" || row.statusLabel === filters.status)
      && (filters.assignment === "All" || row.assignmentLabel === filters.assignment)
      && (filters.conflict === "All" || row.conflictLabel === filters.conflict);
  });
}

export interface CorporateActionInboxViewModel {
  loading: boolean;
  error: string | null;
  model: CorporateActionInboxModel | null;
  rows: CorporateActionInboxRowModel[];
  filters: CorporateActionInboxFilters;
  setFilters: (filters: CorporateActionInboxFilters) => void;
  selectedRowKey: string | null;
  selectedRow: CorporateActionInboxRowModel | null;
  selectRow: (rowKey: string) => void;
  refresh: () => Promise<void>;
  acceptingKey: string | null;
  acceptErrors: Record<string, string>;
  pendingAcceptance: CorporateActionInboxRowModel | null;
  pendingAcceptanceRequest: CorporateActionInboxAcceptRequest | null;
  requestAcceptance: (row: CorporateActionInboxRowModel) => void;
  cancelAcceptance: () => void;
  confirmAcceptance: () => Promise<void>;
  acceptanceReceipt: CorporateActionAcceptanceReceipt | null;
  clearAcceptanceReceipt: () => void;
  bulkAcceptDisabledReason: string;
}

/**
 * View-model for the corporate-action case queue. "Accept" deliberately means append the selected
 * provider proposal as a canonical Security Master fact; it does not calculate entitlement,
 * approve basis treatment, mutate lots, or post a journal.
 */
export function useCorporateActionInboxPanel(
  fetchInbox: CorporateActionInboxFetcher = getCorporateActionInbox,
  acceptProposal: CorporateActionInboxAccepter = acceptCorporateActionInboxProposal,
  createIdempotencyKey: CorporateActionIdempotencyKeyFactory = defaultCorporateActionIdempotencyKey
): CorporateActionInboxViewModel {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [model, setModel] = useState<CorporateActionInboxModel | null>(null);
  const modelRef = useRef<CorporateActionInboxModel | null>(null);
  const [filters, setFilters] = useState(DEFAULT_CORPORATE_ACTION_INBOX_FILTERS);
  const [selectedRowKey, setSelectedRowKey] = useState<string | null>(null);
  const [acceptingKey, setAcceptingKey] = useState<string | null>(null);
  const [acceptErrors, setAcceptErrors] = useState<Record<string, string>>({});
  const [pendingAcceptance, setPendingAcceptance] = useState<CorporateActionInboxRowModel | null>(null);
  const [pendingAcceptanceRequest, setPendingAcceptanceRequest] = useState<CorporateActionInboxAcceptRequest | null>(null);
  const [acceptanceReceipt, setAcceptanceReceipt] = useState<CorporateActionAcceptanceReceipt | null>(null);

  const load = useCallback(async (preserveCurrentModel: boolean): Promise<boolean> => {
    setLoading(true);
    setError(null);
    try {
      const response = await fetchInbox();
      const nextModel = buildCorporateActionInboxModel(response, new Date(), modelRef.current?.rows ?? []);
      modelRef.current = nextModel;
      setModel(nextModel);
      setSelectedRowKey((current) => (
        current && nextModel.rows.some((row) => row.rowId === current)
          ? current
          : nextModel.rows[0]?.rowId ?? null
      ));
      return true;
    } catch (fetchError) {
      setError(describeApiError(fetchError, "Corporate-action inbox is unavailable.").summary);
      if (!preserveCurrentModel) {
        modelRef.current = null;
        setModel(null);
      }
      return false;
    } finally {
      setLoading(false);
    }
  }, [fetchInbox]);

  const refresh = useCallback(async () => {
    await load(false);
  }, [load]);

  const rows = useMemo(
    () => filterCorporateActionInboxRows(model?.rows ?? [], filters),
    [filters, model?.rows]
  );
  const selectedRow = useMemo(
    () => rows.find((row) => row.rowId === selectedRowKey) ?? rows[0] ?? null,
    [rows, selectedRowKey]
  );

  const requestAcceptance = useCallback((row: CorporateActionInboxRowModel) => {
    if (!row.canAcceptCanonicalFact || !row.acceptanceScope || row.expectedVersion === null || row.proposalIdLabel === "Not supplied") return;
    setPendingAcceptance(row);
    setPendingAcceptanceRequest({
      proposalId: row.proposalIdLabel,
      expectedVersion: row.expectedVersion,
      idempotencyKey: createIdempotencyKey(),
      scope: row.acceptanceScope
    });
    setAcceptErrors((current) => ({ ...current, [row.rowId]: "" }));
  }, [createIdempotencyKey]);

  const cancelAcceptance = useCallback(() => {
    if (!acceptingKey) {
      setPendingAcceptance(null);
      setPendingAcceptanceRequest(null);
    }
  }, [acceptingKey]);

  const confirmAcceptance = useCallback(async () => {
    const row = pendingAcceptance;
    const request = pendingAcceptanceRequest;
    if (!row || !request || !row.canAcceptCanonicalFact) return;
    setAcceptingKey(row.rowId);
    setAcceptErrors((current) => ({ ...current, [row.rowId]: "" }));
    try {
      const result = await acceptProposal(request);
      setPendingAcceptance(null);
      setPendingAcceptanceRequest(null);
      setAcceptanceReceipt({ row, result, queueRefreshWarning: null });
      const refreshed = await load(true);
      if (!refreshed) {
        setAcceptanceReceipt({
          row,
          result,
          queueRefreshWarning: "The canonical fact was accepted, but the queue could not be refreshed. Refresh before taking another action."
        });
      }
    } catch (acceptError) {
      setAcceptErrors((current) => ({
        ...current,
        [row.rowId]: describeApiError(acceptError, "The canonical fact could not be accepted.").summary
      }));
    } finally {
      setAcceptingKey(null);
    }
  }, [acceptProposal, load, pendingAcceptance, pendingAcceptanceRequest]);

  useEffect(() => {
    void load(false);
  }, [load]);

  return {
    loading,
    error,
    model,
    rows,
    filters,
    setFilters,
    selectedRowKey: selectedRow?.rowId ?? null,
    selectedRow,
    selectRow: setSelectedRowKey,
    refresh,
    acceptingKey,
    acceptErrors,
    pendingAcceptance,
    pendingAcceptanceRequest,
    requestAcceptance,
    cancelAcceptance,
    confirmAcceptance,
    acceptanceReceipt,
    clearAcceptanceReceipt: () => setAcceptanceReceipt(null),
    bulkAcceptDisabledReason: "Bulk acceptance is unavailable because the current server contract accepts and audits one canonical fact per request."
  };
}

function defaultCorporateActionIdempotencyKey(): string {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }
  return `corporate-action-accept-${Date.now()}`;
}
