import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  getOperationsContinuityWorkflow,
  getOperationsContinuityWorkflows,
  type ApiRequestOptions
} from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { normalizeLocalWorkstationRoute } from "@/lib/workspace";
import type {
  OperationsContinuityWorkflow,
  OperationsContinuityWorkflowSummary,
  OperationsGate,
  OperationsGateKey,
  OperationsGateStatus,
  OperationsNextAction,
  OperationsTimelineEntry,
  OperationsWorkflowBlocker,
  OperationsWorkflowStatus
} from "@/types";

export type OperationsContinuityTone = "ready" | "review" | "blocked" | "neutral";

export interface OperationsContinuityWorkflowRow {
  id: string;
  title: string;
  subtitle: string;
  statusLabel: string;
  statusTone: OperationsContinuityTone;
  gatesLabel: string;
  blockersLabel: string;
  updatedLabel: string;
  ariaLabel: string;
}

export interface OperationsContinuityGateRow {
  id: string;
  gateKey: OperationsGateKey;
  label: string;
  detail: string;
  statusLabel: string;
  statusTone: OperationsContinuityTone;
  requiredLabel: string;
  blockerCountLabel: string;
  completedLabel: string;
  ariaLabel: string;
}

export interface OperationsContinuityBlockerRow {
  id: string;
  code: string;
  message: string;
  gateLabel: string;
  severityLabel: string;
  severityTone: OperationsContinuityTone;
  evidenceLabel: string;
  ariaLabel: string;
}

export interface OperationsContinuityTimelineRow {
  id: string;
  title: string;
  detail: string;
  actorLabel: string;
  timestampLabel: string;
  stateLabel: string;
  hashLabel: string;
  ariaLabel: string;
}

export interface OperationsContinuityNextActionViewModel {
  title: string;
  detail: string;
  label: string;
  href: string | null;
  disabled: boolean;
  disabledReason: string | null;
  ariaLabel: string;
  statusTone: OperationsContinuityTone;
}

export interface OperationsContinuityDetailPanel {
  title: string;
  subtitle: string;
  statusLabel: string;
  statusTone: OperationsContinuityTone;
  metadata: { label: string; value: string }[];
}

export interface OperationsContinuityScreenViewModel {
  title: string;
  subtitle: string;
  loading: boolean;
  statusAnnouncement: string;
  errorText: string | null;
  detailErrorText: string | null;
  reloadLabel: string;
  reloadAriaLabel: string;
  reloadBusy: boolean;
  reloadDisabled: boolean;
  reloadDisabledReason: string | null;
  workflowsSummaryLabel: string;
  workflowsEmptyText: string;
  workflowsTableLabel: string;
  workflows: OperationsContinuityWorkflowRow[];
  selectedWorkflowId: string | null;
  selectWorkflow: (workflowId: string) => void;
  selectedDetail: OperationsContinuityDetailPanel | null;
  nextAction: OperationsContinuityNextActionViewModel;
  gatesLabel: string;
  gatesEmptyText: string;
  gates: OperationsContinuityGateRow[];
  blockersLabel: string;
  blockersEmptyText: string;
  blockers: OperationsContinuityBlockerRow[];
  timelineLabel: string;
  timelineEmptyText: string;
  timeline: OperationsContinuityTimelineRow[];
  refresh: () => Promise<void>;
}

export interface OperationsContinuityScreenServices {
  listWorkflows: (
    filters?: { fundAccountId?: string; periodId?: string; status?: string },
    options?: ApiRequestOptions
  ) => Promise<OperationsContinuityWorkflowSummary[]>;
  getWorkflow: (workflowId: string, options?: ApiRequestOptions) => Promise<OperationsContinuityWorkflow>;
}

export interface BuildOperationsContinuityScreenViewModelOptions {
  workflows: OperationsContinuityWorkflowSummary[];
  selectedWorkflowId: string | null;
  detail: OperationsContinuityWorkflow | null;
  loading: boolean;
  detailLoading: boolean;
  error: string | null;
  detailError: string | null;
  refresh: () => Promise<void>;
  selectWorkflow: (workflowId: string) => void;
}

const defaultServices: OperationsContinuityScreenServices = {
  listWorkflows: (filters = {}, options = {}) => getOperationsContinuityWorkflows(filters, options),
  getWorkflow: (workflowId: string, options = {}) => getOperationsContinuityWorkflow(workflowId, options)
};

