import {
  buildOrderRequirementText,
  buildOrderTicketAcknowledgementState,
  buildOrderTicketStatusAnnouncement,
  buildOrderTicketSubmitDisabledReason
} from "./trading-screen.order-ticket-text";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useRequestLifecycle, type RequestLifecycleStatus } from "@/hooks/use-request-lifecycle";
import * as workstationApi from "@/lib/api";
import { formatCurrency } from "@/lib/format";
import { normalizeFundAccountGuid } from "@/lib/fund-account-scope";
import type { ApiRequestOptions, ApprovePromotionRequest, RejectPromotionRequest } from "@/lib/api";
import { evidenceWorkbenchPath, normalizeLocalWorkstationRoute, WORKSTATION_ROUTE_CATALOG, workflowTargetPath } from "@/lib/workspace";
import {
  buildCircuitBreakerActionResult,
  buildExecutionControlsPanel
} from "@/screens/trading-screen.execution-controls";
import {
  buildTradingReadinessSummaryRows,
  formatReadinessStatusValue,
  formatTradingUtcDateTime as formatUtcDateTime,
  type AcceptanceLevel,
  type TradingReadinessSummaryRow
} from "@/screens/trading-screen.readiness-summary";
import type {
  ExecutionAuditEntry,
  ExecutionCircuitBreakerActivationResponse,
  ExecutionControlSnapshot,
  OperatorWorkItem,
  OrderResult,
  OrderSubmitRequest,
  PaperSessionDetail,
  PaperSessionReplayVerification,
  PaperSessionSummary,
  PromotionDecisionResult,
  PromotionEvaluationResult,
  PromotionRecord,
  ReplayFileRecord,
  ReplayStatus,
  TradingActionResult,
  UpdateExecutionCircuitBreakerRequest,
  TradingFill,
  TradingOperatorReadiness,
  TradingOrder,
  TradingPosition,
  TradingRiskState,
  TradingWorkspaceResponse
} from "@/types";

export {
  formatReadinessStatusValue,
  mapBrokerageSyncLevel,
  mapReadinessStatusLevel
} from "@/screens/trading-screen.readiness-summary";
export type {
  AcceptanceLevel,
  TradingReadinessSummaryRow
} from "@/screens/trading-screen.readiness-summary";

export type TradingDataTone = "default" | "success" | "warning" | "danger" | "muted";
export type TradingRouteWorkstream = "orders" | "positions" | "risk";
export type TradingWorkflowPanelId = "strategy" | "replay" | "promotion";

export interface TradingScreenRouteState {
  pathname: string;
  workstream: TradingRouteWorkstream;
  title: string;
  description: string;
}

export interface TradingWorkflowCommandState {
  id: TradingWorkflowPanelId;
  label: string;
  ariaLabel: string;
  controlsId: string;
  icon: "strategy" | "replay" | "promotion";
  expanded: boolean;
  active: boolean;
}

export interface TradingScreenChipState {
  label: string;
  value: string;
}

export interface TradingWorkflowStripState {
  ariaLabel: string;
  eyebrow: string;
  statusId: string;
  statusText: string;
  chips: TradingScreenChipState[];
  commands: TradingWorkflowCommandState[];
}

export interface TradingLoadingChipState {
  label: string;
  value: string;
}

export interface TradingLoadingState {
  role: "status";
  ariaBusy: true;
  ariaLive: "polite";
  regionLabel: string;
  titleId: string;
  detailId: string;
  title: string;
  detail: string;
  statusLabel: string;
  routeLabel: string;
  chips: TradingLoadingChipState[];
}

export interface TradingScreenShellViewModel {
  route: TradingScreenRouteState;
  loadingState: TradingLoadingState;
  headerChips: TradingScreenChipState[];
  workflowStrip: TradingWorkflowStripState;
  strategySheetOpen: boolean;
  replaySheetOpen: boolean;
  promotionSheetOpen: boolean;
  openWorkflowPanel: (panel: TradingWorkflowPanelId) => void;
  closeWorkflowPanel: (panel: TradingWorkflowPanelId) => void;
  setWorkflowPanelOpen: (panel: TradingWorkflowPanelId, open: boolean) => void;
}

const tradingRouteCopy: Record<TradingRouteWorkstream, { title: string; description: string }> = {
  orders: {
    title: "Orders blotter",
    description: "Working and partially filled orders remain visible in real time so you can cancel, replace, or monitor fill progress without leaving the cockpit."
  },
  positions: {
    title: "Position book",
    description: "Open positions with mark prices, exposure, and unrealized P&L are refreshed from the live execution layer each time the workspace loads."
  },
  risk: {
    title: "Risk guardrails",
    description: "Paper thresholds, drawdown limits, and buying-power constraints are evaluated on every order submission and displayed here for operator review."
  }
};

export function useTradingScreenShellViewModel({
  pathname,
  data
}: {
  pathname: string;
  data: TradingWorkspaceResponse | null;
}): TradingScreenShellViewModel {
  const [activeWorkflowPanel, setActiveWorkflowPanel] = useState<TradingWorkflowPanelId | null>(null);

  const openWorkflowPanel = useCallback((panel: TradingWorkflowPanelId) => {
    setActiveWorkflowPanel(panel);
  }, []);

  const closeWorkflowPanel = useCallback((panel: TradingWorkflowPanelId) => {
    setActiveWorkflowPanel((current) => current === panel ? null : current);
  }, []);

  const setWorkflowPanelOpen = useCallback((panel: TradingWorkflowPanelId, open: boolean) => {
    setActiveWorkflowPanel((current) => {
      if (open) {
        return panel;
      }

      return current === panel ? null : current;
    });
  }, []);

  return useMemo(
    () => buildTradingScreenShellViewModel({
      pathname,
      data,
      activeWorkflowPanel,
      openWorkflowPanel,
      closeWorkflowPanel,
      setWorkflowPanelOpen
    }),
    [activeWorkflowPanel, closeWorkflowPanel, data, openWorkflowPanel, pathname, setWorkflowPanelOpen]
  );
}

export function buildTradingScreenShellViewModel({
  pathname,
  data,
  activeWorkflowPanel,
  openWorkflowPanel = () => {},
  closeWorkflowPanel = () => {},
  setWorkflowPanelOpen = () => {}
}: {
  pathname: string;
  data: TradingWorkspaceResponse | null;
  activeWorkflowPanel: TradingWorkflowPanelId | null;
  openWorkflowPanel?: (panel: TradingWorkflowPanelId) => void;
  closeWorkflowPanel?: (panel: TradingWorkflowPanelId) => void;
  setWorkflowPanelOpen?: (panel: TradingWorkflowPanelId, open: boolean) => void;
}): TradingScreenShellViewModel {
  const workstream = resolveTradingWorkstream(pathname);
  const routeCopy = tradingRouteCopy[workstream];
  const positionCount = data?.positions.length ?? 0;
  const openOrderCount = data?.openOrders.length ?? 0;
  const fillCount = data?.fills.length ?? 0;
  const connection = data?.brokerage.connection ?? "Unavailable";
  const activeLabel = activeWorkflowPanel ? workflowPanelLabel(activeWorkflowPanel) : "None";

  return {
    route: {
      pathname,
      workstream,
      title: routeCopy.title,
      description: routeCopy.description
    },
    loadingState: buildTradingLoadingState(pathname),
    headerChips: [
      { label: "Route", value: pathname },
      { label: "Account", value: data?.brokerage.account ?? "-" },
      { label: "Environment", value: data?.brokerage.environment.toUpperCase() ?? "-" },
      { label: "Orders", value: String(openOrderCount) },
      { label: "Fills", value: String(fillCount) }
    ],
    workflowStrip: {
      ariaLabel: "Workflow control strip",
      eyebrow: "Workflow tools",
      statusId: "trading-workflow-panel-status",
      statusText: activeWorkflowPanel
        ? `${activeLabel} panel open. Press Escape or the close button to return to the trading cockpit.`
        : "No workflow panel open.",
      chips: [
        { label: "Positions", value: String(positionCount) },
        { label: "Connection", value: connection },
        { label: "Panel", value: activeLabel }
      ],
      commands: [
        buildWorkflowCommand("strategy", activeWorkflowPanel),
        buildWorkflowCommand("replay", activeWorkflowPanel),
        buildWorkflowCommand("promotion", activeWorkflowPanel)
      ]
    },
    strategySheetOpen: activeWorkflowPanel === "strategy",
    replaySheetOpen: activeWorkflowPanel === "replay",
    promotionSheetOpen: activeWorkflowPanel === "promotion",
    openWorkflowPanel,
    closeWorkflowPanel,
    setWorkflowPanelOpen
  };
}

export function buildTradingLoadingState(pathname: string): TradingLoadingState {
  const workstream = resolveTradingWorkstream(pathname);
  const routeCopy = tradingRouteCopy[workstream];

  return {
    role: "status",
    ariaBusy: true,
    ariaLive: "polite",
    regionLabel: "Trading cockpit loading state",
    titleId: "trading-loading-title",
    detailId: "trading-loading-detail",
    title: "Loading Trading",
    detail: "Waiting for paper-trading state, order flow, brokerage wiring, and risk guardrail snapshots.",
    statusLabel: "Loading",
    routeLabel: routeCopy.title,
    chips: [
      { label: "Route", value: pathname },
      { label: "Workstream", value: routeCopy.title },
      { label: "Snapshots", value: "Pending" }
    ]
  };
}

export function resolveTradingWorkstream(pathname: string): TradingRouteWorkstream {
  if (pathname.includes("/positions")) {
    return "positions";
  }

  if (pathname.includes("/risk")) {
    return "risk";
  }

  return "orders";
}

function buildWorkflowCommand(
  id: TradingWorkflowPanelId,
  activeWorkflowPanel: TradingWorkflowPanelId | null
): TradingWorkflowCommandState {
  const label = workflowPanelLabel(id);
  const expanded = activeWorkflowPanel === id;

  return {
    id,
    label,
    ariaLabel: expanded ? `${label} panel is open` : workflowPanelOpenAriaLabel(id),
    controlsId: workflowPanelControlsId(id),
    icon: id,
    expanded,
    active: expanded
  };
}

function workflowPanelLabel(panel: TradingWorkflowPanelId): string {
  if (panel === "strategy") {
    return "Strategy controls";
  }

  if (panel === "replay") {
    return "Session replay";
  }

  return "Promotion gate";
}

function workflowPanelOpenAriaLabel(panel: TradingWorkflowPanelId): string {
  if (panel === "strategy") {
    return "Open strategy controls panel";
  }

  if (panel === "replay") {
    return "Open session replay controls panel";
  }

  return "Open promotion gate panel";
}

function workflowPanelControlsId(panel: TradingWorkflowPanelId): string {
  if (panel === "strategy") {
    return "strategy-lifecycle-panel";
  }

  if (panel === "replay") {
    return "session-replay-panel";
  }

  return "promotion-gate-panel";
}

export interface TradingBlotterField {
  label: string;
  value: string;
  tone: TradingDataTone;
}

export interface TradingPositionRow {
  id: string;
  positionKey: string;
  symbol: string;
  side: string;
  quantity: string;
  averagePrice: string;
  markPrice: string;
  dayPnl: string;
  unrealizedPnl: string;
  exposure: string;
  dayPnlTone: TradingDataTone;
  unrealizedPnlTone: TradingDataTone;
  isSelected: boolean;
  detailPanelId: string;
  ariaExpanded: boolean;
  selectAriaLabel: string;
  closeActionLabel: string;
  closeTitleLabel: string;
  closeAriaLabel: string;
  ariaLabel: string;
}

export interface TradingOrderRow {
  id: string;
  orderId: string;
  symbol: string;
  side: string;
  type: string;
  quantity: string;
  limitPrice: string;
  status: string;
  submittedAt: string;
  statusTone: TradingDataTone;
  isSelected: boolean;
  detailPanelId: string;
  ariaExpanded: boolean;
  selectAriaLabel: string;
  cancelActionLabel: string;
  cancelTitleLabel: string;
  cancelAriaLabel: string;
  ariaLabel: string;
}

export interface TradingFillRow {
  id: string;
  fillId: string;
  orderId: string;
  symbol: string;
  side: string;
  quantity: string;
  price: string;
  venue: string;
  timestamp: string;
  cells: string[];
  isSelected: boolean;
  detailPanelId: string;
  ariaExpanded: boolean;
  selectAriaLabel: string;
  ariaLabel: string;
}

export interface TradingBlotterDetail {
  id: string;
  title: string;
  subtitle: string;
  statusLabel: string;
  statusTone: TradingDataTone;
  ariaLabel: string;
  detail: string;
  fields: TradingBlotterField[];
}

export interface TradingBlotterViewModel {
  positionRows: TradingPositionRow[];
  orderRows: TradingOrderRow[];
  fillRows: TradingFillRow[];
  selectedPosition: TradingBlotterDetail | null;
  selectedOrder: TradingBlotterDetail | null;
  selectedFill: TradingBlotterDetail | null;
  selectedPositionRowId: string | null;
  selectedOrderRowId: string | null;
  selectedFillRowId: string | null;
  hasPositions: boolean;
  hasOpenOrders: boolean;
  hasFills: boolean;
  positionsTableLabel: string;
  ordersTableLabel: string;
  fillsTableLabel: string;
  positionDetailId: string;
  orderDetailId: string;
  fillDetailId: string;
  positionEmptyText: string;
  orderEmptyText: string;
  fillEmptyText: string;
  cancelAllDisabled: boolean;
  cancelAllDisabledReason: string | null;
  cancelAllAriaLabel: string;
  selectPosition: (id: string) => void;
  selectOrder: (id: string) => void;
  selectFill: (id: string) => void;
}

export interface TradingReadinessWorkItemRow {
  workItemId: string;
  kind: string;
  label: string;
  detail: string;
  tone: TradingDataTone;
  metadataText: string | null;
  action: TradingReadinessWorkItemAction | null;
  ariaLabel: string;
}

export interface TradingReadinessWorkItemAction {
  label: string;
  href: string;
  ariaLabel: string;
  detail: string;
}

export interface TradingReadinessWarningRow {
  id: string;
  text: string;
  ariaLabel: string;
}

export interface TradingReadinessEvidenceAction {
  label: string;
  href: string;
  ariaLabel: string;
}

export interface TradingReadinessState {
  readiness: TradingOperatorReadiness | null;
  refreshing: boolean;
  errorText: string | null;
  workItems: OperatorWorkItem[];
  warnings: string[];
  summaryRows: TradingReadinessSummaryRow[];
  summaryLabel: string;
  hasOperatorAttention: boolean;
  visibleWorkItems: TradingReadinessWorkItemRow[];
  hiddenWorkItemCount: number;
  workItemOverflowLabel: string | null;
  visibleWarnings: TradingReadinessWarningRow[];
  hiddenWarningCount: number;
  warningOverflowLabel: string | null;
  primaryWorkItemKind: string | null;
  workItemSummaryText: string;
  workItemListLabel: string;
  evidenceAction: TradingReadinessEvidenceAction;
  refreshButtonLabel: string;
  refreshAriaLabel: string;
  refreshBusyLabel: string | null;
  refreshDisabled: boolean;
  refreshDisabledReason: string | null;
  statusAnnouncement: string;
  requestStatus: RequestLifecycleStatus;
}

export interface TradingReadinessViewModel extends TradingReadinessState {
  refresh: () => Promise<void>;
}

export interface TradingReadinessServices {
  getTradingReadiness: (options?: ApiRequestOptions & { fundAccountId?: string }) => Promise<TradingOperatorReadiness | null>;
}

export interface BuildTradingReadinessStateOptions {
  readiness: TradingOperatorReadiness | null;
  refreshing: boolean;
  errorText: string | null;
  requestStatus?: RequestLifecycleStatus;
}

const defaultTradingReadinessServices: TradingReadinessServices = {
  getTradingReadiness: (options?: ApiRequestOptions) => workstationApi.getTradingReadiness(options)
};

const idleTradingReadinessStatus: RequestLifecycleStatus = {
  operation: "trading readiness handoff refresh",
  phase: "idle",
  inFlight: false,
  version: 0,
  message: "Ready to refresh trading readiness.",
  error: null,
  startedAt: null,
  settledAt: null,
  lastSucceededAt: null,
  staleDiscardCount: 0,
  backoff: { attempt: 0, retryCount: 0, nextRetryDelayMs: null, maxRetries: 0 }
};

const idleExecutionEvidenceStatus: RequestLifecycleStatus = {
  operation: "execution evidence refresh",
  phase: "idle",
  inFlight: false,
  version: 0,
  message: "Ready to refresh execution evidence.",
  error: null,
  startedAt: null,
  settledAt: null,
  lastSucceededAt: null,
  staleDiscardCount: 0,
  backoff: { attempt: 0, retryCount: 0, nextRetryDelayMs: null, maxRetries: 0 }
};

const visibleWorkItemLimit = 4;
const visibleWarningLimit = 3;
const tradingPositionDetailId = "trading-position-detail";
const tradingOrderDetailId = "trading-order-detail";
const tradingFillDetailId = "trading-fill-detail";

export function useTradingBlotterViewModel(data: TradingWorkspaceResponse | null): TradingBlotterViewModel {
  const [selectedPositionId, setSelectedPositionId] = useState<string | null>(null);
  const [selectedOrderId, setSelectedOrderId] = useState<string | null>(null);
  const [selectedFillId, setSelectedFillId] = useState<string | null>(null);

  return useMemo(
    () => buildTradingBlotterViewModel({
      data,
      selectedPositionId,
      selectedOrderId,
      selectedFillId,
      selectPosition: setSelectedPositionId,
      selectOrder: setSelectedOrderId,
      selectFill: setSelectedFillId
    }),
    [data, selectedFillId, selectedOrderId, selectedPositionId]
  );
}

export function buildTradingBlotterViewModel({
  data,
  selectedPositionId = null,
  selectedOrderId = null,
  selectedFillId = null,
  selectPosition = () => {},
  selectOrder = () => {},
  selectFill = () => {}
}: {
  data: TradingWorkspaceResponse | null;
  selectedPositionId?: string | null;
  selectedOrderId?: string | null;
  selectedFillId?: string | null;
  selectPosition?: (id: string) => void;
  selectOrder?: (id: string) => void;
  selectFill?: (id: string) => void;
}): TradingBlotterViewModel {
  const positions = data?.positions ?? [];
  const orders = data?.openOrders ?? [];
  const fills = data?.fills ?? [];
  const effectivePositionId = resolveSelectedId(positions, selectedPositionId, positionRowId);
  const effectiveOrderId = resolveSelectedId(orders, selectedOrderId, orderRowId);
  const effectiveFillId = resolveSelectedId(fills, selectedFillId, fillRowId);

  const positionRows = positions.map((position, index) => buildPositionRow(position, index, effectivePositionId));
  const orderRows = orders.map((order, index) => buildOrderRow(order, index, effectiveOrderId));
  const fillRows = fills.map((fill, index) => buildFillRow(fill, index, effectiveFillId));
  const selectedPositionRow = positionRows.find((row) => row.id === effectivePositionId) ?? null;
  const selectedOrderRow = orderRows.find((row) => row.id === effectiveOrderId) ?? null;
  const selectedFillRow = fillRows.find((row) => row.id === effectiveFillId) ?? null;

  return {
    positionRows,
    orderRows,
    fillRows,
    selectedPosition: selectedPositionRow ? buildPositionDetail(selectedPositionRow, data?.risk ?? null) : null,
    selectedOrder: selectedOrderRow ? buildOrderDetail(selectedOrderRow, data?.brokerage.provider ?? null) : null,
    selectedFill: selectedFillRow ? buildFillDetail(selectedFillRow, data?.brokerage.provider ?? null) : null,
    selectedPositionRowId: effectivePositionId,
    selectedOrderRowId: effectiveOrderId,
    selectedFillRowId: effectiveFillId,
    hasPositions: positionRows.length > 0,
    hasOpenOrders: orderRows.length > 0,
    hasFills: fillRows.length > 0,
    positionsTableLabel: "Live positions evidence",
    ordersTableLabel: "Open orders evidence",
    fillsTableLabel: "Recent fills evidence",
    positionDetailId: tradingPositionDetailId,
    orderDetailId: tradingOrderDetailId,
    fillDetailId: tradingFillDetailId,
    positionEmptyText: data ? "No live positions in the active paper session." : "Trading workspace data unavailable.",
    orderEmptyText: data ? "No open orders require operator action." : "Trading workspace data unavailable.",
    fillEmptyText: data ? "No recent fills have been reported for this session." : "Trading workspace data unavailable.",
    cancelAllDisabled: orderRows.length === 0,
    cancelAllDisabledReason: orderRows.length === 0 ? "No open orders require cancellation." : null,
    cancelAllAriaLabel: orderRows.length === 0 ? "No open orders to cancel" : `Cancel all ${orderRows.length} open orders`,
    selectPosition,
    selectOrder,
    selectFill
  };
}

