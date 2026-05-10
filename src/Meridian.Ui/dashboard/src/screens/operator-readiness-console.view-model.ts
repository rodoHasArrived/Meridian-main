import { useCallback, useEffect, useMemo, useState } from "react";
import { getOperatorInbox } from "@/lib/api";
import { legacyWorkspaceRedirect, WORKSPACES } from "@/lib/workspace";
import { WORKSTATION_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import type {
  DataOperationsProviderRecord,
  DataOperationsWorkspaceResponse,
  GovernanceWorkspaceResponse,
  MetricSnapshot,
  OperatorInbox,
  OperatorWorkItem,
  ResearchRunRecord,
  ResearchWorkspaceResponse,
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
  route: string;
  level: ReadinessConsoleLevel;
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

type ReadinessConsoleMetricBase = Omit<ReadinessConsoleMetric, "ariaLabel" | "statusAriaLabel" | "delta" | "tone" | "detailId">;
type ReadinessConsoleApiSourceBase = Omit<ReadinessConsoleApiSource, "ariaLabel" | "statusAriaLabel">;
type ReadinessConsoleRowBase = Omit<ReadinessConsoleRow, "ariaLabel" | "statusAriaLabel" | "detailId">;

export type ReadinessConsolePanelId =
  | "latest-runs"
  | "active-paper-session"
  | "provider-trust"
  | "reconciliation-breaks"
  | "promotion-blockers"
  | "governance-report-packs";

export interface ReadinessConsolePanel {
  id: ReadinessConsolePanelId;
  title: string;
  emptyText: string;
  ariaLabel: string;
  listLabel: string;
  rows: ReadinessConsoleRow[];
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
  nextAction: ReadinessConsoleNextAction;
  metrics: ReadinessConsoleMetric[];
  metricsLabel: string;
  apiSources: ReadinessConsoleApiSource[];
  apiSourcesLabel: string;
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
  workItemsRegionLabel: string;
  workItemsListLabel: string;
  workItemsTableLabel: string;
  workItemsDetailLabel: string;
  selectedWorkItemId: string | null;
  selectedWorkItemDetail: ReadinessConsoleSelectedWorkItemDetail | null;
  selectWorkItem: (id: string) => void;
}

export interface BuildOperatorReadinessConsoleStateOptions {
  research: ResearchWorkspaceResponse | null;
  trading: TradingWorkspaceResponse | null;
  dataOperations: DataOperationsWorkspaceResponse | null;
  governance: GovernanceWorkspaceResponse | null;
  reporting?: GovernanceWorkspaceResponse | null;
  operatorInbox: OperatorInbox | null;
  inboxLoading: boolean;
  inboxError: string | null;
  selectedWorkItemId?: string | null;
  selectWorkItem?: (id: string) => void;
}

export interface OperatorReadinessConsoleServices {
  getOperatorInbox: (fundAccountId?: string) => Promise<OperatorInbox>;
}

const defaultServices: OperatorReadinessConsoleServices = {
  getOperatorInbox: (fundAccountId?: string) => getOperatorInbox(fundAccountId)
};

const workstationWorkspaceKeys = new Set<string>(WORKSPACES.map((workspace) => workspace.key));

export function useOperatorReadinessConsoleViewModel(
  payload: Omit<BuildOperatorReadinessConsoleStateOptions, "operatorInbox" | "inboxLoading" | "inboxError">,
  services: OperatorReadinessConsoleServices = defaultServices
): ReadinessConsoleState {
  const [operatorInbox, setOperatorInbox] = useState<OperatorInbox | null>(null);
  const [inboxLoading, setInboxLoading] = useState(true);
  const [inboxError, setInboxError] = useState<string | null>(null);
  const [selectedWorkItemId, setSelectedWorkItemId] = useState<string | null>(null);

  const activeFundAccountId = payload.trading?.readiness?.brokerageSync?.fundAccountId;
  const selectWorkItem = useCallback((id: string) => {
    setSelectedWorkItemId(id);
  }, []);

  useEffect(() => {
    let cancelled = false;
    setInboxLoading(true);
    setInboxError(null);
    setOperatorInbox(null);

    Promise.resolve(services.getOperatorInbox(activeFundAccountId))
      .then((inbox) => {
        if (!cancelled) {
          setOperatorInbox(inbox ?? null);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setOperatorInbox(null);
          setInboxError(toErrorMessage(err, "Operator inbox failed to load."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setInboxLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [activeFundAccountId, services]);

  return useMemo(
    () => buildOperatorReadinessConsoleState({
      ...payload,
      operatorInbox,
      inboxLoading,
      inboxError,
      selectedWorkItemId,
      selectWorkItem
    }),
    [inboxError, inboxLoading, operatorInbox, payload, selectedWorkItemId, selectWorkItem]
  );
}

export function buildOperatorReadinessConsoleState({
  research,
  trading,
  dataOperations,
  governance,
  reporting,
  operatorInbox,
  inboxLoading,
  inboxError,
  selectedWorkItemId,
  selectWorkItem
}: BuildOperatorReadinessConsoleStateOptions): ReadinessConsoleState {
  const readiness = trading?.readiness ?? null;
  const workItems = operatorInbox?.items ?? readiness?.workItems ?? [];
  const latestRuns = withRowPresentation(buildLatestRunRows(research?.runs ?? []), "latest-runs");
  const activeSessionFacts = withRowPresentation(buildActiveSessionFacts(readiness), "active-session");
  const providerTrustRows = withRowPresentation(buildProviderTrustRows(readiness, dataOperations?.providers ?? []), "provider-trust");
  const reconciliationRows = withRowPresentation(buildReconciliationRows(governance), "reconciliation");
  const promotionRows = withRowPresentation(buildPromotionRows(readiness, workItems), "promotion");
  const prioritizedWorkItems = prioritizeWorkItems(workItems);
  const reportPackFacts = withRowPresentation(buildReportPackFacts(reporting ?? governance), "report-pack");
  const workItemRows = withRowPresentation(buildWorkItemRows(prioritizedWorkItems), "work-items");
  const selectedWorkItemDetail = buildSelectedWorkItemDetail(workItemRows, selectedWorkItemId ?? null);
  const nextAction = buildNextAction({
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
    reporting: reporting ?? governance
  }));
  const overallLevel = determineOverallLevel({
    readiness,
    operatorInbox,
    inboxLoading,
    inboxError,
    reconciliationRows,
    promotionRows,
    reportPackFacts
  });
  const readinessStatusLabel = readiness
    ? formatReadinessStatusValue(readiness.overallStatus)
    : "Awaiting readiness payload";
  const overallLabel = formatEffectiveOverallLabel(readinessStatusLabel, overallLevel);
  const asOf = operatorInbox?.asOf ?? readiness?.asOf ?? "Unavailable";

  return {
    title: "Operator Readiness Console",
    subtitle: "Runs, paper sessions, provider trust, reconciliation, promotion, and report-pack posture from shared readiness payloads.",
    overallLabel,
    overallDetail: buildOverallDetail({
      readiness,
      operatorInbox,
      inboxLoading,
      inboxError,
      reconciliationRows,
      promotionRows,
      reportPackFacts
    }),
    overallLevel,
    asOf,
    statusAnnouncement: buildStatusAnnouncement(overallLabel, asOf, inboxLoading, inboxError),
    inboxSummary: operatorInbox?.summary ?? "Operator inbox not loaded; using workstation payload fallbacks where available.",
    inboxLoadingLabel: inboxLoading ? "Loading operator inbox..." : null,
    inboxErrorText: inboxError,
    nextAction,
    metrics,
    metricsLabel: "Operator readiness metrics",
    apiSources: withApiSourcePresentation(buildApiSources({
      research,
      trading,
      dataOperations,
      governance,
      reporting,
      operatorInbox,
      inboxLoading,
      inboxError
    })),
    apiSourcesLabel: "Shared readiness API sources",
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
      reportPackFacts
    }),
    workItems: workItemRows,
    workItemsSummary: buildWorkItemsSummary(prioritizedWorkItems, workItemRows.length),
    workItemsOverflowText: buildWorkItemsOverflowText(prioritizedWorkItems.length, workItemRows.length),
    workItemsRegionLabel: "Operator inbox review work items",
    workItemsListLabel: "Prioritized operator work items",
    workItemsTableLabel: "Prioritized operator work items table",
    workItemsDetailLabel: "Selected operator work item detail",
    selectedWorkItemId: selectedWorkItemDetail?.id ?? null,
    selectedWorkItemDetail,
    selectWorkItem: selectWorkItem ?? (() => undefined)
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
    ariaLabel: `${row.label}: ${row.value}. ${row.detail} ${row.meta}`,
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
  reportPackFacts
}: {
  latestRuns: ReadinessConsoleRow[];
  activeSessionFacts: ReadinessConsoleRow[];
  providerTrustRows: ReadinessConsoleRow[];
  reconciliationRows: ReadinessConsoleRow[];
  promotionRows: ReadinessConsoleRow[];
  reportPackFacts: ReadinessConsoleRow[];
}): ReadinessConsolePanel[] {
  return [
    buildConsolePanel({
      id: "latest-runs",
      title: "Latest runs",
      emptyText: "No Strategy runs loaded.",
      rows: latestRuns
    }),
    buildConsolePanel({
      id: "active-paper-session",
      title: "Active paper session",
      emptyText: "No active paper session loaded.",
      rows: activeSessionFacts
    }),
    buildConsolePanel({
      id: "provider-trust",
      title: "Provider trust",
      emptyText: "No provider trust rows loaded.",
      rows: providerTrustRows
    }),
    buildConsolePanel({
      id: "reconciliation-breaks",
      title: "Reconciliation breaks",
      emptyText: "No open or in-review reconciliation breaks.",
      rows: reconciliationRows
    }),
    buildConsolePanel({
      id: "promotion-blockers",
      title: "Promotion blockers",
      emptyText: "No promotion blockers surfaced by readiness.",
      rows: promotionRows
    }),
    buildConsolePanel({
      id: "governance-report-packs",
      title: "Governance report packs",
      emptyText: "No reporting readiness payload loaded.",
      rows: reportPackFacts
    })
  ];
}

function buildConsolePanel({
  id,
  title,
  emptyText,
  rows
}: {
  id: ReadinessConsolePanelId;
  title: string;
  emptyText: string;
  rows: ReadinessConsoleRow[];
}): ReadinessConsolePanel {
  return {
    id,
    title,
    emptyText,
    ariaLabel: `${title} readiness evidence`,
    listLabel: `${title} rows`,
    rows
  };
}

function buildLatestRunRows(runs: ResearchRunRecord[]): ReadinessConsoleRowBase[] {
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
      meta: `Created ${session.createdAt}`,
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
  providers: DataOperationsProviderRecord[]
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
      : status.health === "Failed" || status.health === "Degraded"
        ? "blocked"
        : "review"
  };
}

function buildReconciliationRows(governance: GovernanceWorkspaceResponse | null): ReadinessConsoleRowBase[] {
  const directBreaks = (governance?.breakQueue ?? [])
    .filter((item) => item.status === "Open" || item.status === "InReview")
    .slice(0, 5)
    .map(buildBreakQueueRow);

  if (directBreaks.length > 0) {
    return directBreaks;
  }

  return (governance?.reconciliationQueue ?? [])
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
    .forEach((item) => rows.push(buildWorkItemRow(item, false)));

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

function buildReportPackFacts(governance: GovernanceWorkspaceResponse | null): ReadinessConsoleRowBase[] {
  const reporting = governance?.reporting ?? null;
  if (!reporting) {
    return [{
      id: "report-pack",
      label: "Report-pack readiness",
      value: "Unavailable",
      detail: "Governance reporting payload has not loaded.",
      meta: "Wait for Accounting/Reporting bootstrap recovery.",
      level: "review"
    }];
  }

  return [
    {
      id: "report-pack",
      label: "Report-pack readiness",
      value: reporting.reportPackTargets.length > 0 ? "Targets present" : "No targets",
      detail: reporting.summary,
      meta: `${reporting.profileCount} profiles - ${reporting.reportPackTargets.join(", ") || "no targets"}`,
      level: reporting.profileCount > 0 && reporting.reportPackTargets.length > 0 ? "ready" : "review"
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

function buildWorkItemRows(workItems: OperatorWorkItem[]): ReadinessConsoleRowBase[] {
  return workItems.slice(0, 6).map((item) => buildWorkItemRow(item, true));
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

  return {
    id: item.workItemId,
    label: item.label,
    value: item.tone,
    detail: item.detail,
    meta: [item.workspace, item.targetPageTag, item.runId, item.auditReference].filter(Boolean).join(" - ") || item.kind,
    level: levelFromTone(item.tone),
    action
  };
}

function buildWorkItemAction(item: OperatorWorkItem): ReadinessConsoleRowAction | null {
  const route = item.kind === "SecurityMasterCoverage"
    ? fallbackRouteForWorkItemKind(item.kind)
    : normalizeTargetRoute(item.targetRoute) ?? fallbackRouteForWorkItemKind(item.kind);
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

function normalizeTargetRoute(route: string | null | undefined): string | null {
  const trimmed = route?.trim();
  if (!trimmed || !trimmed.startsWith("/") || trimmed.startsWith("//")) {
    return null;
  }

  const canonicalRoute = legacyWorkspaceRedirect(trimmed) ?? trimmed;
  const routeWorkspace = canonicalRoute.split(/[/?#]/).filter(Boolean)[0];
  if (!routeWorkspace || !workstationWorkspaceKeys.has(routeWorkspace)) {
    return null;
  }

  return canonicalRoute;
}

function fallbackRouteForWorkItemKind(kind: OperatorWorkItem["kind"]): string {
  switch (kind) {
    case "PaperReplay":
    case "PromotionReview":
    case "BrokerageSync":
    case "ExecutionControl":
      return "/trading/readiness";
    case "SecurityMasterCoverage":
      return "/data/security-master";
    case "ReconciliationBreak":
      return "/accounting/reconciliation";
    case "ReportPackApproval":
      return "/reporting";
    case "ProviderTrustGate":
      return "/data";
  }
}

function actionLabelForWorkItemKind(kind: OperatorWorkItem["kind"]): string {
  switch (kind) {
    case "PaperReplay":
      return "Open replay evidence";
    case "PromotionReview":
      return "Open promotion review";
    case "BrokerageSync":
      return "Open brokerage sync";
    case "SecurityMasterCoverage":
      return "Open Security Master";
    case "ReconciliationBreak":
      return "Open break queue";
    case "ReportPackApproval":
      return "Open report packs";
    case "ProviderTrustGate":
      return "Open provider trust";
    case "ExecutionControl":
      return "Open execution controls";
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

function buildSelectedWorkItemDetail(
  rows: ReadinessConsoleRow[],
  selectedWorkItemId: string | null
): ReadinessConsoleSelectedWorkItemDetail | null {
  if (rows.length === 0) {
    return null;
  }

  const selectedRow = rows.find((row) => row.id === selectedWorkItemId) ?? rows[0];
  const actionRoute = selectedRow.action?.route ?? "No route action";

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
      { label: "Route", value: actionRoute },
      { label: "Evidence", value: selectedRow.meta }
    ],
    action: selectedRow.action ?? null
  };
}

interface NextActionCandidate {
  title: string;
  detail: string;
  meta: string;
  label: string;
  route: string;
  level: ReadinessConsoleLevel;
  sourcePriority: number;
  index: number;
}

function buildNextAction({
  workItemRows,
  activeSessionFacts,
  providerTrustRows,
  reconciliationRows,
  promotionRows,
  reportPackFacts
}: {
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
        route: row.action?.route ?? "/trading/readiness",
        sourcePriority: 0,
        index
      })),
    ...reconciliationRows.map((row, index) => nextActionCandidateFromRow(row, {
      label: "Open break queue",
      route: "/accounting/reconciliation",
      sourcePriority: 1,
      index
    })),
    ...promotionRows.map((row, index) => nextActionCandidateFromRow(row, {
      label: "Open promotion review",
      route: "/trading/readiness",
      sourcePriority: 2,
      index
    })),
    ...providerTrustRows
      .filter((row) => row.level === "blocked" || row.level === "review")
      .map((row, index) => nextActionCandidateFromRow(row, {
        label: "Open provider trust",
        route: "/data",
        sourcePriority: 3,
        index
      })),
    ...reportPackFacts
      .filter((row) => row.level === "blocked" || row.level === "review")
      .map((row, index) => nextActionCandidateFromRow(row, {
        label: "Open report packs",
        route: "/reporting",
        sourcePriority: 4,
        index
      })),
    ...activeSessionFacts
      .filter((row) => row.level === "blocked" || row.level === "review")
      .map((row, index) => nextActionCandidateFromRow(row, {
        label: "Open Trading cockpit",
        route: "/trading",
        sourcePriority: 5,
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
    route: "/trading",
    level: "ready" as ReadinessConsoleLevel,
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
    ariaLabel: `Primary next action: ${candidate.title}. ${candidate.detail} ${candidate.meta}`,
    actionAriaLabel: `${candidate.label}: ${candidate.title}`
  };
}

function nextActionCandidateFromRow(
  row: ReadinessConsoleRow,
  options: {
    label: string;
    route: string;
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
  reporting: GovernanceWorkspaceResponse | null;
}): ReadinessConsoleMetricBase[] {
  const activeSession = readiness?.activeSession;
  const reportTargets = reporting?.reporting.reportPackTargets.length ?? 0;

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
      label: "Report-pack targets",
      value: String(reportTargets),
      detail: "Governed output targets available from Reporting.",
      level: reportTargets > 0 ? "ready" : "review"
    }
  ];
}

function buildApiSources({
  research,
  trading,
  dataOperations,
  governance,
  reporting,
  operatorInbox,
  inboxLoading,
  inboxError
}: {
  research: ResearchWorkspaceResponse | null;
  trading: TradingWorkspaceResponse | null;
  dataOperations: DataOperationsWorkspaceResponse | null;
  governance: GovernanceWorkspaceResponse | null;
  reporting?: GovernanceWorkspaceResponse | null;
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
      status: research ? `${research.runs.length} runs` : "Unavailable",
      level: research ? "ready" : "review"
    },
    {
      id: "data-confidence",
      label: "Provider posture",
      endpoint: WORKSTATION_API_ENDPOINTS.data,
      status: dataOperations ? `${dataOperations.providers.length} providers` : "Unavailable",
      level: dataOperations ? "ready" : "review"
    },
    {
      id: "governance",
      label: "Accounting",
      endpoint: WORKSTATION_API_ENDPOINTS.accounting,
      status: governance ? `${governance.breakQueue.length} breaks` : "Unavailable",
      level: governance ? "ready" : "review"
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
  reconciliationRows,
  promotionRows,
  reportPackFacts
}: {
  readiness: TradingOperatorReadiness | null;
  operatorInbox: OperatorInbox | null;
  inboxLoading: boolean;
  inboxError: string | null;
  reconciliationRows: ReadinessConsoleRow[];
  promotionRows: ReadinessConsoleRow[];
  reportPackFacts: ReadinessConsoleRow[];
}): ReadinessConsoleLevel {
  if (!readiness) {
    return hasCriticalOperatorInbox(operatorInbox) ? "blocked" : "review";
  }

  if (
    readiness.overallStatus === "Blocked" ||
    (operatorInbox?.criticalCount ?? 0) > 0 ||
    reconciliationRows.some((row) => row.level === "blocked") ||
    promotionRows.some((row) => row.level === "blocked")
  ) {
    return "blocked";
  }

  if (inboxLoading || inboxError) {
    return "review";
  }

  if (
    readiness.overallStatus === "Ready" &&
    (operatorInbox?.warningCount ?? 0) === 0 &&
    reconciliationRows.length === 0 &&
    promotionRows.length === 0 &&
    !hasReportPackReadinessGap(reportPackFacts)
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
  reconciliationRows,
  promotionRows,
  reportPackFacts
}: {
  readiness: TradingOperatorReadiness | null;
  operatorInbox: OperatorInbox | null;
  inboxLoading: boolean;
  inboxError: string | null;
  reconciliationRows: ReadinessConsoleRow[];
  promotionRows: ReadinessConsoleRow[];
  reportPackFacts: ReadinessConsoleRow[];
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
  const reportPackReviewCount = reportPackFacts.filter((row) => row.level !== "ready").length;
  if (reportPackReviewCount > 0) {
    return `${reviewCount} operator review item(s), ${reconciliationRows.length} reconciliation item(s), ${promotionRows.length} promotion blocker(s), and ${reportPackReviewCount} report-pack readiness item(s) still need review.`;
  }

  return `${reviewCount} operator review item(s), ${reconciliationRows.length} reconciliation item(s), ${promotionRows.length} promotion blocker(s), and governed report-pack readiness are visible in the web console.`;
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

function hasReportPackReadinessGap(reportPackFacts: ReadinessConsoleRow[]): boolean {
  return reportPackFacts.length === 0 || reportPackFacts.some((row) => row.level !== "ready");
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

  return "Trading readiness has not loaded yet. The console can still show any available Strategy, Data, or Governance payloads.";
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

function toErrorMessage(err: unknown, fallback: string): string {
  if (err instanceof Error && err.message.trim()) {
    return err.message;
  }

  return fallback;
}