export function useOperationsContinuityScreenViewModel(
  services: OperationsContinuityScreenServices = defaultServices
): OperationsContinuityScreenViewModel {
  const [workflows, setWorkflows] = useState<OperationsContinuityWorkflowSummary[]>([]);
  const [selectedWorkflowId, setSelectedWorkflowId] = useState<string | null>(null);
  const [detail, setDetail] = useState<OperationsContinuityWorkflow | null>(null);
  const [loading, setLoading] = useState(true);
  const [detailLoading, setDetailLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [detailError, setDetailError] = useState<string | null>(null);
  const mountedRef = useRef(true);
  const listRevisionRef = useRef(0);
  const detailRevisionRef = useRef(0);
  const listAbortRef = useRef<AbortController | null>(null);
  const detailAbortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    mountedRef.current = true;

    return () => {
      mountedRef.current = false;
      listRevisionRef.current += 1;
      detailRevisionRef.current += 1;
      listAbortRef.current?.abort();
      detailAbortRef.current?.abort();
    };
  }, []);

  const refresh = useCallback(async () => {
    if (!mountedRef.current) {
      return;
    }

    const revision = listRevisionRef.current + 1;
    listRevisionRef.current = revision;
    detailRevisionRef.current += 1;
    listAbortRef.current?.abort();
    detailAbortRef.current?.abort();
    const controller = new AbortController();
    listAbortRef.current = controller;
    setLoading(true);
    setError(null);
    setDetailError(null);

    try {
      const rows = await services.listWorkflows({}, { signal: controller.signal });
      if (!mountedRef.current || listRevisionRef.current !== revision) {
        return;
      }

      const sorted = [...rows].sort(compareWorkflowSummaries);
      setWorkflows(sorted);
      setSelectedWorkflowId((current) => {
        if (current && sorted.some((workflow) => workflow.workflowId === current)) {
          return current;
        }

        return sorted[0]?.workflowId ?? null;
      });
      if (sorted.length === 0) {
        setDetail(null);
      }
    } catch (err) {
      if (!isAbortError(err) && mountedRef.current && listRevisionRef.current === revision) {
        setError(formatError(err, "Operations continuity workflows could not be loaded."));
        setWorkflows([]);
        setSelectedWorkflowId(null);
        setDetail(null);
      }
    } finally {
      if (mountedRef.current && listRevisionRef.current === revision) {
        setLoading(false);
      }

      if (listAbortRef.current === controller) {
        listAbortRef.current = null;
      }
    }
  }, [services]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(() => {
    if (!selectedWorkflowId) {
      setDetail(null);
      setDetailError(null);
      return;
    }

    const revision = detailRevisionRef.current + 1;
    detailRevisionRef.current = revision;
    detailAbortRef.current?.abort();
    const controller = new AbortController();
    detailAbortRef.current = controller;
    setDetailLoading(true);
    setDetailError(null);

    services.getWorkflow(selectedWorkflowId, { signal: controller.signal })
      .then((workflow) => {
        if (!mountedRef.current || detailRevisionRef.current !== revision) {
          return;
        }

        setDetail(workflow);
      })
      .catch((err) => {
        if (!isAbortError(err) && mountedRef.current && detailRevisionRef.current === revision) {
          setDetail(null);
          setDetailError(formatError(err, "Workflow detail could not be loaded."));
        }
      })
      .finally(() => {
        if (mountedRef.current && detailRevisionRef.current === revision) {
          setDetailLoading(false);
        }

        if (detailAbortRef.current === controller) {
          detailAbortRef.current = null;
        }
      });
  }, [selectedWorkflowId, services]);

  const selectWorkflow = useCallback((workflowId: string) => {
    setSelectedWorkflowId(workflowId);
  }, []);

  return useMemo(() => buildOperationsContinuityScreenViewModel({
    workflows,
    selectedWorkflowId,
    detail,
    loading,
    detailLoading,
    error,
    detailError,
    refresh,
    selectWorkflow
  }), [detail, detailError, detailLoading, error, loading, refresh, selectWorkflow, selectedWorkflowId, workflows]);
}