export function useTradingReadinessViewModel({
  initialReadiness,
  fundAccountId,
  services = defaultTradingReadinessServices
}: {
  initialReadiness: TradingOperatorReadiness | null;
  fundAccountId?: string;
  services?: TradingReadinessServices;
}): TradingReadinessViewModel {
  const [readiness, setReadiness] = useState<TradingOperatorReadiness | null>(initialReadiness);
  const [refreshing, setRefreshing] = useState(false);
  const [errorText, setErrorText] = useState<string | null>(null);
  const refreshRef = useRef<(options?: { attempt?: number }) => Promise<void>>(async () => {});
  const readinessLifecycle = useRequestLifecycle({
    operation: "trading readiness handoff refresh",
    runningMessage: "Refreshing trading readiness handoff evidence.",
    successMessage: "Trading readiness refreshed.",
    failureMessage: "Trading readiness refresh failed.",
    staleMessage: "Older trading readiness response discarded.",
    maxRetries: 2,
    onRetry: ({ attempt }) => {
      void refreshRef.current({ attempt });
    }
  });

  useEffect(() => {
    readinessLifecycle.invalidate();
    setReadiness(initialReadiness);
    setErrorText(null);
    setRefreshing(false);
  }, [initialReadiness, readinessLifecycle.invalidate]);

  const refresh = useCallback(async (options: { attempt?: number } = {}) => {
    const token = readinessLifecycle.start({ attempt: options.attempt });
    if (!token) return;
    token.safeSetState(setRefreshing, true);
    token.safeSetState(setErrorText, null);

    try {
      const nextReadiness = await services.getTradingReadiness({ signal: token.signal, fundAccountId });
      if (!token.isCurrent()) {
        readinessLifecycle.markStale(token.version);
        return;
      }
      token.safeSetState(setReadiness, nextReadiness);
      readinessLifecycle.succeed(token);
    } catch (err) {
      if (!token.isCurrent()) {
        readinessLifecycle.markStale(token.version);
        return;
      }
      token.safeSetState(setErrorText, toErrorMessage(err, "Failed to refresh trading readiness."));
      readinessLifecycle.fail(token, err, { fallback: "Failed to refresh trading readiness." });
    } finally {
      if (token.isCurrent()) {
        token.safeSetState(setRefreshing, false);
      }
      readinessLifecycle.finish(token);
    }
  }, [fundAccountId, readinessLifecycle.fail, readinessLifecycle.finish, readinessLifecycle.markStale, readinessLifecycle.start, readinessLifecycle.succeed, services]);

  useEffect(() => {
    refreshRef.current = refresh;
  }, [refresh]);

  const state = useMemo(
    () => buildTradingReadinessState({ readiness, refreshing, errorText, requestStatus: readinessLifecycle.status }),
    [errorText, readiness, readinessLifecycle.status, refreshing]
  );

  return {
    ...state,
    refresh
  };
}

export function buildTradingReadinessState({
  readiness,
  refreshing,
  errorText,
  requestStatus
}: BuildTradingReadinessStateOptions): TradingReadinessState {
  const summaryRows = readiness ? buildTradingReadinessSummaryRows(readiness) : [];
  const workItems = readiness?.workItems ?? [];
  const warnings = readiness?.warnings ?? [];
  const visibleWorkItems = buildTradingReadinessWorkItemRows(workItems.slice(0, visibleWorkItemLimit));
  const visibleWarnings = buildTradingReadinessWarningRows(warnings.slice(0, visibleWarningLimit));
  const hiddenWorkItemCount = Math.max(0, workItems.length - visibleWorkItemLimit);
  const hiddenWarningCount = Math.max(0, warnings.length - visibleWarningLimit);

  return {
    readiness,
    refreshing,
    errorText,
    workItems,
    warnings,
    summaryRows,
    summaryLabel: "Trading readiness contract summary",
    hasOperatorAttention: workItems.length > 0 || warnings.length > 0,
    visibleWorkItems,
    hiddenWorkItemCount,
    workItemOverflowLabel: hiddenWorkItemCount > 0
      ? `${hiddenWorkItemCount} more readiness item${hiddenWorkItemCount === 1 ? "" : "s"} in the Operator Readiness Console.`
      : null,
    visibleWarnings,
    hiddenWarningCount,
    warningOverflowLabel: hiddenWarningCount > 0
      ? `${hiddenWarningCount} more warning${hiddenWarningCount === 1 ? "" : "s"} in the Operator Readiness Console.`
      : null,
    primaryWorkItemKind: workItems[0]?.kind ?? null,
    workItemSummaryText: `${workItems.length} readiness item${workItems.length === 1 ? "" : "s"} and ${warnings.length} warning${warnings.length === 1 ? "" : "s"}.`,
    workItemListLabel: "Trading readiness operator work items",
    evidenceAction: {
      label: "Evidence",
      href: evidenceWorkbenchPath("paper-readiness", "current"),
      ariaLabel: "Open paper cockpit readiness evidence"
    },
    refreshButtonLabel: refreshing ? "Refreshing..." : "Refresh readiness",
    refreshAriaLabel: refreshing ? "Refreshing trading readiness" : "Refresh trading readiness",
    refreshBusyLabel: refreshing ? "Refreshing readiness..." : null,
    refreshDisabled: refreshing,
    refreshDisabledReason: refreshing ? "Trading readiness refresh is already running." : null,
    statusAnnouncement: buildTradingReadinessAnnouncement({ readiness, refreshing, errorText }),
    requestStatus: requestStatus ?? idleTradingReadinessStatus
  };
}

function buildTradingReadinessWorkItemRows(items: OperatorWorkItem[]): TradingReadinessWorkItemRow[] {
  return items.map((item) => {
    const metadataText = [
      item.workspace,
      item.targetPageTag,
      item.runId,
      item.auditReference
    ].filter(Boolean).join(" · ");

    return {
      workItemId: item.workItemId,
      kind: item.kind,
      label: item.label,
      detail: item.detail,
      tone: mapOperatorWorkItemTone(item.tone),
      metadataText: metadataText || null,
      action: buildTradingReadinessWorkItemAction(item),
      ariaLabel: [
        `${item.tone} readiness item`,
        item.label,
        item.detail,
        metadataText || null
      ].filter(Boolean).map((part) => String(part).trim().replace(/[.]+$/g, "")).join(". ")
    };
  });
}

function buildTradingReadinessWorkItemAction(item: OperatorWorkItem): TradingReadinessWorkItemAction | null {
  const sharedTargetHref = resolveTradingReadinessSharedTargetHref(item);

  switch (item.kind) {
    case "BrokerageSync":
      return {
        label: "Fix provider setup",
        href: resolveBrokerageSyncWorkItemHref(item, sharedTargetHref),
        ariaLabel: `Open provider setup for ${item.label}`,
        detail: "Review provider credentials and connection status in Settings."
      };

    case "PaperReplay":
      return {
        label: "Verify replay",
        href: `${WORKSTATION_ROUTE_CATALOG.trading}#session-replay-panel`,
        ariaLabel: `Open session replay panel for ${item.label}`,
        detail: "Verify replay consistency in the Trading cockpit."
      };

    case "ExecutionControl":
      return {
        label: "Review risk controls",
        href: sharedTargetHref ?? WORKSTATION_ROUTE_CATALOG.tradingRisk,
        ariaLabel: `Open risk controls for ${item.label}`,
        detail: "Review execution guardrails and circuit-breaker state in Trading."
      };

    case "BrokerExecutionReconciliation":
      return {
        label: "Review broker orders",
        href: sharedTargetHref ?? WORKSTATION_ROUTE_CATALOG.tradingReadiness,
        ariaLabel: `Open broker execution reconciliation for ${item.label}`,
        detail: "Review broker/OMS open-order parity before live operation."
      };

    case "PromotionReview": {
      const href = sharedTargetHref ?? `${WORKSTATION_ROUTE_CATALOG.trading}#promotion-gate-panel`;
      return {
        label: "Open promotion gate",
        href,
        ariaLabel: `Open promotion gate panel for ${item.label}`,
        detail: "Complete the promotion approval checklist in the Trading cockpit."
      };
    }

    case "SecurityMasterCoverage":
      return {
        label: "Open security master",
        href: WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster,
        ariaLabel: `Open security master for ${item.label}`,
        detail: "Review missing security coverage in Accounting."
      };

    case "ProviderTrustGate":
      return {
        label: "Open provider readiness",
        href: WORKSTATION_ROUTE_CATALOG.dataProviders,
        ariaLabel: `Open provider readiness for ${item.label}`,
        detail: "Review provider trust evidence and operator sign-off in Data."
      };

    case "ReconciliationBreak":
      return {
        label: "Open reconciliation",
        href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
        ariaLabel: `Open reconciliation break queue for ${item.label}`,
        detail: "Review open reconciliation breaks in Accounting."
      };

    default:
      return null;
  }
}

function resolveBrokerageSyncWorkItemHref(item: OperatorWorkItem, _sharedTargetHref: string | null): string {
  const text = `${item.workItemId} ${item.label} ${item.detail}`.toLowerCase();
  if (text.includes("credential") || text.includes("provider setup") || text.includes("failed")) {
    return WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup;
  }

  const targetsAccount =
    item.targetPageTag === "AccountPortfolio" &&
    text.includes("incomplete") &&
    /\/api\/fund-accounts\/[^/]+\/brokerage-sync/i.test(item.targetRoute ?? "");

  return targetsAccount && item.fundAccountId
    ? `/portfolio/accounts/${encodeURIComponent(item.fundAccountId)}`
    : WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup;
}

function mapOperatorWorkItemTone(tone: OperatorWorkItem["tone"]): TradingDataTone {
  switch (tone) {
    case "Critical":
      return "danger";
    case "Warning":
      return "warning";
    case "Success":
      return "success";
    case "Info":
    default:
      return "default";
  }
}

function resolveTradingReadinessSharedTargetHref(item: OperatorWorkItem): string | null {
  if (item.kind === "PaperReplay") {
    return null;
  }

  return normalizeLocalWorkstationRoute(item.targetRoute)
    ?? (item.targetPageTag || item.workspace ? workflowTargetPath(item.targetPageTag, item.workspace) : null);
}

function buildTradingReadinessWarningRows(warnings: string[]): TradingReadinessWarningRow[] {
  return warnings.map((warning, index) => ({
    id: `warning-${index + 1}-${warning.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/(^-|-$)/g, "") || "item"}`,
    text: warning,
    ariaLabel: `Trading readiness warning: ${warning}`
  }));
}

function buildTradingReadinessAnnouncement({
  readiness,
  refreshing,
  errorText
}: BuildTradingReadinessStateOptions): string {
  if (refreshing) {
    return "Refreshing trading readiness.";
  }

  if (errorText) {
    return `Trading readiness refresh failed: ${errorText}`;
  }

  if (readiness) {
    return `Trading readiness ${formatReadinessStatusValue(readiness.overallStatus).toLowerCase()} as of ${formatUtcDateTime(readiness.asOf)}.`;
  }

  return "";
}

function resolveSelectedId<T>(
  rows: T[],
  requestedId: string | null,
  getId: (row: T, index: number) => string
): string | null {
  if (requestedId && rows.some((row, index) => getId(row, index) === requestedId)) {
    return requestedId;
  }

  return rows.length > 0 ? getId(rows[0], 0) : null;
}

function buildPositionRow(
  position: TradingPosition,
  index: number,
  selectedId: string | null
): TradingPositionRow {
  const id = positionRowId(position, index);
  const positionKey = position.positionKey?.trim() || position.symbol;

  return {
    id,
    positionKey,
    symbol: position.symbol,
    side: position.side,
    quantity: position.quantity,
    averagePrice: position.averagePrice,
    markPrice: position.markPrice,
    dayPnl: position.dayPnl,
    unrealizedPnl: position.unrealizedPnl,
    exposure: position.exposure,
    dayPnlTone: pnlTextTone(position.dayPnl),
    unrealizedPnlTone: pnlTextTone(position.unrealizedPnl),
    isSelected: id === selectedId,
    detailPanelId: tradingPositionDetailId,
    ariaExpanded: id === selectedId,
    selectAriaLabel: `Inspect ${position.symbol} ${position.side.toLowerCase()} position`,
    closeActionLabel: "Close",
    closeTitleLabel: "Close position",
    closeAriaLabel: `Close ${position.symbol} position`,
    ariaLabel: `${position.symbol} ${position.side} position, ${position.quantity} quantity, ${position.exposure} exposure, ${position.unrealizedPnl} unrealized P&L`
  };
}

function buildOrderRow(
  order: TradingOrder,
  index: number,
  selectedId: string | null
): TradingOrderRow {
  const id = orderRowId(order, index);

  return {
    id,
    orderId: order.orderId,
    symbol: order.symbol,
    side: order.side,
    type: order.type,
    quantity: order.quantity,
    limitPrice: order.limitPrice,
    status: order.status,
    submittedAt: order.submittedAt,
    statusTone: orderStatusTone(order.status),
    isSelected: id === selectedId,
    detailPanelId: tradingOrderDetailId,
    ariaExpanded: id === selectedId,
    selectAriaLabel: `Inspect order ${order.orderId}`,
    cancelActionLabel: "Cancel",
    cancelTitleLabel: "Cancel order",
    cancelAriaLabel: `Cancel order ${order.orderId}`,
    ariaLabel: `${order.orderId}, ${order.side} ${order.quantity} ${order.symbol} ${order.type}, ${order.status}, submitted ${order.submittedAt}`
  };
}

function buildFillRow(
  fill: TradingFill,
  index: number,
  selectedId: string | null
): TradingFillRow {
  const id = fillRowId(fill, index);
  const isSelected = id === selectedId;

  return {
    id,
    fillId: fill.fillId,
    orderId: fill.orderId,
    symbol: fill.symbol,
    side: fill.side,
    quantity: fill.quantity,
    price: fill.price,
    venue: fill.venue,
    timestamp: fill.timestamp,
    cells: [
      fill.fillId,
      fill.orderId,
      fill.symbol,
      fill.side,
      fill.quantity,
      fill.price,
      fill.venue,
      fill.timestamp
    ],
    isSelected,
    detailPanelId: tradingFillDetailId,
    ariaExpanded: isSelected,
    selectAriaLabel: `Inspect fill ${fill.fillId}`,
    ariaLabel: `${fill.fillId}, ${fill.side} ${fill.quantity} ${fill.symbol} at ${fill.price} on ${fill.venue}, ${fill.timestamp}`
  };
}

function buildPositionDetail(
  row: TradingPositionRow,
  risk: TradingRiskState | null
): TradingBlotterDetail {
  const guardrailCount = risk?.activeGuardrails.length ?? 0;
  const riskText = risk ? `${risk.state}: ${risk.summary}` : "Risk context unavailable.";

  return {
    id: "selected-position",
    title: row.symbol,
    subtitle: `${row.side} · ${row.quantity} shares`,
    statusLabel: risk?.state ?? "Position",
    statusTone: riskStateTone(risk?.state, row.unrealizedPnlTone),
    ariaLabel: `Position detail for ${row.symbol}`,
    detail: `${row.exposure} exposure with ${row.unrealizedPnl} unrealized P&L. ${riskText}`,
    fields: [
      { label: "Side", value: row.side, tone: "default" },
      { label: "Quantity", value: row.quantity, tone: "default" },
      { label: "Average price", value: row.averagePrice, tone: "muted" },
      { label: "Mark price", value: row.markPrice, tone: "muted" },
      { label: "Day P&L", value: row.dayPnl, tone: row.dayPnlTone },
      { label: "Unrealized P&L", value: row.unrealizedPnl, tone: row.unrealizedPnlTone },
      { label: "Exposure", value: row.exposure, tone: "default" },
      { label: "Risk", value: risk?.state ?? "Unavailable", tone: riskFieldTone(risk?.state) },
      { label: "Guardrails", value: guardrailCount > 0 ? `${guardrailCount} active` : "None active", tone: guardrailCount > 0 ? "warning" : "success" }
    ]
  };
}

function buildOrderDetail(row: TradingOrderRow, provider: string | null): TradingBlotterDetail {
  return {
    id: "selected-order",
    title: row.orderId,
    subtitle: `${row.side} ${row.quantity} ${row.symbol}`,
    statusLabel: row.status,
    statusTone: row.statusTone,
    ariaLabel: `Order detail for ${row.orderId}`,
    detail: `${row.type} order submitted ${row.submittedAt}. ${provider ? `${provider} is the active execution adapter.` : "Execution adapter context unavailable."}`,
    fields: [
      { label: "Symbol", value: row.symbol, tone: "default" },
      { label: "Side", value: row.side, tone: "default" },
      { label: "Type", value: row.type, tone: "default" },
      { label: "Quantity", value: row.quantity, tone: "default" },
      { label: "Limit", value: row.limitPrice || "Market", tone: "muted" },
      { label: "Status", value: row.status, tone: row.statusTone },
      { label: "Submitted", value: row.submittedAt, tone: "muted" },
      { label: "Provider", value: provider ?? "Unavailable", tone: "muted" }
    ]
  };
}

function buildFillDetail(row: TradingFillRow, provider: string | null): TradingBlotterDetail {
  return {
    id: "selected-fill",
    title: row.fillId,
    subtitle: `${row.side} ${row.quantity} ${row.symbol}`,
    statusLabel: "Fill evidence",
    statusTone: "success",
    ariaLabel: `Fill detail for ${row.fillId}`,
    detail: `${row.symbol} fill posted at ${row.price} on ${row.venue}. ${provider ? `${provider} supplied the execution adapter context.` : "Execution adapter context unavailable."}`,
    fields: [
      { label: "Order", value: row.orderId, tone: "muted" },
      { label: "Symbol", value: row.symbol, tone: "default" },
      { label: "Side", value: row.side, tone: row.side === "Buy" ? "success" : "warning" },
      { label: "Quantity", value: row.quantity, tone: "default" },
      { label: "Price", value: row.price, tone: "default" },
      { label: "Venue", value: row.venue, tone: "muted" },
      { label: "Timestamp", value: row.timestamp, tone: "muted" },
      { label: "Provider", value: provider ?? "Unavailable", tone: "muted" }
    ]
  };
}

function positionRowId(position: TradingPosition, index: number): string {
  return `${position.symbol.toLowerCase()}-${position.side.toLowerCase()}-${index}`;
}

function orderRowId(order: TradingOrder, index: number): string {
  return `${order.orderId.toLowerCase()}-${index}`;
}

function fillRowId(fill: TradingFill, index: number): string {
  return fill.fillId || `fill-${index}`;
}

function pnlTextTone(value: string): TradingDataTone {
  if (value.trim().startsWith("+")) return "success";
  if (value.trim().startsWith("-")) return "danger";
  return "default";
}

function orderStatusTone(status: TradingOrder["status"]): TradingDataTone {
  if (status === "Working") return "success";
  if (status === "Partially Filled") return "warning";
  return "muted";
}

function riskFieldTone(state: TradingRiskState["state"] | undefined): TradingDataTone {
  if (state === "Healthy") return "success";
  if (state === "Observe") return "warning";
  if (state === "Constrained") return "danger";
  return "muted";
}

function riskStateTone(
  state: TradingRiskState["state"] | undefined,
  fallback: TradingDataTone
): TradingDataTone {
  if (state === "Constrained") return "danger";
  if (state === "Observe") return "warning";
  if (state === "Healthy") return "success";
  return fallback;
}

export type ExecutionEvidenceTone = "success" | "warning" | "danger";

export interface ExecutionEvidenceFieldRow {
  id: string;
  label: string;
  value: string;
}

export interface ExecutionAuditRow {
  id: string;
  action: string;
  outcome: string;
  outcomeTone: ExecutionEvidenceTone;
  message: string;
  metadataText: string;
  technicalMetadataText: string | null;
  ariaLabel: string;
}

export interface ExecutionControlsPanel {
  title: string;
  statusLabel: string;
  statusTone: ExecutionEvidenceTone;
  ariaLabel: string;
  rows: ExecutionEvidenceFieldRow[];
  breakerAction: TradingConfirmAction;
  breakerActionLabel: string;
  breakerActionDisabled: boolean;
  breakerActionDisabledReason: string | null;
  breakerActionAriaLabel: string;
}

export interface ExecutionEvidenceState {
  auditEntries: ExecutionAuditEntry[];
  controlsSnapshot: ExecutionControlSnapshot | null;
  auditRows: ExecutionAuditRow[];
  controlsPanel: ExecutionControlsPanel | null;
  loading: boolean;
  errorText: string | null;
  auditTitle: string;
  auditListLabel: string;
  auditEmptyText: string;
  auditCountLabel: string;
  controlsEmptyText: string;
  refreshButtonLabel: string;
  refreshAriaLabel: string;
  refreshBusyLabel: string | null;
  refreshDisabled: boolean;
  refreshDisabledReason: string | null;
  statusAnnouncement: string;
  requestStatus: RequestLifecycleStatus;
}

export interface ExecutionEvidenceViewModel extends ExecutionEvidenceState {
  refresh: () => Promise<void>;
}

export interface ExecutionEvidenceServices {
  getExecutionAudit: (take: number) => Promise<ExecutionAuditEntry[]>;
  getExecutionControls: () => Promise<ExecutionControlSnapshot>;
}

export interface BuildExecutionEvidenceStateOptions {
  auditEntries: ExecutionAuditEntry[];
  controlsSnapshot: ExecutionControlSnapshot | null;
  loading: boolean;
  errorText: string | null;
  requestStatus?: RequestLifecycleStatus;
}

const defaultExecutionEvidenceServices: ExecutionEvidenceServices = {
  getExecutionAudit: (take) => workstationApi.getExecutionAudit(take),
  getExecutionControls: () => workstationApi.getExecutionControls()
};

