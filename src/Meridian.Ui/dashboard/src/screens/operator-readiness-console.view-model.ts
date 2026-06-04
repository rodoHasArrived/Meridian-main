import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { getOperatorInbox, type ApiRequestOptions } from "@/lib/api";
import { countPendingReportPackDistributions, getReportPackDistributions } from "@/lib/reporting-distributions";
import { normalizeLocalWorkstationRoute, WORKSTATION_ROUTE_CATALOG, workflowTargetPath } from "@/lib/workspace";
import { WORKSTATION_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import type {
  DataProviderRecord,
  DataWorkspaceResponse,
  AccountingWorkspaceResponse,
  ReportingWorkspaceResponse,
  MetricSnapshot,
  OperatorInbox,
  OperatorWorkItem,
  StrategyRunRecord,
  StrategyWorkspaceResponse,
  ReconciliationBreakQueueItem,
  TradingAcceptanceGate,
  TradingOperatorReadiness,
  TradingWorkspaceResponse,
  WorkstationBrokerageSyncStatus
} from "@/types";

export type ReadinessConsoleLevel = "ready" | "review" | "blocked" | "neutral";

export interface ReadinessConsoleMetric extends MetricSnapshot {
  detail: string;
  level: ReadinessConsoleLevel;
  detailId: string;
  ariaLabel: string;
  statusAriaLabel: string;
}

export interface ReadinessConsoleApiSource {
  id: string;
  label: string;
  endpoint: string;
  status: string;
  level: ReadinessConsoleLevel;
  ariaLabel: string;
  statusAriaLabel: string;
}

export interface ReadinessConsoleRow {
  id: string;
  label: string;
  value: string;
  detail: string;
  meta: string;
  createdAtLabel?: string | null;
  level: ReadinessConsoleLevel;
  ariaLabel: string;
  statusAriaLabel: string;
  detailId: string;
  action?: ReadinessConsoleRowAction | null;
}

export interface ReadinessConsoleNextAction {
  title: string;
  detail: string;
  meta: string;
  label: string;
  route: string | null;
  level: ReadinessConsoleLevel;
  disabledReason: string | null;
  ariaLabel: string;
  actionAriaLabel: string;
}

export interface ReadinessConsoleDetailField {
  label: string;
  value: string;
}

export interface ReadinessConsoleSelectedWorkItemDetail {
  id: string;
  title: string;
  statusLabel: string;
  statusAriaLabel: string;
  detail: string;
  meta: string;
  level: ReadinessConsoleLevel;
  ariaLabel: string;
  fields: ReadinessConsoleDetailField[];
  action: ReadinessConsoleRowAction | null;
}

export interface ReadinessConsoleSelectedEvidenceDetail {
  id: string;
  title: string;
  statusLabel: string;
  statusAriaLabel: string;
  detail: string;
  meta: string;
  level: ReadinessConsoleLevel;
  ariaLabel: string;
  fields: ReadinessConsoleDetailField[];
  action: ReadinessConsoleRowAction | null;
}

export interface ReadinessConsoleRecoveryState {
  title: string;
  detail: string;
  actionLabel: string;
  actionAriaLabel: string;
}

type ReadinessConsoleMetricBase = Omit<ReadinessConsoleMetric, "ariaLabel" | "statusAriaLabel" | "delta" | "tone" | "detailId">;
type ReadinessConsoleApiSourceBase = Omit<ReadinessConsoleApiSource, "ariaLabel" | "statusAriaLabel">;
type ReadinessConsoleRowBase = Omit<ReadinessConsoleRow, "ariaLabel" | "statusAriaLabel" | "detailId">;

export type ReadinessConsolePanelId =
  | "latest-runs"
  | "active-paper-session"
  | "provider-trust"
  | "reconciliation-breaks"
  | "promotion-blockers"
  | "reporting-report-packs";

export interface ReadinessConsolePanel {
  id: ReadinessConsolePanelId;
  title: string;
  emptyText: string;
  ariaLabel: string;
  listLabel: string;
  tableLabel: string;
  detailLabel: string;
  detailPanelId: string;
  selectedRowId: string | null;
  selectedDetail: ReadinessConsoleSelectedEvidenceDetail | null;
  rows: ReadinessConsoleRow[];
  selectRow: (id: string) => void;
}

export interface ReadinessConsoleRowAction {
  label: string;
  route: string;
  ariaLabel: string;
  variant: "secondary" | "outline";
}

export interface ReadinessConsoleState {
  title: string;
  subtitle: string;
  overallLabel: string;
  overallDetail: string;
  overallLevel: ReadinessConsoleLevel;
  asOf: string;
  statusAnnouncement: string;
  inboxSummary: string;
  inboxLoadingLabel: string | null;
  inboxErrorText: string | null;
  inboxRefreshLabel: string;
  inboxRefreshAriaLabel: string;
  inboxRefreshDisabled: boolean;
  inboxRefreshDisabledReason: string | null;
  inboxRefreshBusy: boolean;
  inboxErrorRecovery: ReadinessConsoleRecoveryState | null;
  nextAction: ReadinessConsoleNextAction;
  metrics: ReadinessConsoleMetric[];
  metricsLabel: string;
  apiSources: ReadinessConsoleApiSource[];
  apiSourcesLabel: string;
  checkpointGates: ReadinessConsoleRow[];
  checkpointGatesLabel: string;
  latestRuns: ReadinessConsoleRow[];
  activeSessionFacts: ReadinessConsoleRow[];
  providerTrustRows: ReadinessConsoleRow[];
  reconciliationRows: ReadinessConsoleRow[];
  promotionRows: ReadinessConsoleRow[];
  reportPackFacts: ReadinessConsoleRow[];
  panels: ReadinessConsolePanel[];
  workItems: ReadinessConsoleRow[];
  workItemsSummary: string;
  workItemsOverflowText: string | null;
  workItemsEmptyText: string;
  workItemsEmptyAction: ReadinessConsoleRowAction;
  workItemsRegionLabel: string;
  workItemsListLabel: string;
  workItemsTableLabel: string;
  workItemsDetailLabel: string;
  workItemsDetailPanelId: string;
  selectedWorkItemId: string | null;
  selectedWorkItemDetail: ReadinessConsoleSelectedWorkItemDetail | null;
  selectWorkItem: (id: string) => void;
  refreshInbox: () => Promise<void>;
}

export interface BuildOperatorReadinessConsoleStateOptions {
  strategy: StrategyWorkspaceResponse | null;
  trading: TradingWorkspaceResponse | null;
  data: DataWorkspaceResponse | null;
  accounting: AccountingWorkspaceResponse | null;
  reporting?: ReportingWorkspaceResponse | null;
  operatorInbox: OperatorInbox | null;
  inboxLoading: boolean;
  inboxError: string | null;
  selectedWorkItemId?: string | null;
  selectedPanelRowIds?: Partial<Record<ReadinessConsolePanelId, string>>;
  selectWorkItem?: (id: string) => void;
  selectPanelRow?: (panelId: ReadinessConsolePanelId, rowId: string) => void;
  refreshInbox?: () => Promise<void>;
}

export interface OperatorReadinessConsoleServices {
  getOperatorInbox: (fundAccountId?: string, options?: ApiRequestOptions) => Promise<OperatorInbox>;
}

const defaultServices: OperatorReadinessConsoleServices = {
  getOperatorInbox: (fundAccountId?: string, options?: ApiRequestOptions) => getOperatorInbox(fundAccountId, options)
};

const REPORT_PACKS_ROUTE = WORKSTATION_ROUTE_CATALOG.reportingReportPacks;
const PROVIDER_SETUP_ROUTE = WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup;

export function useOperatorReadinessConsoleViewModel(
  payload: Omit<BuildOperatorReadinessConsoleStateOptions, "operatorInbox" | "inboxLoading" | "inboxError">,
  services: OperatorReadinessConsoleServices = defaultServices
): ReadinessConsoleState {
  const [operatorInbox, setOperatorInbox] = useState<OperatorInbox | null>(null);
  const [inboxLoading, setInboxLoading] = useState(true);
  const [inboxError, setInboxError] = useState<string | null>(null);
  const [selectedWorkItemId, setSelectedWorkItemId] = useState<string | null>(null);
  const [selectedPanelRowIds, setSelectedPanelRowIds] = useState<Partial<Record<ReadinessConsolePanelId, string>>>({});
  const mountedRef = useRef(true);
  const refreshRevisionRef = useRef(0);
  const inboxAbortRef = useRef<AbortController | null>(null);

  const activeFundAccountId = payload.trading?.readiness?.brokerageSync?.fundAccountId;
  const selectWorkItem = useCallback((id: string) => {
    setSelectedWorkItemId(id);
  }, []);
  const selectPanelRow = useCallback((panelId: ReadinessConsolePanelId, rowId: string) => {
    setSelectedPanelRowIds((current) => current[panelId] === rowId ? current : {
      ...current,
      [panelId]: rowId
    });
  }, []);

  useEffect(() => {
    mountedRef.current = true;

    return () => {
      mountedRef.current = false;
      refreshRevisionRef.current += 1;
      inboxAbortRef.current?.abort();
    };
  }, []);

  const refreshInbox = useCallback(async () => {
    if (!mountedRef.current) {
      return;
    }

    const revision = refreshRevisionRef.current + 1;
    refreshRevisionRef.current = revision;
    inboxAbortRef.current?.abort();
    const controller = new AbortController();
    inboxAbortRef.current = controller;
    setInboxLoading(true);
    setInboxError(null);
    setOperatorInbox(null);

    try {
      const inbox = await services.getOperatorInbox(activeFundAccountId, { signal: controller.signal });
      if (!mountedRef.current || refreshRevisionRef.current !== revision) {
        return;
      }

      setOperatorInbox(inbox ?? null);
    } catch (err) {
      if (!mountedRef.current || refreshRevisionRef.current !== revision) {
        return;
      }

      setOperatorInbox(null);
      setInboxError(toErrorMessage(err, "Operator inbox failed to load."));
    } finally {
      if (mountedRef.current && refreshRevisionRef.current === revision) {
        if (inboxAbortRef.current === controller) {
          inboxAbortRef.current = null;
        }
        setInboxLoading(false);
      }
    }
  }, [activeFundAccountId, services]);

  useEffect(() => {
    void refreshInbox();
  }, [refreshInbox]);

  return useMemo(
    () => buildOperatorReadinessConsoleState({
      ...payload,
      operatorInbox,
      inboxLoading,
      inboxError,
      selectedWorkItemId,
      selectedPanelRowIds,
      selectWorkItem,
      selectPanelRow,
      refreshInbox
    }),
    [inboxError, inboxLoading, operatorInbox, payload, selectedPanelRowIds, selectedWorkItemId, selectPanelRow, selectWorkItem, refreshInbox]
  );
}

export function buildOperatorReadinessConsoleState({
  strategy,
  trading,
  data,
  accounting,
  reporting,
  operatorInbox,
  inboxLoading,
  inboxError,
  selectedWorkItemId,
  selectedPanelRowIds,
  selectWorkItem,
  selectPanelRow,
  refreshInbox
}: BuildOperatorReadinessConsoleStateOptions): ReadinessConsoleState {
  const readiness = trading?.readiness ?? null;
  const workItems = mergeOperatorWorkItems(operatorInbox?.items ?? [], readiness?.workItems ?? []);
  const latestRuns = withRowPresentation(buildLatestRunRows(strategy?.runs ?? []), "latest-runs");
  const activeSessionFacts = withRowPresentation(buildActiveSessionFacts(readiness), "active-session");
  const providerTrustRows = withRowPresentation(buildProviderTrustRows(readiness, data?.providers ?? []), "provider-trust");
  const reconciliationRows = withRowPresentation(buildReconciliationRows(accounting), "reconciliation");
  const promotionRows = withRowPresentation(buildPromotionRows(readiness, workItems), "promotion");
  const prioritizedWorkItems = prioritizeWorkItems(workItems);
  const reportPackFacts = withRowPresentation(buildReportPackFacts(reporting ?? accounting), "report-pack");
  const checkpointGates = withRowPresentation(buildCockpitGateRows({
    readiness,
    providerTrustRows,
    reconciliationRows,
    reportPackFacts,
    workItems: prioritizedWorkItems
  }), "cockpit-gates");
  const workItemRows = withRowPresentation(buildWorkItemRows(prioritizedWorkItems), "work-items");
  const selectedWorkItemDetail = buildSelectedWorkItemDetail(workItemRows, prioritizedWorkItems, selectedWorkItemId ?? null);
  const nextAction = buildNextAction({
    checkpointGates,
    workItemRows,
    activeSessionFacts,
    providerTrustRows,
    reconciliationRows,
    promotionRows,
    reportPackFacts
  });
  const metrics = withMetricPresentation(buildMetrics({
    latestRuns,
    readiness,
    providerTrustRows,
    reconciliationRows,
    promotionRows,
    reporting: reporting ?? accounting
  }));
  const overallLevel = determineOverallLevel({
    readiness,
    operatorInbox,
    inboxLoading,
    inboxError,
    checkpointGates
  });
  const readinessStatusLabel = readiness
    ? formatReadinessStatusValue(readiness.overallStatus)
    : "Awaiting readiness payload";
  const overallLabel = formatEffectiveOverallLabel(readinessStatusLabel, overallLevel);
  const asOf = formatReadinessUtcMinute(operatorInbox?.asOf ?? readiness?.asOf ?? null, "Unavailable");

  return {
    title: "Operator Readiness Console",
    subtitle: "Runs, paper sessions, provider trust, reconciliation, promotion, and report-pack posture from shared readiness payloads.",
    overallLabel,
    overallDetail: buildOverallDetail({
      readiness,
      operatorInbox,
      inboxLoading,
      inboxError,
      checkpointGates
    }),
    overallLevel,
    asOf,
    statusAnnouncement: buildStatusAnnouncement(overallLabel, asOf, inboxLoading, inboxError),
    inboxSummary: operatorInbox?.summary ?? "Operator inbox not loaded; using workstation payload fallbacks where available.",
    inboxLoadingLabel: inboxLoading ? "Loading operator inbox..." : null,
    inboxErrorText: inboxError,
    inboxRefreshLabel: inboxLoading ? "Refreshing inbox" : "Refresh inbox",
    inboxRefreshAriaLabel: inboxLoading
      ? "Refreshing operator inbox work items"
      : "Refresh operator inbox work items",
    inboxRefreshDisabled: inboxLoading,
    inboxRefreshDisabledReason: inboxLoading ? "Operator inbox is already refreshing." : null,
    inboxRefreshBusy: inboxLoading,
    inboxErrorRecovery: buildInboxErrorRecovery(inboxError),
    nextAction,
    metrics,
    metricsLabel: "Operator readiness metrics",
    apiSources: withApiSourcePresentation(buildApiSources({
      strategy,
      trading,
      data,
      accounting,
      reporting,
      operatorInbox,
      inboxLoading,
      inboxError
    })),
    apiSourcesLabel: "Shared readiness API sources",
    checkpointGates,
    checkpointGatesLabel: "Full-console readiness checkpoints",
    latestRuns,
    activeSessionFacts,
    providerTrustRows,
    reconciliationRows,
    promotionRows,
    reportPackFacts,
    panels: buildConsolePanels({
      latestRuns,
      activeSessionFacts,
      providerTrustRows,
      reconciliationRows,
      promotionRows,
      reportPackFacts,
      selectedPanelRowIds: selectedPanelRowIds ?? {},
      selectPanelRow
    }),
    workItems: workItemRows,
    workItemsSummary: buildWorkItemsSummary(prioritizedWorkItems, workItemRows.length),
    workItemsOverflowText: buildWorkItemsOverflowText(prioritizedWorkItems.length, workItemRows.length),
    workItemsEmptyText: buildWorkItemsEmptyText(operatorInbox, inboxLoading, inboxError),
    workItemsEmptyAction: buildWorkItemsEmptyAction(nextAction),
    workItemsRegionLabel: "Operator inbox review work items",
    workItemsListLabel: "Prioritized operator work items",
    workItemsTableLabel: "Prioritized operator work items table",
    workItemsDetailLabel: "Selected operator work item detail",
    workItemsDetailPanelId: "operator-readiness-selected-work-item-detail",
    selectedWorkItemId: selectedWorkItemDetail?.id ?? null,
    selectedWorkItemDetail,
    selectWorkItem: selectWorkItem ?? (() => undefined),
    refreshInbox: refreshInbox ?? (() => Promise.resolve())
  };
}

function withMetricPresentation(metrics: ReadinessConsoleMetricBase[]): ReadinessConsoleMetric[] {
  return metrics.map((metric) => ({
    ...metric,
    delta: formatLevelText(metric.level),
    tone: metricTone(metric.level),
    detailId: `readiness-metric-${slugifyId(metric.id)}-detail`,
    ariaLabel: `${metric.label}: ${metric.value}. ${metric.detail}`,
    statusAriaLabel: `${metric.label} status ${formatLevelText(metric.level)}`
  }));
}

function withApiSourcePresentation(sources: ReadinessConsoleApiSourceBase[]): ReadinessConsoleApiSource[] {
  return sources.map((source) => ({
    ...source,
    ariaLabel: `${source.label}: ${source.status}. Endpoint ${source.endpoint}`,
    statusAriaLabel: `${source.label} status ${source.status}`
  }));
}

function withRowPresentation(rows: ReadinessConsoleRowBase[], collectionId: string): ReadinessConsoleRow[] {
  return rows.map((row) => ({
    ...row,
    ariaLabel: [
      `${row.label}: ${row.value}.`,
      row.detail,
      row.meta,
      row.createdAtLabel ? `Created ${row.createdAtLabel}` : null
    ].filter(Boolean).join(" "),
    statusAriaLabel: `${row.label} status ${row.value}`,
    detailId: `readiness-row-${slugifyId(collectionId)}-${slugifyId(row.id)}-detail`
  }));
}

function buildConsolePanels({
  latestRuns,
  activeSessionFacts,
  providerTrustRows,
  reconciliationRows,
  promotionRows,
  reportPackFacts,
  selectedPanelRowIds,
  selectPanelRow
}: {
  latestRuns: ReadinessConsoleRow[];
  activeSessionFacts: ReadinessConsoleRow[];
  providerTrustRows: ReadinessConsoleRow[];
  reconciliationRows: ReadinessConsoleRow[];
  promotionRows: ReadinessConsoleRow[];
  reportPackFacts: ReadinessConsoleRow[];
  selectedPanelRowIds: Partial<Record<ReadinessConsolePanelId, string>>;
  selectPanelRow?: (panelId: ReadinessConsolePanelId, rowId: string) => void;
}): ReadinessConsolePanel[] {
  return [
    buildConsolePanel({
      id: "latest-runs",
      title: "Latest runs",
      emptyText: "No Strategy runs loaded.",
      rows: latestRuns,
      selectedRowId: selectedPanelRowIds["latest-runs"] ?? null,
      selectPanelRow
    }),
    buildConsolePanel({
      id: "active-paper-session",
      title: "Active paper session",
      emptyText: "No active paper session loaded.",
      rows: activeSessionFacts,
      selectedRowId: selectedPanelRowIds["active-paper-session"] ?? null,
      selectPanelRow
    }),
    buildConsolePanel({
      id: "provider-trust",
      title: "Provider trust",
      emptyText: "No provider trust rows loaded.",
      rows: providerTrustRows,
      selectedRowId: selectedPanelRowIds["provider-trust"] ?? null,
      selectPanelRow
    }),
    buildConsolePanel({
      id: "reconciliation-breaks",
      title: "Reconciliation breaks",
      emptyText: "No open or in-review reconciliation breaks.",
      rows: reconciliationRows,
      selectedRowId: selectedPanelRowIds["reconciliation-breaks"] ?? null,
      selectPanelRow
    }),
    buildConsolePanel({
      id: "promotion-blockers",
      title: "Promotion blockers",
      emptyText: "No promotion blockers surfaced by readiness.",
      rows: promotionRows,
      selectedRowId: selectedPanelRowIds["promotion-blockers"] ?? null,
      selectPanelRow
    }),
    buildConsolePanel({
      id: "reporting-report-packs",
      title: "Reporting report packs",
      emptyText: "No reporting readiness payload loaded.",
      rows: reportPackFacts,
      selectedRowId: selectedPanelRowIds["reporting-report-packs"] ?? null,
      selectPanelRow
    })
  ];
}

function buildConsolePanel({
  id,
  title,
  emptyText,
  rows,
  selectedRowId,
  selectPanelRow
}: {
  id: ReadinessConsolePanelId;
  title: string;
  emptyText: string;
  rows: ReadinessConsoleRow[];
  selectedRowId: string | null;
  selectPanelRow?: (panelId: ReadinessConsolePanelId, rowId: string) => void;
}): ReadinessConsolePanel {
  const detailPanelId = `operator-readiness-${id}-evidence-detail`;
  const selectedDetail = buildSelectedEvidenceDetail(rows, selectedRowId, title);

  return {
    id,
    title,
    emptyText,
    ariaLabel: `${title} readiness evidence`,
    listLabel: `${title} rows`,
    tableLabel: `${title} readiness evidence table`,
    detailLabel: `Selected ${title.toLowerCase()} evidence detail`,
    detailPanelId,
    selectedRowId: selectedDetail?.id ?? null,
    selectedDetail,
    rows,
    selectRow: (rowId: string) => {
      selectPanelRow?.(id, rowId);
    }
  };
}

function buildSelectedEvidenceDetail(
  rows: ReadinessConsoleRow[],
  selectedRowId: string | null,
  panelTitle: string
): ReadinessConsoleSelectedEvidenceDetail | null {
  if (rows.length === 0) {
    return null;
  }

  const selectedRow = rows.find((row) => row.id === selectedRowId) ?? rows[0];
  const actionRoute = selectedRow.action?.route ?? "No route action";

  return {
    id: selectedRow.id,
    title: selectedRow.label,
    statusLabel: selectedRow.value,
    statusAriaLabel: selectedRow.statusAriaLabel,
    detail: selectedRow.detail,
    meta: selectedRow.meta,
    level: selectedRow.level,
    ariaLabel: `Selected ${panelTitle.toLowerCase()} evidence: ${selectedRow.label}`,
    fields: [
      { label: "Evidence ID", value: selectedRow.id },
      { label: "Attention", value: formatLevelText(selectedRow.level) },
      ...(selectedRow.createdAtLabel ? [{ label: "Created", value: selectedRow.createdAtLabel }] : []),
      { label: "Route", value: actionRoute },
      { label: "Evidence", value: selectedRow.meta }
    ],
    action: selectedRow.action ?? null
  };
}

function buildLatestRunRows(runs: StrategyRunRecord[]): ReadinessConsoleRowBase[] {
  return runs.slice(0, 5).map((run) => ({
    id: run.id,
    label: run.strategyName,
    value: run.status,
    detail: `${run.mode} run on ${run.engine}; ${run.notes}`,
    meta: `${run.id} · ${run.lastUpdated} · P&L ${run.pnl} · Sharpe ${run.sharpe}`,
    level: run.status === "Needs Review" ? "review" : run.status === "Completed" ? "ready" : "neutral"
  }));
}

function buildActiveSessionFacts(readiness: TradingOperatorReadiness | null): ReadinessConsoleRowBase[] {
  const session = readiness?.activeSession ?? null;
  if (!session) {
    return [{
      id: "active-session",
      label: "Active paper session",
      value: "None",
      detail: "No active paper session is present in the readiness contract.",
      meta: "Trading cockpit must create or restore a session before paper-operation readiness can close.",
      level: "review"
    }];
  }

  return [
    {
      id: "active-session",
      label: "Active paper session",
      value: session.sessionId,
      detail: session.strategyName ?? session.strategyId,
      meta: `${session.orderCount} orders · ${session.positionCount} positions · ${session.symbolCount} symbols`,
      level: session.isActive ? "ready" : "review"
    },
    {
      id: "paper-equity",
      label: "Paper portfolio value",
      value: session.portfolioValue === null || session.portfolioValue === undefined ? "Unavailable" : formatCurrency(session.portfolioValue),
      detail: `Initial cash ${formatCurrency(session.initialCash)}`,
      meta: `Created ${formatReadinessUtcMinute(session.createdAt, "timestamp unavailable")}`,
      level: session.portfolioValue === null || session.portfolioValue === undefined ? "review" : "ready"
    },
    {
      id: "replay-coverage",
      label: "Replay coverage",
      value: readiness?.replay?.isConsistent ? "Consistent" : "Verify",
      detail: readiness?.replay
        ? `Compared ${readiness.replay.comparedFillCount} fills, ${readiness.replay.comparedOrderCount} orders, and ${readiness.replay.comparedLedgerEntryCount} ledger entries.`
        : "No replay verification is attached to the active readiness snapshot.",
      meta: readiness?.replay?.verificationAuditId ?? "No verification audit",
      level: readiness?.replay?.isConsistent ? "ready" : "review"
    }
  ];
}

function buildProviderTrustRows(
  readiness: TradingOperatorReadiness | null,
  providers: DataProviderRecord[]
): ReadinessConsoleRowBase[] {
  const rows: ReadinessConsoleRowBase[] = [];
  const trustGate = readiness?.trustGate ?? null;
  if (trustGate) {
    rows.push({
      id: "dk1-trust-gate",
      label: "DK1 provider trust",
      value: trustGate.status,
      detail: trustGate.detail,
       meta: `${trustGate.readySampleCount}/${trustGate.requiredSampleCount} samples ready · ${trustGate.validatedEvidenceDocumentCount} evidence documents`,
      level: trustGate.blockers.length > 0
        ? "blocked"
        : trustGate.operatorSignoffStatus.toLowerCase().includes("signed")
          ? "ready"
          : "review"
    });
  }

  const brokerage = readiness?.brokerageSync ?? null;
  if (brokerage) {
    rows.push(buildBrokerageTrustRow(brokerage));
  }

  providers.slice(0, 4).forEach((provider) => {
    rows.push({
      id: `provider-${provider.provider}`,
      label: provider.provider,
      value: provider.status,
      detail: provider.recommendedAction ?? provider.note,
      meta: [
        provider.trustScore ? `Trust ${provider.trustScore}` : null,
        provider.signalSource,
        provider.gateImpact
      ].filter(Boolean).join(" · ") || provider.capability,
      level: provider.status === "Healthy" ? "ready" : provider.status === "Degraded" ? "blocked" : "review"
    });
  });

  return rows;
}

function buildBrokerageTrustRow(status: WorkstationBrokerageSyncStatus): ReadinessConsoleRowBase {
  return {
    id: "brokerage-sync",
    label: "Brokerage sync",
    value: status.health,
    detail: status.lastError ?? `${status.positionCount} positions, ${status.openOrderCount} open orders, and ${status.fillCount} fills from brokerage sync.`,
    meta: status.providerId ?? "No provider linked",
    level: status.health === "Healthy" && !status.isStale
      ? "ready"
      : status.health === "Failed"
        ? "blocked"
        : "review"
  };
}

function buildReconciliationRows(accounting: AccountingWorkspaceResponse | null): ReadinessConsoleRowBase[] {
  const directBreaks = (accounting?.breakQueue ?? [])
    .filter((item) => item.status === "Open" || item.status === "InReview")
    .slice(0, 5)
    .map(buildBreakQueueRow);

  if (directBreaks.length > 0) {
    return directBreaks;
  }

  return (accounting?.reconciliationQueue ?? [])
    .filter((item) => item.openBreakCount > 0)
    .slice(0, 5)
    .map((item) => ({
      id: `run-${item.runId}`,
      label: item.strategyName,
      value: `${item.openBreakCount} open`,
      detail: `${item.reconciliationStatus} reconciliation for ${item.mode} run ${item.runId}.`,
      meta: `Updated ${item.lastUpdated}`,
      level: "review"
    }));
}

function buildBreakQueueRow(item: ReconciliationBreakQueueItem): ReadinessConsoleRowBase {
  return {
    id: item.breakId,
    label: item.strategyName,
    value: item.status,
    detail: item.reason,
    meta: `${item.category} - variance ${formatCurrency(item.variance)}`,
    level: item.status === "Open" ? "blocked" : "review"
  };
}

function buildPromotionRows(
  readiness: TradingOperatorReadiness | null,
  workItems: OperatorWorkItem[]
): ReadinessConsoleRowBase[] {
  const rows: ReadinessConsoleRowBase[] = [];
  const promotion = readiness?.promotion ?? null;

  if (promotion?.requiresReview) {
    rows.push({
      id: "promotion-state",
      label: "Promotion review",
      value: promotion.state,
      detail: promotion.reason,
      meta: [promotion.sourceRunId, promotion.targetRunId, promotion.auditReference].filter(Boolean).join(" - ") || "No promotion audit reference",
      level: "review"
    });
  }

  (readiness?.acceptanceGates ?? [])
    .filter((gate) => gate.status !== "Ready")
    .slice(0, 5)
    .forEach((gate) => rows.push(buildAcceptanceGateRow(gate)));

  workItems
    .filter((item) => item.kind === "PromotionReview")
    .forEach((item) => rows.push(buildWorkItemRow(item, true)));

  return dedupeRows(rows).slice(0, 6);
}

function buildAcceptanceGateRow(gate: TradingAcceptanceGate): ReadinessConsoleRowBase {
  return {
    id: `gate-${gate.gateId}`,
    label: gate.label,
    value: formatReadinessStatusValue(gate.status),
    detail: gate.detail,
    meta: [gate.runId, gate.sessionId, gate.auditReference].filter(Boolean).join(" - ") || "No audit reference",
    level: gate.status === "Blocked" ? "blocked" : "review"
  };
}

function buildReportPackFacts(reportingPayload: ReportingWorkspaceResponse | null): ReadinessConsoleRowBase[] {
  const reporting = reportingPayload?.reporting ?? null;
  if (!reporting) {
    return [{
      id: "report-pack",
      label: "Report-pack readiness",
      value: "Unavailable",
      detail: "Reporting payload has not loaded.",
      meta: "Wait for Accounting/Reporting bootstrap recovery.",
      level: "review"
    }];
  }

  const distributions = getReportPackDistributions(reporting);
  const pendingDistributions = countPendingReportPackDistributions(reporting);

  return [
    {
      id: "report-pack",
      label: "Report-pack readiness",
      value: distributions.length > 0 ? "Recipients present" : "No recipients",
      detail: reporting.summary,
      meta: `${reporting.profileCount} profiles - ${distributions.map((distribution) => distribution.recipient).join(", ") || "no recipients"} - ${pendingDistributions} pending`,
      level: reporting.profileCount > 0 && distributions.length > 0 && pendingDistributions === 0 ? "ready" : "review"
    },
    {
      id: "reporting-profiles",
      label: "Recommended profiles",
      value: reporting.recommendedProfiles.join(", ") || "None",
      detail: "Profiles with loader scripts or data dictionaries are preferred for governed output review.",
      meta: `${reporting.profiles.filter((profile) => profile.dataDictionary).length} data dictionaries`,
      level: reporting.recommendedProfiles.length > 0 ? "ready" : "review"
    }
  ];
}

function buildCockpitGateRows({
  readiness,
  providerTrustRows,
  reconciliationRows,
  reportPackFacts,
  workItems
}: {
  readiness: TradingOperatorReadiness | null;
  providerTrustRows: ReadinessConsoleRow[];
  reconciliationRows: ReadinessConsoleRow[];
  reportPackFacts: ReadinessConsoleRow[];
  workItems: OperatorWorkItem[];
}): ReadinessConsoleRowBase[] {
  const gateById = new Map((readiness?.acceptanceGates ?? []).map((gate) => [gate.gateId, gate]));
  const activeSession = readiness?.activeSession ?? null;
  const replay = readiness?.replay ?? null;
  const controls = readiness?.controls ?? null;
  const promotion = readiness?.promotion ?? null;
  const brokerage = readiness?.brokerageSync ?? null;
  const brokerageRow = providerTrustRows.find((row) => row.id === "brokerage-sync") ?? null;
  const reportPackReviewCount = reportPackFacts.filter((row) => row.level !== "ready").length;
  const criticalWorkItemCount = workItems.filter((item) => item.tone === "Critical").length;
  const warningWorkItemCount = workItems.filter((item) => item.tone === "Warning").length;

  return [
    buildCheckpointGateRow({
      id: "session-active",
      label: "Session active",
      fallbackValue: activeSession?.isActive ? "Ready" : "Review required",
      fallbackDetail: activeSession
        ? `${activeSession.sessionId} is ${activeSession.isActive ? "active" : "not active"} with ${activeSession.orderCount} orders and ${activeSession.symbolCount} symbols.`
        : "No active paper session is present in the readiness payload.",
      fallbackMeta: activeSession?.sessionId ?? "No session id",
      fallbackLevel: activeSession?.isActive ? "ready" : "review",
      gate: gateById.get("session"),
      action: {
        label: "Open Trading cockpit",
        route: WORKSTATION_ROUTE_CATALOG.trading,
        ariaLabel: "Open Trading cockpit for paper session readiness",
        variant: "outline"
      }
    }),
    buildCheckpointGateRow({
      id: "replay-verified",
      label: "Replay verified",
      fallbackValue: replay?.isConsistent ? "Ready" : replay ? "Blocked" : "Review required",
      fallbackDetail: replay
        ? `${replay.isConsistent ? "Replay matched" : "Replay mismatch"} across ${replay.comparedFillCount} fills, ${replay.comparedOrderCount} orders, and ${replay.comparedLedgerEntryCount} ledger entries.`
        : "Run replay verification for the active paper session before accepting readiness.",
      fallbackMeta: replay?.verificationAuditId ?? "No verification audit",
      fallbackLevel: replay?.isConsistent ? "ready" : replay ? "blocked" : "review",
      gate: gateById.get("replay"),
      action: {
        label: "Open replay evidence",
        route: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
        ariaLabel: "Open replay evidence for paper readiness",
        variant: replay?.isConsistent ? "outline" : "secondary"
      }
    }),
    buildCheckpointGateRow({
      id: "risk-control-explainable",
      label: "Risk state explainable",
      fallbackValue: controls?.circuitBreakerOpen ? "Blocked" : controls ? "Ready" : "Review required",
      fallbackDetail: controls
        ? controls.circuitBreakerOpen
          ? controls.circuitBreakerReason ?? "Execution circuit breaker is open."
          : `${controls.manualOverrideCount} manual overrides, ${controls.symbolLimitCount} symbol limits, and ${controls.defaultMaxPositionSize ?? "no"} default max-position limit are visible.`
        : "Execution-control evidence has not loaded.",
      fallbackMeta: controls?.circuitBreakerChangedBy ?? controls?.circuitBreakerChangedAt ?? "Execution controls",
      fallbackLevel: controls?.circuitBreakerOpen ? "blocked" : controls ? "ready" : "review",
      gate: gateById.get("audit-controls"),
      action: {
        label: "Open execution controls",
        route: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
        ariaLabel: "Open execution-control evidence for paper readiness",
        variant: controls?.circuitBreakerOpen ? "secondary" : "outline"
      }
    }),
    buildCheckpointGateRow({
      id: "promotion-trace-complete",
      label: "Promotion review trace complete",
      fallbackValue: promotion?.requiresReview ? "Review required" : promotion ? "Ready" : "Review required",
      fallbackDetail: promotion?.reason ?? "Promotion trace evidence is not attached to the readiness payload.",
      fallbackMeta: promotion
        ? [promotion.sourceRunId, promotion.targetRunId, promotion.auditReference].filter(Boolean).join(" - ") || "No promotion audit reference"
        : "No promotion record",
      fallbackLevel: promotion?.requiresReview ? "review" : promotion ? "ready" : "review",
      gate: gateById.get("promotion"),
      action: {
        label: "Open promotion review",
        route: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
        ariaLabel: "Open promotion review trace for paper readiness",
        variant: promotion?.requiresReview ? "secondary" : "outline"
      }
    }),
    buildCheckpointGateRow({
      id: "brokerage-sync",
      label: "Brokerage sync healthy",
      fallbackValue: brokerage?.health ?? "Unavailable",
      fallbackDetail: brokerageRow?.detail ?? "No account-scoped brokerage sync posture is attached to readiness.",
      fallbackMeta: brokerage
        ? [brokerage.fundAccountId, brokerage.providerId, brokerage.externalAccountId].filter(Boolean).join(" - ") || "No provider linked"
        : "No brokerage sync payload",
      fallbackLevel: brokerageRow?.level ?? "review",
      action: {
        label: "Open brokerage sync",
        route: WORKSTATION_ROUTE_CATALOG.portfolioBrokerageSync,
        ariaLabel: "Open brokerage sync for paper readiness",
        variant: brokerageRow?.level === "blocked" ? "secondary" : "outline"
      }
    }),
    buildCheckpointGateRow({
      id: "reconciliation-clear",
      label: "Reconciliation clear",
      fallbackValue: reconciliationRows.length === 0 ? "Ready" : `${reconciliationRows.length} open`,
      fallbackDetail: reconciliationRows.length === 0
        ? "No open or in-review reconciliation breaks are visible."
        : "Open or in-review reconciliation breaks must be cleared before full-console readiness is accepted.",
      fallbackMeta: reconciliationRows[0]?.meta ?? "Accounting break queue",
      fallbackLevel: reconciliationRows.some((row) => row.level === "blocked") ? "blocked" : reconciliationRows.length > 0 ? "review" : "ready",
      action: {
        label: "Open break queue",
        route: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
        ariaLabel: "Open reconciliation break queue for paper readiness",
        variant: reconciliationRows.some((row) => row.level === "blocked") ? "secondary" : "outline"
      }
    }),
    buildCheckpointGateRow({
      id: "report-pack-ready",
      label: "Report pack approval ready",
      fallbackValue: reportPackReviewCount === 0 ? "Ready" : `${reportPackReviewCount} review`,
      fallbackDetail: reportPackReviewCount === 0
        ? "Governed report-pack recipients and recommended profiles are available."
        : "Report-pack recipients or recommended profiles are incomplete.",
      fallbackMeta: reportPackFacts[0]?.meta ?? "Reporting payload unavailable",
      fallbackLevel: reportPackReviewCount === 0 ? "ready" : "review",
      action: {
        label: "Open report packs",
        route: REPORT_PACKS_ROUTE,
        ariaLabel: "Open report packs for paper readiness",
        variant: "outline"
      }
    }),
    {
      id: "operator-work-items",
      label: "Operator work items settled",
      value: criticalWorkItemCount > 0 ? `${criticalWorkItemCount} critical` : warningWorkItemCount > 0 ? `${warningWorkItemCount} warning` : "Ready",
      detail: criticalWorkItemCount > 0
        ? "Critical operator work items keep the console blocked until resolved."
        : warningWorkItemCount > 0
          ? "Warning operator work items keep the console in review until resolved."
          : "No warning or critical operator work items are visible.",
      meta: `${workItems.length} merged work items`,
      level: criticalWorkItemCount > 0 ? "blocked" : warningWorkItemCount > 0 ? "review" : "ready",
      action: workItems.length > 0
        ? buildWorkItemAction(workItems[0])
        : {
            label: "Open Trading cockpit",
            route: WORKSTATION_ROUTE_CATALOG.trading,
            ariaLabel: "Open Trading cockpit after operator work items settle",
            variant: "outline"
          }
    }
  ];
}

function buildCheckpointGateRow({
  id,
  label,
  fallbackValue,
  fallbackDetail,
  fallbackMeta,
  fallbackLevel,
  gate,
  action
}: {
  id: string;
  label: string;
  fallbackValue: string;
  fallbackDetail: string;
  fallbackMeta: string;
  fallbackLevel: ReadinessConsoleLevel;
  gate?: TradingAcceptanceGate;
  action?: ReadinessConsoleRowAction | null;
}): ReadinessConsoleRowBase {
  return {
    id,
    label,
    value: gate ? formatReadinessStatusValue(gate.status) : fallbackValue,
    detail: gate?.detail ?? fallbackDetail,
    meta: gate
      ? [gate.runId, gate.sessionId, gate.auditReference].filter(Boolean).join(" - ") || fallbackMeta
      : fallbackMeta,
    level: gate ? levelFromReadiness(gate.status) : fallbackLevel,
    action: action ?? null
  };
}

function buildWorkItemRows(workItems: OperatorWorkItem[]): ReadinessConsoleRowBase[] {
  return workItems.slice(0, 6).map((item) => buildWorkItemRow(item, true));
}

function mergeOperatorWorkItems(primaryItems: OperatorWorkItem[], fallbackItems: OperatorWorkItem[]): OperatorWorkItem[] {
  const byId = new Map<string, { item: OperatorWorkItem; index: number }>();
  [...fallbackItems, ...primaryItems].forEach((item, index) => {
    const existing = byId.get(item.workItemId);
    if (!existing || shouldReplaceWorkItem(existing.item, item)) {
      byId.set(item.workItemId, { item, index: existing?.index ?? index });
    }
  });

  return Array.from(byId.values())
    .sort((left, right) => left.index - right.index)
    .map(({ item }) => item);
}

function shouldReplaceWorkItem(existing: OperatorWorkItem, candidate: OperatorWorkItem): boolean {
  const toneDelta = tonePriority(candidate.tone) - tonePriority(existing.tone);
  if (toneDelta !== 0) {
    return toneDelta < 0;
  }

  return timestampPriority(candidate.createdAt) >= timestampPriority(existing.createdAt);
}

function prioritizeWorkItems(workItems: OperatorWorkItem[]): OperatorWorkItem[] {
  return workItems
    .map((item, index) => ({ item, index }))
    .sort((left, right) => {
      const toneDelta = tonePriority(left.item.tone) - tonePriority(right.item.tone);
      if (toneDelta !== 0) {
        return toneDelta;
      }

      const timeDelta = timestampPriority(right.item.createdAt) - timestampPriority(left.item.createdAt);
      if (timeDelta !== 0) {
        return timeDelta;
      }

      return left.index - right.index;
    })
    .map(({ item }) => item);
}

function buildWorkItemRow(item: OperatorWorkItem, includeAction: boolean): ReadinessConsoleRowBase {
  const action = includeAction ? buildWorkItemAction(item) : null;
  const createdAtLabel = formatReadinessUtcMinute(item.createdAt, "Timestamp unavailable");
  const signoffSummary = buildSignoffSummary(item);

  return {
    id: item.workItemId,
    label: item.label,
    value: item.tone,
    detail: item.detail,
    meta: [item.workspace, item.targetPageTag, item.runId, item.auditReference, signoffSummary].filter(Boolean).join(" - ") || item.kind,
    createdAtLabel,
    level: levelFromTone(item.tone),
    action
  };
}

function buildSignoffSummary(item: OperatorWorkItem): string | null {
  const role = item.requiredSignoffRole?.trim();
  const status = item.signoffStatus?.trim();
  if (!role && !status) {
    return null;
  }

  if (role && status) {
    return `${role} sign-off ${status}`;
  }

  return role ? `${role} sign-off` : `Sign-off ${status}`;
}

function buildWorkItemAction(item: OperatorWorkItem): ReadinessConsoleRowAction | null {
  const normalizedRoute = item.kind === "BrokerageSync"
    ? PROVIDER_SETUP_ROUTE
    : normalizeLocalWorkstationRoute(item.targetRoute)
      ?? routeFromWorkItemTarget(item)
      ?? fallbackRouteForWorkItemKind(item.kind);
  const route = item.kind === "ReportPackApproval" && normalizedRoute === WORKSTATION_ROUTE_CATALOG.reporting
    ? REPORT_PACKS_ROUTE
    : normalizedRoute;
  if (!route) {
    return null;
  }

  const label = actionLabelForWorkItemKind(item.kind);
  return {
    label,
    route,
    ariaLabel: `${label}: ${item.label}`,
    variant: item.tone === "Critical" ? "secondary" : "outline"
  };
}

function routeFromWorkItemTarget(item: OperatorWorkItem): string | null {
  if (!item.targetPageTag && !item.workspace) {
    return null;
  }

  return workflowTargetPath(item.targetPageTag, item.workspace);
}

function fallbackRouteForWorkItemKind(kind: string): string {
  switch (kind) {
    case "PaperReplay":
    case "PromotionReview":
    case "ExecutionControl":
      return WORKSTATION_ROUTE_CATALOG.tradingReadiness;
    case "BrokerageSync":
      return PROVIDER_SETUP_ROUTE;
    case "SecurityMasterCoverage":
      return WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster;
    case "ReconciliationBreak":
      return WORKSTATION_ROUTE_CATALOG.accountingReconciliation;
    case "LedgerPeriodClose":
      return WORKSTATION_ROUTE_CATALOG.accountingReconciliation;
    case "ReportPackApproval":
      return REPORT_PACKS_ROUTE;
    case "ProviderTrustGate":
      return WORKSTATION_ROUTE_CATALOG.data;
    default:
      return WORKSTATION_ROUTE_CATALOG.tradingReadiness;
  }
}

function actionLabelForWorkItemKind(kind: string): string {
  switch (kind) {
    case "PaperReplay":
      return "Open replay evidence";
    case "PromotionReview":
      return "Open promotion review";
    case "BrokerageSync":
      return "Fix provider setup";
    case "SecurityMasterCoverage":
      return "Open Security Master";
    case "ReconciliationBreak":
      return "Open break queue";
    case "LedgerPeriodClose":
      return "Open reconciliation";
    case "ReportPackApproval":
      return "Open report packs";
    case "ProviderTrustGate":
      return "Open provider trust";
    case "ExecutionControl":
      return "Open execution controls";
    default:
      return "Open operator item";
  }
}

function buildWorkItemsSummary(workItems: OperatorWorkItem[], visibleCount: number): string {
  if (workItems.length === 0) {
    return "No operator work items returned by the shared operator-inbox contract.";
  }

  const counts = countWorkItemTones(workItems);
  const toneSummary = [
    counts.Critical > 0 ? `${formatCount(counts.Critical, "critical item")}` : null,
    counts.Warning > 0 ? `${formatCount(counts.Warning, "warning")}` : null,
    counts.Info > 0 ? `${formatCount(counts.Info, "info item")}` : null,
    counts.Success > 0 ? `${formatCount(counts.Success, "success item")}` : null
  ].filter(Boolean).join(", ");

  return `Showing ${visibleCount} of ${formatCount(workItems.length, "operator work item")}; ${toneSummary}. Critical items sort first.`;
}

function buildWorkItemsOverflowText(totalCount: number, visibleCount: number): string | null {
  const hiddenCount = totalCount - visibleCount;
  if (hiddenCount <= 0) {
    return null;
  }

  return `${formatCount(hiddenCount, "additional work item")} hidden from this view after priority sorting.`;
}

function buildWorkItemsEmptyText(
  operatorInbox: OperatorInbox | null,
  inboxLoading: boolean,
  inboxError: string | null
): string {
  if (inboxLoading) {
    return "Operator inbox work items are loading. The console will select the first actionable item when the queue arrives.";
  }

  if (inboxError) {
    return "Operator inbox work items are unavailable. Use the retry action above, or continue from the primary next action while the console preserves fallback readiness evidence.";
  }

  if (operatorInbox) {
    return "No operator work items need attention. Continue monitoring the paper cockpit and readiness evidence before accepting new live risk.";
  }

  return "No operator work items returned. Continue from the primary next action while shared readiness payloads finish loading.";
}

function buildWorkItemsEmptyAction(nextAction: ReadinessConsoleNextAction): ReadinessConsoleRowAction {
  const nextActionRoute = nextAction.route ?? WORKSTATION_ROUTE_CATALOG.tradingReadiness;
  return {
    label: nextAction.level === "ready" ? "Open Trading cockpit" : nextAction.label,
    route: nextAction.level === "ready" ? WORKSTATION_ROUTE_CATALOG.trading : nextActionRoute,
    ariaLabel: nextAction.level === "ready"
      ? "Open Trading cockpit from empty operator inbox"
      : `${nextAction.actionAriaLabel} from empty operator inbox`,
    variant: nextAction.level === "blocked" ? "secondary" : "outline"
  };
}

function buildSelectedWorkItemDetail(
  rows: ReadinessConsoleRow[],
  workItems: OperatorWorkItem[],
  selectedWorkItemId: string | null
): ReadinessConsoleSelectedWorkItemDetail | null {
  if (rows.length === 0) {
    return null;
  }

  const selectedRow = rows.find((row) => row.id === selectedWorkItemId) ?? rows[0];
  const actionRoute = selectedRow.action?.route ?? "No route action";
  const selectedWorkItem = workItems.find((item) => item.workItemId === selectedRow.id) ?? null;

  return {
    id: selectedRow.id,
    title: selectedRow.label,
    statusLabel: selectedRow.value,
    statusAriaLabel: selectedRow.statusAriaLabel,
    detail: selectedRow.detail,
    meta: selectedRow.meta,
    level: selectedRow.level,
    ariaLabel: `Selected operator work item: ${selectedRow.label}`,
    fields: [
      { label: "Work item ID", value: selectedRow.id },
      { label: "Attention", value: formatLevelText(selectedRow.level) },
      ...(selectedRow.createdAtLabel ? [{ label: "Created", value: selectedRow.createdAtLabel }] : []),
      ...(selectedWorkItem?.requiredSignoffRole ? [{ label: "Required sign-off role", value: selectedWorkItem.requiredSignoffRole }] : []),
      ...(selectedWorkItem?.signoffStatus ? [{ label: "Sign-off status", value: selectedWorkItem.signoffStatus }] : []),
      ...(selectedWorkItem?.toleranceProfileId ? [{ label: "Tolerance profile", value: selectedWorkItem.toleranceProfileId }] : []),
      { label: "Route", value: actionRoute },
      { label: "Evidence", value: selectedRow.meta }
    ],
    action: selectedRow.action ?? null
  };
}

function buildInboxErrorRecovery(inboxError: string | null): ReadinessConsoleRecoveryState | null {
  if (!inboxError) {
    return null;
  }

  return {
    title: "Operator inbox unavailable",
    detail: `${inboxError}. Retry the shared inbox before accepting readiness; fallback rows remain visible for triage.`,
    actionLabel: "Retry inbox",
    actionAriaLabel: "Retry loading operator inbox work items after failure"
  };
}

interface NextActionCandidate {
  title: string;
  detail: string;
  meta: string;
  label: string;
  route: string | null;
  level: ReadinessConsoleLevel;
  disabledReason?: string | null;
  sourcePriority: number;
  index: number;
}

function buildNextAction({
  checkpointGates,
  workItemRows,
  activeSessionFacts,
  providerTrustRows,
  reconciliationRows,
  promotionRows,
  reportPackFacts
}: {
  checkpointGates: ReadinessConsoleRow[];
  workItemRows: ReadinessConsoleRow[];
  activeSessionFacts: ReadinessConsoleRow[];
  providerTrustRows: ReadinessConsoleRow[];
  reconciliationRows: ReadinessConsoleRow[];
  promotionRows: ReadinessConsoleRow[];
  reportPackFacts: ReadinessConsoleRow[];
}): ReadinessConsoleNextAction {
  const candidates = [
    ...workItemRows
      .filter((row) => row.level === "blocked" || row.level === "review")
      .map((row, index) => nextActionCandidateFromRow(row, {
        label: row.action?.label ?? "Review item",
        route: row.action?.route ?? WORKSTATION_ROUTE_CATALOG.tradingReadiness,
        sourcePriority: 0,
        index
      })),
    ...checkpointGates
      .filter((row) => row.level === "blocked" || row.level === "review")
      .map((row, index) => nextActionCandidateFromRow(row, {
        label: row.action?.label ?? "Review checkpoint",
        route: row.action?.route ?? null,
        sourcePriority: 1,
        index
      })),
    ...reconciliationRows.map((row, index) => nextActionCandidateFromRow(row, {
      label: "Open break queue",
      route: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
      sourcePriority: 2,
      index
    })),
    ...promotionRows.map((row, index) => nextActionCandidateFromRow(row, {
      label: "Open promotion review",
      route: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
      sourcePriority: 3,
      index
    })),
    ...providerTrustRows
      .filter((row) => row.level === "blocked" || row.level === "review")
      .map((row, index) => nextActionCandidateFromRow(row, {
        label: "Open provider trust",
        route: WORKSTATION_ROUTE_CATALOG.data,
        sourcePriority: 4,
        index
      })),
    ...reportPackFacts
      .filter((row) => row.level === "blocked" || row.level === "review")
      .map((row, index) => nextActionCandidateFromRow(row, {
        label: "Open report packs",
        route: REPORT_PACKS_ROUTE,
        sourcePriority: 5,
        index
      })),
    ...activeSessionFacts
      .filter((row) => row.level === "blocked" || row.level === "review")
      .map((row, index) => nextActionCandidateFromRow(row, {
        label: "Open Trading cockpit",
        route: WORKSTATION_ROUTE_CATALOG.trading,
        sourcePriority: 6,
        index
      }))
  ].sort((left, right) => {
    const levelDelta = levelPriority(left.level) - levelPriority(right.level);
    if (levelDelta !== 0) {
      return levelDelta;
    }

    const sourceDelta = left.sourcePriority - right.sourcePriority;
    if (sourceDelta !== 0) {
      return sourceDelta;
    }

    return left.index - right.index;
  });

  const candidate = candidates[0] ?? {
    title: "No blocking review items",
    detail: "The visible readiness queue is settled. Continue monitoring the paper cockpit and latest workstation evidence.",
    meta: "Operator inbox and readiness evidence are clear",
    label: "Open Trading cockpit",
    route: WORKSTATION_ROUTE_CATALOG.trading,
    level: "ready" as ReadinessConsoleLevel,
    disabledReason: null,
    sourcePriority: 99,
    index: 0
  };

  return {
    title: candidate.title,
    detail: candidate.detail,
    meta: candidate.meta,
    label: candidate.label,
    route: candidate.route,
    level: candidate.level,
    disabledReason: candidate.route ? null : "No route-backed action is available for this checkpoint.",
    ariaLabel: `Primary next action: ${candidate.title}. ${candidate.detail} ${candidate.meta}`,
    actionAriaLabel: `${candidate.label}: ${candidate.title}`
  };
}

function nextActionCandidateFromRow(
  row: ReadinessConsoleRow,
  options: {
    label: string;
    route: string | null;
    sourcePriority: number;
    index: number;
  }
): NextActionCandidate {
  return {
    title: row.label,
    detail: row.detail,
    meta: row.meta,
    label: options.label,
    route: options.route,
    level: row.level,
    sourcePriority: options.sourcePriority,
    index: options.index
  };
}

function countWorkItemTones(workItems: OperatorWorkItem[]): Record<OperatorWorkItem["tone"], number> {
  return workItems.reduce<Record<OperatorWorkItem["tone"], number>>((counts, item) => {
    counts[item.tone] += 1;
    return counts;
  }, {
    Critical: 0,
    Warning: 0,
    Info: 0,
    Success: 0
  });
}

function levelPriority(level: ReadinessConsoleLevel): number {
  switch (level) {
    case "blocked":
      return 0;
    case "review":
      return 1;
    case "neutral":
      return 2;
    case "ready":
      return 3;
  }
}

function tonePriority(tone: OperatorWorkItem["tone"]): number {
  switch (tone) {
    case "Critical":
      return 0;
    case "Warning":
      return 1;
    case "Info":
      return 2;
    case "Success":
      return 3;
  }
}

function timestampPriority(value: string): number {
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

function formatCount(count: number, singular: string): string {
  return `${count} ${count === 1 ? singular : `${singular}s`}`;
}

function buildMetrics({
  latestRuns,
  readiness,
  providerTrustRows,
  reconciliationRows,
  promotionRows,
  reporting
}: {
  latestRuns: ReadinessConsoleRow[];
  readiness: TradingOperatorReadiness | null;
  providerTrustRows: ReadinessConsoleRow[];
  reconciliationRows: ReadinessConsoleRow[];
  promotionRows: ReadinessConsoleRow[];
  reporting: ReportingWorkspaceResponse | null;
}): ReadinessConsoleMetricBase[] {
  const activeSession = readiness?.activeSession;
  const reportDistributions = getReportPackDistributions(reporting?.reporting);
  const pendingReportDistributions = countPendingReportPackDistributions(reporting?.reporting);

  return [
    {
      id: "latest-runs",
      label: "Latest runs",
      value: String(latestRuns.length),
      detail: "Recent Strategy run rows available to the console.",
      level: latestRuns.length > 0 ? "ready" : "review"
    },
    {
      id: "active-session",
      label: "Active paper session",
      value: activeSession?.sessionId ?? "None",
      detail: activeSession ? `${activeSession.orderCount} orders and ${activeSession.positionCount} positions.` : "No active session in readiness.",
      level: activeSession?.isActive ? "ready" : "review"
    },
    {
      id: "provider-trust",
      label: "Provider trust",
      value: `${providerTrustRows.filter((row) => row.level === "ready").length}/${providerTrustRows.length}`,
      detail: "Ready provider-trust rows versus all visible trust rows.",
      level: providerTrustRows.some((row) => row.level === "blocked")
        ? "blocked"
        : providerTrustRows.length > 0 && providerTrustRows.every((row) => row.level === "ready")
          ? "ready"
          : providerTrustRows.length > 0
            ? "review"
            : "neutral"
    },
    {
      id: "reconciliation-breaks",
      label: "Reconciliation breaks",
      value: String(reconciliationRows.length),
      detail: "Open or in-review reconciliation items surfaced from Accounting.",
      level: reconciliationRows.some((row) => row.level === "blocked") ? "blocked" : reconciliationRows.length > 0 ? "review" : "ready"
    },
    {
      id: "promotion-blockers",
      label: "Promotion blockers",
      value: String(promotionRows.length),
      detail: "Promotion work items and non-ready acceptance gates.",
      level: promotionRows.some((row) => row.level === "blocked") ? "blocked" : promotionRows.length > 0 ? "review" : "ready"
    },
    {
      id: "report-packs",
      label: "Report-pack recipients",
      value: String(reportDistributions.length),
      detail: `${pendingReportDistributions} report-pack distributions pending.`,
      level: reportDistributions.length > 0 && pendingReportDistributions === 0 ? "ready" : "review"
    }
  ];
}

function buildApiSources({
  strategy,
  trading,
  data,
  accounting,
  reporting,
  operatorInbox,
  inboxLoading,
  inboxError
}: {
  strategy: StrategyWorkspaceResponse | null;
  trading: TradingWorkspaceResponse | null;
  data: DataWorkspaceResponse | null;
  accounting: AccountingWorkspaceResponse | null;
  reporting?: ReportingWorkspaceResponse | null;
  operatorInbox: OperatorInbox | null;
  inboxLoading: boolean;
  inboxError: string | null;
}): ReadinessConsoleApiSourceBase[] {
  return [
    {
      id: "trading-readiness",
      label: "Trading readiness",
      endpoint: WORKSTATION_API_ENDPOINTS.tradingReadiness,
      status: trading?.readiness ? formatReadinessStatusValue(trading.readiness.overallStatus) : "Unavailable",
      level: trading?.readiness ? levelFromReadiness(trading.readiness.overallStatus) : "review"
    },
    {
      id: "operator-inbox",
      label: "Operator inbox",
      endpoint: WORKSTATION_API_ENDPOINTS.operatorInbox,
      status: inboxLoading ? "Loading" : operatorInbox ? `${operatorInbox.reviewCount} review items` : inboxError ?? "Unavailable",
      level: inboxLoading ? "neutral" : operatorInbox ? (operatorInbox.criticalCount > 0 ? "blocked" : operatorInbox.warningCount > 0 ? "review" : "ready") : "review"
    },
    {
      id: "strategy-runs",
      label: "Strategy runs",
      endpoint: WORKSTATION_API_ENDPOINTS.strategy,
      status: strategy ? `${strategy.runs.length} runs` : "Unavailable",
      level: strategy ? "ready" : "review"
    },
    {
      id: "data-confidence",
      label: "Provider posture",
      endpoint: WORKSTATION_API_ENDPOINTS.data,
      status: data ? `${data.providers.length} providers` : "Unavailable",
      level: data ? "ready" : "review"
    },
    {
      id: "accounting",
      label: "Accounting",
      endpoint: WORKSTATION_API_ENDPOINTS.accounting,
      status: accounting ? `${accounting.breakQueue.length} breaks` : "Unavailable",
      level: accounting ? "ready" : "review"
    },
    {
      id: "reporting",
      label: "Reporting",
      endpoint: WORKSTATION_API_ENDPOINTS.reporting,
      status: reporting ? `${reporting.reporting.profileCount} report profiles` : "Unavailable",
      level: reporting ? "ready" : "review"
    }
  ];
}

function determineOverallLevel({
  readiness,
  operatorInbox,
  inboxLoading,
  inboxError,
  checkpointGates
}: {
  readiness: TradingOperatorReadiness | null;
  operatorInbox: OperatorInbox | null;
  inboxLoading: boolean;
  inboxError: string | null;
  checkpointGates: ReadinessConsoleRow[];
}): ReadinessConsoleLevel {
  if (!readiness) {
    return hasCriticalOperatorInbox(operatorInbox) ? "blocked" : "review";
  }

  if (
    readiness.overallStatus === "Blocked" ||
    (operatorInbox?.criticalCount ?? 0) > 0 ||
    checkpointGates.some((row) => row.level === "blocked")
  ) {
    return "blocked";
  }

  if (inboxLoading || inboxError) {
    return "review";
  }

  if (
    readiness.overallStatus === "Ready" &&
    (operatorInbox?.warningCount ?? 0) === 0 &&
    checkpointGates.every((row) => row.level === "ready")
  ) {
    return "ready";
  }

  return "review";
}

function buildOverallDetail({
  readiness,
  operatorInbox,
  inboxLoading,
  inboxError,
  checkpointGates
}: {
  readiness: TradingOperatorReadiness | null;
  operatorInbox: OperatorInbox | null;
  inboxLoading: boolean;
  inboxError: string | null;
  checkpointGates: ReadinessConsoleRow[];
}): string {
  if (!readiness) {
    return buildMissingReadinessDetail(operatorInbox, inboxLoading, inboxError);
  }

  const sourceStatus = formatReadinessStatusValue(readiness.overallStatus).toLowerCase();
  if (inboxLoading) {
    return `Trading readiness is ${sourceStatus}, but the operator inbox is still loading. The console stays in review until shared work items settle.`;
  }

  if (inboxError) {
    return `Trading readiness is ${sourceStatus}, but the operator inbox failed to load: ${inboxError}. Review fallback work items before accepting readiness.`;
  }

  const reviewCount = operatorInbox?.reviewCount ?? readiness.workItems.length;
  const blockedGateCount = checkpointGates.filter((row) => row.level === "blocked").length;
  const reviewGateCount = checkpointGates.filter((row) => row.level === "review").length;
  if (blockedGateCount > 0 || reviewGateCount > 0) {
    return `${reviewCount} operator review item(s), ${blockedGateCount} blocked checkpoint(s), and ${reviewGateCount} review checkpoint(s) still need attention.`;
  }

  return `${reviewCount} operator review item(s), all full-console checkpoints, and governed report-pack readiness are clear in the web console.`;
}

function buildStatusAnnouncement(
  overallLabel: string,
  asOf: string,
  inboxLoading: boolean,
  inboxError: string | null
): string {
  if (inboxLoading) {
    return "Loading operator readiness console.";
  }

  if (inboxError) {
    return `Operator inbox failed: ${inboxError}`;
  }

  return `Operator readiness console ${overallLabel.toLowerCase()} as of ${asOf}.`;
}

function dedupeRows(rows: ReadinessConsoleRowBase[]): ReadinessConsoleRowBase[] {
  const seen = new Set<string>();
  return rows.filter((row) => {
    if (seen.has(row.id)) {
      return false;
    }

    seen.add(row.id);
    return true;
  });
}

function formatReadinessStatusValue(status: string): string {
  return status === "ReviewRequired" ? "Review required" : status;
}

function formatLevelText(level: ReadinessConsoleLevel): string {
  switch (level) {
    case "ready":
      return "Ready";
    case "review":
      return "Review";
    case "blocked":
      return "Blocked";
    case "neutral":
      return "Info";
  }
}

function metricTone(level: ReadinessConsoleLevel): MetricSnapshot["tone"] {
  switch (level) {
    case "ready":
      return "success";
    case "review":
      return "warning";
    case "blocked":
      return "danger";
    case "neutral":
      return "default";
  }
}

function slugifyId(value: string): string {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "") || "row";
}

function formatEffectiveOverallLabel(
  readinessStatusLabel: string,
  overallLevel: ReadinessConsoleLevel
): string {
  if (readinessStatusLabel === "Awaiting readiness payload") {
    return overallLevel === "blocked" ? "Blocked" : readinessStatusLabel;
  }

  if (overallLevel === "blocked") {
    return "Blocked";
  }

  if (overallLevel === "review" && readinessStatusLabel === "Ready") {
    return "Review pending";
  }

  return readinessStatusLabel;
}

function buildMissingReadinessDetail(
  operatorInbox: OperatorInbox | null,
  inboxLoading: boolean,
  inboxError: string | null
): string {
  if (inboxLoading) {
    return "Trading readiness has not loaded yet, and the operator inbox is still loading. The console stays in review until shared work items settle.";
  }

  if (inboxError) {
    return `Trading readiness has not loaded yet, and the operator inbox failed to load: ${inboxError}. Review any visible fallback work items before accepting readiness.`;
  }

  if (operatorInbox) {
    const criticalCount = Math.max(
      operatorInbox.criticalCount,
      operatorInbox.items.filter((item) => item.tone === "Critical").length
    );
    const warningCount = Math.max(
      operatorInbox.warningCount,
      operatorInbox.items.filter((item) => item.tone === "Warning").length
    );
    const severitySummary = [
      criticalCount > 0 ? formatCount(criticalCount, "critical item") : null,
      warningCount > 0 ? formatCount(warningCount, "warning") : null
    ].filter(Boolean).join(" and ");

    if (severitySummary) {
      return `Trading readiness has not loaded yet, but the operator inbox reports ${severitySummary}. Resolve the primary next action before accepting readiness.`;
    }

    return "Trading readiness has not loaded yet. The operator inbox is clean, but readiness cannot be accepted until the trading readiness payload loads.";
  }

  return "Trading readiness has not loaded yet. The console can still show any available Strategy, Data, Accounting, or Reporting payloads.";
}

function hasCriticalOperatorInbox(operatorInbox: OperatorInbox | null): boolean {
  return (operatorInbox?.criticalCount ?? 0) > 0 ||
    operatorInbox?.items.some((item) => item.tone === "Critical") === true;
}

function levelFromReadiness(status: string): ReadinessConsoleLevel {
  if (status === "Ready") {
    return "ready";
  }

  if (status === "Blocked") {
    return "blocked";
  }

  return "review";
}

function levelFromTone(tone: string): ReadinessConsoleLevel {
  if (tone === "Success") {
    return "ready";
  }

  if (tone === "Critical") {
    return "blocked";
  }

  if (tone === "Warning") {
    return "review";
  }

  return "neutral";
}

function formatCurrency(value: number): string {
  return value.toLocaleString(undefined, {
    style: "currency",
    currency: "USD",
    maximumFractionDigits: 2
  });
}

function formatReadinessUtcMinute(value: string | null | undefined, fallback: string): string {
  if (!value) {
    return fallback;
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return fallback;
  }

  return `${UTC_MONTH_LABELS[date.getUTCMonth()]} ${date.getUTCDate()}, ${padUtc(date.getUTCHours())}:${padUtc(date.getUTCMinutes())} UTC`;
}

function padUtc(value: number): string {
  return String(value).padStart(2, "0");
}

const UTC_MONTH_LABELS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

function toErrorMessage(err: unknown, fallback: string): string {
  if (err instanceof Error && err.message.trim()) {
    return err.message;
  }

  return fallback;
}