export function buildOperationsContinuityScreenViewModel({
  workflows,
  selectedWorkflowId,
  detail,
  loading,
  detailLoading,
  error,
  detailError,
  refresh,
  selectWorkflow
}: BuildOperationsContinuityScreenViewModelOptions): OperationsContinuityScreenViewModel {
  const selectedSummary = workflows.find((workflow) => workflow.workflowId === selectedWorkflowId) ?? workflows[0] ?? null;
  const effectiveDetail = detail?.workflowId === selectedSummary?.workflowId ? detail : null;
  const gateSource = effectiveDetail?.gates ?? selectedSummary?.gates ?? [];
  const nextAction = buildNextActionViewModel({
    workflow: effectiveDetail ?? selectedSummary,
    gates: gateSource,
    loading: loading || detailLoading,
    detailError
  });
  const blockers = buildBlockerRows(effectiveDetail?.blockers ?? collectGateBlockers(gateSource));
  const timeline = buildTimelineRows(effectiveDetail?.timeline ?? []);
  const gates = gateSource.map(mapGateRow);
  const rows = workflows.map(mapWorkflowRow);
  const selectedDetail = selectedSummary
    ? buildDetailPanel(selectedSummary, effectiveDetail, blockers.length)
    : null;

  return {
    title: "Operations continuity",
    subtitle: "Track account-period close workflows from broker intake through Security Master coverage, ledger posting, reconciliation, approval, and close evidence.",
    loading,
    statusAnnouncement: buildStatusAnnouncement({ loading, rows, selectedSummary, detailLoading, detailError }),
    errorText: error,
    detailErrorText: detailError,
    reloadLabel: loading ? "Refreshing" : "Refresh workflows",
    reloadAriaLabel: loading ? "Refreshing operations continuity workflows" : "Refresh operations continuity workflows",
    reloadBusy: loading,
    reloadDisabled: loading,
    reloadDisabledReason: loading ? "Operations continuity workflows are already refreshing." : null,
    workflowsSummaryLabel: `${rows.length} workflow${rows.length === 1 ? "" : "s"}`,
    workflowsEmptyText: "No operations continuity workflows are available for this workstation context.",
    workflowsTableLabel: "Operations continuity workflows",
    workflows: rows,
    selectedWorkflowId: selectedSummary?.workflowId ?? null,
    selectWorkflow,
    selectedDetail,
    nextAction,
    gatesLabel: "Gates",
    gatesEmptyText: "Open a workflow to inspect gate posture.",
    gates,
    blockersLabel: "Blockers",
    blockersEmptyText: "No blockers are surfaced for the selected workflow.",
    blockers,
    timelineLabel: "Timeline",
    timelineEmptyText: detailLoading ? "Loading workflow timeline." : "Open workflow detail to inspect the hash-chained timeline.",
    timeline,
    refresh
  };
}

function buildDetailPanel(
  summary: OperationsContinuityWorkflowSummary,
  detail: OperationsContinuityWorkflow | null,
  blockerCount: number
): OperationsContinuityDetailPanel {
  return {
    title: `${summary.periodId} close workflow`,
    subtitle: `Fund ${summary.fundAccountId} from ${summary.brokerSource || "broker source pending"}.`,
    statusLabel: statusLabel(summary.status),
    statusTone: statusTone(summary.status),
    metadata: [
      { label: "Workflow", value: summary.workflowId },
      { label: "Version", value: String(detail?.version ?? summary.version) },
      { label: "Updated", value: formatDate(summary.updatedAtUtc) },
      { label: "Security Master", value: summary.securityMasterSnapshotId ?? "Snapshot pending" },
      { label: "Break cases", value: detail ? String(detail.breakCases.length) : "Detail pending" },
      { label: "Blockers", value: String(blockerCount) }
    ]
  };
}

function buildNextActionViewModel({
  workflow,
  gates,
  loading,
  detailError
}: {
  workflow: OperationsContinuityWorkflow | OperationsContinuityWorkflowSummary | null;
  gates: OperationsGate[];
  loading: boolean;
  detailError: string | null;
}): OperationsContinuityNextActionViewModel {
  const rawAction = selectHighestValueAction(workflow, gates);
  const href = normalizeLocalWorkstationRoute(rawAction?.route) ?? null;
  const disabledReason = loading
    ? "Wait for the selected workflow to finish loading before taking the next action."
    : detailError
      ? "Resolve the workflow detail error before using the next action."
      : !workflow
        ? "Start or load an operations continuity workflow before taking an action."
        : !rawAction
          ? workflow.status === "Closed"
            ? "This workflow is closed; governed reopen is not available from this first browser surface."
            : "No server-recommended next action is available for the selected workflow."
          : !href
            ? "The server did not provide a local workstation route for this action."
            : null;

  return {
    title: rawAction?.label ?? "No action available",
    detail: rawAction?.gate ? `Recommended by the ${gateLabel(rawAction.gate)} gate.` : "Server-derived recommendation for the selected close workflow.",
    label: rawAction ? "Open next action" : "Next action unavailable",
    href,
    disabled: disabledReason !== null,
    disabledReason,
    ariaLabel: rawAction ? `Open operations continuity next action: ${rawAction.label}` : "Operations continuity next action unavailable",
    statusTone: disabledReason ? "neutral" : "ready"
  };
}