export function useExecutionEvidenceViewModel({
  services = defaultExecutionEvidenceServices,
  auditTake = 8
}: {
  services?: ExecutionEvidenceServices;
  auditTake?: number;
} = {}): ExecutionEvidenceViewModel {
  const [auditEntries, setAuditEntries] = useState<ExecutionAuditEntry[]>([]);
  const [controlsSnapshot, setControlsSnapshot] = useState<ExecutionControlSnapshot | null>(null);
  const [loading, setLoading] = useState(false);
  const [errorText, setErrorText] = useState<string | null>(null);
  const refreshRef = useRef<(options?: { attempt?: number }) => Promise<void>>(async () => {});
  const executionLifecycle = useRequestLifecycle({
    operation: "execution evidence refresh",
    runningMessage: "Refreshing execution audit and control evidence.",
    successMessage: "Execution evidence refreshed.",
    failureMessage: "Execution evidence refresh failed.",
    staleMessage: "Older execution evidence response discarded.",
    maxRetries: 2,
    onRetry: ({ attempt }) => {
      void refreshRef.current({ attempt });
    }
  });

  const refresh = useCallback(async (options: { attempt?: number } = {}) => {
    const token = executionLifecycle.start({ busyMode: "drop", attempt: options.attempt });
    if (!token) return;
    token.safeSetState(setLoading, true);
    token.safeSetState(setErrorText, null);

    const [auditResult, controlsResult] = await Promise.allSettled([
      Promise.resolve().then(() => services.getExecutionAudit(auditTake)),
      Promise.resolve().then(() => services.getExecutionControls())
    ]);

    if (!token.isCurrent()) {
      executionLifecycle.markStale(token.version);
      return;
    }

    if (auditResult.status === "fulfilled") {
      token.safeSetState(setAuditEntries, auditResult.value);
    } else {
      token.safeSetState(setAuditEntries, []);
    }

    if (controlsResult.status === "fulfilled") {
      token.safeSetState(setControlsSnapshot, controlsResult.value);
    } else {
      token.safeSetState(setControlsSnapshot, null);
    }

    const failures = [auditResult, controlsResult].filter((result) => result.status === "rejected");
    if (failures.length > 0) {
      const firstFailure = failures[0];
      const reason = firstFailure.status === "rejected" ? firstFailure.reason : null;
      token.safeSetState(setErrorText, toErrorMessage(reason, "Execution evidence refresh failed."));
      executionLifecycle.fail(token, reason, { fallback: "Execution evidence refresh failed." });
    } else {
      executionLifecycle.succeed(token);
    }

    if (token.isCurrent()) {
      token.safeSetState(setLoading, false);
    }
    executionLifecycle.finish(token);
  }, [auditTake, executionLifecycle.fail, executionLifecycle.finish, executionLifecycle.markStale, executionLifecycle.start, executionLifecycle.succeed, services]);

  useEffect(() => {
    refreshRef.current = refresh;
  }, [refresh]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const state = useMemo(
    () => buildExecutionEvidenceState({ auditEntries, controlsSnapshot, loading, errorText, requestStatus: executionLifecycle.status }),
    [auditEntries, controlsSnapshot, errorText, executionLifecycle.status, loading]
  );

  return {
    ...state,
    refresh
  };
}

export function buildExecutionEvidenceState({
  auditEntries,
  controlsSnapshot,
  loading,
  errorText,
  requestStatus
}: BuildExecutionEvidenceStateOptions): ExecutionEvidenceState {
  const auditRows = auditEntries.map(buildExecutionAuditRow);
  const controlsPanel = controlsSnapshot ? buildExecutionControlsPanel(controlsSnapshot) : null;

  return {
    auditEntries,
    controlsSnapshot,
    auditRows,
    controlsPanel,
    loading,
    errorText,
    auditTitle: "Recent execution audit",
    auditListLabel: "Recent execution audit entries",
    auditEmptyText: loading ? "Loading execution audit entries..." : "No execution audit entries available.",
    auditCountLabel: `${auditRows.length} audit ${auditRows.length === 1 ? "entry" : "entries"}`,
    controlsEmptyText: loading ? "Loading execution controls snapshot..." : "Snapshot unavailable.",
    refreshButtonLabel: loading ? "Refreshing..." : "Refresh evidence",
    refreshAriaLabel: loading ? "Refreshing execution evidence" : "Refresh execution audit and controls evidence",
    refreshBusyLabel: loading ? "Refreshing evidence..." : null,
    refreshDisabled: loading,
    refreshDisabledReason: loading ? "Execution evidence refresh is already running." : null,
    statusAnnouncement: buildExecutionEvidenceAnnouncement({ auditRows, controlsPanel, loading, errorText }),
    requestStatus: requestStatus ?? idleExecutionEvidenceStatus
  };
}

function buildExecutionAuditRow(entry: ExecutionAuditEntry): ExecutionAuditRow {
  const metadataText = formatExecutionAuditMetadata(entry);
  const technicalMetadataText = formatExecutionAuditTechnicalMetadata(entry);
  const message = entry.message?.trim() || "No operator message recorded.";
  const action = formatExecutionAuditAction(entry.action);

  return {
    id: entry.auditId,
    action,
    outcome: entry.outcome,
    outcomeTone: mapExecutionOutcomeTone(entry.outcome),
    message,
    metadataText,
    technicalMetadataText,
    ariaLabel: `${action} ${entry.outcome}. ${message} ${metadataText}`.trim()
  };
}

function formatExecutionAuditMetadata(entry: ExecutionAuditEntry): string {
  const occurredAt = new Date(entry.occurredAt);
  if (Number.isNaN(occurredAt.getTime())) {
    return "Recorded time unavailable";
  }

  return `Recorded ${new Intl.DateTimeFormat("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
    timeZone: "UTC",
    timeZoneName: "short"
  }).format(occurredAt).replace("24:", "00:")}`;
}

function formatExecutionAuditTechnicalMetadata(entry: ExecutionAuditEntry): string | null {
  const parts = [
    entry.occurredAt,
    entry.metadata?.sessionId ? `session ${entry.metadata.sessionId}` : null,
    entry.orderId ? `order ${entry.orderId}` : null,
    entry.symbol ? `symbol ${entry.symbol}` : null,
    entry.runId ? `run ${entry.runId}` : null
  ].filter(Boolean);

  return parts.length > 0 ? parts.join(" · ") : null;
}

function formatExecutionAuditAction(action: string): string {
  const normalized = action.trim();
  if (normalized === "ReplayPaperSession") {
    return "Paper session replay";
  }

  const words = normalized
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/[._-]+/g, " ")
    .replace(/\s+/g, " ")
    .trim();
  return words ? `${words.charAt(0).toUpperCase()}${words.slice(1)}` : "Execution activity";
}

function mapExecutionOutcomeTone(outcome: string): ExecutionEvidenceTone {
  const normalized = outcome.toLowerCase();
  if (normalized.includes("fail") || normalized.includes("reject") || normalized.includes("error")) {
    return "danger";
  }

  if (normalized.includes("complete") || normalized.includes("accept") || normalized.includes("success")) {
    return "success";
  }

  return "warning";
}

function buildExecutionEvidenceAnnouncement({
  auditRows,
  controlsPanel,
  loading,
  errorText
}: {
  auditRows: ExecutionAuditRow[];
  controlsPanel: ExecutionControlsPanel | null;
  loading: boolean;
  errorText: string | null;
}): string {
  if (loading) {
    return "Refreshing execution evidence.";
  }

  if (errorText) {
    return `Execution evidence refresh failed: ${errorText}`;
  }

  if (controlsPanel) {
    return `${controlsPanel.statusLabel}. ${auditRows.length} audit ${auditRows.length === 1 ? "entry" : "entries"} loaded.`;
  }

  if (auditRows.length > 0) {
    return `${auditRows.length} execution audit ${auditRows.length === 1 ? "entry" : "entries"} loaded.`;
  }

  return "";
}

export type PaperSessionField = "strategyId" | "initialCash";
export type PaperSessionCommandKind = "loading" | "creating" | "restoring" | "verifying" | "closing";

export interface PaperSessionForm {
  strategyId: string;
  initialCash: string;
}

export interface PaperSessionBusyCommand {
  kind: PaperSessionCommandKind;
  sessionId?: string;
}

export interface PaperSessionRow {
  sessionId: string;
  strategyId: string;
  initialCashText: string;
  statusLabel: string;
  isActive: boolean;
  isSelected: boolean;
  ariaLabel: string;
  restoreButtonLabel: string;
  verifyButtonLabel: string;
  closeButtonLabel: string;
  restoreAriaLabel: string;
  verifyAriaLabel: string;
  closeAriaLabel: string;
  canRestore: boolean;
  canVerify: boolean;
  canClose: boolean;
  restoreDisabledReason: string | null;
  verifyDisabledReason: string | null;
  closeDisabledReason: string | null;
}

export interface PaperSessionFormFieldState {
  id: string;
  label: string;
  ariaLabel: string;
  field: PaperSessionField;
  value: string;
  type: "text" | "number";
  placeholder?: string;
  autoComplete: string;
  describedBy: string;
  disabled: boolean;
  disabledReason: string | null;
  invalid: boolean;
  min?: number;
  step?: number;
}

export interface PaperSessionFieldRow {
  label: string;
  value: string;
}

export interface PaperSessionMetricRow {
  label: string;
  value: string;
}

export interface PaperSessionReplayPanel {
  tone: "success" | "warning";
  statusLabel: string;
  ariaLabel: string;
  metadataText: string;
  rows: PaperSessionFieldRow[];
  mismatchReasons: string[];
}

export interface PaperSessionDetailPanel {
  sessionId: string;
  statusLabel: string;
  statusTone: AcceptanceLevel;
  ariaLabel: string;
  infoRows: PaperSessionFieldRow[];
  metricRows: PaperSessionMetricRow[];
  replay: PaperSessionReplayPanel | null;
}

export interface PaperSessionState {
  sessions: PaperSessionSummary[];
  selectedSessionId: string | null;
  selectedSessionDetail: PaperSessionDetail | null;
  sessionReplayVerification: PaperSessionReplayVerification | null;
  form: PaperSessionForm;
  showCreateForm: boolean;
  busyCommand: PaperSessionBusyCommand | null;
  isBusy: boolean;
  errorText: string | null;
  rows: PaperSessionRow[];
  detail: PaperSessionDetailPanel | null;
  emptyText: string;
  selectedSessionLabel: string | null;
  formPanelId: string;
  formDescriptionId: string;
  strategyIdField: PaperSessionFormFieldState;
  initialCashField: PaperSessionFormFieldState;
  formRequirementText: string;
  toggleCreateButtonLabel: string;
  toggleCreateButtonAriaLabel: string;
  toggleCreateButtonDisabledReason: string | null;
  createButtonLabel: string;
  createButtonBusy: boolean;
  createButtonBusyLabel: string | null;
  cancelCreateButtonLabel: string;
  createButtonAriaLabel: string;
  canSubmitCreate: boolean;
  createButtonDisabledReason: string | null;
  canCloseCreateForm: boolean;
  cancelCreateButtonDisabledReason: string | null;
  statusAnnouncement: string;
}

export interface PaperSessionServices {
  getExecutionSessions: () => Promise<PaperSessionSummary[]>;
  createPaperSession: (strategyId: string, strategyName: string | null, initialCash: number) => Promise<PaperSessionSummary>;
  closePaperSession: (sessionId: string) => Promise<TradingActionResult>;
  getPaperSessionDetail: (sessionId: string) => Promise<PaperSessionDetail>;
  getPaperSessionReplayVerification: (sessionId: string) => Promise<PaperSessionReplayVerification>;
}

export interface BuildPaperSessionStateOptions {
  sessions: PaperSessionSummary[];
  selectedSessionId: string | null;
  selectedSessionDetail: PaperSessionDetail | null;
  sessionReplayVerification: PaperSessionReplayVerification | null;
  form: PaperSessionForm;
  showCreateForm: boolean;
  busyCommand: PaperSessionBusyCommand | null;
  errorText: string | null;
}

export interface PaperSessionCreateRequest {
  strategyId: string;
  initialCash: number;
}

export const emptyPaperSessionForm: PaperSessionForm = {
  strategyId: "",
  initialCash: "100000"
};

const defaultPaperSessionServices: PaperSessionServices = {
  getExecutionSessions: () => workstationApi.getExecutionSessions(),
  createPaperSession: (strategyId, strategyName, initialCash) => workstationApi.createPaperSession(strategyId, strategyName, initialCash),
  closePaperSession: (sessionId) => workstationApi.closePaperSession(sessionId),
  getPaperSessionDetail: (sessionId) => workstationApi.getPaperSessionDetail(sessionId),
  getPaperSessionReplayVerification: (sessionId) => workstationApi.getPaperSessionReplayVerification(sessionId)
};

