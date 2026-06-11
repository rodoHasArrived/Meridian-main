import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  getPrivateCapitalCloseCockpit,
  getOperationsContinuityWorkflow,
  getOperationsContinuityWorkflows,
  type ApiRequestOptions,
  type PrivateCapitalCloseCockpitQuery
} from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { normalizeLocalWorkstationRoute } from "@/lib/workspace";
import type {
  OperationsContinuityWorkflow,
  OperationsContinuityWorkflowSummary,
  OperationsAccountingRecordEvidenceCategory,
  OperationsAccountingRecordSummary,
  OperationsBreakCase,
  OperationsDashboardSummary,
  OperationsEvidencePackageSummary,
  OperationsReconciliationLaneSummary,
  OperationsCloseChecklistTask,
  OperationsClosePackagePublication,
  OperationsGate,
  OperationsGateKey,
  OperationsGateStatus,
  OperationsNextAction,
  OperationsTimelineEntry,
  OperationsWorkflowBlocker,
  OperationsWorkflowStatus,
  PrivateCapitalCloseCockpit,
  PrivateCapitalCloseCockpitApproval,
  PrivateCapitalCloseCockpitLane,
  PrivateCapitalCloseCockpitWorkflow,
  PrivateCapitalNavSupportPackage,
  EvidenceStatus
} from "@/types";

export type OperationsContinuityTone = "ready" | "review" | "blocked" | "neutral";
export type OperationsContinuityRowClassName = "bg-success/5" | "bg-warning/5" | "bg-danger/5" | undefined;

export const OPERATIONS_CONTINUITY_WORKFLOW_DETAIL_PANEL_ID = "operations-continuity-workflow-detail";

export interface OperationsContinuityWorkflowRow {
  id: string;
  title: string;
  subtitle: string;
  statusLabel: string;
  statusTone: OperationsContinuityTone;
  gatesLabel: string;
  blockersLabel: string;
  updatedLabel: string;
  detailPanelId: string;
  expanded: boolean;
  rowClassName: OperationsContinuityRowClassName;
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

export interface OperationsContinuityChecklistRow {
  id: string;
  label: string;
  gateLabel: string;
  ownerLabel: string;
  statusLabel: string;
  statusTone: OperationsContinuityTone;
  dueLabel: string;
  expiresLabel: string;
  requiredEvidence: string;
  approvalLabel: string;
  evidenceLabel: string;
  remediationHref: string | null;
  remediationLabel: string;
  acknowledgementLabel: string;
  ariaLabel: string;
}

export interface OperationsContinuityChecklistSummary {
  taskCountLabel: string;
  readyCountLabel: string;
  blockedCountLabel: string;
  acknowledgementCountLabel: string;
  approvalCountLabel: string;
  evidenceCountLabel: string;
  dueSoonLabel: string;
  statusTone: OperationsContinuityTone;
}

export interface OperationsContinuityBreakCaseRow {
  id: string;
  caseLabel: string;
  categoryLabel: string;
  statusLabel: string;
  statusTone: OperationsContinuityTone;
  severityLabel: string;
  severityTone: OperationsContinuityTone;
  ownerLabel: string;
  dueLabel: string;
  slaLabel: string;
  escalationLabel: string;
  varianceLabel: string;
  materialityLabel: string;
  sourceLabel: string;
  rootCauseLabel: string;
  symbolLabel: string;
  evidenceLabel: string;
  approvalLabel: string;
  blockedOutputsLabel: string;
  actionLabel: string;
  ariaLabel: string;
}

export interface OperationsContinuityReconciliationLaneRow {
  id: string;
  label: string;
  statusLabel: string;
  statusTone: OperationsContinuityTone;
  breakCountLabel: string;
  summary: string;
  evidenceLabel: string;
  routeHref: string | null;
  routeLabel: string;
  requiredActionsLabel: string;
  ariaLabel: string;
}

export interface OperationsContinuityDashboardMetricRow {
  id: string;
  label: string;
  valueLabel: string;
  statusLabel: string;
  statusTone: OperationsContinuityTone;
  detail: string;
  evidenceLabel: string;
  requiredActionsLabel: string;
  routeHref: string | null;
  routeLabel: string;
  ariaLabel: string;
}

export interface OperationsContinuityDashboardViewModel {
  title: string;
  statusLabel: string;
  statusTone: OperationsContinuityTone;
  stageLabel: string;
  summaryLabel: string;
  metricCountLabel: string;
  evidenceLabel: string;
  requiredActionsLabel: string;
  metricsEmptyText: string;
  metrics: OperationsContinuityDashboardMetricRow[];
}

export interface OperationsContinuityEvidencePackageRow {
  id: string;
  label: string;
  statusLabel: string;
  statusTone: OperationsContinuityTone;
  readinessLabel: string;
  summary: string;
  evidenceLabel: string;
  requiredActionsLabel: string;
  routeHref: string | null;
  routeLabel: string;
  ariaLabel: string;
}

export interface OperationsContinuityClosePackageViewModel {
  title: string;
  statusLabel: string;
  statusTone: OperationsContinuityTone;
  packageIdLabel: string;
  reportPackLabel: string;
  manifestLabel: string;
  manifestHref: string | null;
  evidenceHashLabel: string;
  publishedLabel: string;
  signerLabel: string;
  rationaleLabel: string;
  evidenceLabel: string;
  approvalLabel: string;
}

export interface OperationsContinuityCloseGovernanceViewModel {
  title: string;
  statusLabel: string;
  statusTone: OperationsContinuityTone;
  lockLabel: string;
  lockDetail: string;
  closeAuditLabel: string;
  closePackageLabel: string;
  reopenAuditLabel: string;
  reopenIncidentLabel: string;
  reopenEvidenceLabel: string;
  reopenRationaleLabel: string;
  commandGuardLabel: string;
}

export interface OperationsContinuityCloseCockpitLaneRow {
  id: string;
  label: string;
  statusLabel: string;
  statusTone: OperationsContinuityTone;
  summary: string;
  evidenceLabel: string;
  routeHref: string | null;
  routeLabel: string;
  requiredActionsLabel: string;
  ariaLabel: string;
}

export interface OperationsContinuityCloseCockpitWorkflowRow {
  id: string;
  periodLabel: string;
  statusLabel: string;
  statusTone: OperationsContinuityTone;
  readinessLabel: string;
  readinessTone: OperationsContinuityTone;
  blockersLabel: string;
  checklistLabel: string;
  packageHref: string | null;
  packageLabel: string;
  workflowHref: string | null;
  ariaLabel: string;
}

export interface OperationsContinuityCloseCockpitApprovalRow {
  id: string;
  workflowLabel: string;
  statusLabel: string;
  statusTone: OperationsContinuityTone;
  actorLabel: string;
  decidedLabel: string;
  rationale: string;
  evidenceLabel: string;
  workflowHref: string | null;
  routeLabel: string;
  ariaLabel: string;
}

export interface OperationsContinuityNavSupportPackageRow {
  id: string;
  label: string;
  statusLabel: string;
  statusTone: OperationsContinuityTone;
  summary: string;
  componentSummaryLabel: string;
  shadowNavLabel: string;
  evidenceLabel: string;
  requiredActionsLabel: string;
  routeHref: string | null;
  routeLabel: string;
  ariaLabel: string;
}

export interface OperationsContinuityCloseCockpitViewModel {
  title: string;
  statusLabel: string;
  statusTone: OperationsContinuityTone;
  summaryLabel: string;
  scopeLabel: string;
  freshnessLabel: string;
  readinessLabel: string;
  laneCountLabel: string;
  proofLaneSummaryLabel: string;
  workflowCountLabel: string;
  blockerCountLabel: string;
  fundEventCountLabel: string;
  reportOutputCountLabel: string;
  nextActionLabel: string;
  nextActionHref: string | null;
  nextActionDisabled: boolean;
  nextActionDisabledReason: string | null;
  liveCapabilitiesLabel: string;
  plannedCapabilitiesLabel: string;
  errorText: string | null;
  lanesEmptyText: string;
  workflowsEmptyText: string;
  approvalHistoryEmptyText: string;
  navSupportPackagesEmptyText: string;
  lanes: OperationsContinuityCloseCockpitLaneRow[];
  workflows: OperationsContinuityCloseCockpitWorkflowRow[];
  approvalHistory: OperationsContinuityCloseCockpitApprovalRow[];
  navSupportPackages: OperationsContinuityNavSupportPackageRow[];
}

export interface OperationsAccountingRecordEvidenceRow {
  id: string;
  label: string;
  statusLabel: string;
  statusTone: OperationsContinuityTone;
  detail: string;
  requiredEvidenceLabel: string;
  evidenceLabel: string;
  routeHref: string | null;
  routeLabel: string;
  ariaLabel: string;
}

export interface OperationsAccountingRecordSummaryViewModel {
  title: string;
  summaryLabel: string;
  statusLabel: string;
  statusTone: OperationsContinuityTone;
  recordIdLabel: string;
  evidenceLabel: string;
  auditPackLabel: string;
  auditPackTimingLabel: string;
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
  id: string;
  ariaLabel: string;
  title: string;
  subtitle: string;
  description: string;
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
  workflowsTableCaption: string;
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
  breakCasesLabel: string;
  breakCasesEmptyText: string;
  reconciliationLanesLabel: string;
  reconciliationLanesEmptyText: string;
  evidencePackagesLabel: string;
  evidencePackagesEmptyText: string;
  dashboard: OperationsContinuityDashboardViewModel;
  breakCases: OperationsContinuityBreakCaseRow[];
  reconciliationLanes: OperationsContinuityReconciliationLaneRow[];
  evidencePackages: OperationsContinuityEvidencePackageRow[];
  checklistLabel: string;
  checklistSummary: OperationsContinuityChecklistSummary;
  checklistEmptyText: string;
  checklist: OperationsContinuityChecklistRow[];
  closeGovernance: OperationsContinuityCloseGovernanceViewModel;
  closePackage: OperationsContinuityClosePackageViewModel;
  closeCockpit: OperationsContinuityCloseCockpitViewModel;
  accountingRecordLabel: string;
  accountingRecordSummary: OperationsAccountingRecordSummaryViewModel;
  accountingRecordEmptyText: string;
  accountingRecordEvidence: OperationsAccountingRecordEvidenceRow[];
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
  getCloseCockpit?: (
    query?: PrivateCapitalCloseCockpitQuery,
    options?: ApiRequestOptions
  ) => Promise<PrivateCapitalCloseCockpit>;
}

export interface BuildOperationsContinuityScreenViewModelOptions {
  workflows: OperationsContinuityWorkflowSummary[];
  selectedWorkflowId: string | null;
  detail: OperationsContinuityWorkflow | null;
  loading: boolean;
  detailLoading: boolean;
  closeCockpit?: PrivateCapitalCloseCockpit | null;
  closeCockpitLoading?: boolean;
  closeCockpitError?: string | null;
  error: string | null;
  detailError: string | null;
  refresh: () => Promise<void>;
  selectWorkflow: (workflowId: string) => void;
}

const defaultServices: OperationsContinuityScreenServices = {
  listWorkflows: (filters = {}, options = {}) => getOperationsContinuityWorkflows(filters, options),
  getWorkflow: (workflowId: string, options = {}) => getOperationsContinuityWorkflow(workflowId, options),
  getCloseCockpit: (query = {}, options = {}) => getPrivateCapitalCloseCockpit(query, options)
};

export function useOperationsContinuityScreenViewModel(
  services: OperationsContinuityScreenServices = defaultServices
): OperationsContinuityScreenViewModel {
  const [workflows, setWorkflows] = useState<OperationsContinuityWorkflowSummary[]>([]);
  const [selectedWorkflowId, setSelectedWorkflowId] = useState<string | null>(null);
  const [detail, setDetail] = useState<OperationsContinuityWorkflow | null>(null);
  const [loading, setLoading] = useState(true);
  const [detailLoading, setDetailLoading] = useState(false);
  const [closeCockpit, setCloseCockpit] = useState<PrivateCapitalCloseCockpit | null>(null);
  const [closeCockpitLoading, setCloseCockpitLoading] = useState(false);
  const [closeCockpitError, setCloseCockpitError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [detailError, setDetailError] = useState<string | null>(null);
  const mountedRef = useRef(true);
  const listRevisionRef = useRef(0);
  const detailRevisionRef = useRef(0);
  const closeCockpitRevisionRef = useRef(0);
  const listAbortRef = useRef<AbortController | null>(null);
  const detailAbortRef = useRef<AbortController | null>(null);
  const closeCockpitAbortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    mountedRef.current = true;

    return () => {
      mountedRef.current = false;
      listRevisionRef.current += 1;
      detailRevisionRef.current += 1;
      closeCockpitRevisionRef.current += 1;
      listAbortRef.current?.abort();
      detailAbortRef.current?.abort();
      closeCockpitAbortRef.current?.abort();
    };
  }, []);