function selectHighestValueAction(
  workflow: OperationsContinuityWorkflow | OperationsContinuityWorkflowSummary | null,
  gates: OperationsGate[]
): OperationsNextAction | null {
  if (!workflow) {
    return null;
  }

  const gateStatusByKey = new Map(gates.map((gate) => [gate.gateKey, gate.status]));
  const allActions = [
    ...(workflow.nextActions ?? []).map((action) => ({
      action,
      gateStatus: action.gate ? (gateStatusByKey.get(action.gate) ?? "NotStarted") : "NotStarted" as OperationsGateStatus
    })),
    ...gates.flatMap((gate) => gate.nextActions.map((action) => ({ action, gateStatus: gate.status })))
  ];

  allActions.sort((left, right) => gateStatusPriority(right.gateStatus) - gateStatusPriority(left.gateStatus));
  return allActions.find((entry) => entry.action.label.trim())?.action ?? null;
}

function mapWorkflowRow(workflow: OperationsContinuityWorkflowSummary): OperationsContinuityWorkflowRow {
  const blockerCount = collectGateBlockers(workflow.gates).length;
  const passedGateCount = workflow.gates.filter((gate) => gate.status === "Passed").length;
  return {
    id: workflow.workflowId,
    title: `${workflow.periodId} close`,
    subtitle: `${workflow.brokerSource || "Broker source pending"} / ${workflow.fundAccountId}`,
    statusLabel: statusLabel(workflow.status),
    statusTone: statusTone(workflow.status),
    gatesLabel: `${passedGateCount}/${workflow.gates.length} gates passed`,
    blockersLabel: blockerCount === 0 ? "No blockers" : `${blockerCount} blocker${blockerCount === 1 ? "" : "s"}`,
    updatedLabel: formatDate(workflow.updatedAtUtc),
    ariaLabel: `${workflow.periodId} operations continuity workflow, ${statusLabel(workflow.status)}, ${passedGateCount} of ${workflow.gates.length} gates passed`
  };
}

function mapGateRow(gate: OperationsGate): OperationsContinuityGateRow {
  return {
    id: gate.gateKey,
    gateKey: gate.gateKey,
    label: gate.displayName || gateLabel(gate.gateKey),
    detail: gate.description || "No gate description supplied.",
    statusLabel: gateStatusLabel(gate.status),
    statusTone: gateTone(gate.status),
    requiredLabel: gate.isRequired ? "Required" : "Optional",
    blockerCountLabel: gate.blockers.length === 0 ? "No blockers" : `${gate.blockers.length} blocker${gate.blockers.length === 1 ? "" : "s"}`,
    completedLabel: gate.completedAtUtc ? `${formatDate(gate.completedAtUtc)} by ${gate.completedBy ?? "unknown"}` : "Not completed",
    ariaLabel: `${gate.displayName || gateLabel(gate.gateKey)} gate, ${gateStatusLabel(gate.status)}, ${gate.blockers.length} blockers`
  };
}

function buildBlockerRows(blockers: OperationsWorkflowBlocker[]): OperationsContinuityBlockerRow[] {
  return blockers.map((blocker, index) => ({
    id: `${blocker.code}:${index}`,
    code: blocker.code,
    message: blocker.message,
    gateLabel: blocker.gate ? gateLabel(blocker.gate) : "Workflow",
    severityLabel: blocker.severity || "Review",
    severityTone: severityTone(blocker.severity),
    evidenceLabel: blocker.evidenceLinks.length === 0 ? "No linked evidence" : `${blocker.evidenceLinks.length} evidence link${blocker.evidenceLinks.length === 1 ? "" : "s"}`,
    ariaLabel: `${blocker.code}, ${blocker.severity || "Review"} blocker for ${blocker.gate ? gateLabel(blocker.gate) : "workflow"}`
  }));
}