export function usePaperSessionsViewModel({
  services = defaultPaperSessionServices,
  onSessionEvidenceChanged
}: {
  services?: PaperSessionServices;
  onSessionEvidenceChanged?: () => Promise<void> | void;
} = {}) {
  const [sessions, setSessions] = useState<PaperSessionSummary[]>([]);
  const [selectedSessionId, setSelectedSessionId] = useState<string | null>(null);
  const [selectedSessionDetail, setSelectedSessionDetail] = useState<PaperSessionDetail | null>(null);
  const [sessionReplayVerification, setSessionReplayVerification] = useState<PaperSessionReplayVerification | null>(null);
  const [form, setForm] = useState<PaperSessionForm>(emptyPaperSessionForm);
  const [showCreateForm, setShowCreateForm] = useState(false);
  const [busyCommand, setBusyCommand] = useState<PaperSessionBusyCommand | null>({ kind: "loading" });
  const [errorText, setErrorText] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setBusyCommand({ kind: "loading" });
    setErrorText(null);

    services.getExecutionSessions()
      .then((rows) => {
        if (!cancelled) {
          setSessions(rows);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setSessions([]);
          setErrorText(toErrorMessage(err, "Failed to load paper sessions."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setBusyCommand(null);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [services]);

  const state = useMemo(
    () => buildPaperSessionState({
      sessions,
      selectedSessionId,
      selectedSessionDetail,
      sessionReplayVerification,
      form,
      showCreateForm,
      busyCommand,
      errorText
    }),
    [busyCommand, errorText, form, selectedSessionDetail, selectedSessionId, sessionReplayVerification, sessions, showCreateForm]
  );

  const toggleCreateForm = useCallback(() => {
    setShowCreateForm((current) => !current);
    setErrorText(null);
  }, []);

  const closeCreateForm = useCallback(() => {
    setShowCreateForm(false);
    setErrorText(null);
  }, []);

  const updateField = useCallback((field: PaperSessionField, value: string) => {
    setForm((current) => ({ ...current, [field]: value }));
    setErrorText(null);
  }, []);

  const createSession = useCallback(async () => {
    const validationError = validatePaperSessionForm(form);
    if (validationError) {
      setErrorText(validationError);
      return;
    }

    const request = buildPaperSessionCreateRequest(form);
    setBusyCommand({ kind: "creating" });
    setErrorText(null);

    try {
      const summary = await services.createPaperSession(request.strategyId, null, request.initialCash);
      setSessions((current) => [summary, ...current]);
      setShowCreateForm(false);
      setForm(emptyPaperSessionForm);
      await onSessionEvidenceChanged?.();
    } catch (err) {
      setErrorText(toErrorMessage(err, "Failed to create session."));
    } finally {
      setBusyCommand(null);
    }
  }, [form, onSessionEvidenceChanged, services]);

  const closeSession = useCallback(async (sessionId: string) => {
    setBusyCommand({ kind: "closing", sessionId });
    setErrorText(null);

    try {
      const result = await services.closePaperSession(sessionId);
      setSessions((current) => current.map((session) => (
        session.sessionId === sessionId
          ? { ...session, closedAt: result.occurredAt, isActive: false }
          : session
      )));
      setSelectedSessionDetail((current) => {
        if (!current || current.summary.sessionId !== sessionId) {
          return current;
        }

        return {
          ...current,
          summary: {
            ...current.summary,
            closedAt: result.occurredAt,
            isActive: false
          }
        };
      });
      await onSessionEvidenceChanged?.();
    } catch (err) {
      setErrorText(toErrorMessage(err, "Failed to close session."));
    } finally {
      setBusyCommand(null);
    }
  }, [onSessionEvidenceChanged, services]);

  const restoreSession = useCallback(async (sessionId: string) => {
    setBusyCommand({ kind: "restoring", sessionId });
    setErrorText(null);

    try {
      const detail = await services.getPaperSessionDetail(sessionId);
      setSelectedSessionId(sessionId);
      setSelectedSessionDetail(detail);
      setSessionReplayVerification(null);
    } catch (err) {
      setErrorText(toErrorMessage(err, "Failed to restore session."));
    } finally {
      setBusyCommand(null);
    }
  }, [services]);

  const verifySessionReplay = useCallback(async (sessionId: string) => {
    setBusyCommand({ kind: "verifying", sessionId });
    setErrorText(null);

    try {
      const [detail, verification] = await Promise.all([
        services.getPaperSessionDetail(sessionId),
        services.getPaperSessionReplayVerification(sessionId)
      ]);
      setSelectedSessionId(sessionId);
      setSelectedSessionDetail(detail);
      setSessionReplayVerification(verification);
      await onSessionEvidenceChanged?.();
    } catch (err) {
      setErrorText(toErrorMessage(err, "Failed to verify session replay."));
    } finally {
      setBusyCommand(null);
    }
  }, [onSessionEvidenceChanged, services]);

  return {
    ...state,
    toggleCreateForm,
    closeCreateForm,
    updateField,
    createSession,
    closeSession,
    restoreSession,
    verifySessionReplay
  };
}

export function buildPaperSessionState({
  sessions,
  selectedSessionId,
  selectedSessionDetail,
  sessionReplayVerification,
  form,
  showCreateForm,
  busyCommand,
  errorText
}: BuildPaperSessionStateOptions): PaperSessionState {
  const validationError = validatePaperSessionForm(form);
  const isBusy = busyCommand !== null;
  const busyReason = buildPaperSessionBusyReason(busyCommand);
  const canSubmitCreate = !isBusy && validationError === null;
  const canCloseCreateForm = !isBusy;
  const formPanelId = "paper-session-create-form";
  const formDescriptionId = "paper-session-create-requirements";
  const fieldDisabledReason = isBusy ? busyReason : null;

  return {
    sessions,
    selectedSessionId,
    selectedSessionDetail,
    sessionReplayVerification,
    form,
    showCreateForm,
    busyCommand,
    isBusy,
    errorText,
    rows: buildPaperSessionRows({ sessions, selectedSessionId, busyCommand }),
    detail: selectedSessionDetail
      ? buildPaperSessionDetailPanel(selectedSessionDetail, sessionReplayVerification)
      : null,
    emptyText: busyCommand?.kind === "loading"
      ? "Loading paper sessions."
      : "No paper sessions active. Create one above to start tracking execution.",
    selectedSessionLabel: selectedSessionId ? `Selected session: ${selectedSessionId}` : null,
    formPanelId,
    formDescriptionId,
    strategyIdField: {
      id: "paper-session-strategy-id",
      label: "Strategy ID",
      ariaLabel: "Strategy ID",
      field: "strategyId",
      value: form.strategyId,
      type: "text",
      placeholder: "my-strategy-01",
      autoComplete: "off",
      describedBy: formDescriptionId,
      disabled: isBusy,
      disabledReason: fieldDisabledReason,
      invalid: false
    },
    initialCashField: {
      id: "paper-session-initial-cash",
      label: "Initial cash ($)",
      ariaLabel: "Initial cash ($)",
      field: "initialCash",
      value: form.initialCash,
      type: "number",
      autoComplete: "off",
      describedBy: formDescriptionId,
      disabled: isBusy,
      disabledReason: fieldDisabledReason,
      invalid: validationError !== null,
      min: 1000,
      step: 1000
    },
    formRequirementText: busyReason ?? validationError ?? "Strategy ID is optional. Initial cash must be at least $1,000.",
    toggleCreateButtonLabel: showCreateForm ? "Close form" : "New session",
    toggleCreateButtonAriaLabel: showCreateForm ? "Close paper session creation form" : "Open paper session creation form",
    toggleCreateButtonDisabledReason: isBusy && !showCreateForm ? busyReason : null,
    createButtonLabel: busyCommand?.kind === "creating" ? "Creating…" : "Create session",
    createButtonBusy: busyCommand?.kind === "creating",
    createButtonBusyLabel: busyCommand?.kind === "creating" ? "Creating…" : null,
    cancelCreateButtonLabel: "Cancel",
    createButtonAriaLabel: busyCommand?.kind === "creating" ? "Creating paper session" : "Create paper session",
    canSubmitCreate,
    createButtonDisabledReason: canSubmitCreate ? null : busyReason ?? validationError,
    canCloseCreateForm,
    cancelCreateButtonDisabledReason: canCloseCreateForm ? null : busyReason,
    statusAnnouncement: buildPaperSessionAnnouncement({
      sessions,
      selectedSessionDetail,
      sessionReplayVerification,
      busyCommand,
      errorText
    })
  };
}

export function validatePaperSessionForm(form: PaperSessionForm): string | null {
  const initialCash = parsePaperSessionCash(form.initialCash);

  if (initialCash === null || initialCash < 1_000) {
    return "Enter initial cash of at least $1,000.";
  }

  return null;
}

export function buildPaperSessionCreateRequest(
  form: PaperSessionForm,
  now: () => number = Date.now
): PaperSessionCreateRequest {
  return {
    strategyId: form.strategyId.trim() || `strat-${now()}`,
    initialCash: parsePaperSessionCash(form.initialCash) ?? 100_000
  };
}

function buildPaperSessionRows({
  sessions,
  selectedSessionId,
  busyCommand
}: {
  sessions: PaperSessionSummary[];
  selectedSessionId: string | null;
  busyCommand: PaperSessionBusyCommand | null;
}): PaperSessionRow[] {
  return sessions.map((session) => {
    const rowBusy = busyCommand?.sessionId === session.sessionId;
    const anyBusy = busyCommand !== null;
    const busyReason = buildPaperSessionBusyReason(busyCommand);
    const statusLabel = getPaperSessionStatus(session);
    const restoreDisabledReason = anyBusy ? busyReason : null;
    const verifyDisabledReason = anyBusy ? busyReason : null;
    const closeDisabledReason = !session.isActive
      ? "Only active paper sessions can be closed."
      : anyBusy
        ? busyReason
        : null;

    return {
      sessionId: session.sessionId,
      strategyId: session.strategyId,
      initialCashText: formatUsdValue(session.initialCash),
      statusLabel,
      isActive: session.isActive,
      isSelected: selectedSessionId === session.sessionId,
      ariaLabel: `${session.sessionId}, ${session.strategyId}, ${statusLabel}, ${formatUsdValue(session.initialCash)} initial cash`,
      restoreButtonLabel: rowBusy && busyCommand?.kind === "restoring" ? "Restoring…" : "Restore",
      verifyButtonLabel: rowBusy && busyCommand?.kind === "verifying" ? "Verifying…" : "Verify replay",
      closeButtonLabel: rowBusy && busyCommand?.kind === "closing" ? "Closing…" : "Close",
      restoreAriaLabel: rowBusy && busyCommand?.kind === "restoring"
        ? `Restoring paper session ${session.sessionId}`
        : `Restore paper session ${session.sessionId}`,
      verifyAriaLabel: rowBusy && busyCommand?.kind === "verifying"
        ? `Verifying replay for paper session ${session.sessionId}`
        : `Verify replay for paper session ${session.sessionId}`,
      closeAriaLabel: rowBusy && busyCommand?.kind === "closing"
        ? `Closing paper session ${session.sessionId}`
        : `Close paper session ${session.sessionId}`,
      canRestore: !anyBusy,
      canVerify: !anyBusy,
      canClose: session.isActive && !anyBusy,
      restoreDisabledReason,
      verifyDisabledReason,
      closeDisabledReason
    };
  });
}

function buildPaperSessionBusyReason(busyCommand: PaperSessionBusyCommand | null): string | null {
  if (!busyCommand) {
    return null;
  }

  switch (busyCommand.kind) {
    case "loading":
      return "Paper sessions are still loading.";
    case "creating":
      return "Paper session creation is in progress.";
    case "restoring":
      return `Paper session ${busyCommand.sessionId ?? "restore"} is restoring.`;
    case "verifying":
      return `Paper session ${busyCommand.sessionId ?? "replay"} verification is running.`;
    case "closing":
      return `Paper session ${busyCommand.sessionId ?? "close"} is closing.`;
  }
}

function buildPaperSessionDetailPanel(
  detail: PaperSessionDetail,
  replayVerification: PaperSessionReplayVerification | null
): PaperSessionDetailPanel {
  const replay = replayVerification?.summary.sessionId === detail.summary.sessionId
    ? buildPaperSessionReplayPanel(replayVerification)
    : null;

  return {
    sessionId: detail.summary.sessionId,
    statusLabel: getPaperSessionStatus(detail.summary),
    statusTone: detail.summary.isActive ? "ready" : "review",
    ariaLabel: `Paper session detail for ${detail.summary.sessionId}`,
    infoRows: [
      { label: "Strategy", value: detail.summary.strategyId },
      { label: "Initial cash", value: formatUsdValue(detail.summary.initialCash) },
      { label: "Tracked symbols", value: detail.symbols.length > 0 ? detail.symbols.join(", ") : "None" },
      { label: "Orders retained", value: String(detail.orderHistory?.length ?? 0) }
    ],
    metricRows: detail.portfolio
      ? [
          { label: "Cash", value: formatUsdValue(detail.portfolio.cash) },
          { label: "Portfolio value", value: formatUsdValue(detail.portfolio.portfolioValue) },
          { label: "Open positions", value: String(detail.portfolio.positions.length) }
        ]
      : [],
    replay
  };
}

function buildPaperSessionReplayPanel(verification: PaperSessionReplayVerification): PaperSessionReplayPanel {
  return {
    tone: verification.isConsistent ? "success" : "warning",
    statusLabel: verification.isConsistent ? "Matched current state" : "Mismatch detected",
    ariaLabel: verification.isConsistent
      ? `Replay verification matched current state for ${verification.summary.sessionId}`
      : `Replay verification mismatch detected for ${verification.summary.sessionId}`,
    metadataText: `Source: ${verification.replaySource} · Verified at ${verification.verifiedAt}`,
    rows: [
      { label: "Compared fills", value: String(verification.comparedFillCount) },
      { label: "Compared orders", value: String(verification.comparedOrderCount) },
      { label: "Compared ledger entries", value: String(verification.comparedLedgerEntryCount) },
      { label: "Verification audit", value: verification.verificationAuditId ?? "Unavailable" },
      { label: "Last persisted fill", value: verification.lastPersistedFillAt ?? "N/A" },
      { label: "Last persisted order update", value: verification.lastPersistedOrderUpdateAt ?? "N/A" }
    ],
    mismatchReasons: verification.mismatchReasons.slice(0, 3)
  };
}

function buildPaperSessionAnnouncement({
  sessions,
  selectedSessionDetail,
  sessionReplayVerification,
  busyCommand,
  errorText
}: {
  sessions: PaperSessionSummary[];
  selectedSessionDetail: PaperSessionDetail | null;
  sessionReplayVerification: PaperSessionReplayVerification | null;
  busyCommand: PaperSessionBusyCommand | null;
  errorText: string | null;
}): string {
  if (busyCommand) {
    if (busyCommand.kind === "loading") {
      return "Loading paper sessions.";
    }

    if (busyCommand.kind === "creating") {
      return "Creating paper session.";
    }

    if (busyCommand.sessionId) {
      return `${capitalizeWord(busyCommand.kind)} paper session ${busyCommand.sessionId}.`;
    }
  }

  if (errorText) {
    return `Paper session workflow failed: ${errorText}`;
  }

  if (sessionReplayVerification) {
    return sessionReplayVerification.isConsistent
      ? `Replay verification matched current state for ${sessionReplayVerification.summary.sessionId}.`
      : `Replay verification mismatch detected for ${sessionReplayVerification.summary.sessionId}.`;
  }

  if (selectedSessionDetail) {
    return `Paper session ${selectedSessionDetail.summary.sessionId} restored.`;
  }

  return `${sessions.length} paper session${sessions.length === 1 ? "" : "s"} loaded.`;
}

function parsePaperSessionCash(value: string): number | null {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function getPaperSessionStatus(session: PaperSessionSummary): string {
  return session.isActive ? "Active" : "Closed";
}

function formatUsdValue(value: number): string {
  return formatCurrency(value);
}

export type SessionReplayCommandKind =
  | "loading"
  | "starting"
  | "pausing"
  | "resuming"
  | "stopping"
  | "seeking"
  | "applying-speed";

export interface SessionReplayFileOption {
  path: string;
  name: string;
  ariaLabel: string;
  metadataText: string;
}

export interface SessionReplayStatusPanel {
  role: "status" | "alert";
  ariaLive: "polite" | "assertive";
  ariaLabel: string;
  tone: "default" | "warning" | "danger" | "success";
  title: string;
  detail: string;
}

export interface SessionReplayControlsState {
  files: ReplayFileRecord[];
  fileOptions: SessionReplayFileOption[];
  selectedFilePath: string;
  replayStatus: ReplayStatus | null;
  replaySpeed: string;
  seekMs: string;
  loadingFiles: boolean;
  activeCommand: SessionReplayCommandKind | null;
  errorText: string | null;
  isBusy: boolean;
  sectionTitleId: string;
  sectionDescriptionId: string;
  sectionTitle: string;
  sectionDescription: string;
  fileSelectId: string;
  fileSelectLabel: string;
  fileSelectAriaLabel: string;
  fileSelectDescribedBy: string;
  fileEmptyOptionText: string;
  speedInputId: string;
  speedLabel: string;
  speedAriaLabel: string;
  speedHelpId: string;
  speedHelpText: string;
  speedDescribedBy: string;
  seekInputId: string;
  seekLabel: string;
  seekAriaLabel: string;
  seekHelpId: string;
  seekHelpText: string;
  seekDescribedBy: string;
  statusId: string;
  errorId: string;
  speedValidationText: string | null;
  seekValidationText: string | null;
  activeErrorText: string | null;
  statusPanel: SessionReplayStatusPanel;
  canStart: boolean;
  canPause: boolean;
  canResume: boolean;
  canStop: boolean;
  canSeek: boolean;
  canApplySpeed: boolean;
  startDisabledReason: string | null;
  pauseDisabledReason: string | null;
  resumeDisabledReason: string | null;
  stopDisabledReason: string | null;
  seekDisabledReason: string | null;
  applySpeedDisabledReason: string | null;
  startButtonLabel: string;
  pauseButtonLabel: string;
  resumeButtonLabel: string;
  stopButtonLabel: string;
  seekButtonLabel: string;
  applySpeedButtonLabel: string;
  statusText: string;
  statusAnnouncement: string;
}

export interface SessionReplayControlsViewModel extends SessionReplayControlsState {
  selectReplayFile: (path: string) => void;
  updateReplaySpeed: (value: string) => void;
  updateSeekMs: (value: string) => void;
  startReplay: () => Promise<void>;
  pauseReplay: () => Promise<void>;
  resumeReplay: () => Promise<void>;
  stopReplay: () => Promise<void>;
  seekReplay: () => Promise<void>;
  applyReplaySpeed: () => Promise<void>;
}

export interface SessionReplayControlsServices {
  getReplayFiles: () => Promise<{ files: ReplayFileRecord[]; total: number; timestamp: string }>;
  startReplay: (filePath: string, speedMultiplier?: number) => Promise<{ sessionId: string }>;
  getReplayStatus: (sessionId: string) => Promise<ReplayStatus>;
  pauseReplay: (sessionId: string) => Promise<unknown>;
  resumeReplay: (sessionId: string) => Promise<unknown>;
  stopReplay: (sessionId: string) => Promise<unknown>;
  seekReplay: (sessionId: string, positionMs: number) => Promise<unknown>;
  setReplaySpeed: (sessionId: string, speedMultiplier: number) => Promise<unknown>;
}

export interface BuildSessionReplayControlsStateOptions {
  files: ReplayFileRecord[];
  selectedFilePath: string;
  replayStatus: ReplayStatus | null;
  replaySpeed: string;
  seekMs: string;
  loadingFiles: boolean;
  activeCommand: SessionReplayCommandKind | null;
  errorText: string | null;
}

const defaultSessionReplayControlsServices: SessionReplayControlsServices = {
  getReplayFiles: () => workstationApi.getReplayFiles(),
  startReplay: (filePath, speedMultiplier) => workstationApi.startReplay(filePath, speedMultiplier),
  getReplayStatus: (sessionId) => workstationApi.getReplayStatus(sessionId),
  pauseReplay: (sessionId) => workstationApi.pauseReplay(sessionId),
  resumeReplay: (sessionId) => workstationApi.resumeReplay(sessionId),
  stopReplay: (sessionId) => workstationApi.stopReplay(sessionId),
  seekReplay: (sessionId, positionMs) => workstationApi.seekReplay(sessionId, positionMs),
  setReplaySpeed: (sessionId, speedMultiplier) => workstationApi.setReplaySpeed(sessionId, speedMultiplier)
};

export function useSessionReplayControlsViewModel(
  services: SessionReplayControlsServices = defaultSessionReplayControlsServices
): SessionReplayControlsViewModel {
  const [files, setFiles] = useState<ReplayFileRecord[]>([]);
  const [selectedFilePath, setSelectedFilePath] = useState("");
  const [replayStatus, setReplayStatus] = useState<ReplayStatus | null>(null);
  const [replaySpeed, setReplaySpeed] = useState("1");
  const [seekMs, setSeekMs] = useState("0");
  const [loadingFiles, setLoadingFiles] = useState(true);
  const [activeCommand, setActiveCommand] = useState<SessionReplayCommandKind | null>("loading");
  const [errorText, setErrorText] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    setLoadingFiles(true);
    setActiveCommand("loading");
    setErrorText(null);

    services.getReplayFiles()
      .then((result) => {
        if (cancelled) {
          return;
        }

        setFiles(result.files);
        setSelectedFilePath((current) => current || result.files[0]?.path || "");
      })
      .catch((err) => {
        if (cancelled) {
          return;
        }

        setFiles([]);
        setSelectedFilePath("");
        setErrorText(toErrorMessage(err, "Failed to load replay files."));
      })
      .finally(() => {
        if (!cancelled) {
          setLoadingFiles(false);
          setActiveCommand(null);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [services]);

  const state = useMemo(
    () => buildSessionReplayControlsState({
      files,
      selectedFilePath,
      replayStatus,
      replaySpeed,
      seekMs,
      loadingFiles,
      activeCommand,
      errorText
    }),
    [activeCommand, errorText, files, loadingFiles, replaySpeed, replayStatus, seekMs, selectedFilePath]
  );

  const selectReplayFile = useCallback((path: string) => {
    setSelectedFilePath(path);
    setErrorText(null);
  }, []);

  const updateReplaySpeed = useCallback((value: string) => {
    setReplaySpeed(value);
    setErrorText(null);
  }, []);

  const updateSeekMs = useCallback((value: string) => {
    setSeekMs(value);
    setErrorText(null);
  }, []);

  const startReplayCommand = useCallback(async () => {
    const speed = parseReplaySpeed(replaySpeed);

    if (!selectedFilePath) {
      setErrorText("Select a replay file before starting replay.");
      return;
    }

    if (speed === null) {
      setErrorText(validateReplaySpeed(replaySpeed));
      return;
    }

    setActiveCommand("starting");
    setErrorText(null);

    try {
      const started = await services.startReplay(selectedFilePath, speed);
      setReplayStatus(await services.getReplayStatus(started.sessionId));
    } catch (err) {
      setErrorText(toErrorMessage(err, "Failed to start replay."));
    } finally {
      setActiveCommand(null);
    }
  }, [replaySpeed, selectedFilePath, services]);

  const runReplayCommand = useCallback(async (
    command: Exclude<SessionReplayCommandKind, "loading" | "starting">,
    action: (sessionId: string) => Promise<unknown>,
    fallback: string,
    options: { clearStatus?: boolean } = {}
  ) => {
    const status = replayStatus;

    if (!status) {
      setErrorText("Start a replay before using controls.");
      return;
    }

    setActiveCommand(command);
    setErrorText(null);

    try {
      await action(status.sessionId);
      if (options.clearStatus) {
        setReplayStatus(null);
      } else {
        setReplayStatus(await services.getReplayStatus(status.sessionId));
      }
    } catch (err) {
      setErrorText(toErrorMessage(err, fallback));
    } finally {
      setActiveCommand(null);
    }
  }, [replayStatus, services]);

  const seekReplayCommand = useCallback(async () => {
    const positionMs = parseReplaySeekMs(seekMs);

    if (positionMs === null) {
      setErrorText(validateReplaySeekMs(seekMs));
      return;
    }

    await runReplayCommand(
      "seeking",
      (sessionId) => services.seekReplay(sessionId, positionMs),
      "Replay seek failed."
    );
  }, [runReplayCommand, seekMs, services]);

  const applyReplaySpeedCommand = useCallback(async () => {
    const speed = parseReplaySpeed(replaySpeed);

    if (speed === null) {
      setErrorText(validateReplaySpeed(replaySpeed));
      return;
    }

    await runReplayCommand(
      "applying-speed",
      (sessionId) => services.setReplaySpeed(sessionId, speed),
      "Replay speed update failed."
    );
  }, [replaySpeed, runReplayCommand, services]);

  return {
    ...state,
    selectReplayFile,
    updateReplaySpeed,
    updateSeekMs,
    startReplay: startReplayCommand,
    pauseReplay: () => runReplayCommand(
      "pausing",
      (sessionId) => services.pauseReplay(sessionId),
      "Replay pause failed."
    ),
    resumeReplay: () => runReplayCommand(
      "resuming",
      (sessionId) => services.resumeReplay(sessionId),
      "Replay resume failed."
    ),
    stopReplay: () => runReplayCommand(
      "stopping",
      (sessionId) => services.stopReplay(sessionId),
      "Replay stop failed.",
      { clearStatus: true }
    ),
    seekReplay: seekReplayCommand,
    applyReplaySpeed: applyReplaySpeedCommand
  };
}

export function buildSessionReplayControlsState({
  files,
  selectedFilePath,
  replayStatus,
  replaySpeed,
  seekMs,
  loadingFiles,
  activeCommand,
  errorText
}: BuildSessionReplayControlsStateOptions): SessionReplayControlsState {
  const speedValidationText = validateReplaySpeed(replaySpeed);
  const seekValidationText = validateReplaySeekMs(seekMs);
  const isBusy = loadingFiles || activeCommand !== null;
  const hasReplayStatus = replayStatus !== null;
  const statusId = "session-replay-status";
  const errorId = "session-replay-error";
  const speedHelpId = "session-replay-speed-help";
  const seekHelpId = "session-replay-seek-help";
  const activeErrorText = errorText ?? speedValidationText ?? seekValidationText;
  const fileSelectDescribedBy = [statusId, activeErrorText ? errorId : null].filter(Boolean).join(" ");
  const speedDescribedBy = [statusId, speedHelpId, activeErrorText ? errorId : null]
    .filter(Boolean)
    .join(" ");
  const seekDescribedBy = [statusId, seekHelpId, activeErrorText ? errorId : null]
    .filter(Boolean)
    .join(" ");
  const selectedFileName = files.find((file) => file.path === selectedFilePath)?.name ?? null;
  const statusPanel = buildSessionReplayStatusPanel({
    files,
    selectedFilePath,
    selectedFileName,
    replayStatus,
    loadingFiles,
    activeCommand,
    activeErrorText
  });
  const baseDisabledReason = buildSessionReplayBaseDisabledReason({
    loadingFiles,
    activeCommand,
    hasReplayStatus,
    selectedFilePath,
    speedValidationText,
    seekValidationText
  });

  return {
    files,
    fileOptions: files.map(buildSessionReplayFileOption),
    selectedFilePath,
    replayStatus,
    replaySpeed,
    seekMs,
    loadingFiles,
    activeCommand,
    errorText,
    isBusy,
    sectionTitleId: "session-replay-title",
    sectionDescriptionId: "session-replay-description",
    sectionTitle: "Session replay controls",
    sectionDescription: "Start and control replay for reconnect/resume validation.",
    fileSelectId: "session-replay-file",
    fileSelectLabel: "Replay file",
    fileSelectAriaLabel: loadingFiles ? "Replay file, loading files" : "Replay file",
    fileSelectDescribedBy,
    fileEmptyOptionText: loadingFiles ? "Loading replay files..." : "No replay files available",
    speedInputId: "session-replay-speed",
    speedLabel: "Replay speed",
    speedAriaLabel: "Replay speed multiplier",
    speedHelpId,
    speedHelpText: "Multiplier greater than 0.",
    speedDescribedBy,
    seekInputId: "session-replay-seek",
    seekLabel: "Seek position",
    seekAriaLabel: "Seek position in milliseconds",
    seekHelpId,
    seekHelpText: "Position in milliseconds from replay start.",
    seekDescribedBy,
    statusId,
    errorId,
    speedValidationText,
    seekValidationText,
    activeErrorText,
    statusPanel,
    canStart: !isBusy && selectedFilePath.trim().length > 0 && speedValidationText === null,
    canPause: !isBusy && hasReplayStatus,
    canResume: !isBusy && hasReplayStatus,
    canStop: !isBusy && hasReplayStatus,
    canSeek: !isBusy && hasReplayStatus && seekValidationText === null,
    canApplySpeed: !isBusy && hasReplayStatus && speedValidationText === null,
    startDisabledReason: !isBusy && selectedFilePath.trim().length === 0
      ? "Select a replay file before starting replay."
      : !isBusy && speedValidationText
        ? speedValidationText
        : baseDisabledReason,
    pauseDisabledReason: baseDisabledReason,
    resumeDisabledReason: baseDisabledReason,
    stopDisabledReason: baseDisabledReason,
    seekDisabledReason: !isBusy && seekValidationText ? seekValidationText : baseDisabledReason,
    applySpeedDisabledReason: !isBusy && speedValidationText ? speedValidationText : baseDisabledReason,
    startButtonLabel: activeCommand === "starting" ? "Starting..." : "Start",
    pauseButtonLabel: activeCommand === "pausing" ? "Pausing..." : "Pause",
    resumeButtonLabel: activeCommand === "resuming" ? "Resuming..." : "Resume",
    stopButtonLabel: activeCommand === "stopping" ? "Stopping..." : "Stop",
    seekButtonLabel: activeCommand === "seeking" ? "Seeking..." : "Seek",
    applySpeedButtonLabel: activeCommand === "applying-speed" ? "Applying speed..." : "Apply speed",
    statusText: buildSessionReplayStatusText({ files, selectedFilePath, replayStatus, loadingFiles }),
    statusAnnouncement: buildSessionReplayAnnouncement({
      files,
      selectedFilePath,
      replayStatus,
      loadingFiles,
      activeCommand,
      errorText
    })
  };
}

function buildSessionReplayFileOption(file: ReplayFileRecord): SessionReplayFileOption {
  const metadata = formatReplayFileMetadata(file);

  return {
    path: file.path,
    name: file.name,
    metadataText: metadata,
    ariaLabel: metadata ? `${file.name}, ${metadata}` : file.name
  };
}

function formatReplayFileMetadata(file: ReplayFileRecord): string {
  const details = [
    file.symbol,
    file.eventType,
    file.isCompressed ? "compressed" : null,
    file.lastModified
  ].filter(Boolean);

  return details.length > 0 ? details.join(" / ") : "";
}

function buildSessionReplayStatusText({
  files,
  selectedFilePath,
  replayStatus,
  loadingFiles
}: {
  files: ReplayFileRecord[];
  selectedFilePath: string;
  replayStatus: ReplayStatus | null;
  loadingFiles: boolean;
}): string {
  if (replayStatus) {
    return `Replay ${replayStatus.status} · ${replayStatus.eventsProcessed}/${replayStatus.totalEvents} (${replayStatus.progressPercent}%)`;
  }

  if (loadingFiles) {
    return "Loading replay files.";
  }

  if (files.length === 0) {
    return "No replay files available.";
  }

  const selectedFileName = files.find((file) => file.path === selectedFilePath)?.name ?? "selected file";
  return `Ready to replay ${selectedFileName}.`;
}

function buildSessionReplayAnnouncement({
  files,
  selectedFilePath,
  replayStatus,
  loadingFiles,
  activeCommand,
  errorText
}: {
  files: ReplayFileRecord[];
  selectedFilePath: string;
  replayStatus: ReplayStatus | null;
  loadingFiles: boolean;
  activeCommand: SessionReplayCommandKind | null;
  errorText: string | null;
}): string {
  if (activeCommand) {
    return formatSessionReplayCommandAnnouncement(activeCommand);
  }

  if (errorText) {
    return `Session replay failed: ${errorText}`;
  }

  if (replayStatus) {
    return `Replay ${replayStatus.status} for ${replayStatus.sessionId} at ${replayStatus.progressPercent} percent.`;
  }

  if (loadingFiles) {
    return "Loading replay files.";
  }

  if (files.length === 0) {
    return "No replay files available.";
  }

  const selectedFileName = files.find((file) => file.path === selectedFilePath)?.name ?? "selected file";
  return `Replay file ${selectedFileName} selected.`;
}

function formatSessionReplayCommandAnnouncement(command: SessionReplayCommandKind): string {
  const announcements: Record<SessionReplayCommandKind, string> = {
    loading: "Loading replay files.",
    starting: "Starting session replay.",
    pausing: "Pausing session replay.",
    resuming: "Resuming session replay.",
    stopping: "Stopping session replay.",
    seeking: "Seeking session replay.",
    "applying-speed": "Applying replay speed."
  };

  return announcements[command];
}

function validateReplaySpeed(value: string): string | null {
  return parseReplaySpeed(value) === null
    ? "Enter a replay speed greater than 0."
    : null;
}

function validateReplaySeekMs(value: string): string | null {
  return parseReplaySeekMs(value) === null
    ? "Enter a seek position of 0 ms or greater."
    : null;
}

function parseReplaySpeed(value: string): number | null {
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
}

function parseReplaySeekMs(value: string): number | null {
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null;
}

export type TradingConfirmAction =
  | { kind: "cancel-order"; orderId: string }
  | { kind: "cancel-all" }
  | { kind: "close-position"; positionKey: string; symbol: string }
  | { kind: "pause-strategy"; strategyId: string }
  | { kind: "stop-strategy"; strategyId: string }
  | { kind: "open-circuit-breaker" }
  | { kind: "close-circuit-breaker" };

export interface StrategyLifecycleControlsState {
  strategyId: string;
  strategyIdValue: string;
  strategyIdInputId: string;
  strategyIdHelpId: string;
  strategyIdStatusId: string;
  titleId: string;
  title: string;
  description: string;
  strategyIdLabel: string;
  strategyIdPlaceholder: string;
  helpText: string;
  statusText: string;
  statusAnnouncement: string;
  canPause: boolean;
  canStop: boolean;
  pauseButtonLabel: string;
  stopButtonLabel: string;
  pauseAriaLabel: string;
  stopAriaLabel: string;
  pauseDisabledReason: string | null;
  stopDisabledReason: string | null;
  pauseAction: TradingConfirmAction | null;
  stopAction: TradingConfirmAction | null;
}

export interface StrategyLifecycleControlsViewModel extends StrategyLifecycleControlsState {
  updateStrategyId: (value: string) => void;
  openPauseConfirm: () => void;
  openStopConfirm: () => void;
}

export function useStrategyLifecycleControlsViewModel({
  openConfirm
}: {
  openConfirm: (action: TradingConfirmAction) => void;
}): StrategyLifecycleControlsViewModel {
  const [strategyId, setStrategyId] = useState("");
  const state = useMemo(() => buildStrategyLifecycleControlsState(strategyId), [strategyId]);

  const openPauseConfirm = useCallback(() => {
    if (state.pauseAction) {
      openConfirm(state.pauseAction);
    }
  }, [openConfirm, state.pauseAction]);

  const openStopConfirm = useCallback(() => {
    if (state.stopAction) {
      openConfirm(state.stopAction);
    }
  }, [openConfirm, state.stopAction]);

  return {
    ...state,
    updateStrategyId: setStrategyId,
    openPauseConfirm,
    openStopConfirm
  };
}

export function buildStrategyLifecycleControlsState(strategyId: string): StrategyLifecycleControlsState {
  const strategyIdValue = strategyId.trim();
  const hasStrategyId = strategyIdValue.length > 0;
  const missingStrategyReason = "Enter a registered strategy ID before using lifecycle actions.";
  const pauseAction: TradingConfirmAction | null = hasStrategyId
    ? { kind: "pause-strategy", strategyId: strategyIdValue }
    : null;
  const stopAction: TradingConfirmAction | null = hasStrategyId
    ? { kind: "stop-strategy", strategyId: strategyIdValue }
    : null;
  const statusText = hasStrategyId
    ? `Ready to open lifecycle confirmation for ${strategyIdValue}.`
    : "Enter a registered strategy ID to enable lifecycle actions.";

  return {
    strategyId,
    strategyIdValue,
    strategyIdInputId: "trading-strategy-lifecycle-id",
    strategyIdHelpId: "trading-strategy-lifecycle-help",
    strategyIdStatusId: "trading-strategy-lifecycle-status",
    titleId: "trading-strategy-lifecycle-title",
    title: "Strategy lifecycle",
    description: "Pause or stop a running strategy by its registered ID. Changes open a confirmation before execution.",
    strategyIdLabel: "Strategy ID",
    strategyIdPlaceholder: "e.g. mean-reversion-fx-01",
    helpText: "Use the registered strategy ID from the active paper session or Strategy run record.",
    statusText,
    statusAnnouncement: hasStrategyId
      ? `Strategy lifecycle controls ready for ${strategyIdValue}.`
      : "Strategy lifecycle controls are waiting for a strategy ID.",
    canPause: hasStrategyId,
    canStop: hasStrategyId,
    pauseButtonLabel: "Pause",
    stopButtonLabel: "Stop",
    pauseAriaLabel: hasStrategyId
      ? `Open pause confirmation for strategy ${strategyIdValue}`
      : "Enter a strategy ID before pausing a strategy",
    stopAriaLabel: hasStrategyId
      ? `Open stop confirmation for strategy ${strategyIdValue}`
      : "Enter a strategy ID before stopping a strategy",
    pauseDisabledReason: hasStrategyId ? null : missingStrategyReason,
    stopDisabledReason: hasStrategyId ? null : missingStrategyReason,
    pauseAction,
    stopAction
  };
}

export interface TradingConfirmState {
  action: TradingConfirmAction | null;
  busy: boolean;
  result: TradingActionResult | null;
  error: string | null;
  acknowledged: boolean;
}

export interface TradingConfirmResultPanel {
  status: string;
  message: string;
  actionId: string;
  tone: "success" | "warning";
  ariaLabel: string;
}

export interface TradingConfirmErrorPanel {
  text: string;
  ariaLabel: string;
}

export interface TradingConfirmAcknowledgementState {
  id: string;
  label: string;
  description: string;
  checked: boolean;
  disabled: boolean;
  disabledReason: string | null;
}

export interface TradingConfirmDialogState {
  open: boolean;
  title: string;
  description: string;
  dialogTitleId: string;
  dialogDescriptionId: string;
  statusAnnouncement: string;
  cancelButtonLabel: string;
  confirmButtonLabel: string;
  confirmButtonVariant: "default" | "destructive";
  closeButtonLabel: string;
  confirmAriaLabel: string;
  closeAriaLabel: string;
  confirmDisabledReason: string | null;
  acknowledgement: TradingConfirmAcknowledgementState;
  canClose: boolean;
  canConfirm: boolean;
  isCompleted: boolean;
  resultPanel: TradingConfirmResultPanel | null;
  errorPanel: TradingConfirmErrorPanel | null;
}

export interface TradingConfirmServices {
  cancelOrder: (orderId: string) => Promise<TradingActionResult>;
  cancelAllOrders: () => Promise<TradingActionResult>;
  closePosition: (positionKey: string, fundAccountId?: string | null) => Promise<TradingActionResult>;
  pauseStrategy: (strategyId: string) => Promise<{ success: boolean; reason: string | null }>;
  stopStrategy: (strategyId: string) => Promise<{ success: boolean; reason: string | null }>;
  updateExecutionCircuitBreaker: (
    request: UpdateExecutionCircuitBreakerRequest
  ) => Promise<ExecutionCircuitBreakerActivationResponse>;
}

export interface TradingConfirmViewModel extends TradingConfirmDialogState {
  state: TradingConfirmState;
  openConfirm: (action: TradingConfirmAction) => void;
  closeConfirm: () => void;
  setReviewAcknowledged: (value: boolean) => void;
  executeConfirm: () => Promise<void>;
}

const defaultTradingConfirmServices: TradingConfirmServices = {
  cancelOrder: (orderId) => workstationApi.cancelOrder(orderId),
  cancelAllOrders: () => workstationApi.cancelAllOrders(),
  closePosition: (positionKey, fundAccountId) => workstationApi.closePosition(positionKey, fundAccountId),
  pauseStrategy: (strategyId) => workstationApi.pauseStrategy(strategyId),
  stopStrategy: (strategyId) => workstationApi.stopStrategy(strategyId),
  updateExecutionCircuitBreaker: (request) => workstationApi.updateExecutionCircuitBreaker(request)
};

export function useTradingConfirmViewModel({
  services = defaultTradingConfirmServices,
  onActionSettled,
  fundAccountId
}: {
  services?: TradingConfirmServices;
  onActionSettled?: () => Promise<void> | void;
  fundAccountId?: string | null;
} = {}): TradingConfirmViewModel {
  const [state, setState] = useState<TradingConfirmState>(() => createTradingConfirmState());
  const executingRef = useRef(false);

  const openConfirm = useCallback((action: TradingConfirmAction) => {
    setState((current) => current.busy ? current : createTradingConfirmState(action));
  }, []);

  const closeConfirm = useCallback(() => {
    setState((current) => current.busy ? current : createTradingConfirmState());
  }, []);

  const setReviewAcknowledged = useCallback((value: boolean) => {
    setState((current) => {
      if (!current.action || current.busy || current.result) {
        return current;
      }

      return { ...current, acknowledged: value };
    });
  }, []);

  const executeConfirm = useCallback(async () => {
    const action = state.action;
    if (!action || !state.acknowledged || state.busy || state.result || executingRef.current) {
      return;
    }

    executingRef.current = true;
    setState((current) => ({ ...current, busy: true, result: null, error: null }));

    try {
      const result = await executeTradingConfirmAction(action, services, fundAccountId);
      await onActionSettled?.();
      setState((current) => ({ ...current, busy: false, result }));
    } catch (err) {
      setState((current) => ({
        ...current,
        busy: false,
        error: toErrorMessage(err, "Action failed.")
      }));
    } finally {
      executingRef.current = false;
    }
  }, [fundAccountId, onActionSettled, services, state]);

  const dialogState = useMemo(() => buildTradingConfirmDialogState(state), [state]);

  return {
    ...dialogState,
    state,
    openConfirm,
    closeConfirm,
    setReviewAcknowledged,
    executeConfirm
  };
}

export function createTradingConfirmState(action: TradingConfirmAction | null = null, acknowledged = false): TradingConfirmState {
  return {
    action,
    busy: false,
    result: null,
    error: null,
    acknowledged
  };
}

export function buildTradingConfirmDialogState(state: TradingConfirmState): TradingConfirmDialogState {
  const title = state.action ? tradingConfirmActionLabel(state.action) : "";
  const description = state.action ? tradingConfirmActionDescription(state.action) : "";
  const isCompleted = state.result !== null;
  const actionId = state.action ? sanitizeDomId(tradingConfirmActionDomKey(state.action)) : "none";
  const resultPanel = state.result ? buildTradingConfirmResultPanel(state.result) : null;
  const errorPanel = state.error ? { text: state.error, ariaLabel: `Confirmation action failed: ${state.error}` } : null;
  const confirmDisabledReason = buildTradingConfirmDisabledReason(state, isCompleted);
  const acknowledgementDisabledReason = buildTradingConfirmAcknowledgementDisabledReason(state, isCompleted);
  const canConfirm = Boolean(state.action) && !state.busy && !isCompleted && state.acknowledged;

  return {
    open: state.action !== null,
    title,
    description,
    dialogTitleId: `trading-confirm-${actionId}-title`,
    dialogDescriptionId: `trading-confirm-${actionId}-description`,
    statusAnnouncement: buildTradingConfirmStatusAnnouncement({ title, busy: state.busy, result: state.result, error: state.error }),
    cancelButtonLabel: "Cancel",
    confirmButtonLabel: state.busy ? "Processing..." : "Confirm",
    confirmButtonVariant: isDestructiveTradingAction(state.action) ? "destructive" : "default",
    closeButtonLabel: "Close",
    confirmAriaLabel: title ? `Confirm ${title.toLowerCase()}` : "Confirm trading action",
    closeAriaLabel: title ? `Close ${title.toLowerCase()} confirmation` : "Close trading action confirmation",
    confirmDisabledReason,
    acknowledgement: {
      id: `trading-confirm-${actionId}-acknowledgement`,
      label: "I reviewed this trading action",
      description: state.action ? tradingConfirmAcknowledgementDescription(state.action) : "Review the trading action before confirming.",
      checked: state.acknowledged,
      disabled: acknowledgementDisabledReason !== null,
      disabledReason: acknowledgementDisabledReason
    },
    canClose: !state.busy,
    canConfirm,
    isCompleted,
    resultPanel,
    errorPanel
  };
}

function isDestructiveTradingAction(action: TradingConfirmAction | null): boolean {
  return action?.kind === "cancel-order"
    || action?.kind === "cancel-all"
    || action?.kind === "close-position"
    || action?.kind === "stop-strategy"
    || action?.kind === "open-circuit-breaker";
}

function buildTradingConfirmDisabledReason(state: TradingConfirmState, isCompleted: boolean): string | null {
  if (!state.action) {
    return "Open a trading action before confirming.";
  }

  if (state.busy) {
    return "Wait for this trading action to finish.";
  }

  if (isCompleted) {
    return "This trading action already has a result.";
  }

  if (!state.acknowledged) {
    return "Review and acknowledge the trading action before confirming.";
  }

  return null;
}

function buildTradingConfirmAcknowledgementDisabledReason(state: TradingConfirmState, isCompleted: boolean): string | null {
  if (!state.action) {
    return "Open a trading action before acknowledging.";
  }

  if (state.busy) {
    return "Wait for this trading action to finish.";
  }

  if (isCompleted) {
    return "This trading action already has a result.";
  }

  return null;
}

function buildTradingConfirmResultPanel(result: TradingActionResult): TradingConfirmResultPanel {
  const isSuccess = result.status === "Accepted" || result.status === "Completed";

  return {
    status: result.status,
    message: result.message,
    actionId: result.actionId,
    tone: isSuccess ? "success" : "warning",
    ariaLabel: `Action ${result.status.toLowerCase()}: ${result.message}`
  };
}

async function executeTradingConfirmAction(
  action: TradingConfirmAction,
  services: TradingConfirmServices,
  fundAccountId?: string | null
): Promise<TradingActionResult> {
  if (action.kind === "cancel-order") {
    return services.cancelOrder(action.orderId);
  }

  if (action.kind === "cancel-all") {
    return services.cancelAllOrders();
  }

  if (action.kind === "close-position") {
    return services.closePosition(action.positionKey, fundAccountId);
  }

  if (action.kind === "open-circuit-breaker" || action.kind === "close-circuit-breaker") {
    const shouldOpen = action.kind === "open-circuit-breaker";
    const response = await services.updateExecutionCircuitBreaker({
      isOpen: shouldOpen,
      reason: shouldOpen
        ? "Kill switch pulled from the browser workstation."
        : "Circuit breaker reset from the browser workstation."
    });
    return buildCircuitBreakerActionResult(shouldOpen, response);
  }

  const raw = action.kind === "pause-strategy"
    ? await services.pauseStrategy(action.strategyId)
    : await services.stopStrategy(action.strategyId);
  const actionName = action.kind === "pause-strategy" ? "paused" : "stopped";

  return {
    actionId: `act-${Date.now()}`,
    status: raw.success ? "Completed" : "Rejected",
    message: raw.reason ?? (raw.success ? `Strategy ${actionName}.` : `${capitalizeWord(actionName)} rejected.`),
    occurredAt: new Date().toISOString()
  };
}

function tradingConfirmActionLabel(action: TradingConfirmAction): string {
  switch (action.kind) {
    case "cancel-order": return `Cancel order ${action.orderId}`;
    case "cancel-all": return "Cancel all open orders";
    case "close-position": return `Close position - ${action.symbol}`;
    case "pause-strategy": return `Pause strategy - ${action.strategyId}`;
    case "stop-strategy": return `Stop strategy - ${action.strategyId}`;
    case "open-circuit-breaker": return "Open execution circuit breaker";
    case "close-circuit-breaker": return "Reset execution circuit breaker";
  }
}

function tradingConfirmActionDescription(action: TradingConfirmAction): string {
  switch (action.kind) {
    case "cancel-order":
      return "This will request cancellation of the selected order. Partial fills that already occurred are not reversed.";
    case "cancel-all":
      return "This will request cancellation of every open order in the current session. Partial fills that already occurred are not reversed.";
    case "close-position":
      return "A market order will be submitted to flatten the full position at the next available price. You will remain responsible for the resulting fill.";
    case "pause-strategy":
      return "The strategy will stop processing new signals until manually resumed. Open positions and orders remain unchanged.";
    case "stop-strategy":
      return "The strategy will be stopped and its session will be closed. Open positions remain until manually flattened.";
    case "open-circuit-breaker":
      return "This halts new order submission across gateways and immediately sweeps every open order for cancellation. Partial fills that already occurred are not reversed, and positions remain until manually flattened.";
    case "close-circuit-breaker":
      return "This resets the breaker and allows order submission to resume. Orders cancelled by the halt are not restored.";
  }
}

function tradingConfirmAcknowledgementDescription(action: TradingConfirmAction): string {
  switch (action.kind) {
    case "cancel-order":
      return `Cancel order ${action.orderId} after reviewing partial-fill risk.`;
    case "cancel-all":
      return "Cancel every open order in the current session after reviewing partial-fill risk.";
    case "close-position":
      return `Flatten ${action.symbol} with a market order after reviewing fill risk.`;
    case "pause-strategy":
      return `Pause strategy ${action.strategyId} while leaving existing orders and positions unchanged.`;
    case "stop-strategy":
      return `Stop strategy ${action.strategyId} while leaving open positions for manual handling.`;
    case "open-circuit-breaker":
      return "Halt order submission and cancel every open order after reviewing partial-fill risk.";
    case "close-circuit-breaker":
      return "Allow order submission to resume after confirming the book is in a state the desk accepts.";
  }
}

function tradingConfirmActionDomKey(action: TradingConfirmAction): string {
  switch (action.kind) {
    case "cancel-order": return `${action.kind}-${action.orderId}`;
    case "cancel-all": return action.kind;
    case "close-position": return `${action.kind}-${action.positionKey}`;
    case "pause-strategy":
    case "stop-strategy":
      return `${action.kind}-${action.strategyId}`;
    case "open-circuit-breaker":
    case "close-circuit-breaker":
      return action.kind;
  }
}

function buildTradingConfirmStatusAnnouncement({
  title,
  busy,
  result,
  error
}: {
  title: string;
  busy: boolean;
  result: TradingActionResult | null;
  error: string | null;
}): string {
  if (!title) {
    return "";
  }

  if (busy) {
    return `${title} processing.`;
  }

  if (error) {
    return `${title} failed: ${error}`;
  }

  if (result) {
    return `${title} ${result.status.toLowerCase()}: ${result.message}`;
  }

  return `${title} confirmation open.`;
}

function sanitizeDomId(value: string): string {
  const normalized = value.trim().toLowerCase().replace(/[^a-z0-9_-]+/g, "-").replace(/^-+|-+$/g, "");
  return normalized || "action";
}

function capitalizeWord(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}

export type OrderTicketField = "symbol" | "side" | "type" | "quantity" | "limitPrice";
export type OrderTicketPhase = "idle" | "submitting" | "submitted" | "parked" | "error";

export interface OrderTicketServices {
  submitOrder: (request: OrderSubmitRequest) => Promise<OrderResult>;
}

export interface OrderTicketFieldControl {
  field: OrderTicketField;
  id: string;
  label: string;
  value: string;
  describedBy: string;
  ariaLabel: string;
  invalid: boolean;
  required: boolean;
}

export interface OrderTicketSelectControl extends OrderTicketFieldControl {
  options: Array<{ value: string; label: string }>;
}

export interface OrderTicketControls {
  symbol: OrderTicketFieldControl;
  side: OrderTicketSelectControl;
  type: OrderTicketSelectControl;
  quantity: OrderTicketFieldControl;
  limitPrice: OrderTicketFieldControl | null;
}

export interface OrderTicketAcknowledgementState {
  id: string;
  label: string;
  description: string;
  checked: boolean;
  disabled: boolean;
  disabledReason: string | null;
}

export interface OrderTicketState {
  form: OrderSubmitRequest;
  open: boolean;
  phase: OrderTicketPhase;
  orderId: string | null;
  /** Governed-approval queue entry holding a parked order, when phase is "parked". */
  escalationId: string | null;
  /** Non-blocking risk warnings the rails raised for the last submitted order. */
  riskWarnings: string[];
  /** Operator-facing text for a parked order; null in every other phase. */
  parkedText: string | null;
  errorText: string | null;
  validationError: string | null;
  invalidField: OrderTicketField | null;
  canSubmit: boolean;
  canClose: boolean;
  requiresLimitPrice: boolean;
  priceLabel: string;
  formId: string;
  requirementId: string;
  controls: OrderTicketControls;
  openButtonLabel: string;
  submitButtonLabel: string;
  submitAriaLabel: string;
  submitDisabledReason: string | null;
  submitBusy: boolean;
  submitBusyLabel: string | null;
  closeButtonLabel: string;
  closeAriaLabel: string;
  closeDisabledReason: string | null;
  acknowledgement: OrderTicketAcknowledgementState;
  requirementText: string;
  successText: string | null;
  statusAnnouncement: string;
}

export interface BuildOrderTicketStateOptions {
  form: OrderSubmitRequest;
  open: boolean;
  phase: OrderTicketPhase;
  orderId: string | null;
  errorText: string | null;
  escalationId?: string | null;
  riskWarnings?: string[];
  acknowledged?: boolean;
}

export const emptyOrderTicketForm: OrderSubmitRequest = {
  symbol: "",
  side: "Buy",
  type: "Market",
  quantity: 0,
  limitPrice: null
};

const defaultOrderTicketServices: OrderTicketServices = {
  submitOrder: (request) => workstationApi.submitOrder(request)
};

export function useOrderTicketViewModel({
  services = defaultOrderTicketServices,
  onOrderAccepted,
  fundAccountId,
  positions = [],
  risk = null
}: {
  services?: OrderTicketServices;
  onOrderAccepted?: () => Promise<void> | void;
  fundAccountId?: string | null;
  positions?: TradingPosition[];
  risk?: TradingRiskState | null;
} = {}) {
  const [form, setForm] = useState<OrderSubmitRequest>(emptyOrderTicketForm);
  const [open, setOpen] = useState(false);
  const [phase, setPhase] = useState<OrderTicketPhase>("idle");
  const [escalationId, setEscalationId] = useState<string | null>(null);
  const [riskWarnings, setRiskWarnings] = useState<string[]>([]);
  const [orderId, setOrderId] = useState<string | null>(null);
  const [errorText, setErrorText] = useState<string | null>(null);
  const [acknowledged, setAcknowledged] = useState(false);
  const submittingRef = useRef(false);

  const state = useMemo(
    () => buildOrderTicketState({ form, open, phase, orderId, errorText, escalationId, riskWarnings, acknowledged }),
    [acknowledged, errorText, escalationId, form, open, orderId, phase, riskWarnings]
  );

  const preview = useMemo(
    () => buildOrderPreview({ form, positions, risk }),
    [form, positions, risk]
  );

  const openTicket = useCallback(() => {
    setOpen(true);
    setErrorText(null);
    if (phase !== "submitted") {
      return;
    }

    setPhase("idle");
    setOrderId(null);
  }, [phase]);

  const closeTicket = useCallback(() => {
    if (phase === "submitting") {
      return;
    }

    setOpen(false);
    setPhase("idle");
    setOrderId(null);
    setErrorText(null);
    setAcknowledged(false);
  }, [phase]);

  const toggleTicket = useCallback(() => {
    if (open) {
      closeTicket();
      return;
    }

    openTicket();
  }, [closeTicket, open, openTicket]);

  const updateField = useCallback((field: OrderTicketField, value: string) => {
    setForm((current) => updateOrderTicketForm(current, field, value));
    setPhase((current) => current === "submitted" ? "idle" : current);
    setOrderId(null);
    setErrorText(null);
    setAcknowledged(false);
  }, []);

  const normalizeSymbol = useCallback(() => {
    setForm((current) => ({
      ...current,
      symbol: normalizeOrderSymbol(current.symbol)
    }));
  }, []);

  // The post-submit refresh must never rewrite the submission outcome. By the time it runs
  // the server has already accepted or durably parked the order, so a failed refresh is a
  // stale-screen problem, not a submission failure. Letting it fall into the submit catch
  // replaced "parked for governed approval — do not resubmit" with a generic error, and an
  // operator acting on that retries and creates a second independently releasable order.
  const refreshAfterSettledOutcome = useCallback(async () => {
    try {
      await onOrderAccepted?.();
    } catch {
      // Intentionally swallowed: the order's fate is already decided and displayed. The
      // surfaces this refreshes carry their own loading and error state.
    }
  }, [onOrderAccepted]);

  const submitOrderTicket = useCallback(async () => {
    if (phase === "submitting" || submittingRef.current) {
      return;
    }

    const validationError = validateOrderTicketForm(form);
    if (validationError) {
      setPhase("error");
      setOrderId(null);
      setErrorText(validationError);
      return;
    }

    if (!acknowledged) {
      setPhase("error");
      setOrderId(null);
      setErrorText("Review the order preview and acknowledge before submitting.");
      return;
    }

    submittingRef.current = true;
    setPhase("submitting");
    setOrderId(null);
    setEscalationId(null);
    setRiskWarnings([]);
    setErrorText(null);

    try {
      const result = await services.submitOrder(buildOrderSubmitRequest(form, fundAccountId));
      if (result.success) {
        setPhase("submitted");
        setOrderId(result.orderId);
        setEscalationId(null);
        // Non-blocking warnings the rails raised while approving describe exposure the
        // operator now holds. An unqualified success banner would drop them.
        setRiskWarnings(result.riskWarnings ?? []);
        setErrorText(null);
        setOpen(false);
        setAcknowledged(false);
        setForm(emptyOrderTicketForm);
        await refreshAfterSettledOutcome();
        return;
      }

      if (result.requiresApproval) {
        // Parked for governed approval: nothing routed, but a live queue entry can still
        // execute this order. Treating it as a submission failure would tell the operator
        // the opposite of what happened.
        setPhase("parked");
        setOrderId(result.orderId);
        setEscalationId(result.escalationId ?? null);
        setRiskWarnings(result.riskWarnings ?? []);
        setErrorText(null);
        setOpen(false);
        setAcknowledged(false);
        setForm(emptyOrderTicketForm);
        await refreshAfterSettledOutcome();
        return;
      }

      setPhase("error");
      setErrorText(result.errorMessage ?? result.reason ?? "Order failed.");
    } catch (err) {
      setPhase("error");
      setErrorText(toErrorMessage(err, "Order submission failed."));
    } finally {
      submittingRef.current = false;
    }
  }, [acknowledged, form, fundAccountId, refreshAfterSettledOutcome, phase, services]);

  return {
    ...state,
    preview,
    openTicket,
    closeTicket,
    toggleTicket,
    updateField,
    normalizeSymbol,
    setAcknowledged,
    submitOrder: submitOrderTicket
  };
}

export function buildOrderTicketState({
  form,
  open,
  phase,
  orderId,
  errorText,
  escalationId = null,
  riskWarnings = [],
  acknowledged = false
}: BuildOrderTicketStateOptions): OrderTicketState {
  const validationError = validateOrderTicketForm(form);
  const requiresLimitPrice = orderTypeRequiresPrice(form.type);
  const successText = phase === "submitted"
    ? `Order submitted${orderId ? ` - ${orderId}` : ""}.`
    : null;
  const parkedText = phase === "parked"
    ? `Order parked for governed risk approval${escalationId ? ` - escalation ${escalationId}` : ""}. `
      + "It routes only once the risk desk approves it."
    : null;
  const invalidField = getOrderTicketInvalidField(form);
  const requirementId = "order-ticket-requirements";
  const acknowledgement = buildOrderTicketAcknowledgementState(acknowledged, phase, validationError);
  const submitDisabledReason = buildOrderTicketSubmitDisabledReason(phase, validationError, acknowledgement);
  const closeDisabledReason = phase === "submitting" ? "Order submission is in progress." : null;

  return {
    form,
    open,
    phase,
    orderId,
    escalationId,
    riskWarnings,
    parkedText,
    errorText,
    validationError,
    invalidField,
    canSubmit: submitDisabledReason === null,
    canClose: phase !== "submitting",
    requiresLimitPrice,
    priceLabel: `${form.type} price`,
    formId: "trading-order-ticket",
    requirementId,
    controls: buildOrderTicketControls(form, invalidField, requirementId),
    openButtonLabel: open ? "Close order ticket" : "New order",
    submitButtonLabel: phase === "submitting" ? "Submitting…" : "Submit order",
    submitAriaLabel: phase === "submitting" ? "Submitting order request" : "Submit order request",
    submitDisabledReason,
    submitBusy: phase === "submitting",
    submitBusyLabel: phase === "submitting" ? "Submitting…" : null,
    closeButtonLabel: "Cancel",
    closeAriaLabel: closeDisabledReason ?? "Close order ticket without submitting",
    closeDisabledReason,
    acknowledgement,
    requirementText: buildOrderRequirementText(form, phase, validationError),
    successText,
    statusAnnouncement: buildOrderTicketStatusAnnouncement({ phase, errorText, orderId, escalationId, riskWarnings })
  };
}

function buildOrderTicketControls(
  form: OrderSubmitRequest,
  invalidField: OrderTicketField | null,
  requirementId: string
): OrderTicketControls {
  return {
    symbol: {
      field: "symbol",
      id: "order-ticket-symbol",
      label: "Symbol",
      value: form.symbol,
      describedBy: requirementId,
      ariaLabel: "Order symbol",
      invalid: invalidField === "symbol",
      required: true
    },
    side: {
      field: "side",
      id: "order-ticket-side",
      label: "Side",
      value: form.side,
      describedBy: requirementId,
      ariaLabel: "Order side",
      invalid: false,
      required: true,
      options: [
        { value: "Buy", label: "Buy" },
        { value: "Sell", label: "Sell" }
      ]
    },
    type: {
      field: "type",
      id: "order-ticket-type",
      label: "Type",
      value: form.type,
      describedBy: requirementId,
      ariaLabel: "Order type",
      invalid: false,
      required: true,
      options: [
        { value: "Market", label: "Market" },
        { value: "Limit", label: "Limit" },
        { value: "Stop", label: "Stop" }
      ]
    },
    quantity: {
      field: "quantity",
      id: "order-ticket-quantity",
      label: "Quantity",
      value: form.quantity ? String(form.quantity) : "",
      describedBy: requirementId,
      ariaLabel: "Order quantity",
      invalid: invalidField === "quantity",
      required: true
    },
    limitPrice: orderTypeRequiresPrice(form.type)
      ? {
          field: "limitPrice",
          id: "order-ticket-limit-price",
          label: `${form.type} price`,
          value: form.limitPrice ? String(form.limitPrice) : "",
          describedBy: requirementId,
          ariaLabel: `${form.type} order price`,
          invalid: invalidField === "limitPrice",
          required: true
        }
      : null
  };
}

export function updateOrderTicketForm(
  current: OrderSubmitRequest,
  field: OrderTicketField,
  value: string
): OrderSubmitRequest {
  if (field === "symbol") {
    return { ...current, symbol: value };
  }

  if (field === "side") {
    return { ...current, side: value === "Sell" ? "Sell" : "Buy" };
  }

  if (field === "type") {
    const type = value === "Limit" || value === "Stop" ? value : "Market";
    return {
      ...current,
      type,
      limitPrice: orderTypeRequiresPrice(type) ? current.limitPrice : null
    };
  }

  if (field === "quantity") {
    return { ...current, quantity: parsePositiveNumber(value) ?? 0 };
  }

  return { ...current, limitPrice: parsePositiveNumber(value) };
}

export function buildOrderSubmitRequest(form: OrderSubmitRequest, fundAccountId?: string | null): OrderSubmitRequest {
  const request: OrderSubmitRequest = {
    symbol: normalizeOrderSymbol(form.symbol),
    side: form.side,
    type: form.type,
    quantity: form.quantity
  };
  const normalizedFundAccountId = normalizeFundAccountGuid(fundAccountId ?? form.fundAccountId);

  if (orderTypeRequiresPrice(form.type)) {
    request.limitPrice = form.limitPrice;
  }

  if (normalizedFundAccountId) {
    request.fundAccountId = normalizedFundAccountId;
  }

  return request;
}

export function validateOrderTicketForm(form: OrderSubmitRequest): string | null {
  if (!normalizeOrderSymbol(form.symbol)) {
    return "Enter a symbol before submitting an order.";
  }

  if (!Number.isFinite(form.quantity) || form.quantity <= 0) {
    return "Enter an order quantity greater than zero.";
  }

  if (orderTypeRequiresPrice(form.type) && (!Number.isFinite(form.limitPrice) || (form.limitPrice ?? 0) <= 0)) {
    return `Enter a ${form.type.toLowerCase()} price greater than zero.`;
  }

  return null;
}

function getOrderTicketInvalidField(form: OrderSubmitRequest): OrderTicketField | null {
  if (!normalizeOrderSymbol(form.symbol)) {
    return "symbol";
  }

  if (!Number.isFinite(form.quantity) || form.quantity <= 0) {
    return "quantity";
  }

  if (orderTypeRequiresPrice(form.type) && (!Number.isFinite(form.limitPrice) || (form.limitPrice ?? 0) <= 0)) {
    return "limitPrice";
  }

  return null;
}

function normalizeOrderSymbol(symbol: string): string {
  return symbol.trim().toUpperCase();
}

function orderTypeRequiresPrice(type: OrderSubmitRequest["type"]): boolean {
  return type === "Limit" || type === "Stop";
}

function parsePositiveNumber(value: string): number | null {
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
}

export type OrderPreviewLevel = "info" | "warning" | "danger";
export type OrderPreviewPriceSource = "limit" | "stop" | "mark" | "average" | "none";
export type OrderPreviewEffect =
  | "open-long"
  | "open-short"
  | "add-long"
  | "add-short"
  | "reduce-long"
  | "reduce-short"
  | "close-long"
  | "close-short"
  | "flip-to-short"
  | "flip-to-long";

export interface OrderPreviewWarning {
  id: string;
  level: OrderPreviewLevel;
  message: string;
}

export interface OrderPreviewExistingPosition {
  symbol: string;
  side: TradingPosition["side"];
  signedQuantity: number;
  averagePrice: number | null;
  markPrice: number | null;
}

export interface OrderPreview {
  available: boolean;
  symbol: string;
  side: OrderSubmitRequest["side"];
  type: OrderSubmitRequest["type"];
  quantity: number;
  referencePrice: number | null;
  referencePriceLabel: string;
  priceSource: OrderPreviewPriceSource;
  priceSourceLabel: string;
  notional: number | null;
  notionalLabel: string;
  existing: OrderPreviewExistingPosition | null;
  effect: OrderPreviewEffect | null;
  effectLabel: string;
  effectDetail: string;
  resultingSignedQuantity: number;
  resultingPositionLabel: string;
  warnings: OrderPreviewWarning[];
  riskNote: string | null;
  summaryLine: string;
  ariaSummary: string;
}

export interface BuildOrderPreviewOptions {
  form: OrderSubmitRequest;
  positions: TradingPosition[];
  risk: TradingRiskState | null;
}

export function buildOrderPreview({ form, positions, risk }: BuildOrderPreviewOptions): OrderPreview {
  const symbol = normalizeOrderSymbol(form.symbol);
  const quantity = Number.isFinite(form.quantity) && form.quantity > 0 ? form.quantity : 0;
  const existing = symbol ? findExistingPosition(positions, symbol) : null;
  const referencePriceInfo = pickReferencePrice(form, existing);
  const referencePrice = referencePriceInfo.price;
  const priceSource = referencePriceInfo.source;
  const notional = referencePrice !== null && quantity > 0 ? referencePrice * quantity : null;
  const available = symbol.length > 0 && quantity > 0;

  const signedDelta = form.side === "Buy" ? quantity : -quantity;
  const existingSigned = existing?.signedQuantity ?? 0;
  const resultingSignedQuantity = available ? existingSigned + signedDelta : existingSigned;
  const effect = available ? deriveOrderEffect(existingSigned, signedDelta) : null;
  const effectLabel = effect ? describeOrderEffectLabel(effect) : "Awaiting symbol and quantity";
  const effectDetail = available
    ? describeOrderEffectDetail(effect, existingSigned, signedDelta, resultingSignedQuantity, symbol)
    : "Enter a symbol and quantity to preview the position impact.";

  const warnings = available
    ? collectOrderPreviewWarnings({ form, quantity, referencePrice, priceSource, existing, effect, risk })
    : [];

  const riskNote = buildOrderPreviewRiskNote(risk);
  const referencePriceLabel = referencePrice === null ? "—" : formatPrice(referencePrice);
  const notionalLabel = notional === null ? "—" : formatMoney(notional);
  const resultingPositionLabel = available
    ? describeResultingPosition(resultingSignedQuantity, symbol)
    : "—";
  const summaryLine = available
    ? buildOrderPreviewSummary({ symbol, side: form.side, type: form.type, quantity, referencePrice, priceSource })
    : "Order preview will appear once a symbol and quantity are entered.";
  const ariaSummary = available
    ? `${summaryLine}. ${effectLabel}.${warnings.length > 0 ? ` ${warnings.length} warning${warnings.length === 1 ? "" : "s"}.` : ""}`
    : summaryLine;

  return {
    available,
    symbol,
    side: form.side,
    type: form.type,
    quantity,
    referencePrice,
    referencePriceLabel,
    priceSource,
    priceSourceLabel: describePriceSourceLabel(priceSource),
    notional,
    notionalLabel,
    existing,
    effect,
    effectLabel,
    effectDetail,
    resultingSignedQuantity,
    resultingPositionLabel,
    warnings,
    riskNote,
    summaryLine,
    ariaSummary
  };
}

function findExistingPosition(positions: TradingPosition[], symbol: string): OrderPreviewExistingPosition | null {
  const match = positions.find((position) => normalizeOrderSymbol(position.symbol) === symbol);
  if (!match) {
    return null;
  }

  const magnitude = parseLooseNumber(match.quantity) ?? 0;
  const signedQuantity = match.side === "Short" ? -magnitude : magnitude;
  return {
    symbol: match.symbol,
    side: match.side,
    signedQuantity,
    averagePrice: parseLooseNumber(match.averagePrice),
    markPrice: parseLooseNumber(match.markPrice)
  };
}

function pickReferencePrice(
  form: OrderSubmitRequest,
  existing: OrderPreviewExistingPosition | null
): { price: number | null; source: OrderPreviewPriceSource } {
  if (form.type === "Limit" && form.limitPrice && form.limitPrice > 0) {
    return { price: form.limitPrice, source: "limit" };
  }

  if (form.type === "Stop" && form.limitPrice && form.limitPrice > 0) {
    return { price: form.limitPrice, source: "stop" };
  }

  if (existing?.markPrice && existing.markPrice > 0) {
    return { price: existing.markPrice, source: "mark" };
  }

  if (existing?.averagePrice && existing.averagePrice > 0) {
    return { price: existing.averagePrice, source: "average" };
  }

  return { price: null, source: "none" };
}

function deriveOrderEffect(existingSigned: number, delta: number): OrderPreviewEffect {
  if (existingSigned === 0) {
    return delta > 0 ? "open-long" : "open-short";
  }

  const target = existingSigned + delta;
  if (target === 0) {
    return existingSigned > 0 ? "close-long" : "close-short";
  }

  const sameSign = (existingSigned > 0 && target > 0) || (existingSigned < 0 && target < 0);
  if (sameSign) {
    const grew = Math.abs(target) > Math.abs(existingSigned);
    if (existingSigned > 0) {
      return grew ? "add-long" : "reduce-long";
    }
    return grew ? "add-short" : "reduce-short";
  }

  return existingSigned > 0 ? "flip-to-short" : "flip-to-long";
}

function describeOrderEffectLabel(effect: OrderPreviewEffect): string {
  switch (effect) {
    case "open-long":
      return "Opens new long position";
    case "open-short":
      return "Opens new short position";
    case "add-long":
      return "Adds to existing long position";
    case "add-short":
      return "Adds to existing short position";
    case "reduce-long":
      return "Reduces existing long position";
    case "reduce-short":
      return "Reduces existing short position";
    case "close-long":
      return "Closes existing long position";
    case "close-short":
      return "Closes existing short position";
    case "flip-to-short":
      return "Flips long position to short";
    case "flip-to-long":
      return "Flips short position to long";
  }
}

function describeOrderEffectDetail(
  effect: OrderPreviewEffect | null,
  existingSigned: number,
  delta: number,
  resultingSigned: number,
  symbol: string
): string {
  if (!effect) {
    return "";
  }

  const before = describePositionLine(existingSigned, symbol);
  const after = describePositionLine(resultingSigned, symbol);
  const deltaLabel = `${delta > 0 ? "+" : ""}${formatQuantity(delta)} shares`;
  return `${before} → ${after} (${deltaLabel}).`;
}

function describePositionLine(signedQuantity: number, symbol: string): string {
  if (signedQuantity === 0) {
    return `flat ${symbol}`;
  }
  const direction = signedQuantity > 0 ? "long" : "short";
  return `${formatQuantity(Math.abs(signedQuantity))} ${symbol} ${direction}`;
}

function describeResultingPosition(signedQuantity: number, symbol: string): string {
  if (signedQuantity === 0) {
    return `Flat ${symbol}`;
  }
  const direction = signedQuantity > 0 ? "Long" : "Short";
  return `${direction} ${formatQuantity(Math.abs(signedQuantity))} ${symbol}`;
}

function describePriceSourceLabel(source: OrderPreviewPriceSource): string {
  switch (source) {
    case "limit":
      return "Limit price";
    case "stop":
      return "Stop trigger";
    case "mark":
      return "Last mark";
    case "average":
      return "Position avg";
    case "none":
      return "No reference price";
  }
}

function collectOrderPreviewWarnings({
  form,
  quantity,
  referencePrice,
  priceSource,
  existing,
  effect,
  risk
}: {
  form: OrderSubmitRequest;
  quantity: number;
  referencePrice: number | null;
  priceSource: OrderPreviewPriceSource;
  existing: OrderPreviewExistingPosition | null;
  effect: OrderPreviewEffect | null;
  risk: TradingRiskState | null;
}): OrderPreviewWarning[] {
  const warnings: OrderPreviewWarning[] = [];

  if (form.type === "Market" && referencePrice === null) {
    warnings.push({
      id: "no-reference-price",
      level: "warning",
      message: "No mark or limit price available — notional cannot be estimated before fills."
    });
  }

  if (priceSource === "average" && form.type === "Market") {
    warnings.push({
      id: "stale-reference-price",
      level: "info",
      message: "Reference price is the position average; live mark is not available."
    });
  }

  if (effect && effect.startsWith("flip-")) {
    const existingMagnitude = existing ? Math.abs(existing.signedQuantity) : 0;
    const overshoot = quantity - existingMagnitude;
    warnings.push({
      id: "position-flip",
      level: "danger",
      message: existingMagnitude > 0
        ? `Quantity exceeds open position by ${formatQuantity(overshoot)} — this order will reverse the position.`
        : "Order will open a new opposite-side position."
    });
  }

  if (effect === "open-short" || effect === "add-short") {
    warnings.push({
      id: "short-exposure",
      level: "info",
      message: "Order increases short exposure — confirm the strategy permits short selling."
    });
  }

  if (risk?.state === "Constrained") {
    warnings.push({
      id: "risk-constrained",
      level: "danger",
      message: `Risk state is Constrained: ${risk.summary || "guardrails are blocking new exposure"}.`
    });
  } else if (risk?.state === "Observe") {
    warnings.push({
      id: "risk-observe",
      level: "warning",
      message: `Risk state is Observe: ${risk.summary || "review guardrails before submitting"}.`
    });
  }

  return warnings;
}

function buildOrderPreviewRiskNote(risk: TradingRiskState | null): string | null {
  if (!risk) {
    return null;
  }
  const buyingPower = risk.buyingPowerUsed ? `Buying power used ${risk.buyingPowerUsed}` : null;
  const state = `Risk ${risk.state}`;
  return [state, buyingPower].filter(Boolean).join(" • ");
}

function buildOrderPreviewSummary({
  symbol,
  side,
  type,
  quantity,
  referencePrice,
  priceSource
}: {
  symbol: string;
  side: OrderSubmitRequest["side"];
  type: OrderSubmitRequest["type"];
  quantity: number;
  referencePrice: number | null;
  priceSource: OrderPreviewPriceSource;
}): string {
  const qtyText = formatQuantity(quantity);
  const priceText = referencePrice !== null
    ? ` ≈ ${formatMoney(referencePrice * quantity)} @ ${formatPrice(referencePrice)}${priceSource === "limit" || priceSource === "stop" ? "" : " (" + describePriceSourceLabel(priceSource).toLowerCase() + ")"}`
    : "";
  return `${side} ${qtyText} ${symbol} ${type.toLowerCase()}${priceText}`;
}

function parseLooseNumber(value: string | null | undefined): number | null {
  if (value === null || value === undefined) {
    return null;
  }
  const stripped = String(value).replace(/[\s,$%]/g, "");
  if (!stripped || stripped === "—" || stripped === "-") {
    return null;
  }
  const parsed = Number(stripped);
  return Number.isFinite(parsed) ? parsed : null;
}

function formatPrice(value: number): string {
  return value.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function formatMoney(value: number): string {
  const sign = value < 0 ? "-" : "";
  const absolute = Math.abs(value);
  return `${sign}$${absolute.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

function formatQuantity(value: number): string {
  if (!Number.isFinite(value)) {
    return "0";
  }
  return Math.abs(value).toLocaleString("en-US", { maximumFractionDigits: 4 });
}

export type PromotionGateField =
  | "runId"
  | "approvedBy"
  | "approvalReason"
  | "rejectionReason"
  | "reviewNotes"
  | "manualOverrideId"
  | "evidenceReferences";

export interface PromotionGateForm {
  runId: string;
  approvedBy: string;
  approvalReason: string;
  rejectionReason: string;
  reviewNotes: string;
  manualOverrideId: string;
  evidenceReferences: string;
  approvalChecklist: string[];
}

export type PromotionGatePhase = "idle" | "evaluating" | "approving" | "rejecting";
export type PromotionOutcomeLevel = "success" | "warning" | "danger";

export interface PromotionOutcome {
  level: PromotionOutcomeLevel;
  message: string;
}

export interface PromotionGateCommandState {
  label: string;
  ariaLabel: string;
  disabled: boolean;
  disabledReason: string | null;
  busy: boolean;
  busyLabel: string | null;
}

export interface PromotionGateFieldState {
  field: PromotionGateField;
  id: string;
  label: string;
  ariaLabel: string;
  placeholder: string;
  describedBy: string | null;
  helpText: string | null;
  helpId: string | null;
  required: boolean;
}

export interface PromotionEvaluationMetricRow {
  id: string;
  label: string;
  value: string;
}

export interface PromotionEvaluationWarningRow {
  id: string;
  text: string;
}

export interface PromotionEvaluationPanelState {
  ariaLabel: string;
  role: "status" | "alert";
  ariaLive: "polite" | "assertive";
  tone: "success" | "warning" | "danger";
  title: string;
  eligibleLabel: string;
  eligibleTone: "success" | "warning";
  metrics: PromotionEvaluationMetricRow[];
  reason: string | null;
  warnings: PromotionEvaluationWarningRow[];
  blockingReasons: PromotionEvaluationWarningRow[];
  blockingListLabel: string | null;
}

export interface PromotionGateServices {
  evaluatePromotion: (runId: string) => Promise<PromotionEvaluationResult>;
  approvePromotion: (request: ApprovePromotionRequest) => Promise<PromotionDecisionResult>;
  rejectPromotion: (request: RejectPromotionRequest) => Promise<PromotionDecisionResult>;
  getPromotionHistory: () => Promise<PromotionRecord[]>;
}

export interface PromotionGateState {
  form: PromotionGateForm;
  trimmedForm: PromotionGateForm;
  evaluation: PromotionEvaluationResult | null;
  history: PromotionRecord[];
  busy: boolean;
  phase: PromotionGatePhase;
  errorText: string | null;
  outcome: PromotionOutcome | null;
  canEvaluate: boolean;
  canPromote: boolean;
  canReject: boolean;
  evaluateButtonLabel: string;
  promoteButtonLabel: string;
  rejectButtonLabel: string;
  evaluateCommand: PromotionGateCommandState;
  promoteCommand: PromotionGateCommandState;
  rejectCommand: PromotionGateCommandState;
  fields: Record<PromotionGateField, PromotionGateFieldState>;
  nextActionText: string;
  approvalRequirementText: string;
  rejectionRequirementText: string;
  historyEmptyText: string;
  statusAnnouncement: string;
  approvalChecklist: PromotionApprovalChecklistItem[];
  evaluationPanel: PromotionEvaluationPanelState | null;
  historyRows: PromotionHistoryRow[];
}

export interface BuildPromotionGateStateOptions {
  form: PromotionGateForm;
  busy: boolean;
  phase: PromotionGatePhase;
  errorText: string | null;
  outcome: PromotionOutcome | null;
  evaluation: PromotionEvaluationResult | null;
  history: PromotionRecord[];
}

export const emptyPromotionGateForm: PromotionGateForm = {
  runId: "",
  approvedBy: "",
  approvalReason: "",
  rejectionReason: "",
  reviewNotes: "",
  manualOverrideId: "",
  evidenceReferences: "",
  approvalChecklist: []
};

export const paperPromotionApprovalChecklist: string[] = [
  "DK1_TRUST_PACKET_REVIEWED",
  "RUN_LINEAGE_REVIEWED",
  "PORTFOLIO_LEDGER_CONTINUITY_REVIEWED",
  "RISK_CONTROLS_REVIEWED"
];

export const livePromotionApprovalChecklist: string[] = [
  ...paperPromotionApprovalChecklist,
  "PAPER_VALIDATION_REVIEWED",
  "RECONCILIATION_EVIDENCE_REVIEWED",
  "BROKER_EXECUTION_RECONCILIATION_REVIEWED",
  "ACCOUNTING_RECORDS_REVIEWED",
  "GOVERNED_REPORTING_REVIEWED",
  "GOVERNANCE_SIGNOFF_REVIEWED",
  "EXCEPTION_HANDLING_REVIEWED",
  "ROLLBACK_KILL_SWITCH_REVIEWED",
  "AUDIT_RETENTION_REVIEWED",
  "LIVE_OVERRIDE_REVIEWED"
];

const defaultPromotionServices: PromotionGateServices = {
  evaluatePromotion: (runId) => workstationApi.evaluatePromotion(runId),
  approvePromotion: (request) => workstationApi.approvePromotion(request),
  rejectPromotion: (request) => workstationApi.rejectPromotion(request),
  getPromotionHistory: () => workstationApi.getPromotionHistory()
};

export function usePromotionGateViewModel(
  services: PromotionGateServices = defaultPromotionServices
) {
  const [form, setForm] = useState<PromotionGateForm>(emptyPromotionGateForm);
  const [evaluation, setEvaluation] = useState<PromotionEvaluationResult | null>(null);
  const [history, setHistory] = useState<PromotionRecord[]>([]);
  const [errorText, setErrorText] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [phase, setPhase] = useState<PromotionGatePhase>("idle");
  const [outcome, setOutcome] = useState<PromotionOutcome | null>(null);
  const commandRevisionRef = useRef(0);
  const mountedRef = useRef(true);

  const nextCommandRevision = useCallback(() => {
    commandRevisionRef.current += 1;
    return commandRevisionRef.current;
  }, []);

  const isCurrentCommandRevision = useCallback((revision: number) => (
    mountedRef.current && commandRevisionRef.current === revision
  ), []);

  useEffect(() => {
    let cancelled = false;

    services.getPromotionHistory()
      .then((rows) => {
        if (!cancelled) {
          setHistory(rows);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setHistory([]);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [services]);

  useEffect(() => () => {
    mountedRef.current = false;
    commandRevisionRef.current += 1;
  }, []);

  const state = useMemo(
    () => buildPromotionGateState({ form, busy, phase, errorText, outcome, evaluation, history }),
    [busy, errorText, evaluation, form, history, outcome, phase]
  );

  const refreshHistory = useCallback(async (commandRevision?: number) => {
    const rows = await services.getPromotionHistory();
    if (commandRevision === undefined || isCurrentCommandRevision(commandRevision)) {
      setHistory(rows);
    }
  }, [isCurrentCommandRevision, services]);

  const updateField = useCallback((field: PromotionGateField, value: string) => {
    commandRevisionRef.current += 1;
    setForm((current) => ({ ...current, [field]: value }));
    setErrorText(null);
    setOutcome(null);
    setBusy(false);
    setPhase("idle");

    if (field === "runId") {
      setEvaluation(null);
      setForm((current) => ({ ...current, approvalChecklist: [] }));
    }
  }, []);

  const evaluateGateChecks = useCallback(async () => {
    const runId = form.runId.trim();
    if (!runId) {
      setErrorText("Run id is required before evaluating gate checks.");
      return;
    }

    const commandRevision = nextCommandRevision();
    setBusy(true);
    setPhase("evaluating");
    setErrorText(null);
    setOutcome(null);

    try {
      const result = await services.evaluatePromotion(runId);
      if (!isCurrentCommandRevision(commandRevision)) {
        return;
      }

      if (!isPromotionEvaluationForRun(result, runId)) {
        setEvaluation(null);
        setErrorText(`Promotion evaluation returned results for ${result.runId || "another run"} while ${runId} is selected. Evaluate the selected run again.`);
        return;
      }

      setEvaluation(result);
      setForm((current) => ({
        ...current,
        approvalChecklist: []
      }));
    } catch (err) {
      if (isCurrentCommandRevision(commandRevision)) {
        setErrorText(toErrorMessage(err, "Evaluation failed."));
      }
    } finally {
      if (isCurrentCommandRevision(commandRevision)) {
        setBusy(false);
        setPhase("idle");
      }
    }
  }, [form.runId, isCurrentCommandRevision, nextCommandRevision, services]);

  const promoteToPaper = useCallback(async () => {
    const validationError = validatePromotionApproval(form, evaluation);
    if (validationError) {
      setErrorText(validationError);
      setOutcome(null);
      return;
    }

    const commandRevision = nextCommandRevision();
    setBusy(true);
    setPhase("approving");
    setErrorText(null);
    setOutcome(null);

    try {
      const result = await services.approvePromotion(buildPromotionApprovalRequest(form, evaluation));
      if (!isCurrentCommandRevision(commandRevision)) {
        return;
      }

      setOutcome(buildApprovalOutcome(result));
      await refreshHistory(commandRevision);
    } catch (err) {
      if (isCurrentCommandRevision(commandRevision)) {
        setErrorText(toErrorMessage(err, "Promotion approval failed."));
      }
    } finally {
      if (isCurrentCommandRevision(commandRevision)) {
        setBusy(false);
        setPhase("idle");
      }
    }
  }, [evaluation, form, isCurrentCommandRevision, nextCommandRevision, refreshHistory, services]);

  const rejectPromotionDecision = useCallback(async () => {
    const validationError = validatePromotionRejection(form);
    if (validationError) {
      setErrorText(validationError);
      setOutcome(null);
      return;
    }

    const commandRevision = nextCommandRevision();
    setBusy(true);
    setPhase("rejecting");
    setErrorText(null);
    setOutcome(null);

    try {
      const result = await services.rejectPromotion(buildPromotionRejectionRequest(form));
      if (!isCurrentCommandRevision(commandRevision)) {
        return;
      }

      setOutcome(buildRejectionOutcome(result));
      await refreshHistory(commandRevision);

      if (result.success && isCurrentCommandRevision(commandRevision)) {
        setForm((current) => ({ ...current, rejectionReason: "" }));
      }
    } catch (err) {
      if (isCurrentCommandRevision(commandRevision)) {
        setErrorText(toErrorMessage(err, "Promotion rejection failed."));
      }
    } finally {
      if (isCurrentCommandRevision(commandRevision)) {
        setBusy(false);
        setPhase("idle");
      }
    }
  }, [form, isCurrentCommandRevision, nextCommandRevision, refreshHistory, services]);

  return {
    ...state,
    updateField,
    evaluateGateChecks,
    promoteToPaper,
    rejectPromotion: rejectPromotionDecision
  };
}

export function buildPromotionGateState({
  form,
  busy,
  phase,
  errorText,
  outcome,
  evaluation,
  history
}: BuildPromotionGateStateOptions): PromotionGateState {
  const trimmedForm = trimPromotionGateForm(form);
  const effectiveEvaluation = selectPromotionEvaluationForRun(evaluation, trimmedForm.runId);
  const canEvaluate = !busy && Boolean(trimmedForm.runId);
  const canPromote = !busy && validatePromotionApproval(trimmedForm, effectiveEvaluation) === null;
  const canReject = !busy && validatePromotionRejection(trimmedForm) === null;
  const evaluationPanel = buildPromotionEvaluationPanel(effectiveEvaluation);
  const approvalRequirementText = buildApprovalRequirementText(trimmedForm, effectiveEvaluation);
  const rejectionRequirementText = buildRejectionRequirementText(trimmedForm);
  const evaluateButtonLabel = phase === "evaluating" ? "Evaluating..." : "Evaluate gate checks";
  const promoteButtonLabel = phase === "approving" ? "Promoting..." : "Confirm promote";
  const rejectButtonLabel = phase === "rejecting" ? "Rejecting..." : "Reject promotion";

  return {
    form,
    trimmedForm,
    evaluation: effectiveEvaluation,
    history,
    busy,
    phase,
    errorText,
    outcome,
    canEvaluate,
    canPromote,
    canReject,
    evaluateButtonLabel,
    promoteButtonLabel,
    rejectButtonLabel,
    evaluateCommand: {
      label: evaluateButtonLabel,
      ariaLabel: phase === "evaluating" ? "Evaluating gate checks" : "Evaluate gate checks",
      disabled: !canEvaluate,
      disabledReason: buildEvaluateDisabledReason(trimmedForm, busy, phase),
      busy: phase === "evaluating",
      busyLabel: "Evaluating..."
    },
    promoteCommand: {
      label: promoteButtonLabel,
      ariaLabel: phase === "approving" ? "Writing promotion decision" : "Confirm promote",
      disabled: !canPromote,
      disabledReason: canPromote ? null : validatePromotionApproval(trimmedForm, effectiveEvaluation),
      busy: phase === "approving",
      busyLabel: "Promoting..."
    },
    rejectCommand: {
      label: rejectButtonLabel,
      ariaLabel: phase === "rejecting" ? "Writing promotion rejection" : "Reject promotion",
      disabled: !canReject,
      disabledReason: canReject ? null : validatePromotionRejection(trimmedForm),
      busy: phase === "rejecting",
      busyLabel: "Rejecting..."
    },
    fields: buildPromotionGateFields(),
    nextActionText: buildNextActionText({ trimmedForm, evaluation: effectiveEvaluation, busy, phase }),
    approvalRequirementText,
    rejectionRequirementText,
    historyEmptyText: "No promotion decisions recorded.",
    statusAnnouncement: buildPromotionStatusAnnouncement({ phase, errorText, outcome, evaluation: effectiveEvaluation, history }),
    approvalChecklist: buildPromotionApprovalChecklist(effectiveEvaluation, trimmedForm.evidenceReferences),
    evaluationPanel,
    historyRows: buildPromotionHistoryRows(history)
  };
}

function buildSessionReplayStatusPanel({
  files,
  selectedFilePath,
  selectedFileName,
  replayStatus,
  loadingFiles,
  activeCommand,
  activeErrorText
}: {
  files: ReplayFileRecord[];
  selectedFilePath: string;
  selectedFileName: string | null;
  replayStatus: ReplayStatus | null;
  loadingFiles: boolean;
  activeCommand: SessionReplayCommandKind | null;
  activeErrorText: string | null;
}): SessionReplayStatusPanel {
  if (activeErrorText) {
    return {
      role: "alert",
      ariaLive: "assertive",
      ariaLabel: `Session replay error: ${activeErrorText}`,
      tone: "danger",
      title: "Replay blocked",
      detail: activeErrorText
    };
  }

  if (activeCommand) {
    return {
      role: "status",
      ariaLive: "polite",
      ariaLabel: formatSessionReplayCommandAnnouncement(activeCommand),
      tone: "warning",
      title: "Replay command running",
      detail: formatSessionReplayCommandAnnouncement(activeCommand)
    };
  }

  if (replayStatus) {
    return {
      role: "status",
      ariaLive: "polite",
      ariaLabel: `Replay ${replayStatus.status} for ${replayStatus.sessionId}`,
      tone: "success",
      title: `Replay ${replayStatus.status}`,
      detail: `Processed ${replayStatus.eventsProcessed}/${replayStatus.totalEvents} events at ${replayStatus.progressPercent}%.`
    };
  }

  if (loadingFiles) {
    return {
      role: "status",
      ariaLive: "polite",
      ariaLabel: "Loading replay files",
      tone: "warning",
      title: "Loading replay files",
      detail: "Fetching available JSONL replay files."
    };
  }

  if (files.length === 0) {
    return {
      role: "status",
      ariaLive: "polite",
      ariaLabel: "No replay files available",
      tone: "default",
      title: "No replay files available",
      detail: "Generate or import replay evidence before starting session replay."
    };
  }

  return {
    role: "status",
    ariaLive: "polite",
    ariaLabel: `Ready to replay ${selectedFileName ?? "selected file"}`,
    tone: selectedFilePath.trim().length > 0 ? "success" : "default",
    title: selectedFilePath.trim().length > 0 ? "Replay file selected" : "Select replay file",
    detail: selectedFilePath.trim().length > 0
      ? `Ready to replay ${selectedFileName ?? "selected file"}.`
      : "Select a replay file to enable replay controls."
  };
}

function buildPromotionGateFields(): Record<PromotionGateField, PromotionGateFieldState> {
  return {
    runId: {
      field: "runId",
      id: "promotion-run-id",
      label: "Run id",
      ariaLabel: "Run id",
      placeholder: "backtest run id",
      describedBy: "promotion-run-help promotion-action-state",
      helpText: "Evaluate this run before writing a promotion decision.",
      helpId: "promotion-run-help",
      required: false
    },
    approvedBy: {
      field: "approvedBy",
      id: "promotion-operator-id",
      label: "Operator id",
      ariaLabel: "Operator id",
      placeholder: "operator id",
      describedBy: "promotion-action-state",
      helpText: null,
      helpId: null,
      required: true
    },
    approvalReason: {
      field: "approvalReason",
      id: "promotion-approval-reason",
      label: "Approval reason",
      ariaLabel: "Approval reason",
      placeholder: "why this promotion is approved",
      describedBy: "promotion-action-state",
      helpText: null,
      helpId: null,
      required: true
    },
    rejectionReason: {
      field: "rejectionReason",
      id: "promotion-rejection-reason",
      label: "Rejection reason",
      ariaLabel: "Rejection reason",
      placeholder: "why this promotion is rejected",
      describedBy: "promotion-action-state",
      helpText: null,
      helpId: null,
      required: false
    },
    reviewNotes: {
      field: "reviewNotes",
      id: "promotion-review-notes",
      label: "Review notes",
      ariaLabel: "Review notes",
      placeholder: "optional review notes",
      describedBy: null,
      helpText: null,
      helpId: null,
      required: false
    },
    manualOverrideId: {
      field: "manualOverrideId",
      id: "promotion-manual-override",
      label: "Manual override id",
      ariaLabel: "Manual override id",
      placeholder: "optional manual override id",
      describedBy: null,
      helpText: null,
      helpId: null,
      required: false
    },
    evidenceReferences: {
      field: "evidenceReferences",
      id: "promotion-evidence-references",
      label: "Evidence references",
      ariaLabel: "Promotion evidence references",
      placeholder: "TOKEN:evidence-path, one per line",
      describedBy: "promotion-evidence-references-help",
      helpText: "Paper and live approvals require one CHECKLIST_ID:<retained-reference> entry for every canonical checklist item. Gate eligibility does not record acceptance.",
      helpId: "promotion-evidence-references-help",
      required: true
    }
  };
}

function buildSessionReplayBaseDisabledReason({
  loadingFiles,
  activeCommand,
  hasReplayStatus,
  selectedFilePath,
  speedValidationText,
  seekValidationText
}: {
  loadingFiles: boolean;
  activeCommand: SessionReplayCommandKind | null;
  hasReplayStatus: boolean;
  selectedFilePath: string;
  speedValidationText: string | null;
  seekValidationText: string | null;
}): string | null {
  if (loadingFiles) {
    return "Replay files are still loading.";
  }

  if (activeCommand) {
    return formatSessionReplayCommandAnnouncement(activeCommand);
  }

  if (selectedFilePath.trim().length === 0) {
    return "Select a replay file before using replay controls.";
  }

  if (speedValidationText) {
    return speedValidationText;
  }

  if (seekValidationText) {
    return seekValidationText;
  }

  if (!hasReplayStatus) {
    return "Start a replay before using this control.";
  }

  return null;
}

function buildEvaluateDisabledReason(
  trimmedForm: PromotionGateForm,
  busy: boolean,
  phase: PromotionGatePhase
): string | null {
  if (phase === "evaluating") {
    return "Promotion gate checks are already evaluating.";
  }

  if (busy) {
    return "Wait for the current promotion command to finish.";
  }

  if (!trimmedForm.runId) {
    return "Enter a backtest run id before evaluating gate checks.";
  }

  return null;
}

function buildPromotionEvaluationPanel(
  evaluation: PromotionEvaluationResult | null
): PromotionEvaluationPanelState | null {
  if (!evaluation) {
    return null;
  }

  const warnings: PromotionEvaluationWarningRow[] = [];
  if (evaluation.requiresHumanApproval) {
    warnings.push({
      id: "human-approval",
      text: "Human approval required"
    });
  }

  if (evaluation.requiresManualOverride) {
    warnings.push({
      id: "manual-override",
      text: `Manual override required${evaluation.requiredManualOverrideKind ? `: ${evaluation.requiredManualOverrideKind}` : ""}`
    });
  }

  const blockingReasons = (evaluation.blockingReasons ?? [])
    .filter((reason) => reason.trim().length > 0)
    .map((reason, index) => ({
      id: `blocking-${index}`,
      text: reason
    }));

  return {
    ariaLabel: evaluation.isEligible ? "Promotion evaluation eligible" : "Promotion evaluation blocked",
    role: evaluation.isEligible ? "status" : "alert",
    ariaLive: evaluation.isEligible ? "polite" : "assertive",
    tone: evaluation.isEligible ? "success" : blockingReasons.length > 0 ? "danger" : "warning",
    title: "Evaluation results",
    eligibleLabel: evaluation.isEligible ? "Eligible: Yes" : "Eligible: No",
    eligibleTone: evaluation.isEligible ? "success" : "warning",
    metrics: [
      { id: "sharpe", label: "Sharpe", value: evaluation.sharpeRatio.toFixed(2) },
      { id: "max-drawdown", label: "Max DD", value: `${evaluation.maxDrawdownPercent.toFixed(1)}%` },
      { id: "total-return", label: "Return", value: `${evaluation.totalReturn.toFixed(1)}%` }
    ],
    reason: evaluation.reason?.trim() ? evaluation.reason : null,
    warnings,
    blockingReasons,
    blockingListLabel: blockingReasons.length > 0 ? "Promotion blocking reasons" : null
  };
}

function buildPromotionHistoryRows(history: PromotionRecord[]): PromotionHistoryRow[] {
  return history.slice(0, 4).map((record) => {
    const parts = [
      record.promotedAt,
      record.strategyId,
      `${record.sourceRunType}->${record.targetRunType}`
    ];

    if (record.decision) parts.push(record.decision);
    if (record.sourceRunId) parts.push(`source: ${record.sourceRunId}`);
    else if (record.runId) parts.push(`source: ${record.runId}`);
    if (record.targetRunId) parts.push(`target: ${record.targetRunId}`);
    if (record.approvedBy) parts.push(`by ${record.approvedBy}`);
    if (record.approvalReason) parts.push(`reason: ${record.approvalReason}`);
    if (record.auditReference) parts.push(`audit: ${record.auditReference}`);
    if (record.manualOverrideId) parts.push(`override: ${record.manualOverrideId}`);
    if (record.reviewNotes) parts.push(`notes: ${record.reviewNotes}`);

    const label = parts.join(" | ");
    return {
      id: record.promotionId,
      label,
      ariaLabel: `Promotion ${record.promotionId}: ${label}`
    };
  });
}

export function validatePromotionApproval(
  form: PromotionGateForm,
  evaluation: PromotionEvaluationResult | null
): string | null {
  const trimmedForm = trimPromotionGateForm(form);

  if (!trimmedForm.runId) {
    return "Run id, operator, and approval reason are required.";
  }

  if (!evaluation) {
    return "Evaluate gate checks before confirming promotion.";
  }

  if (!isPromotionEvaluationForRun(evaluation, trimmedForm.runId)) {
    return `Evaluate gate checks for ${trimmedForm.runId} before confirming promotion.`;
  }

  if (!evaluation.isEligible) {
    return evaluation.blockingReasons?.[0] ?? evaluation.reason ?? "Promotion gate is blocked.";
  }

  if (!trimmedForm.runId || !trimmedForm.approvedBy || !trimmedForm.approvalReason) {
    return "Run id, operator, and approval reason are required.";
  }

  const liveTarget = isLivePromotionTarget(evaluation);
  const requiredChecklist = liveTarget
    ? livePromotionApprovalChecklist
    : paperPromotionApprovalChecklist;
  const evidenceReferences = parsePromotionEvidenceReferences(trimmedForm.evidenceReferences);

  if (liveTarget) {
    if (!trimmedForm.manualOverrideId) {
      return "Live promotion approval requires an active AllowLivePromotion override id.";
    }
  }

  const missingEvidence = getMissingPromotionEvidenceReferences(requiredChecklist, evidenceReferences);
  if (missingEvidence.length > 0) {
    return `${liveTarget ? "Live" : "Paper"} promotion evidence references are incomplete: ${missingEvidence.join(", ")}.`;
  }

  const invalidEvidence = getInvalidPromotionEvidenceReferences(
    requiredChecklist,
    evidenceReferences,
    trimmedForm.manualOverrideId);
  if (invalidEvidence.length > 0) {
    return `${liveTarget ? "Live" : "Paper"} promotion evidence references are invalid: ${invalidEvidence.join(", ")}.`;
  }

  return null;
}

export function validatePromotionRejection(form: PromotionGateForm): string | null {
  const trimmedForm = trimPromotionGateForm(form);

  if (!trimmedForm.runId || !trimmedForm.approvedBy || !trimmedForm.rejectionReason) {
    return "Run id, operator, and rejection reason are required.";
  }

  return null;
}

export function buildPromotionApprovalRequest(
  form: PromotionGateForm,
  evaluation: PromotionEvaluationResult | null = null
): ApprovePromotionRequest {
  const trimmedForm = trimPromotionGateForm(form);
  const evidenceReferences = parsePromotionEvidenceReferences(trimmedForm.evidenceReferences);
  const approvalChecklist = evaluation
    ? buildPromotionApprovalChecklistTokens(evaluation)
    : trimmedForm.approvalChecklist;
  return {
    runId: trimmedForm.runId,
    approvedBy: trimmedForm.approvedBy,
    approvalReason: trimmedForm.approvalReason,
    approvalChecklist: approvalChecklist.length > 0 ? approvalChecklist : undefined,
    evidenceReferences: evidenceReferences.length > 0 ? evidenceReferences : undefined,
    reviewNotes: trimmedForm.reviewNotes || undefined,
    manualOverrideId: trimmedForm.manualOverrideId || undefined
  };
}

export function buildPromotionRejectionRequest(form: PromotionGateForm): RejectPromotionRequest {
  const trimmedForm = trimPromotionGateForm(form);
  return {
    runId: trimmedForm.runId,
    reason: trimmedForm.rejectionReason,
    rejectedBy: trimmedForm.approvedBy,
    reviewNotes: trimmedForm.reviewNotes || undefined,
    manualOverrideId: trimmedForm.manualOverrideId || undefined
  };
}

function trimPromotionGateForm(form: PromotionGateForm): PromotionGateForm {
  return {
    runId: form.runId.trim(),
    approvedBy: form.approvedBy.trim(),
    approvalReason: form.approvalReason.trim(),
    rejectionReason: form.rejectionReason.trim(),
    reviewNotes: form.reviewNotes.trim(),
    manualOverrideId: form.manualOverrideId.trim(),
    evidenceReferences: form.evidenceReferences.trim(),
    approvalChecklist: form.approvalChecklist
  };
}

function selectPromotionEvaluationForRun(
  evaluation: PromotionEvaluationResult | null,
  runId: string
): PromotionEvaluationResult | null {
  if (!evaluation || !isPromotionEvaluationForRun(evaluation, runId)) {
    return null;
  }

  return evaluation;
}

function isPromotionEvaluationForRun(
  evaluation: PromotionEvaluationResult,
  runId: string
): boolean {
  return Boolean(runId) && evaluation.runId === runId;
}

function buildApprovalOutcome(result: PromotionDecisionResult): PromotionOutcome {
  return {
    level: result.success ? "success" : "danger",
    message: result.success
      ? `Promoted. Promotion ID: ${result.promotionId ?? "n/a"}${result.auditReference ? ` · Audit reference ${result.auditReference}` : ""}`
      : result.reason
  };
}

function buildRejectionOutcome(result: PromotionDecisionResult): PromotionOutcome {
  return {
    level: result.success ? "warning" : "danger",
    message: `${result.reason}${result.auditReference ? ` · Audit reference ${result.auditReference}` : ""}`
  };
}

function buildNextActionText({
  trimmedForm,
  evaluation,
  busy,
  phase
}: {
  trimmedForm: PromotionGateForm;
  evaluation: PromotionEvaluationResult | null;
  busy: boolean;
  phase: PromotionGatePhase;
}): string {
  if (busy) {
    if (phase === "evaluating") {
      return "Evaluating promotion gate checks.";
    }

    if (phase === "approving") {
      return "Writing promotion decision and refreshing audit trail.";
    }

    if (phase === "rejecting") {
      return "Writing rejection decision and refreshing audit trail.";
    }
  }

  if (!trimmedForm.runId) {
    return "Enter a backtest run id to evaluate promotion readiness.";
  }

  if (!evaluation) {
    return "Evaluate gate checks before approving or rejecting this run.";
  }

  if (!evaluation.isEligible) {
    return "Gate is blocked. Reject with rationale or review the blocking reason.";
  }

  if (!trimmedForm.approvedBy || !trimmedForm.approvalReason) {
    return "Add operator and approval reason before confirming promotion.";
  }

  return "Promotion trace is ready for confirmation.";
}

function buildApprovalRequirementText(
  trimmedForm: PromotionGateForm,
  evaluation: PromotionEvaluationResult | null
): string {
  if (!evaluation) {
    return "Approval remains disabled until gate checks return an eligible result.";
  }

  if (!evaluation.isEligible) {
    return evaluation.blockingReasons?.[0] ?? evaluation.reason ?? "Gate checks did not return eligible.";
  }

  if (!trimmedForm.approvedBy || !trimmedForm.approvalReason) {
    return "Approval requires an operator id and approval reason.";
  }

  const validationError = validatePromotionApproval(trimmedForm, evaluation);
  if (validationError) {
    return validationError;
  }

  return "Approval request includes run id, operator, rationale, and keyed retained evidence for every canonical checklist requirement.";
}

function buildRejectionRequirementText(trimmedForm: PromotionGateForm): string {
  if (!trimmedForm.runId || !trimmedForm.approvedBy || !trimmedForm.rejectionReason) {
    return "Rejection requires run id, operator id, and rejection reason.";
  }

  return "Rejection request is ready to write an audit-linked decision.";
}

function buildPromotionStatusAnnouncement({
  phase,
  errorText,
  outcome,
  evaluation,
  history
}: {
  phase: PromotionGatePhase;
  errorText: string | null;
  outcome: PromotionOutcome | null;
  evaluation: PromotionEvaluationResult | null;
  history: PromotionRecord[];
}): string {
  if (phase === "evaluating") {
    return "Evaluating promotion gate checks.";
  }

  if (phase === "approving") {
    return "Writing promotion approval.";
  }

  if (phase === "rejecting") {
    return "Writing promotion rejection.";
  }

  if (errorText) {
    return `Promotion gate failed: ${errorText}`;
  }

  if (outcome) {
    return "Promotion gate command completed.";
  }

  if (evaluation) {
    return evaluation.isEligible
      ? "Promotion gate checks returned eligible."
      : "Promotion gate checks returned blocked.";
  }

  if (history.length > 0) {
    return `${history.length} promotion decision ${history.length === 1 ? "record" : "records"} loaded.`;
  }

  return "";
}

export interface PromotionApprovalChecklistItem {
  id: string;
  label: string;
  status: "ready" | "review" | "blocked";
  description: string | null;
  ariaLabel: string;
}

export interface PromotionHistoryRow {
  id: string;
  label: string;
  ariaLabel: string;
}

export function buildPromotionApprovalChecklist(
  evaluation: PromotionEvaluationResult | null,
  evidenceReferenceText = ""
): PromotionApprovalChecklistItem[] {
  const evidenceReferences = parsePromotionEvidenceReferences(evidenceReferenceText);
  const evidenceStatus = (checklistId: string): PromotionApprovalChecklistItem["status"] => {
    const reference = evidenceReferences.find((item) => getPromotionEvidenceReferenceKey(item) === checklistId);
    return reference && getPromotionEvidenceReferenceValue(reference) ? "ready" : "review";
  };
  const items: Array<Omit<PromotionApprovalChecklistItem, "ariaLabel">> = [
    {
      id: "dk1-data-trust",
      label: "DK1 data trust",
      status: evidenceStatus("DK1_TRUST_PACKET_REVIEWED"),
      description: "Requires operator-entered keyed evidence for the reviewed DK1 trust packet"
    },
    {
      id: "run-lineage",
      label: "Run lineage",
      status: evidenceStatus("RUN_LINEAGE_REVIEWED"),
      description: evaluation?.found
        ? `Run found for ${evaluation.strategyName ?? evaluation.strategyId}; operator-entered lineage evidence is still required`
        : "Run must be found and its retained lineage evidence reviewed"
    },
    {
      id: "risk-metrics",
      label: "Risk metrics",
      status: evaluation && !evaluation.isEligible ? "blocked" : evidenceStatus("RISK_CONTROLS_REVIEWED"),
      description: evaluation
        ? `Eligibility metrics: Sharpe ${evaluation.sharpeRatio.toFixed(2)} · Max DD ${evaluation.maxDrawdownPercent.toFixed(1)}% · Return ${evaluation.totalReturn.toFixed(1)}%; keyed review evidence remains authoritative`
        : "Metrics and retained risk-control evidence require review"
    },
    {
      id: "portfolio-ledger-continuity",
      label: "Portfolio/Ledger continuity",
      status: evidenceStatus("PORTFOLIO_LEDGER_CONTINUITY_REVIEWED"),
      description: evaluation?.ready
        ? "Run state is eligible; operator-entered continuity evidence is still required"
        : "Awaiting run state and retained continuity evidence review"
    }
  ];

  if (isLivePromotionTarget(evaluation)) {
    items.push(
      {
        id: "paper-validation",
        label: "Paper validation",
        status: evidenceStatus("PAPER_VALIDATION_REVIEWED"),
        description: "Retained paper-run validation evidence reviewed"
      },
      {
        id: "reconciliation-evidence",
        label: "Reconciliation evidence",
        status: evidenceStatus("RECONCILIATION_EVIDENCE_REVIEWED"),
        description: "Portfolio, ledger, and operations reconciliation evidence reviewed"
      },
      {
        id: "broker-execution-reconciliation",
        label: "Broker order parity",
        status: evidenceStatus("BROKER_EXECUTION_RECONCILIATION_REVIEWED"),
        description: "Broker and OMS open-order reconciliation evidence reviewed"
      },
      {
        id: "accounting-records",
        label: "Accounting records",
        status: evidenceStatus("ACCOUNTING_RECORDS_REVIEWED"),
        description: "Accounting-record evidence is retained for the live readiness scope"
      },
      {
        id: "governed-reporting",
        label: "Governed reporting",
        status: evidenceStatus("GOVERNED_REPORTING_REVIEWED"),
        description: "Governed report-pack evidence supports the live readiness decision"
      },
      {
        id: "governance-signoff",
        label: "Governance sign-off",
        status: evidenceStatus("GOVERNANCE_SIGNOFF_REVIEWED"),
        description: "Required governance sign-off is retained"
      },
      {
        id: "exception-handling",
        label: "Exception handling",
        status: evidenceStatus("EXCEPTION_HANDLING_REVIEWED"),
        description: "Open exceptions and escalation posture have been reviewed"
      },
      {
        id: "rollback-kill-switch",
        label: "Rollback/kill-switch",
        status: evidenceStatus("ROLLBACK_KILL_SWITCH_REVIEWED"),
        description: "Rollback and kill-switch posture are ready before live operation"
      },
      {
        id: "audit-retention",
        label: "Audit retention",
        status: evidenceStatus("AUDIT_RETENTION_REVIEWED"),
        description: "Audit-retention evidence is retained for the approval"
      },
      {
        id: "live-override",
        label: "Live override",
        status: evidenceStatus("LIVE_OVERRIDE_REVIEWED"),
        description: "Active AllowLivePromotion override evidence is attached"
      }
    );
  }

  return items.map((item) => ({
    ...item,
    ariaLabel: `${item.label}: ${item.status}${item.description ? `. ${item.description}` : ""}`
  }));
}

function buildPromotionApprovalChecklistTokens(evaluation: PromotionEvaluationResult | null): string[] {
  return isLivePromotionTarget(evaluation)
    ? livePromotionApprovalChecklist
    : paperPromotionApprovalChecklist;
}

function parsePromotionEvidenceReferences(value: string): string[] {
  return value
    .split(/[\n,;]+/u)
    .map((item) => item.trim())
    .filter((item) => item.length > 0)
    .filter((item, index, items) => items.findIndex((candidate) => candidate.toLowerCase() === item.toLowerCase()) === index);
}

function getMissingPromotionEvidenceReferences(requiredTokens: string[], evidenceReferences: string[]): string[] {
  const evidenceKeys = new Set(evidenceReferences.map((reference) => getPromotionEvidenceReferenceKey(reference)));
  return requiredTokens.filter((token) => !evidenceKeys.has(token));
}

function getInvalidPromotionEvidenceReferences(
  requiredTokens: string[],
  evidenceReferences: string[],
  manualOverrideId: string
): string[] {
  return requiredTokens.flatMap((token) => {
    const reference = evidenceReferences.find((item) => getPromotionEvidenceReferenceKey(item) === token);
    if (!reference) {
      return [];
    }

    const retainedReference = getPromotionEvidenceReferenceValue(reference);
    if (!retainedReference) {
      return [`${token} must include retained evidence after ':'`];
    }

    if (token === "LIVE_OVERRIDE_REVIEWED" && manualOverrideId && !containsPromotionEvidenceReferenceToken(retainedReference, manualOverrideId)) {
      return [`${token} must reference active manual override ${manualOverrideId}`];
    }

    return [];
  });
}

function getPromotionEvidenceReferenceKey(reference: string): string {
  const [key] = reference.split(":", 1);
  return key.trim().replace(/[ -]/gu, "_").toUpperCase();
}

function getPromotionEvidenceReferenceValue(reference: string): string {
  const separatorIndex = reference.indexOf(":");
  return separatorIndex < 0 ? "" : reference.slice(separatorIndex + 1).trim();
}

function containsPromotionEvidenceReferenceToken(referenceValue: string, token: string): boolean {
  if (referenceValue.toLowerCase() === token.toLowerCase()) {
    return true;
  }

  return referenceValue
    .split(/[/\\#?&=,;\s]+/u)
    .filter((segment) => segment.length > 0)
    .some((segment) => segment.toLowerCase() === token.toLowerCase());
}

function isLivePromotionTarget(evaluation: PromotionEvaluationResult | null): boolean {
  return typeof evaluation?.targetMode === "string" &&
    evaluation.targetMode.trim().toLowerCase() === "live";
}

function toErrorMessage(err: unknown, fallback: string): string {
  if (err instanceof Error && err.message.trim()) {
    return err.message;
  }

  return fallback;
}