  const refresh = useCallback(async () => {
    if (!mountedRef.current) {
      return;
    }

    const revision = listRevisionRef.current + 1;
    listRevisionRef.current = revision;
    detailRevisionRef.current += 1;
    closeCockpitRevisionRef.current += 1;
    listAbortRef.current?.abort();
    detailAbortRef.current?.abort();
    closeCockpitAbortRef.current?.abort();
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

  const closeCockpitScope = useMemo(() => {
    return workflows.find((workflow) => workflow.workflowId === selectedWorkflowId) ?? workflows[0] ?? null;
  }, [selectedWorkflowId, workflows]);

  useEffect(() => {
    if (loading) {
      return;
    }

    const revision = closeCockpitRevisionRef.current + 1;
    closeCockpitRevisionRef.current = revision;
    closeCockpitAbortRef.current?.abort();
    const controller = new AbortController();
    closeCockpitAbortRef.current = controller;
    setCloseCockpitLoading(true);
    setCloseCockpitError(null);

    const query = closeCockpitScope ? buildCloseCockpitQuery(closeCockpitScope) : {};
    const getCloseCockpit = services.getCloseCockpit ?? defaultServices.getCloseCockpit!;

    getCloseCockpit(query, { signal: controller.signal })
      .then((cockpit) => {
        if (!mountedRef.current || closeCockpitRevisionRef.current !== revision) {
          return;
        }

        setCloseCockpit(cockpit);
      })
      .catch((err) => {
        if (!isAbortError(err) && mountedRef.current && closeCockpitRevisionRef.current === revision) {
          setCloseCockpit(null);
          setCloseCockpitError(formatError(err, "Private-capital close cockpit could not be loaded."));
        }
      })
      .finally(() => {
        if (mountedRef.current && closeCockpitRevisionRef.current === revision) {
          setCloseCockpitLoading(false);
        }

        if (closeCockpitAbortRef.current === controller) {
          closeCockpitAbortRef.current = null;
        }
      });
  }, [closeCockpitScope, loading, services]);

  const selectWorkflow = useCallback((workflowId: string) => {
    setSelectedWorkflowId(workflowId);
  }, []);

  return useMemo(() => buildOperationsContinuityScreenViewModel({
    workflows,
    selectedWorkflowId,
    detail,
    loading,
    detailLoading,
    closeCockpit,
    closeCockpitLoading,
    closeCockpitError,
    error,
    detailError,
    refresh,
    selectWorkflow
  }), [closeCockpit, closeCockpitError, closeCockpitLoading, detail, detailError, detailLoading, error, loading, refresh, selectWorkflow, selectedWorkflowId, workflows]);
}

export function buildOperationsContinuityScreenViewModel({
  workflows,
  selectedWorkflowId,
  detail,
  loading,
  detailLoading,
  closeCockpit = null,
  closeCockpitLoading = false,
  closeCockpitError = null,
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
  const breakCases = buildBreakCaseRows(effectiveDetail?.breakCases ?? []);
  const reconciliationLanes = buildReconciliationLaneRows(effectiveDetail?.reconciliationLanes ?? []);
  const evidencePackages = buildEvidencePackageRows(effectiveDetail?.evidencePackages ?? []);
  const dashboard = buildOperationsDashboardViewModel(effectiveDetail?.dashboardSummary ?? null, detailLoading);
  const checklistSource = effectiveDetail?.closeChecklist ?? [];
  const checklist = buildChecklistRows(checklistSource);
  const accountingRecordSummary = buildAccountingRecordSummaryViewModel(
    effectiveDetail?.accountingRecordSummary ?? null,
    detailLoading
  );
  const accountingRecordEvidence = buildAccountingRecordEvidenceRows(effectiveDetail?.accountingRecordSummary ?? null);
  const timeline = buildTimelineRows(effectiveDetail?.timeline ?? []);
  const gates = gateSource.map(mapGateRow);
  const rows = workflows.map((workflow) => mapWorkflowRow(workflow, selectedSummary?.workflowId ?? null));
  const selectedDetail = selectedSummary
    ? buildDetailPanel(selectedSummary, effectiveDetail, blockers.length)
    : null;
  const closeCockpitPanel = buildCloseCockpitViewModel(
    closeCockpit,
    closeCockpitLoading,
    closeCockpitError,
    selectedSummary
  );

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
    workflowsEmptyText: loading
      ? "Loading operations continuity workflows..."
      : error
        ? "Operations continuity workflows could not be loaded."
        : "No operations continuity workflows are available for this workstation context.",
    workflowsTableLabel: "Operations continuity workflows",
    workflowsTableCaption: "Operations continuity workflows. Select a row to inspect close-lane gates, blockers, next action, and timeline.",
    workflows: rows,
    selectedWorkflowId: selectedSummary?.workflowId ?? null,
    selectWorkflow,
    selectedDetail,
    nextAction,
    gatesLabel: "Gates",
    gatesEmptyText: detailLoading ? "Loading selected workflow gates..." : "Open a workflow to inspect gate posture.",
    gates,
    blockersLabel: "Blockers",
    blockersEmptyText: detailLoading ? "Loading selected workflow blockers..." : "No blockers are surfaced for the selected workflow.",
    blockers,
    breakCasesLabel: "Break assignment and escalation",
    breakCasesEmptyText: detailLoading ? "Loading reconciliation break cases..." : "No reconciliation break cases are surfaced for the selected workflow.",
    reconciliationLanesLabel: "Reconciliation lane coverage",
    reconciliationLanesEmptyText: detailLoading ? "Loading reconciliation lane coverage..." : "No reconciliation lane coverage is surfaced for the selected workflow.",
    evidencePackagesLabel: "Evidence packages",
    evidencePackagesEmptyText: detailLoading ? "Loading evidence packages..." : "No evidence packages are surfaced for the selected workflow.",
    dashboard,
    breakCases,
    reconciliationLanes,
    evidencePackages,
    checklistLabel: "Close checklist",
    checklistSummary: buildChecklistSummary(checklistSource),
    checklistEmptyText: detailLoading ? "Loading selected workflow checklist..." : "No close checklist tasks are available for the selected workflow.",
    checklist,
    closeGovernance: buildCloseGovernanceViewModel(effectiveDetail, detailLoading),
    closePackage: buildClosePackageViewModel(effectiveDetail?.closePackage ?? null, detailLoading),
    closeCockpit: closeCockpitPanel,
    accountingRecordLabel: "Accounting record evidence",
    accountingRecordSummary,
    accountingRecordEmptyText: detailLoading
      ? "Loading selected workflow accounting-record evidence..."
      : "No accounting-record evidence categories are available for the selected workflow.",
    accountingRecordEvidence,
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
    id: OPERATIONS_CONTINUITY_WORKFLOW_DETAIL_PANEL_ID,
    ariaLabel: `Operations continuity detail for ${summary.periodId} close workflow`,
    title: `${summary.periodId} close workflow`,
    subtitle: `Fund ${summary.fundAccountId} from ${summary.brokerSource || "broker source pending"}.`,
    description: "Selected close-lane evidence, gate progress, Security Master snapshot, and blocker count.",
    statusLabel: statusLabel(summary.status),
    statusTone: statusTone(summary.status),
    metadata: [
      { label: "Workflow", value: summary.workflowId },
      { label: "Version", value: String(detail?.version ?? summary.version) },
      { label: "Updated", value: formatDate(summary.updatedAtUtc) },
      { label: "Security Master", value: summary.securityMasterSnapshotId ?? "Snapshot pending" },
      { label: "Reconciliation", value: detail ? splitEnumLabel(detail.reconciliationState) : "Detail pending" },
      { label: "Approval", value: detail ? splitEnumLabel(detail.approvalState) : "Detail pending" },
      { label: "Sign-off", value: detail ? buildSignoffSummary(detail) : "Detail pending" },
      { label: "Report pack", value: detail ? buildReportPackSummary(detail) : "Detail pending" },
      { label: "Period lock", value: detail ? buildPeriodLockSummary(detail) : "Detail pending" },
      { label: "Close package", value: detail ? buildClosePackageSummary(detail.closePackage) : "Detail pending" },
      { label: "Close evidence", value: detail ? buildCloseEvidenceSummary(detail) : "Detail pending" },
      { label: "Latest audit", value: detail ? buildLatestAuditSummary(detail) : "Detail pending" },
      { label: "Break cases", value: detail ? String(detail.breakCases.length) : "Detail pending" },
      { label: "Blockers", value: String(blockerCount) }
    ]
  };
}

function buildSignoffSummary(detail: OperationsContinuityWorkflow): string {
  const latestApproval = [...detail.approvals].sort(compareApprovalsByDecisionTime)[0] ?? null;
  if (!latestApproval) {
    return "No sign-off decision recorded";
  }

  const actor = latestApproval.reviewer?.trim() || latestApproval.operator?.trim() || "unknown reviewer";
  const timestamp = latestApproval.decidedAtUtc ?? latestApproval.submittedAtUtc;
  const decisionLabel = splitEnumLabel(latestApproval.status);
  const timestampLabel = timestamp ? ` at ${formatDate(timestamp)}` : "";
  const rationale = latestApproval.rationale?.trim();
  return rationale
    ? `${decisionLabel} by ${actor}${timestampLabel}: ${rationale}`
    : `${decisionLabel} by ${actor}${timestampLabel}`;
}

function buildReportPackSummary(detail: OperationsContinuityWorkflow): string {
  const readiness = detail.reportPackReadiness;
  if (readiness.isReady) {
    return `Ready${readiness.reportPackId ? `: ${readiness.reportPackId}` : ""}`;
  }

  return readiness.blockingReason?.trim()
    ? `Blocked: ${readiness.blockingReason}`
    : "Blocked until close evidence is complete";
}

function buildPeriodLockSummary(detail: OperationsContinuityWorkflow): string {
  const latestReopen = selectLatestTimelineEvent(detail.timeline, "workflow-reopened");
  if (detail.status === "Closed") {
    return "Locked after close publication";
  }

  if (latestReopen) {
    return `Reopened by ${latestReopen.actor || "unknown actor"} at ${formatDate(latestReopen.occurredAtUtc)}`;
  }

  return "Open until close publication";
}

function buildClosePackageSummary(closePackage: OperationsClosePackagePublication | null): string {
  if (!closePackage) {
    return "Not published";
  }

  return `${closePackage.closePackageId} signed by ${closePackage.publishedBy || "unknown"} at ${formatDate(closePackage.publishedAtUtc)}`;
}

function buildCloseEvidenceSummary(detail: OperationsContinuityWorkflow): string {
  const evidenceCount = detail.evidenceLinks.length
    + detail.reportPackReadiness.evidenceLinks.length
    + detail.approvals.reduce((total, approval) => total + approval.evidenceLinks.length, 0)
    + detail.breakCases.reduce((total, breakCase) => total + breakCase.evidenceLinks.length, 0);

  return evidenceCount === 0
    ? "No close evidence links"
    : `${evidenceCount} close evidence link${evidenceCount === 1 ? "" : "s"}`;
}

function buildLatestAuditSummary(detail: OperationsContinuityWorkflow): string {
  const latest = [...detail.timeline].sort((left, right) => right.occurredAtUtc.localeCompare(left.occurredAtUtc))[0] ?? null;
  if (!latest) {
    return "No audit event recorded";
  }

  const hashLabel = latest.currentHash ? ` / ${latest.currentHash.slice(0, 12)}` : "";
  return `${eventLabel(latest.eventType)} ${latest.auditId.slice(0, 8)}${hashLabel}`;
}

function compareApprovalsByDecisionTime(
  left: OperationsContinuityWorkflow["approvals"][number],
  right: OperationsContinuityWorkflow["approvals"][number]
): number {
  const leftTime = left.decidedAtUtc ?? left.submittedAtUtc ?? "";
  const rightTime = right.decidedAtUtc ?? right.submittedAtUtc ?? "";
  return rightTime.localeCompare(leftTime);
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
            ? "This workflow is closed and locked; use the governed reopen command with admin incident metadata before applying further transitions."
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

function mapWorkflowRow(
  workflow: OperationsContinuityWorkflowSummary,
  selectedWorkflowId: string | null
): OperationsContinuityWorkflowRow {
  const blockerCount = collectGateBlockers(workflow.gates).length;
  const passedGateCount = workflow.gates.filter((gate) => gate.status === "Passed").length;
  const tone = statusTone(workflow.status);
  return {
    id: workflow.workflowId,
    title: `${workflow.periodId} close`,
    subtitle: `${workflow.brokerSource || "Broker source pending"} / ${workflow.fundAccountId}`,
    statusLabel: statusLabel(workflow.status),
    statusTone: tone,
    gatesLabel: `${passedGateCount}/${workflow.gates.length} gates passed`,
    blockersLabel: blockerCount === 0 ? "No blockers" : `${blockerCount} blocker${blockerCount === 1 ? "" : "s"}`,
    updatedLabel: formatDate(workflow.updatedAtUtc),
    detailPanelId: OPERATIONS_CONTINUITY_WORKFLOW_DETAIL_PANEL_ID,
    expanded: workflow.workflowId === selectedWorkflowId,
    rowClassName: workflowRowClassName(tone),
    ariaLabel: `${workflow.periodId} operations continuity workflow, ${statusLabel(workflow.status)}, ${passedGateCount} of ${workflow.gates.length} gates passed`
  };
}

function workflowRowClassName(tone: OperationsContinuityTone): OperationsContinuityRowClassName {
  switch (tone) {
    case "ready":
      return "bg-success/5";
    case "review":
      return "bg-warning/5";
    case "blocked":
      return "bg-danger/5";
    default:
      return undefined;
  }
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

function buildReconciliationLaneRows(lanes: OperationsReconciliationLaneSummary[]): OperationsContinuityReconciliationLaneRow[] {
  return [...lanes]
    .sort(compareReconciliationLanes)
    .map((lane) => {
      const href = normalizeLocalWorkstationRoute(lane.routeHint) ?? null;
      const evidenceCount = lane.evidenceLinks.length;
      const actions = (lane.requiredActions ?? [])
        .map((action) => action.trim())
        .filter(Boolean);

      return {
        id: lane.laneId,
        label: lane.label || splitEnumLabel(lane.laneId),
        statusLabel: evidenceStatusLabel(lane.status),
        statusTone: lane.isReady ? "ready" : evidenceStatusTone(lane.status),
        breakCountLabel: lane.breakCount === 0 ? "No open breaks" : `${lane.breakCount} open break${lane.breakCount === 1 ? "" : "s"}`,
        summary: lane.summary?.trim() || "No reconciliation lane summary returned.",
        evidenceLabel: evidenceCount === 0 ? "No retained evidence" : `${evidenceCount} evidence link${evidenceCount === 1 ? "" : "s"}`,
        routeHref: href,
        routeLabel: href ? "Open lane" : "No local route",
        requiredActionsLabel: actions.length === 0 ? "No required actions" : actions.join("; "),
        ariaLabel: `${lane.label || lane.laneId}, ${evidenceStatusLabel(lane.status)}, ${lane.breakCount} open breaks`
      };
    });
}

function compareReconciliationLanes(
  left: OperationsReconciliationLaneSummary,
  right: OperationsReconciliationLaneSummary
): number {
  const leftIndex = FINANCIAL_OPERATIONS_RECONCILIATION_LANE_ORDER.indexOf(left.laneId);
  const rightIndex = FINANCIAL_OPERATIONS_RECONCILIATION_LANE_ORDER.indexOf(right.laneId);
  const normalizedLeft = leftIndex < 0 ? Number.MAX_SAFE_INTEGER : leftIndex;
  const normalizedRight = rightIndex < 0 ? Number.MAX_SAFE_INTEGER : rightIndex;
  return normalizedLeft - normalizedRight || left.label.localeCompare(right.label);
}

function buildOperationsDashboardViewModel(
  dashboard: OperationsDashboardSummary | null,
  loading: boolean
): OperationsContinuityDashboardViewModel {
  if (!dashboard) {
    return {
      title: "Operational dashboard",
      statusLabel: loading ? "Loading" : "Missing",
      statusTone: loading ? "neutral" : "review",
      stageLabel: loading ? "Loading core flow" : "Core flow unavailable",
      summaryLabel: loading
        ? "Loading source-backed Financial Operations dashboard summary."
        : "The shared operations API did not return a Financial Operations dashboard summary.",
      metricCountLabel: "0/0 metrics ready",
      evidenceLabel: "No retained evidence",
      requiredActionsLabel: loading ? "Loading required actions" : "Return a shared dashboard summary before relying on local dashboard state.",
      metricsEmptyText: loading ? "Loading operational dashboard metrics..." : "No operational dashboard metrics returned by the shared operations API.",
      metrics: []
    };
  }

  const evidenceCount = dashboard.evidenceLinks.length;
  const requiredActions = (dashboard.requiredActions ?? [])
    .map((action) => action.trim())
    .filter(Boolean);
  const metrics = [...dashboard.metrics]
    .sort(compareDashboardMetrics)
    .map(mapDashboardMetricRow);

  return {
    title: "Operational dashboard",
    statusLabel: evidenceStatusLabel(dashboard.status),
    statusTone: dashboard.isReady ? "ready" : evidenceStatusTone(dashboard.status),
    stageLabel: `Core flow: ${dashboard.stage || "Stage pending"}`,
    summaryLabel: dashboard.summary?.trim() || "No operational dashboard summary returned.",
    metricCountLabel: `${dashboard.readyMetricCount}/${dashboard.totalMetricCount} metrics ready`,
    evidenceLabel: evidenceCount === 0 ? "No retained evidence" : `${evidenceCount} retained evidence link${evidenceCount === 1 ? "" : "s"}`,
    requiredActionsLabel: requiredActions.length === 0 ? "No required actions" : requiredActions.join("; "),
    metricsEmptyText: "No operational dashboard metrics returned by the shared operations API.",
    metrics
  };
}

function mapDashboardMetricRow(metric: OperationsDashboardSummary["metrics"][number]): OperationsContinuityDashboardMetricRow {
  const routeHref = normalizeLocalWorkstationRoute(metric.routeHint) ?? null;
  const evidenceCount = metric.evidenceLinks.length;
  const actions = (metric.requiredActions ?? [])
    .map((action) => action.trim())
    .filter(Boolean);

  return {
    id: metric.metricId,
    label: metric.label || splitEnumLabel(metric.metricId),
    valueLabel: metric.value?.trim() || "Value pending",
    statusLabel: evidenceStatusLabel(metric.status),
    statusTone: evidenceStatusTone(metric.status),
    detail: metric.detail?.trim() || "No metric detail returned.",
    evidenceLabel: evidenceCount === 0 ? "No retained evidence" : `${evidenceCount} evidence link${evidenceCount === 1 ? "" : "s"}`,
    requiredActionsLabel: actions.length === 0 ? "No required actions" : actions.join("; "),
    routeHref,
    routeLabel: routeHref ? "Open metric" : "No local route",
    ariaLabel: `${metric.label || metric.metricId}, ${evidenceStatusLabel(metric.status)}, ${metric.value || "value pending"}`
  };
}

function compareDashboardMetrics(
  left: OperationsDashboardSummary["metrics"][number],
  right: OperationsDashboardSummary["metrics"][number]
): number {
  const leftIndex = FINANCIAL_OPERATIONS_DASHBOARD_METRIC_ORDER.indexOf(left.metricId);
  const rightIndex = FINANCIAL_OPERATIONS_DASHBOARD_METRIC_ORDER.indexOf(right.metricId);
  const normalizedLeft = leftIndex < 0 ? Number.MAX_SAFE_INTEGER : leftIndex;
  const normalizedRight = rightIndex < 0 ? Number.MAX_SAFE_INTEGER : rightIndex;
  return normalizedLeft - normalizedRight || left.label.localeCompare(right.label);
}

function buildEvidencePackageRows(packages: OperationsEvidencePackageSummary[]): OperationsContinuityEvidencePackageRow[] {
  return [...packages]
    .sort(compareEvidencePackages)
    .map((packageDto) => {
      const routeHref = normalizeLocalWorkstationRoute(packageDto.routeHint) ?? null;
      const evidenceCount = Math.max(packageDto.evidenceLinkCount, packageDto.evidenceLinks.length);
      const actions = (packageDto.requiredActions ?? [])
        .map((action) => action.trim())
        .filter(Boolean);

      return {
        id: packageDto.packageId,
        label: packageDto.label || splitEnumLabel(packageDto.packageId),
        statusLabel: evidenceStatusLabel(packageDto.status),
        statusTone: packageDto.isReady ? "ready" : evidenceStatusTone(packageDto.status),
        readinessLabel: `${packageDto.completeCategoryCount}/${packageDto.requiredCategoryCount} categories complete`,
        summary: packageDto.summary?.trim() || "No evidence package summary returned.",
        evidenceLabel: evidenceCount === 0 ? "No retained evidence" : `${evidenceCount} evidence link${evidenceCount === 1 ? "" : "s"}`,
        requiredActionsLabel: actions.length === 0 ? "No required actions" : actions.join("; "),
        routeHref,
        routeLabel: routeHref ? "Open package" : "No local route",
        ariaLabel: `${packageDto.label || packageDto.packageId}, ${evidenceStatusLabel(packageDto.status)}, ${packageDto.completeCategoryCount} of ${packageDto.requiredCategoryCount} categories complete`
      };
    });
}

function compareEvidencePackages(
  left: OperationsEvidencePackageSummary,
  right: OperationsEvidencePackageSummary
): number {
  const leftIndex = FINANCIAL_OPERATIONS_EVIDENCE_PACKAGE_ORDER.indexOf(evidencePackageOrderKey(left));
  const rightIndex = FINANCIAL_OPERATIONS_EVIDENCE_PACKAGE_ORDER.indexOf(evidencePackageOrderKey(right));
  const normalizedLeft = leftIndex < 0 ? Number.MAX_SAFE_INTEGER : leftIndex;
  const normalizedRight = rightIndex < 0 ? Number.MAX_SAFE_INTEGER : rightIndex;
  return normalizedLeft - normalizedRight || left.label.localeCompare(right.label);
}

function evidencePackageOrderKey(packageDto: OperationsEvidencePackageSummary): string {
  const text = `${packageDto.packageId} ${packageDto.label}`.toLowerCase();
  if (text.includes("accounting-record") || text.includes("accounting record")) {
    return "accounting-record";
  }

  if (text.includes("report-pack") || text.includes("report pack")) {
    return "report-pack";
  }

  if (text.includes("close-package") || text.includes("close package")) {
    return "close-package";
  }

  if (text.includes("audit-support") || text.includes("audit support")) {
    return "audit-support";
  }

  return packageDto.packageId;
}

function buildBreakCaseRows(breakCases: OperationsBreakCase[]): OperationsContinuityBreakCaseRow[] {
  return [...breakCases]
    .sort(compareBreakCases)
    .map((breakCase) => {
      const statusLabel = splitEnumLabel(breakCase.status || "Open");
      const ownerLabel = breakCase.owner?.trim() || "Unassigned";
      const dueLabel = breakCase.dueDate ? formatDate(breakCase.dueDate) : "No due date";
      const slaLabel = buildSlaLabel(breakCase);
      const escalationLabel = buildEscalationLabel(breakCase);
      const varianceLabel = buildVarianceLabel(breakCase);
      const materialityLabel = buildMaterialityLabel(breakCase);
      const sourceLabel = buildBreakSourceLabel(breakCase);
      const rootCauseLabel = buildRootCauseLabel(breakCase);
      const symbolLabel = breakCase.symbol?.trim() || breakCase.securityId?.trim() || "No security";
      const actionLabel = breakCase.suggestedAction?.trim() || "No suggested action";
      const approvalLabel = buildApprovalLabel(breakCase);
      const blockedOutputsLabel = buildBlockedOutputsLabel(breakCase);
      const evidenceCount = breakCase.evidenceLinks.length;
      return {
        id: breakCase.breakId,
        caseLabel: breakCase.breakId,
        categoryLabel: `${breakCase.category || "Uncategorized"} / ${breakCase.checkId || "check pending"}`,
        statusLabel,
        statusTone: breakStatusTone(breakCase.status),
        severityLabel: breakCase.severity?.trim() || "Review",
        severityTone: severityTone(breakCase.severity),
        ownerLabel,
        dueLabel,
        slaLabel,
        escalationLabel,
        varianceLabel,
        materialityLabel,
        sourceLabel,
        rootCauseLabel,
        symbolLabel,
        evidenceLabel: evidenceCount === 0 ? "No retained evidence" : `${evidenceCount} retained evidence link${evidenceCount === 1 ? "" : "s"}`,
        approvalLabel,
        blockedOutputsLabel,
        actionLabel,
        ariaLabel: `${breakCase.breakId}, ${statusLabel}, ${ownerLabel}, ${slaLabel}, ${blockedOutputsLabel}`
      };
    });
}

function compareBreakCases(left: OperationsBreakCase, right: OperationsBreakCase): number {
  const leftPriority = breakCasePriority(left);
  const rightPriority = breakCasePriority(right);
  if (leftPriority !== rightPriority) {
    return rightPriority - leftPriority;
  }

  return (left.dueDate ?? "9999-12-31").localeCompare(right.dueDate ?? "9999-12-31")
    || left.breakId.localeCompare(right.breakId);
}

function breakCasePriority(breakCase: OperationsBreakCase): number {
  const status = breakCase.status?.trim().toLowerCase() ?? "";
  const severity = breakCase.severity?.trim().toLowerCase() ?? "";
  const openScore = status === "resolved" || status === "dismissed" || status === "matched" ? 0 : 10;
  const severityScore = severity === "critical" || severity === "error"
    ? 4
    : severity === "warning" || severity === "warn"
      ? 2
      : 1;
  const ownerScore = breakCase.owner?.trim() ? 0 : 1;
  return openScore + severityScore + ownerScore;
}

function buildEscalationLabel(breakCase: OperationsBreakCase): string {
  const level = breakCase.escalationLevel?.trim();
  const reason = breakCase.escalationReason?.trim();
  const timestamp = breakCase.escalatedAtUtc ? ` at ${formatDate(breakCase.escalatedAtUtc)}` : "";
  if (level && reason) {
    return `${level}${timestamp}: ${reason}`;
  }

  if (level) {
    return `${level}${timestamp}`;
  }

  return "No escalation recorded";
}

function buildVarianceLabel(breakCase: OperationsBreakCase): string {
  const expected = formatDecimal(breakCase.expectedAmount);
  const actual = formatDecimal(breakCase.actualAmount);
  const variance = formatDecimal(breakCase.variance);
  if (expected === "Not recorded" && actual === "Not recorded" && variance === "Not recorded") {
    return "No amount variance";
  }

  return `Expected ${expected} / Actual ${actual} / Variance ${variance}`;
}

function buildBreakSourceLabel(breakCase: OperationsBreakCase): string {
  const expected = breakCase.expectedSource?.trim() || "expected source pending";
  const actual = breakCase.actualSource?.trim() || "actual source pending";
  return `${expected} vs ${actual}`;
}

function buildSlaLabel(breakCase: OperationsBreakCase): string {
  const state = breakCase.slaState?.trim();
  const due = breakCase.slaDueAtUtc ? formatDate(breakCase.slaDueAtUtc) : null;
  if (state && due) {
    return `SLA ${splitEnumLabel(state)} due ${due}`;
  }

  if (state) {
    return `SLA ${splitEnumLabel(state)}`;
  }

  return "SLA not recorded";
}

function buildMaterialityLabel(breakCase: OperationsBreakCase): string {
  return typeof breakCase.materiality === "number" && Number.isFinite(breakCase.materiality)
    ? `Materiality ${formatDecimal(breakCase.materiality)}`
    : "Materiality not recorded";
}

function buildRootCauseLabel(breakCase: OperationsBreakCase): string {
  return breakCase.rootCauseCode?.trim()
    ? `Root cause ${splitEnumLabel(breakCase.rootCauseCode.trim())}`
    : "Root cause pending";
}

function buildApprovalLabel(breakCase: OperationsBreakCase): string {
  return breakCase.approvalState?.trim()
    ? `Approval ${splitEnumLabel(breakCase.approvalState.trim())}`
    : "Approval state pending";
}

function buildBlockedOutputsLabel(breakCase: OperationsBreakCase): string {
  const outputs = (breakCase.blockedOutputs ?? [])
    .map((output) => output.trim())
    .filter(Boolean);
  return outputs.length > 0
    ? `Blocks ${outputs.join(", ")}`
    : "No blocked outputs";
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

function selectLatestTimelineEvent(
  timeline: OperationsTimelineEntry[],
  eventType: string
): OperationsTimelineEntry | null {
  const normalized = eventType.trim().toLowerCase();
  return timeline
    .filter((entry) => entry.eventType.trim().toLowerCase() === normalized)
    .sort((left, right) => right.occurredAtUtc.localeCompare(left.occurredAtUtc))[0] ?? null;
}

function buildChecklistRows(tasks: OperationsCloseChecklistTask[]): OperationsContinuityChecklistRow[] {
  return tasks.map((task) => {
    const remediationHref = normalizeLocalWorkstationRoute(task.remediationRoute) ?? null;
    const acknowledged = task.acknowledgedAtUtc
      ? `Acknowledged by ${task.acknowledgedBy?.trim() || "unknown"} at ${formatDate(task.acknowledgedAtUtc)}`
      : task.canAcknowledge
        ? "Ready for acknowledgement"
        : task.blockingReason?.trim() || "Acknowledgement blocked until required evidence is complete";

    return {
      id: task.taskId,
      label: task.label || gateLabel(task.gate),
      gateLabel: gateLabel(task.gate),
      ownerLabel: task.owner?.trim() || "Owner pending",
      statusLabel: splitEnumLabel(task.status || "Unknown"),
      statusTone: checklistStatusTone(task.status),
      dueLabel: task.dueDate ? formatDate(task.dueDate) : "No due date",
      expiresLabel: task.expiresOn ? formatDate(task.expiresOn) : "No expiration",
      requiredEvidence: task.requiredEvidence?.trim() || task.evidencePointer?.trim() || "Required evidence pending",
      approvalLabel: task.requiredApprovalCount === 0
        ? "No control approvals required"
        : `${task.requiredApprovalCount} control approval${task.requiredApprovalCount === 1 ? "" : "s"} required`,
      evidenceLabel: task.evidencePointer?.trim() || "Evidence pointer pending",
      remediationHref,
      remediationLabel: remediationHref ? "Open remediation" : "No remediation route",
      acknowledgementLabel: acknowledged,
      ariaLabel: `${task.label || gateLabel(task.gate)} checklist task, ${splitEnumLabel(task.status || "Unknown")}, ${task.owner?.trim() || "owner pending"}`
    };
  });
}

function buildCloseGovernanceViewModel(
  workflow: OperationsContinuityWorkflow | null,
  loading: boolean
): OperationsContinuityCloseGovernanceViewModel {
  if (!workflow) {
    return {
      title: "Period lock and reopen evidence",
      statusLabel: loading ? "Loading" : "No workflow",
      statusTone: "neutral",
      lockLabel: loading ? "Lock state loading" : "No close workflow selected",
      lockDetail: loading
        ? "Loading the selected workflow lock state from Operations Continuity."
        : "Select an operations continuity workflow to inspect close-lock and reopen evidence.",
      closeAuditLabel: "No close audit event",
      closePackageLabel: "No close package published",
      reopenAuditLabel: "No governed reopen recorded",
      reopenIncidentLabel: "No incident correlation",
      reopenEvidenceLabel: "No reopen evidence links",
      reopenRationaleLabel: "No reopen rationale recorded",
      commandGuardLabel: "Workflow command availability is server-owned."
    };
  }

  const latestClose = selectLatestTimelineEvent(workflow.timeline, "workflow-closed");
  const latestReopen = selectLatestTimelineEvent(workflow.timeline, "workflow-reopened");
  const closePackage = workflow.closePackage;
  const isClosed = workflow.status === "Closed";
  const statusLabel = isClosed ? "Locked" : latestReopen ? "Reopened" : "Open";
  const statusTone: OperationsContinuityTone = isClosed ? "ready" : latestReopen ? "review" : "neutral";
  const lockLabel = isClosed
    ? "Closed period is locked"
    : latestReopen
      ? "Period reopened under governance"
      : "Period remains open";
  const lockDetail = isClosed
    ? "Closed operations continuity workflows are immutable until a governed admin reopen command succeeds."
    : latestReopen
      ? "The workflow is open after a governed reopen; subsequent close evidence must retain the reopen audit trail."
      : "Close publication has not locked this account period yet.";
  const commandGuardLabel = isClosed
    ? "Mutations must use the governed reopen command with incident metadata, justification, approval reference, and impact summary."
    : latestReopen
      ? "Close can proceed only after refreshed gate evidence and the reopen incident trail remain linked."
      : "Close and reopen command decisions remain enforced by the shared workflow API.";

  return {
    title: "Period lock and reopen evidence",
    statusLabel,
    statusTone,
    lockLabel,
    lockDetail,
    closeAuditLabel: latestClose
      ? `${eventLabel(latestClose.eventType)} ${latestClose.auditId.slice(0, 8)} / ${latestClose.currentHash.slice(0, 12)}`
      : "No close audit event",
    closePackageLabel: closePackage
      ? `${closePackage.closePackageId} / ${closePackage.evidenceHash.slice(0, 12)}`
      : "No close package published",
    reopenAuditLabel: latestReopen
      ? `${eventLabel(latestReopen.eventType)} by ${latestReopen.actor || "unknown actor"} at ${formatDate(latestReopen.occurredAtUtc)} / ${latestReopen.currentHash.slice(0, 12)}`
      : "No governed reopen recorded",
    reopenIncidentLabel: latestReopen?.correlationId?.trim()
      ? `Incident ${latestReopen.correlationId}`
      : "No incident correlation",
    reopenEvidenceLabel: latestReopen && latestReopen.references.length > 0
      ? `${latestReopen.references.length} reopen evidence link${latestReopen.references.length === 1 ? "" : "s"}`
      : "No reopen evidence links",
    reopenRationaleLabel: latestReopen?.rationale?.trim() || "No reopen rationale recorded",
    commandGuardLabel
  };
}

function buildCloseCockpitQuery(workflow: OperationsContinuityWorkflowSummary): PrivateCapitalCloseCockpitQuery {
  return {
    fundAccountId: workflow.fundAccountId,
    periodId: workflow.periodId
  };
}

const FINANCIAL_OPERATIONS_PROOF_LANE_LABELS: Record<string, string> = {
  "partner-capital-tie-outs": "partner tie-outs",
  "expense-fee-allocation": "expense/fee allocation",
  "nav-support": "NAV support",
  "close-package": "evidence package",
  "period-lock": "period lock"
};

const FINANCIAL_OPERATIONS_RECONCILIATION_LANE_ORDER = [
  "cash-reconciliation",
  "position-reconciliation",
  "trade-reconciliation",
  "income-reconciliation",
  "mbs-factor-reconciliation",
  "bank-reconciliation",
  "gl-reconciliation"
];

const FINANCIAL_OPERATIONS_DASHBOARD_METRIC_ORDER = [
  "receive-activity",
  "match-records",
  "resolve-exceptions",
  "approve-results",
  "produce-evidence",
  "close-support"
];

const FINANCIAL_OPERATIONS_EVIDENCE_PACKAGE_ORDER = [
  "accounting-record",
  "report-pack",
  "close-package",
  "audit-support"
];

function buildCloseCockpitViewModel(
  cockpit: PrivateCapitalCloseCockpit | null,
  loading: boolean,
  error: string | null,
  selectedSummary: OperationsContinuityWorkflowSummary | null
): OperationsContinuityCloseCockpitViewModel {
  if (!cockpit) {
    return {
      title: "Private-capital close cockpit",
      statusLabel: loading ? "Loading" : error ? "Unavailable" : "No cockpit",
      statusTone: error ? "blocked" : "neutral",
      summaryLabel: loading
        ? "Loading private-capital close evidence from the shared operations API."
        : error
          ? "Private-capital close evidence is unavailable."
          : "No private-capital close cockpit data is available for this workstation context.",
      scopeLabel: selectedSummary
        ? `Selected ${selectedSummary.periodId} / ${selectedSummary.fundAccountId}`
        : "No close scope selected",
      freshnessLabel: "Projection pending",
      readinessLabel: "Readiness unavailable",
      laneCountLabel: "No cockpit lanes",
      proofLaneSummaryLabel: loading
        ? "Proof lanes loading"
        : error
          ? "Proof lanes unavailable"
          : "No Financial Operations proof lanes",
      workflowCountLabel: "No close workflows",
      blockerCountLabel: "No blockers loaded",
      fundEventCountLabel: "No fund events loaded",
      reportOutputCountLabel: "No report outputs loaded",
      nextActionLabel: "No cockpit action available",
      nextActionHref: null,
      nextActionDisabled: true,
      nextActionDisabledReason: loading
        ? "Wait for the close cockpit to finish loading."
        : error
          ? "Resolve the close-cockpit loading error before taking a cockpit action."
          : "No close cockpit action was returned by the shared operations API.",
      liveCapabilitiesLabel: "No live close capabilities loaded",
      plannedCapabilitiesLabel: "No planned close capabilities loaded",
      errorText: error,
      lanesEmptyText: loading
        ? "Loading private-capital cockpit lanes..."
        : error
          ? "Private-capital cockpit lanes could not be loaded."
          : "No private-capital cockpit lanes returned by the shared operations API.",
      workflowsEmptyText: loading
        ? "Loading private-capital close workflows..."
        : error
          ? "Private-capital close workflows could not be loaded."
          : "No private-capital close workflows returned by the shared operations API.",
      approvalHistoryEmptyText: loading
        ? "Loading private-capital approval history..."
        : error
          ? "Private-capital approval history could not be loaded."
          : "No private-capital approval history returned by the shared operations API.",
      navSupportPackagesEmptyText: loading
        ? "Loading NAV support packages..."
        : error
          ? "NAV support packages could not be loaded."
          : "No NAV support packages returned by the shared operations API.",
      lanes: [],
      workflows: [],
      approvalHistory: [],
      navSupportPackages: []
    };
  }

  const nextAction = buildCockpitNextAction(cockpit.nextActions, loading, error);
  const lanes = cockpit.lanes.map(mapCloseCockpitLaneRow);
  const workflows = [...cockpit.workflows]
    .sort(compareCloseCockpitWorkflows)
    .map(mapCloseCockpitWorkflowRow);
  const approvalHistory = [...(cockpit.approvalHistory ?? [])]
    .sort(compareCloseCockpitApprovals)
    .map(mapCloseCockpitApprovalRow);
  const navSupportPackages = [...(cockpit.navSupportPackages ?? [])]
    .sort(compareNavSupportPackages)
    .map(mapNavSupportPackageRow);
  const blockerCount = cockpit.blockers.length;

  return {
    title: "Private-capital close cockpit",
    statusLabel: evidenceStatusLabel(cockpit.overallStatus),
    statusTone: cockpitStatusTone(cockpit),
    summaryLabel: `${cockpit.readyLaneCount}/${cockpit.lanes.length} lanes ready across ${cockpit.workflowCount} close workflow${cockpit.workflowCount === 1 ? "" : "s"}.`,
    scopeLabel: buildCloseCockpitScopeLabel(cockpit),
    freshnessLabel: `Projected ${formatDate(cockpit.projectedAtUtc)}`,
    readinessLabel: `${formatPercent(cockpit.readinessScore)} readiness`,
    laneCountLabel: `${cockpit.readyLaneCount} ready / ${cockpit.blockedLaneCount} blocked lane${cockpit.lanes.length === 1 ? "" : "s"}`,
    proofLaneSummaryLabel: buildFinancialOperationsProofLaneSummary(cockpit.lanes),
    workflowCountLabel: `${cockpit.workflowCount} close workflow${cockpit.workflowCount === 1 ? "" : "s"}`,
    blockerCountLabel: blockerCount === 0 ? "No close blockers" : `${blockerCount} close blocker${blockerCount === 1 ? "" : "s"}`,
    fundEventCountLabel: `${cockpit.fundEventCount} fund event${cockpit.fundEventCount === 1 ? "" : "s"}`,
    reportOutputCountLabel: `${cockpit.deliveredReportOutputCount}/${cockpit.reportOutputCount} report outputs delivered`,
    nextActionLabel: nextAction.label,
    nextActionHref: nextAction.href,
    nextActionDisabled: nextAction.disabled,
    nextActionDisabledReason: nextAction.disabledReason,
    liveCapabilitiesLabel: listLabel(cockpit.liveCapabilities, "No live close capabilities reported"),
    plannedCapabilitiesLabel: listLabel(cockpit.plannedCapabilities, "No planned close capabilities reported"),
    errorText: error,
    lanesEmptyText: "No private-capital cockpit lanes returned by the shared operations API.",
    workflowsEmptyText: "No private-capital close workflows returned by the shared operations API.",
    approvalHistoryEmptyText: "No private-capital approval history returned by the shared operations API.",
    navSupportPackagesEmptyText: "No NAV support packages returned by the shared operations API.",
    lanes,
    workflows,
    approvalHistory,
    navSupportPackages
  };
}

function buildFinancialOperationsProofLaneSummary(lanes: PrivateCapitalCloseCockpitLane[]): string {
  const proofLanes = Object.entries(FINANCIAL_OPERATIONS_PROOF_LANE_LABELS)
    .map(([laneId, label]) => {
      const lane = lanes.find((candidate) => candidate.laneId === laneId);
      return lane ? { label, lane } : null;
    })
    .filter((item): item is { label: string; lane: PrivateCapitalCloseCockpitLane } => item !== null);

  if (proofLanes.length === 0) {
    return "No Financial Operations proof lanes returned";
  }

  const readyCount = proofLanes.filter((item) => item.lane.isReady).length;
  const reviewLabels = proofLanes
    .filter((item) => !item.lane.isReady)
    .map((item) => item.label);

  return reviewLabels.length === 0
    ? `${readyCount}/${proofLanes.length} proof lanes ready`
    : `${readyCount}/${proofLanes.length} proof lanes ready; review ${reviewLabels.join(", ")}`;
}

function mapCloseCockpitLaneRow(lane: PrivateCapitalCloseCockpitLane): OperationsContinuityCloseCockpitLaneRow {
  const href = normalizeLocalWorkstationRoute(lane.route) ?? null;
  const evidenceCount = Math.max(lane.evidenceLinkCount, lane.evidenceLinks.length);
  const actions = lane.requiredActions
    .map((action) => action.trim())
    .filter(Boolean);

  return {
    id: lane.laneId,
    label: lane.label || splitEnumLabel(lane.laneId),
    statusLabel: evidenceStatusLabel(lane.status),
    statusTone: lane.isReady ? "ready" : evidenceStatusTone(lane.status),
    summary: lane.summary?.trim() || "No lane summary returned.",
    evidenceLabel: evidenceCount === 0 ? "No retained evidence" : `${evidenceCount} evidence link${evidenceCount === 1 ? "" : "s"}`,
    routeHref: href,
    routeLabel: href ? "Open lane" : "No local route",
    requiredActionsLabel: actions.length === 0 ? "No required actions" : actions.join("; "),
    ariaLabel: `${lane.label || lane.laneId}, ${evidenceStatusLabel(lane.status)}, ${evidenceCount} evidence links`
  };
}

function mapCloseCockpitWorkflowRow(
  workflow: PrivateCapitalCloseCockpitWorkflow
): OperationsContinuityCloseCockpitWorkflowRow {
  const workflowHref = normalizeLocalWorkstationRoute(workflow.workflowRoute) ?? null;
  const packageHref = normalizeLocalWorkstationRoute(workflow.closePackageRoute) ?? null;
  const readinessTone: OperationsContinuityTone = workflow.isReadyToClose
    ? "ready"
    : workflow.blockerCount > 0
      ? "blocked"
      : "review";

  return {
    id: workflow.workflowId,
    periodLabel: `${workflow.periodId} / ${workflow.fundAccountId}`,
    statusLabel: statusLabel(workflow.status),
    statusTone: statusTone(workflow.status),
    readinessLabel: `${formatPercent(workflow.closeReadinessScore)} ready`,
    readinessTone,
    blockersLabel: workflow.blockerCount === 0 ? "No blockers" : `${workflow.blockerCount} blocker${workflow.blockerCount === 1 ? "" : "s"}`,
    checklistLabel: workflow.openChecklistCount === 0 ? "Checklist clear" : `${workflow.openChecklistCount} open checklist item${workflow.openChecklistCount === 1 ? "" : "s"}`,
    packageHref,
    packageLabel: workflow.closePackageId?.trim() || "Close package pending",
    workflowHref,
    ariaLabel: `${workflow.periodId} private-capital close workflow, ${statusLabel(workflow.status)}, ${formatPercent(workflow.closeReadinessScore)} ready`
  };
}

function mapCloseCockpitApprovalRow(
  approval: PrivateCapitalCloseCockpitApproval
): OperationsContinuityCloseCockpitApprovalRow {
  const workflowHref = normalizeLocalWorkstationRoute(approval.workflowRoute) ?? null;
  const evidenceCount = Math.max(approval.evidenceLinkCount, approval.evidenceLinks.length);
  const operator = approval.operator?.trim();
  const reviewer = approval.reviewer?.trim();
  const actors = [
    reviewer ? `Reviewer ${reviewer}` : null,
    operator ? `Operator ${operator}` : null
  ].filter((value): value is string => value !== null);
  const decidedAt = approval.decidedAtUtc ?? approval.submittedAtUtc ?? null;

  return {
    id: approval.approvalId,
    workflowLabel: `${approval.periodId} / ${approval.fundAccountId}`,
    statusLabel: splitEnumLabel(approval.status),
    statusTone: approvalStatusTone(approval.status),
    actorLabel: actors.length === 0 ? "No approval actor returned" : actors.join(" / "),
    decidedLabel: decidedAt ? formatDate(decidedAt) : "Decision time pending",
    rationale: approval.rationale?.trim() || "No approval rationale returned.",
    evidenceLabel: evidenceCount === 0 ? "No retained evidence" : `${evidenceCount} evidence link${evidenceCount === 1 ? "" : "s"}`,
    workflowHref,
    routeLabel: workflowHref ? "Open workflow" : "No local route",
    ariaLabel: `${approval.periodId} approval ${approval.approvalId}, ${splitEnumLabel(approval.status)}`
  };
}

function mapNavSupportPackageRow(packageDto: PrivateCapitalNavSupportPackage): OperationsContinuityNavSupportPackageRow {
  const routeHref = normalizeLocalWorkstationRoute(packageDto.route) ?? null;
  const evidenceCount = Math.max(packageDto.evidenceLinkCount, packageDto.evidenceLinks.length);
  const components = packageDto.components ?? [];
  const readyComponentCount = components.filter((component) => component.isReady).length;
  const reviewComponents = components
    .filter((component) => !component.isReady)
    .map((component) => component.label || splitEnumLabel(component.componentId));
  const actions = (packageDto.requiredActions ?? [])
    .map((action) => action.trim())
    .filter(Boolean);

  return {
    id: packageDto.packageId,
    label: packageDto.label || "NAV support package",
    statusLabel: evidenceStatusLabel(packageDto.status),
    statusTone: packageDto.isReady ? "ready" : evidenceStatusTone(packageDto.status),
    summary: packageDto.summary?.trim() || "No NAV support package summary returned.",
    componentSummaryLabel: reviewComponents.length === 0
      ? `${readyComponentCount}/${components.length} components ready`
      : `${readyComponentCount}/${components.length} components ready; review ${reviewComponents.join(", ")}`,
    shadowNavLabel: packageDto.shadowNav === null || packageDto.shadowNav === undefined
      ? "Shadow NAV not retained"
      : `${formatCurrency(packageDto.shadowNav, packageDto.currency ?? "USD")} shadow NAV`,
    evidenceLabel: evidenceCount === 0 ? "No retained evidence" : `${evidenceCount} evidence link${evidenceCount === 1 ? "" : "s"}`,
    requiredActionsLabel: actions.length === 0 ? "No required actions" : actions.join("; "),
    routeHref,
    routeLabel: routeHref ? "Open package" : "No local route",
    ariaLabel: `${packageDto.label || packageDto.packageId}, ${evidenceStatusLabel(packageDto.status)}, ${readyComponentCount} of ${components.length} components ready`
  };
}

function buildCockpitNextAction(
  nextActions: OperationsNextAction[],
  loading: boolean,
  error: string | null
): { label: string; href: string | null; disabled: boolean; disabledReason: string | null } {
  const action = nextActions.find((candidate) => candidate.label.trim()) ?? null;
  const href = normalizeLocalWorkstationRoute(action?.route) ?? null;
  const disabledReason = loading
    ? "Wait for the close cockpit to finish loading."
    : error
      ? "Resolve the close-cockpit loading error before taking a cockpit action."
      : !action
        ? "No close cockpit action was returned by the shared operations API."
        : !href
          ? "The shared operations API did not provide a local workstation route for this cockpit action."
          : null;

  return {
    label: action?.label ?? "No cockpit action available",
    href,
    disabled: disabledReason !== null,
    disabledReason
  };
}

function buildCloseCockpitScopeLabel(cockpit: PrivateCapitalCloseCockpit): string {
  const scope = [
    cockpit.fundProfileId ? `Fund profile ${cockpit.fundProfileId}` : null,
    cockpit.ledgerBookId ? `Ledger book ${cockpit.ledgerBookId}` : null,
    cockpit.fundAccountId ? `Fund account ${cockpit.fundAccountId}` : null,
    cockpit.periodId ? `Period ${cockpit.periodId}` : null,
    cockpit.entityId ? `Entity ${cockpit.entityId}` : null
  ].filter(Boolean);

  return scope.length === 0 ? "All private-capital close scopes" : scope.join(" / ");
}

function cockpitStatusTone(cockpit: PrivateCapitalCloseCockpit): OperationsContinuityTone {
  if (cockpit.isReadyToClose) {
    return "ready";
  }

  if (cockpit.blockedLaneCount > 0 || cockpit.blockers.length > 0 || cockpit.overallStatus === "Blocked" || cockpit.overallStatus === "Missing") {
    return "blocked";
  }

  return evidenceStatusTone(cockpit.overallStatus);
}

function evidenceStatusTone(status: EvidenceStatus): OperationsContinuityTone {
  switch (status) {
    case "Ready":
      return "ready";
    case "Blocked":
    case "Missing":
      return "blocked";
    case "ReviewRequired":
    case "Stale":
      return "review";
    default:
      return "neutral";
  }
}

function evidenceStatusLabel(status: EvidenceStatus): string {
  return splitEnumLabel(status);
}

function compareCloseCockpitWorkflows(
  left: PrivateCapitalCloseCockpitWorkflow,
  right: PrivateCapitalCloseCockpitWorkflow
): number {
  return right.updatedAtUtc.localeCompare(left.updatedAtUtc)
    || right.periodId.localeCompare(left.periodId)
    || left.workflowId.localeCompare(right.workflowId);
}

function compareCloseCockpitApprovals(
  left: PrivateCapitalCloseCockpitApproval,
  right: PrivateCapitalCloseCockpitApproval
): number {
  const leftTime = left.decidedAtUtc ?? left.submittedAtUtc ?? "";
  const rightTime = right.decidedAtUtc ?? right.submittedAtUtc ?? "";
  return rightTime.localeCompare(leftTime)
    || left.periodId.localeCompare(right.periodId)
    || left.approvalId.localeCompare(right.approvalId);
}

function compareNavSupportPackages(
  left: PrivateCapitalNavSupportPackage,
  right: PrivateCapitalNavSupportPackage
): number {
  const leftReady = left.isReady ? 1 : 0;
  const rightReady = right.isReady ? 1 : 0;
  return rightReady - leftReady || left.packageId.localeCompare(right.packageId);
}

function approvalStatusTone(status: PrivateCapitalCloseCockpitApproval["status"]): OperationsContinuityTone {
  switch (status) {
    case "Approved":
      return "ready";
    case "Rejected":
      return "blocked";
    case "Submitted":
    case "ReviewerAssigned":
      return "review";
    default:
      return "neutral";
  }
}

function listLabel(values: string[], emptyLabel: string): string {
  const clean = values.map((value) => value.trim()).filter(Boolean);
  return clean.length === 0 ? emptyLabel : clean.join(", ");
}

function buildClosePackageViewModel(
  closePackage: OperationsClosePackagePublication | null,
  loading: boolean
): OperationsContinuityClosePackageViewModel {
  if (!closePackage) {
    return {
      title: "Close package",
      statusLabel: loading ? "Loading" : "Not published",
      statusTone: "neutral",
      packageIdLabel: loading ? "Package pending" : "No close package published",
      reportPackLabel: "Report pack pending",
      manifestLabel: "Retained manifest pending",
      manifestHref: null,
      evidenceHashLabel: "Evidence hash pending",
      publishedLabel: "Publication pending",
      signerLabel: "Signer pending",
      rationaleLabel: "Sign-off rationale pending",
      evidenceLabel: "No retained evidence links",
      approvalLabel: "No checklist control approvals"
    };
  }

  const manifestHref = normalizeLocalWorkstationRoute(closePackage.retainedManifestRoute) ?? null;
  const evidenceCount = closePackage.evidenceLinks.length;
  const approvalCount = closePackage.checklistControlApprovals.length;

  return {
    title: "Close package",
    statusLabel: "Published",
    statusTone: "ready",
    packageIdLabel: closePackage.closePackageId || "Package id pending",
    reportPackLabel: closePackage.reportPackId ? `Report pack ${closePackage.reportPackId}` : "Report pack pending",
    manifestLabel: closePackage.retainedManifestId || "Retained manifest pending",
    manifestHref,
    evidenceHashLabel: closePackage.evidenceHash || "Evidence hash pending",
    publishedLabel: `Published ${formatDate(closePackage.publishedAtUtc)}`,
    signerLabel: closePackage.publishedBy ? `Signed by ${closePackage.publishedBy}` : "Signer pending",
    rationaleLabel: closePackage.signOffRationale?.trim() || "Sign-off rationale pending",
    evidenceLabel: evidenceCount === 0
      ? "No retained evidence links"
      : `${evidenceCount} retained evidence link${evidenceCount === 1 ? "" : "s"}`,
    approvalLabel: approvalCount === 0
      ? "No checklist control approvals"
      : `${approvalCount} checklist control approval${approvalCount === 1 ? "" : "s"}`
  };
}

function buildAccountingRecordSummaryViewModel(
  summary: OperationsAccountingRecordSummary | null,
  loading: boolean
): OperationsAccountingRecordSummaryViewModel {
  if (!summary) {
    return {
      title: "Accounting record",
      summaryLabel: loading ? "Accounting-record evidence is loading." : "Accounting-record evidence is not available.",
      statusLabel: loading ? "Loading" : "Not available",
      statusTone: "neutral",
      recordIdLabel: loading ? "Record pending" : "No accounting record selected",
      evidenceLabel: "No retained evidence links",
      auditPackLabel: "Audit pack readiness unavailable",
      auditPackTimingLabel: "60 sec target not measured"
    };
  }

  const evidenceCount = summary.evidenceLinks.length
    + summary.evidenceCategories.reduce((total, category) => total + category.evidenceLinks.length, 0);

  const readiness = summary.auditPackReadiness ?? null;
  const missingCount = readiness?.missingEvidenceCategories?.length ?? 0;
  const generatedIn = readiness ? `${readiness.generatedInSeconds.toFixed(3)}s` : "not measured";

  return {
    title: "Accounting record",
    summaryLabel: summary.summary?.trim()
      || `${summary.completeCategoryCount}/${summary.requiredCategoryCount} evidence categories complete.`,
    statusLabel: summary.isAuditReady ? "Audit ready" : "Review required",
    statusTone: summary.isAuditReady ? "ready" : "review",
    recordIdLabel: summary.recordId || "Record id pending",
    evidenceLabel: evidenceCount === 0
      ? "No retained evidence links"
      : `${evidenceCount} retained evidence link${evidenceCount === 1 ? "" : "s"}`,
    auditPackLabel: readiness
      ? readiness.isComplete
        ? "Audit pack complete"
        : `${missingCount} audit-pack categor${missingCount === 1 ? "y" : "ies"} missing`
      : "Audit pack readiness unavailable",
    auditPackTimingLabel: readiness
      ? `${generatedIn} generation, ${readiness.slaTargetSeconds}s target ${readiness.slaMet ? "met" : "missed"}`
      : "60 sec target not measured"
  };
}

function buildAccountingRecordEvidenceRows(
  summary: OperationsAccountingRecordSummary | null
): OperationsAccountingRecordEvidenceRow[] {
  return (summary?.evidenceCategories ?? []).map(mapAccountingRecordEvidenceRow);
}

function mapAccountingRecordEvidenceRow(
  category: OperationsAccountingRecordEvidenceCategory
): OperationsAccountingRecordEvidenceRow {
  const href = normalizeLocalWorkstationRoute(category.routeHint) ?? null;
  const statusLabelText = category.isComplete ? "Complete" : "Review required";
  const requiredEvidence = (category.requiredEvidence ?? [])
    .map((item) => item.trim())
    .filter(Boolean);
  return {
    id: category.key,
    label: category.label || splitEnumLabel(category.key),
    statusLabel: statusLabelText,
    statusTone: category.isComplete ? "ready" : "review",
    detail: category.status?.trim() || (category.isComplete ? "Evidence category is complete." : "Evidence category still needs review."),
    requiredEvidenceLabel: requiredEvidence.length === 0
      ? "Required evidence not specified"
      : requiredEvidence.join(", "),
    evidenceLabel: category.evidenceLinks.length === 0
      ? "No linked evidence"
      : `${category.evidenceLinks.length} evidence link${category.evidenceLinks.length === 1 ? "" : "s"}`,
    routeHref: href,
    routeLabel: href ? "Open source" : "No source route",
    ariaLabel: `${category.label || category.key}, ${statusLabelText}, ${category.evidenceLinks.length} evidence links`
  };
}

function buildChecklistSummary(tasks: OperationsCloseChecklistTask[]): OperationsContinuityChecklistSummary {
  const readyCount = tasks.filter(isChecklistTaskReady).length;
  const blockedCount = tasks.filter(isChecklistTaskBlocked).length;
  const acknowledgementCount = tasks.filter((task) => Boolean(task.acknowledgedAtUtc)).length;
  const approvalCount = tasks.reduce((total, task) => total + Math.max(0, task.requiredApprovalCount ?? 0), 0);
  const evidenceCount = tasks.filter((task) => Boolean(task.evidencePointer?.trim())).length;
  const dueSoon = tasks
    .filter((task) => task.dueDate && !isChecklistTaskReady(task))
    .sort((left, right) => String(left.dueDate).localeCompare(String(right.dueDate)))[0] ?? null;
  const tone: OperationsContinuityTone = blockedCount > 0
    ? "blocked"
    : tasks.length > 0 && readyCount === tasks.length
      ? "ready"
      : tasks.length > 0
        ? "review"
        : "neutral";

  return {
    taskCountLabel: `${tasks.length} close task${tasks.length === 1 ? "" : "s"}`,
    readyCountLabel: `${readyCount} ready`,
    blockedCountLabel: `${blockedCount} blocked`,
    acknowledgementCountLabel: `${acknowledgementCount} acknowledged`,
    approvalCountLabel: approvalCount === 0
      ? "No control approvals required"
      : `${approvalCount} control approval${approvalCount === 1 ? "" : "s"} required`,
    evidenceCountLabel: `${evidenceCount}/${tasks.length} evidence pointer${tasks.length === 1 ? "" : "s"}`,
    dueSoonLabel: dueSoon?.dueDate ? `Next due ${formatDate(dueSoon.dueDate)}: ${dueSoon.label || gateLabel(dueSoon.gate)}` : "No open due dates",
    statusTone: tone
  };
}

function isChecklistTaskReady(task: OperationsCloseChecklistTask): boolean {
  const normalized = task.status?.trim().toLowerCase() ?? "";
  return normalized === "done" ||
    normalized === "complete" ||
    normalized === "completed" ||
    normalized === "acknowledged" ||
    Boolean(task.acknowledgedAtUtc);
}

function isChecklistTaskBlocked(task: OperationsCloseChecklistTask): boolean {
  const normalized = task.status?.trim().toLowerCase() ?? "";
  return normalized === "blocked" ||
    normalized === "expired" ||
    Boolean(task.blockingReason?.trim());
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

function checklistStatusTone(status: string | null | undefined): OperationsContinuityTone {
  const normalized = status?.trim().toLowerCase() ?? "";
  if (normalized === "done" || normalized === "complete" || normalized === "completed" || normalized === "acknowledged") {
    return "ready";
  }

  if (normalized === "blocked" || normalized === "expired") {
    return "blocked";
  }

  if (normalized === "review" || normalized === "reviewrequired" || normalized === "pending" || normalized === "open") {
    return "review";
  }

  return "neutral";
}

function breakStatusTone(status: string | null | undefined): OperationsContinuityTone {
  const normalized = status?.trim().toLowerCase() ?? "";
  if (normalized === "resolved" || normalized === "dismissed" || normalized === "matched") {
    return "ready";
  }

  if (normalized === "open" || normalized === "inreview" || normalized === "reviewrequired") {
    return "review";
  }

  if (normalized === "blocked") {
    return "blocked";
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
  return value
    .replace(/[-_]+/g, " ")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/\s+/g, " ")
    .trim();
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

function formatDecimal(value: number | null | undefined): string {
  return typeof value === "number" && Number.isFinite(value)
    ? value.toLocaleString("en-US", { maximumFractionDigits: 4 })
    : "Not recorded";
}

function formatCurrency(value: number | null | undefined, currency: string): string {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return "Not recorded";
  }

  if (!/^[A-Z]{3}$/.test(currency)) {
    return `${formatDecimal(value)} ${currency}`;
  }

  return new Intl.NumberFormat("en-US", {
    style: "currency",
    currency,
    maximumFractionDigits: 2
  }).format(value);
}

function formatPercent(value: number | null | undefined): string {
  return typeof value === "number" && Number.isFinite(value)
    ? `${Math.round(value)}%`
    : "Not measured";
}