function buildTimelineRows(timeline: OperationsTimelineEntry[]): OperationsContinuityTimelineRow[] {
  return [...timeline]
    .sort((left, right) => right.occurredAtUtc.localeCompare(left.occurredAtUtc))
    .map((entry) => ({
      id: entry.auditId,
      title: eventLabel(entry.eventType),
      detail: entry.rationale ?? "No rationale recorded.",
      actorLabel: entry.actor || "Unknown actor",
      timestampLabel: formatDate(entry.occurredAtUtc),
      stateLabel: `${statusLabel(entry.fromState)} to ${statusLabel(entry.toState)}`,
      hashLabel: entry.currentHash ? entry.currentHash.slice(0, 12) : "Hash pending",
      ariaLabel: `${eventLabel(entry.eventType)} by ${entry.actor || "unknown actor"} at ${formatDate(entry.occurredAtUtc)}`
    }));
}

function collectGateBlockers(gates: OperationsGate[]): OperationsWorkflowBlocker[] {
  return gates.flatMap((gate) => gate.blockers ?? []);
}

function statusLabel(status: OperationsWorkflowStatus): string {
  return splitEnumLabel(status);
}

function gateStatusLabel(status: OperationsGateStatus): string {
  return splitEnumLabel(status);
}

function gateLabel(gate: OperationsGateKey): string {
  return splitEnumLabel(gate);
}

function eventLabel(eventType: string): string {
  return eventType
    .split(/[-_\s]+/)
    .filter(Boolean)
    .map((part) => `${part.slice(0, 1).toUpperCase()}${part.slice(1)}`)
    .join(" ") || "Workflow event";
}

function statusTone(status: OperationsWorkflowStatus): OperationsContinuityTone {
  switch (status) {
    case "Closed":
    case "ReadyForClose":
      return "ready";
    case "Blocked":
      return "blocked";
    case "ApprovalPending":
    case "ReconciliationActive":
    case "LedgerPostingDraft":
    case "SecurityMasterValidation":
    case "CollectingBrokerData":
      return "review";
    default:
      return "neutral";
  }
}

function gateTone(status: OperationsGateStatus): OperationsContinuityTone {
  switch (status) {
    case "Passed":
      return "ready";
    case "Blocked":
      return "blocked";
    case "ReviewRequired":
    case "InProgress":
      return "review";
    default:
      return "neutral";
  }
}

function severityTone(severity: string | null | undefined): OperationsContinuityTone {
  const normalized = severity?.trim().toLowerCase() ?? "";
  if (normalized === "critical" || normalized === "error") {
    return "blocked";
  }

  if (normalized === "warning" || normalized === "warn") {
    return "review";
  }

  return "neutral";
}

function gateStatusPriority(status: OperationsGateStatus): number {
  switch (status) {
    case "Blocked":
      return 4;
    case "ReviewRequired":
      return 3;
    case "InProgress":
      return 2;
    case "NotStarted":
      return 1;
    default:
      return 0;
  }
}

function compareWorkflowSummaries(left: OperationsContinuityWorkflowSummary, right: OperationsContinuityWorkflowSummary): number {
  return right.updatedAtUtc.localeCompare(left.updatedAtUtc);
}

function buildStatusAnnouncement({
  loading,
  rows,
  selectedSummary,
  detailLoading,
  detailError
}: {
  loading: boolean;
  rows: OperationsContinuityWorkflowRow[];
  selectedSummary: OperationsContinuityWorkflowSummary | null;
  detailLoading: boolean;
  detailError: string | null;
}): string {
  if (loading) {
    return "Loading operations continuity workflows.";
  }

  if (detailLoading) {
    return "Loading selected operations continuity workflow detail.";
  }

  if (detailError) {
    return `Selected workflow detail failed to load: ${detailError}`;
  }

  if (!selectedSummary) {
    return "No operations continuity workflows are available.";
  }

  return `${rows.length} operations continuity workflows loaded. Selected ${selectedSummary.periodId} close.`;
}

function formatError(err: unknown, fallback: string): string {
  const display = describeApiError(err, fallback);
  return display.summary || (err instanceof Error ? err.message : fallback);
}

function isAbortError(err: unknown): boolean {
  return err instanceof DOMException && err.name === "AbortError";
}

function splitEnumLabel(value: string): string {
  return value.replace(/([a-z0-9])([A-Z])/g, "$1 $2");
}

function formatDate(value: string | null | undefined): string {
  if (!value) {
    return "Not recorded";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  const month = date.toLocaleString("en-US", { month: "short", timeZone: "UTC" });
  const day = date.getUTCDate().toString().padStart(2, "0");
  const hour = date.getUTCHours().toString().padStart(2, "0");
  const minute = date.getUTCMinutes().toString().padStart(2, "0");
  return `${month} ${day}, ${hour}:${minute} UTC`;
}
