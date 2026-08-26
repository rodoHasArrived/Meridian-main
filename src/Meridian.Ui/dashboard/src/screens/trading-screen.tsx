import { Activity, AlertTriangle, Cable, CandlestickChart, CheckCircle, ClipboardList, FastForward, FlaskConical, Layers, PauseCircle, PlayCircle, PlusCircle, RotateCcw, StopCircle, Trash2, Wallet, XCircle } from "lucide-react";
import React from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FieldSupportText, joinDescribedByIds } from "@/components/ui/field-support";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle
} from "@/components/ui/dialog";
import { GuardrailPanelBody } from "@/components/ui/guardrail-utilization";
import { Input } from "@/components/ui/input";
import { OrderStatusBanner } from "@/components/ui/order-status-banner";
import { Select } from "@/components/ui/select";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { TradingRiskControls } from "@/components/ui/trading-risk-controls";
import {
  promotionEvaluationPanelTone,
  promotionEvaluationTextTone,
  promotionOutcomeTone,
  riskTone,
  wiringTone
} from "@/screens/trading-screen.tones";
import {
  Sheet,
  SheetBody,
  SheetCloseButton,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle
} from "@/components/ui/sheet";
import { DenseRowDetailPanel } from "@/components/meridian/dense-row-detail-accessibility";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { OperationalTrustSummary, type OperationalTrustTone } from "@/components/meridian/operational-trust-summary";
import { StatStrip } from "@/components/meridian/stat-strip";
import { WorkspaceTabStrip } from "@/components/meridian/workspace-primitives";
import { normalizeFundAccountGuid } from "@/lib/fund-account-scope";
import { cn } from "@/lib/utils";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import {
  AcceptanceStatusCard,
  acceptanceTone,
  mapAcceptanceGate,
  type CockpitAcceptanceItem
} from "@/screens/trading-screen.acceptance-panel";
import {
  formatReadinessStatusValue,
  useExecutionEvidenceViewModel,
  usePaperSessionsViewModel,
  useSessionReplayControlsViewModel,
  useStrategyLifecycleControlsViewModel,
  useTradingBlotterViewModel,
  useTradingConfirmViewModel,
  useOrderTicketViewModel,
  usePromotionGateViewModel,
  useTradingReadinessViewModel,
  useTradingScreenShellViewModel,
  type OrderPreview,
  type OrderPreviewEffect,
  type OrderPreviewLevel,
  type OrderPreviewWarning,
  type PaperSessionDetailPanel,
  type PaperSessionReplayPanel,
  type TradingLoadingState,
  type TradingBlotterDetail,
  type TradingDataTone,
  type TradingFillRow,
  type TradingOrderRow,
  type TradingPositionRow,
  type TradingWorkflowCommandState,
  type TradingConfirmViewModel
} from "@/screens/trading-screen.view-model";
import { ExecutionControlsHeader } from "@/screens/trading-screen.execution-controls-header";
import { LIVE_GOVERNED_APPROVAL_SERVICES, useGovernedApprovalsViewModel } from "@/screens/trading-screen.governed-approvals";
import type { ExecutionAuditEntry, ExecutionControlSnapshot, PaperSessionDetail, PaperSessionReplayVerification, PaperSessionSummary, PromotionEvaluationResult, PromotionRecord, TradingOperatorReadiness, TradingWorkspaceResponse } from "@/types";

interface TradingScreenProps {
  data: TradingWorkspaceResponse | null;
  fundAccountId?: string | null;
}

const promotionChecklistDotTone = {
  ready: "bg-success",
  blocked: "bg-danger",
  review: "bg-warning"
} as const;

const dataToneClass: Record<TradingDataTone, string> = {
  default: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger",
  muted: "text-muted-foreground"
};

const dataTonePanelClass: Record<TradingDataTone, string> = {
  default: "border-border/70",
  success: "border-success/35 bg-success/10",
  warning: "border-warning/35 bg-warning/10",
  danger: "border-danger/35 bg-danger/10",
  muted: "border-border/70 bg-secondary/20"
};

type TradingRouteViewId = "overview" | "orders" | "positions" | "risk";

interface TradingRouteTab {
  id: string;
  label: string;
  route: string;
  view: TradingRouteViewId | null;
}

/**
 * Route-scoped views: each Trading sub-route renders its focused tool and the
 * workspace root renders the acceptance/session overview. The tab strip and
 * the sidebar sub-navigation share this taxonomy, so there is exactly one
 * navigation system for the workspace.
 */
const tradingRouteTabs: TradingRouteTab[] = [
  { id: "overview", label: "Overview", route: WORKSTATION_ROUTE_CATALOG.trading, view: "overview" },
  { id: "orders", label: "Orders", route: WORKSTATION_ROUTE_CATALOG.tradingOrders, view: "orders" },
  { id: "positions", label: "Positions", route: WORKSTATION_ROUTE_CATALOG.tradingPositions, view: "positions" },
  { id: "risk", label: "Risk", route: WORKSTATION_ROUTE_CATALOG.tradingRisk, view: "risk" },
  { id: "readiness", label: "Readiness", route: WORKSTATION_ROUTE_CATALOG.tradingReadiness, view: null }
];

export function resolveTradingRouteView(pathname: string): TradingRouteViewId {
  const segments = pathname.split("/").filter(Boolean);
  if (segments.includes("positions")) {
    return "positions";
  }

  if (segments.includes("risk")) {
    return "risk";
  }

  if (segments.includes("orders")) {
    return "orders";
  }

  return "overview";
}

const tradingRouteViewCopy: Record<TradingRouteViewId, { title: string; description: string }> = {
  overview: {
    title: "Trading overview",
    description: "Paper cockpit acceptance, session workflows, and promotion evidence. Orders, positions, and risk have focused routes."
  },
  orders: {
    title: "Orders blotter",
    description: "Working orders, recent fills, and order actions for the active paper session."
  },
  positions: {
    title: "Position book",
    description: "Open positions with marks, exposure, and unrealized P&L."
  },
  risk: {
    title: "Risk guardrails",
    description: "Guardrail posture, execution controls, and adapter health."
  }
};

function buildPositionColumns(confirmVm: TradingConfirmViewModel): DenseDataTableColumn<TradingPositionRow>[] {
  return [
    {
      id: "symbol",
      label: "Symbol",
      className: "font-mono font-semibold text-foreground",
      render: (position) => position.symbol
    },
    {
      id: "side",
      label: "Side",
      className: "font-mono text-foreground",
      render: (position) => position.side
    },
    {
      id: "quantity",
      label: "Qty",
      align: "right",
      className: "font-mono text-foreground",
      render: (position) => position.quantity
    },
    {
      id: "average",
      label: "Avg",
      align: "right",
      className: "font-mono text-muted-foreground",
      render: (position) => position.averagePrice
    },
    {
      id: "mark",
      label: "Mark",
      align: "right",
      className: "font-mono text-muted-foreground",
      render: (position) => position.markPrice
    },
    {
      id: "day-pnl",
      label: "Day P&L",
      align: "right",
      render: (position) => (
        <span className={cn("font-mono font-semibold", dataToneClass[position.dayPnlTone])}>
          {position.dayPnl}
        </span>
      )
    },
    {
      id: "unrealized",
      label: "Unrealized",
      align: "right",
      render: (position) => (
        <span className={cn("font-mono font-semibold", dataToneClass[position.unrealizedPnlTone])}>
          {position.unrealizedPnl}
        </span>
      )
    },
    {
      id: "exposure",
      label: "Exposure",
      align: "right",
      className: "font-mono text-foreground",
      render: (position) => position.exposure
    },
    {
      id: "actions",
      label: "",
      srLabel: "Row actions",
      align: "right",
      render: (position) => (
        <Button
          size="sm"
          variant="destructive"
          onClick={() => confirmVm.openConfirm({ kind: "close-position", positionKey: position.positionKey, symbol: position.symbol })}
          aria-label={position.closeAriaLabel}
          title={position.closeTitleLabel}
        >
          {position.closeActionLabel}
        </Button>
      )
    }
  ];
}

function buildOrderColumns(confirmVm: TradingConfirmViewModel): DenseDataTableColumn<TradingOrderRow>[] {
  return [
    {
      id: "order",
      label: "Order",
      className: "font-mono font-semibold text-foreground",
      render: (order) => order.orderId
    },
    {
      id: "symbol",
      label: "Symbol",
      className: "font-mono text-foreground",
      render: (order) => order.symbol
    },
    {
      id: "side",
      label: "Side",
      className: "font-mono text-foreground",
      render: (order) => order.side
    },
    {
      id: "type",
      label: "Type",
      className: "font-mono text-foreground",
      render: (order) => order.type
    },
    {
      id: "quantity",
      label: "Qty",
      align: "right",
      className: "font-mono text-foreground",
      render: (order) => order.quantity
    },
    {
      id: "limit",
      label: "Limit",
      align: "right",
      className: "font-mono text-muted-foreground",
      render: (order) => order.limitPrice
    },
    {
      id: "status",
      label: "Status",
      render: (order) => (
        <span className={cn("font-mono font-semibold", dataToneClass[order.statusTone])}>
          {order.status}
        </span>
      )
    },
    {
      id: "submitted",
      label: "Submitted",
      className: "font-mono text-muted-foreground",
      render: (order) => order.submittedAt
    },
    {
      id: "actions",
      label: "",
      srLabel: "Row actions",
      align: "right",
      render: (order) => (
        <Button
          size="sm"
          variant="destructive"
          onClick={() => confirmVm.openConfirm({ kind: "cancel-order", orderId: order.orderId })}
          aria-label={order.cancelAriaLabel}
          title={order.cancelTitleLabel}
        >
          {order.cancelActionLabel}
        </Button>
      )
    }
  ];
}

const fillColumns: DenseDataTableColumn<TradingFillRow>[] = [
  {
    id: "fill",
    label: "Fill",
    className: "font-mono font-semibold text-foreground",
    render: (fill) => fill.fillId
  },
  {
    id: "order",
    label: "Order",
    className: "font-mono text-muted-foreground",
    render: (fill) => fill.orderId
  },
  {
    id: "symbol",
    label: "Symbol",
    className: "font-mono text-foreground",
    render: (fill) => fill.symbol
  },
  {
    id: "side",
    label: "Side",
    className: "font-mono text-foreground",
    render: (fill) => fill.side
  },
  {
    id: "quantity",
    label: "Qty",
    align: "right",
    className: "font-mono text-foreground",
    render: (fill) => fill.quantity
  },
  {
    id: "price",
    label: "Price",
    align: "right",
    className: "font-mono text-foreground",
    render: (fill) => fill.price
  },
  {
    id: "venue",
    label: "Venue",
    className: "font-mono text-muted-foreground",
    render: (fill) => fill.venue
  },
  {
    id: "timestamp",
    label: "Timestamp",
    className: "font-mono text-muted-foreground",
    render: (fill) => fill.timestamp
  }
];

const sessionReplayStatusPanelClass = {
  default: "border-border/70 bg-secondary/25 text-muted-foreground",
  success: "border-success/30 bg-success/10 text-success",
  warning: "border-warning/30 bg-warning/10 text-warning",
  danger: "border-danger/30 bg-danger/10 text-danger"
} as const;

function TradingLoadingPanel({ state }: { state: TradingLoadingState }) {
  return (
    <Card
      role={state.role}
      aria-busy={state.ariaBusy}
      aria-live={state.ariaLive}
      aria-label={state.regionLabel}
      aria-labelledby={state.titleId}
      aria-describedby={state.detailId}
      className="panel-surface border-[var(--state-pending-bd)] bg-[var(--state-pending-bg)]"
    >
      <CardHeader className="space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex flex-wrap items-center gap-2">
            <span
              className="inline-flex h-2.5 w-2.5 animate-pulse rounded-full bg-[var(--state-pending-fg)]"
              aria-hidden="true"
            />
            <span className="state-matrix-badge state-pending">{state.statusLabel}</span>
            <span className="toolbar-chip" aria-label={`Route ${state.routeLabel}`}>
              <span className="text-muted-foreground">Route</span>
              <b>{state.routeLabel}</b>
            </span>
          </div>
          <RotateCcw className="h-4 w-4 animate-spin text-[var(--state-pending-fg)]" aria-hidden="true" />
        </div>
        <div>
          <CardTitle id={state.titleId}>{state.title}</CardTitle>
          <CardDescription id={state.detailId} className="mt-2 max-w-3xl leading-6">
            {state.detail}
          </CardDescription>
        </div>
        <div className="flex flex-wrap gap-2" aria-label="Trading loading dependencies">
          {state.chips.map((chip) => (
            <span key={chip.label} className="toolbar-chip">
              <span className="text-muted-foreground">{chip.label}</span>
              <span className="font-mono text-[var(--state-pending-fg)]">{chip.value}</span>
            </span>
          ))}
        </div>
      </CardHeader>
    </Card>
  );
}

export function TradingScreen({ data, fundAccountId: operatingFundAccountId }: TradingScreenProps) {
  const { pathname, search } = useLocation();
  const shellVm = useTradingScreenShellViewModel({ pathname, data });
  const blotterVm = useTradingBlotterViewModel(data);
  const fundAccountId = normalizeFundAccountGuid(operatingFundAccountId)
    ?? normalizeFundAccountGuid(data?.brokerage?.account);
  const tradingReadiness = useTradingReadinessViewModel({ initialReadiness: data?.readiness ?? null, fundAccountId });
  const executionEvidence = useExecutionEvidenceViewModel();

  const governedApprovals = useGovernedApprovalsViewModel(LIVE_GOVERNED_APPROVAL_SERVICES);
  const orderTicket = useOrderTicketViewModel({
    fundAccountId,
    positions: data?.positions ?? [],
    risk: data?.risk ?? null,
    onOrderAccepted: async () => {
      await Promise.all([
        executionEvidence.refresh(),
        tradingReadiness.refresh(),
        governedApprovals.refresh()
      ]);
    }
  });

  const confirmVm = useTradingConfirmViewModel({
    fundAccountId,
    onActionSettled: async () => {
      await Promise.all([
        executionEvidence.refresh(),
        tradingReadiness.refresh()
      ]);
    }
  });
  const positionColumns = React.useMemo(() => buildPositionColumns(confirmVm), [confirmVm]);
  const orderColumns = React.useMemo(() => buildOrderColumns(confirmVm), [confirmVm]);

  const paperSessions = usePaperSessionsViewModel({
    onSessionEvidenceChanged: refreshSessionEvidence
  });

  const strategyLifecycle = useStrategyLifecycleControlsViewModel({
    openConfirm: confirmVm.openConfirm
  });
  const sessionReplay = useSessionReplayControlsViewModel();
  const promotionGate = usePromotionGateViewModel();
  const navigate = useNavigate();
  const routeView = resolveTradingRouteView(pathname);
  const [sessionCloseTarget, setSessionCloseTarget] = React.useState<string | null>(null);
  const [sessionCloseAcknowledged, setSessionCloseAcknowledged] = React.useState(false);
  const sessionCloseBusy = paperSessions.busyCommand?.kind === "closing"
    && paperSessions.busyCommand.sessionId === sessionCloseTarget;

  function openSessionCloseConfirmation(sessionId: string) {
    setSessionCloseTarget(sessionId);
    setSessionCloseAcknowledged(false);
  }

  function cancelSessionCloseConfirmation() {
    if (sessionCloseBusy) {
      return;
    }

    setSessionCloseTarget(null);
    setSessionCloseAcknowledged(false);
  }

  async function confirmSessionClose() {
    if (!sessionCloseTarget || !sessionCloseAcknowledged || sessionCloseBusy) {
      return;
    }

    await paperSessions.closeSession(sessionCloseTarget);
    setSessionCloseTarget(null);
    setSessionCloseAcknowledged(false);
  }

  async function refreshSessionEvidence() {
    await Promise.all([
      executionEvidence.refresh(),
      tradingReadiness.refresh()
    ]);
  }

  if (!data) {
    return <TradingLoadingPanel state={shellVm.loadingState} />;
  }

  const cockpitAcceptance = buildCockpitAcceptance({
    operatorReadiness: tradingReadiness.readiness,
    sessions: paperSessions.sessions,
    selectedSessionDetail: paperSessions.selectedSessionDetail,
    sessionReplayVerification: paperSessions.sessionReplayVerification,
    executionAudit: executionEvidence.auditEntries,
    executionControls: executionEvidence.controlsSnapshot,
    promotionEval: promotionGate.evaluation,
    promotionHistory: promotionGate.history,
    promotionApprovedBy: promotionGate.form.approvedBy,
    promotionApprovalReason: promotionGate.form.approvalReason
  });
  const routeTabs = tradingRouteTabs.map((tab) => ({
    id: tab.id,
    label: tab.label,
    selected: tab.view === routeView
  }));
  const showOverview = routeView === "overview";
  const showOrders = routeView === "orders";
  const showPositions = routeView === "positions";
  const showRisk = routeView === "risk";
  const routeCopy = tradingRouteViewCopy[routeView];
  const readinessStatus = tradingReadiness.readiness?.overallStatus ?? null;
  const sourceTone: OperationalTrustTone = data.brokerage.connection === "Connected"
    ? "ready"
    : data.brokerage.connection === "Degraded"
      ? "review"
      : "blocked";
  const completenessCount = data.positions.length + data.openOrders.length + data.fills.length;
  const readinessTone: OperationalTrustTone = readinessStatus === "Ready"
    ? "ready"
    : readinessStatus === "Blocked"
      ? "blocked"
      : readinessStatus
        ? "review"
        : "unknown";

  return (
    <div className="space-y-5">
      <StatStrip metrics={data.metrics} label="Trading headline metrics" />

      <section
        id="trading-overview"
        role="region"
        aria-label="Execution cockpit context"
        className="flex flex-wrap items-end justify-between gap-3"
      >
        <div className="min-w-0">
          <h2 className="font-display text-lg font-semibold leading-tight text-foreground">
            {routeCopy.title}
          </h2>
          <p className="mt-0.5 max-w-3xl text-xs leading-5 text-muted-foreground">
            {routeCopy.description}
          </p>
        </div>
        <WorkspaceTabStrip
          label="Trading routes"
          tabs={routeTabs}
          onSelect={(id) => {
            const tab = tradingRouteTabs.find((candidate) => candidate.id === id);
            if (tab) {
              // Preserve the querystring: the operating scope (symbol, fund
              // account) is threaded through search params across the shell.
              navigate({ pathname: tab.route, search });
            }
          }}
        />
      </section>

      <OperationalTrustSummary
        label="Trading data confidence"
        source={{
          value: `${data.brokerage.provider} · ${data.brokerage.environment}`,
          detail: data.brokerage.connection,
          tone: sourceTone
        }}
        scope={{
          value: fundAccountId ?? data.brokerage.account ?? "All loaded accounts",
          detail: fundAccountId ? "Operating fund account" : "Brokerage account scope",
          tone: fundAccountId || data.brokerage.account ? "ready" : "unknown"
        }}
        freshness={{
          value: data.brokerage.lastHeartbeat || "Unavailable",
          detail: "Latest brokerage heartbeat",
          tone: sourceTone
        }}
        completeness={{
          value: `${data.positions.length} positions · ${data.openOrders.length} orders · ${data.fills.length} fills`,
          detail: `${completenessCount} execution records loaded`,
          tone: completenessCount > 0 ? "ready" : "review"
        }}
        blocker={readinessStatus ? {
          value: formatReadinessStatusValue(readinessStatus),
          detail: "Operator readiness posture",
          tone: readinessTone
        } : undefined}
      />

      {showOverview ? (
      <section id="trading-posture" className="workspace-section-band" aria-labelledby="trading-posture-heading">
        <div className="workspace-section-subheader">
          <div className="min-w-0">
            <p className="eyebrow-label">Posture</p>
            <h3 id="trading-posture-heading" className="workspace-section-title">Trading readiness posture</h3>
          </div>
        </div>

      <AcceptanceStatusCard
        items={cockpitAcceptance}
        readinessVm={tradingReadiness}
      />

      <div
        role="region"
        aria-label={shellVm.workflowStrip.ariaLabel}
        className="panel-surface flex flex-wrap items-center gap-3 px-4 py-3"
      >
        <span className="mr-1 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
          {shellVm.workflowStrip.eyebrow}
        </span>
        {shellVm.workflowStrip.chips.map((chip) => (
          <CockpitChip key={chip.label} label={chip.label} value={chip.value} />
        ))}
        <span id={shellVm.workflowStrip.statusId} className="sr-only" aria-live="polite">
          {shellVm.workflowStrip.statusText}
        </span>
        {shellVm.workflowStrip.commands.map((command) => (
          <WorkflowPanelButton
            key={command.id}
            command={command}
            onOpen={() => shellVm.openWorkflowPanel(command.id)}
          />
        ))}
      </div>
      </section>
      ) : null}

      {showRisk ? (
      <section id="trading-exceptions" className="workspace-section-band" aria-labelledby="trading-exceptions-heading">
        <div className="workspace-section-subheader">
          <div className="min-w-0">
            <p className="eyebrow-label">Exceptions</p>
            <h3 id="trading-exceptions-heading" className="workspace-section-title">Risk and adapter exceptions</h3>
          </div>
        </div>
      <section className="grid gap-4 xl:grid-cols-[1.1fr_0.9fr]">
        <Card className="panel-surface">
          <CardHeader>
            <div className="eyebrow-label">Risk State</div>
            <CardTitle className="flex items-center gap-2">
              <Activity className="h-5 w-5 text-primary" />
              Paper risk cockpit
            </CardTitle>
            <CardDescription>{data.risk.summary}</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 md:grid-cols-3">
            <Stat label="State" value={data.risk.state} tone={riskTone[data.risk.state]} />
            <Stat label="Net Exposure" value={data.risk.netExposure} />
            <Stat label="Gross Exposure" value={data.risk.grossExposure} />
            <Stat label="VaR (95%)" value={data.risk.var95} />
            <Stat label="Max Drawdown" value={data.risk.maxDrawdown} />
            <Stat label="Buying Power Used" value={data.risk.buyingPowerUsed} />
          </CardContent>
          <CardContent className="pt-0">
            <div className="rounded-xl border border-border/70 bg-secondary/35 p-4">
              <div className="mb-2 flex items-center gap-2 text-xs font-medium uppercase tracking-[0.16em] text-muted-foreground">
                <AlertTriangle className="h-4 w-4" />
                Active guardrails
              </div>
              <GuardrailPanelBody
                guardrails={data.risk.guardrails}
                activeGuardrails={data.risk.activeGuardrails}
              />
            </div>
            <div className="mt-3 rounded-xl border border-border/70 bg-background/80 p-4">
              <ExecutionControlsHeader
                executionEvidence={executionEvidence}
                onConfirm={confirmVm.openConfirm}
              />
              <span className="sr-only" aria-live="polite">{executionEvidence.statusAnnouncement}</span>
              {executionEvidence.errorText && (
                <p role="alert" className="mb-2 rounded-md border border-warning/35 bg-warning/10 px-3 py-2 text-xs text-warning">
                  {executionEvidence.errorText}
                </p>
              )}
              {executionEvidence.controlsPanel ? (
                <TechnicalDetails label="Audit details">
                  <dl
                    aria-label={executionEvidence.controlsPanel.ariaLabel}
                    className="grid gap-2 text-xs text-muted-foreground sm:grid-cols-2"
                  >
                    {executionEvidence.controlsPanel.rows.map((row) => (
                      <div key={row.id} className="rounded-md border border-border/60 bg-secondary/20 px-2.5 py-2">
                        <dt>{row.label}:</dt>
                        <dd className="mt-1 break-words font-mono text-foreground">{row.value}</dd>
                      </div>
                    ))}
                  </dl>
                </TechnicalDetails>
              ) : (
                <p className="text-xs text-muted-foreground">{executionEvidence.controlsEmptyText}</p>
              )}
            </div>
            <TradingRiskControls governedApprovals={governedApprovals} />
          </CardContent>
        </Card>

        <Card className="panel-surface-strong bg-panel-strong">
          <CardHeader>
            <div className="eyebrow-label">Brokerage Wiring</div>
            <CardTitle className="flex items-center gap-2">
              <Cable className="h-5 w-5 text-primary" />
              Execution adapter health
            </CardTitle>
            <CardDescription>{data.brokerage.notes}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3 text-sm">
            <KeyValueRow label="Provider" value={data.brokerage.provider} />
            <KeyValueRow label="Account" value={data.brokerage.account} />
            <KeyValueRow label="Environment" value={data.brokerage.environment.toUpperCase()} />
            <KeyValueRow label="Connection" value={data.brokerage.connection} tone={wiringTone[data.brokerage.connection]} />
            <KeyValueRow label="Last heartbeat" value={data.brokerage.lastHeartbeat} />
            <KeyValueRow label="Order ingress" value={data.brokerage.orderIngress} />
            <KeyValueRow label="Fill feed" value={data.brokerage.fillFeed} />
          </CardContent>
        </Card>
      </section>
      </section>
      ) : null}

      {showPositions ? (
      <section id="trading-actions" className="workspace-section-band" aria-label="Position book and selected detail">
      <section className="grid items-start gap-4 xl:grid-cols-[minmax(0,1fr)_360px]">
        <Card className="panel-surface">
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <Wallet className="h-4 w-4 text-primary" />
                  Live positions
                </CardTitle>
                <CardDescription className="mt-2">
                  Select a holding to keep risk, exposure, and guardrail context visible.
                </CardDescription>
              </div>
              <CockpitChip label="Rows" value={String(blotterVm.positionRows.length)} />
            </div>
          </CardHeader>
          <CardContent className="space-y-3">
            <DenseDataTable
              ariaLabel={blotterVm.positionsTableLabel}
              caption="Select a position to update the position detail status window."
              columns={positionColumns}
              rows={blotterVm.positionRows}
              getRowId={(position) => position.id}
              getRowAriaLabel={(position) => position.ariaLabel}
              getRowSelectAriaLabel={(position) => position.selectAriaLabel}
              getRowAriaControls={(position) => position.detailPanelId}
              getRowAriaExpanded={(position) => position.ariaExpanded}
              selectedRowId={blotterVm.selectedPositionRowId}
              onRowSelect={(position) => blotterVm.selectPosition(position.id)}
              emptyText={blotterVm.positionEmptyText}
            />
          </CardContent>
        </Card>

        <div className="min-w-0 xl:sticky xl:top-4">
          <TradingBlotterDetailPanel id={blotterVm.positionDetailId} detail={blotterVm.selectedPosition} emptyText={blotterVm.positionEmptyText} selectedSourceLabel="Selected from live positions" />
        </div>
      </section>
      </section>
      ) : null}

      {showOrders ? (
      <section id="trading-actions" className="workspace-section-band" aria-label="Order blotter and selected detail">
      <section className="grid items-start gap-4 xl:grid-cols-[minmax(0,1fr)_360px]">
        <div className="min-w-0 space-y-4">
        <Card className="panel-surface">
          <CardHeader>
            <div className="flex items-center justify-between gap-3">
              <CardTitle className="flex items-center gap-2 text-base">
                <ClipboardList className="h-4 w-4 text-primary" />
                Open orders
              </CardTitle>
              <div className="panel-action-zone">
                <Button
                  size="sm"
                  variant="destructive"
                  onClick={() => confirmVm.openConfirm({ kind: "cancel-all" })}
                  disabled={blotterVm.cancelAllDisabled}
                  disabledReason={blotterVm.cancelAllDisabledReason}
                  aria-label={blotterVm.cancelAllAriaLabel}
                  title="Cancel all open orders"
                >
                  <Trash2 className="mr-2 h-4 w-4" />
                  Cancel all
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  onClick={orderTicket.toggleTicket}
                  aria-expanded={orderTicket.open}
                  aria-controls="trading-order-ticket"
                >
                  <PlusCircle className="mr-2 h-4 w-4" />
                  {orderTicket.openButtonLabel}
                </Button>
              </div>
            </div>
          </CardHeader>
          {orderTicket.open && (
            <CardContent id={orderTicket.formId} className="border-b border-border/60 pb-6">
              <form onSubmit={(event) => { event.preventDefault(); void orderTicket.submitOrder(); }} className="space-y-4" aria-describedby={orderTicket.requirementId}>
                <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
                  <div className="space-y-1">
                    <label htmlFor={orderTicket.controls.symbol.id} className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">{orderTicket.controls.symbol.label}</label>
                    <Input
                      id={orderTicket.controls.symbol.id}
                      type="text"
                      placeholder="AAPL"
                      value={orderTicket.controls.symbol.value}
                      onChange={(e) => orderTicket.updateField(orderTicket.controls.symbol.field, e.target.value)}
                      onBlur={orderTicket.normalizeSymbol}
                      aria-label={orderTicket.controls.symbol.ariaLabel}
                      aria-describedby={orderTicket.controls.symbol.describedBy}
                      error={orderTicket.controls.symbol.invalid}
                      className="font-mono"
                      required={orderTicket.controls.symbol.required}
                    />
                  </div>
                  <div className="space-y-1">
                    <label htmlFor={orderTicket.controls.side.id} className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">{orderTicket.controls.side.label}</label>
                    <Select
                      id={orderTicket.controls.side.id}
                      value={orderTicket.controls.side.value}
                      onChange={(e) => orderTicket.updateField(orderTicket.controls.side.field, e.target.value)}
                      aria-label={orderTicket.controls.side.ariaLabel}
                      aria-describedby={orderTicket.controls.side.describedBy}
                      required={orderTicket.controls.side.required}
                    >
                      {orderTicket.controls.side.options.map((option) => (
                        <option key={option.value} value={option.value}>{option.label}</option>
                      ))}
                    </Select>
                  </div>
                  <div className="space-y-1">
                    <label htmlFor={orderTicket.controls.type.id} className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">{orderTicket.controls.type.label}</label>
                    <Select
                      id={orderTicket.controls.type.id}
                      value={orderTicket.controls.type.value}
                      onChange={(e) => orderTicket.updateField(orderTicket.controls.type.field, e.target.value)}
                      aria-label={orderTicket.controls.type.ariaLabel}
                      aria-describedby={orderTicket.controls.type.describedBy}
                      required={orderTicket.controls.type.required}
                    >
                      {orderTicket.controls.type.options.map((option) => (
                        <option key={option.value} value={option.value}>{option.label}</option>
                      ))}
                    </Select>
                  </div>
                  <div className="space-y-1">
                    <label htmlFor={orderTicket.controls.quantity.id} className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">{orderTicket.controls.quantity.label}</label>
                    <Input
                      id={orderTicket.controls.quantity.id}
                      type="number"
                      min={1}
                      step={1}
                      value={orderTicket.controls.quantity.value}
                      onChange={(e) => orderTicket.updateField(orderTicket.controls.quantity.field, e.target.value)}
                      aria-label={orderTicket.controls.quantity.ariaLabel}
                      aria-describedby={orderTicket.controls.quantity.describedBy}
                      error={orderTicket.controls.quantity.invalid}
                      className="font-mono"
                      required={orderTicket.controls.quantity.required}
                    />
                  </div>
                  {orderTicket.controls.limitPrice && (
                    <div className="space-y-1">
                      <label htmlFor={orderTicket.controls.limitPrice.id} className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                        {orderTicket.controls.limitPrice.label}
                      </label>
                      <Input
                        id={orderTicket.controls.limitPrice.id}
                        type="number"
                        min={0}
                        step={0.01}
                        value={orderTicket.controls.limitPrice.value}
                        onChange={(e) => orderTicket.controls.limitPrice && orderTicket.updateField(orderTicket.controls.limitPrice.field, e.target.value)}
                        aria-label={orderTicket.controls.limitPrice.ariaLabel}
                        aria-describedby={orderTicket.controls.limitPrice.describedBy}
                        error={orderTicket.controls.limitPrice.invalid}
                        className="font-mono"
                        required={orderTicket.controls.limitPrice.required}
                      />
                    </div>
                  )}
                </div>

                <p id={orderTicket.requirementId} className="text-xs text-muted-foreground">
                  {orderTicket.requirementText}
                </p>
                <span className="sr-only" aria-live="polite">{orderTicket.statusAnnouncement}</span>

                <OrderPreviewPanel preview={orderTicket.preview} />

                <label
                  htmlFor={orderTicket.acknowledgement.id}
                  className="flex items-start gap-3 rounded-md border border-border/70 bg-secondary/20 px-3 py-2 text-sm"
                >
                  <input
                    id={orderTicket.acknowledgement.id}
                    type="checkbox"
                    checked={orderTicket.acknowledgement.checked}
                    disabled={orderTicket.acknowledgement.disabled}
                    onChange={(event) => orderTicket.setAcknowledged(event.target.checked)}
                    aria-describedby={joinDescribedByIds(
                      `${orderTicket.acknowledgement.id}-description`,
                      `${orderTicket.acknowledgement.id}-disabled-reason`
                    )}
                    className="mt-1 h-4 w-4 accent-primary"
                  />
                  <span>
                    <span className="block font-medium text-foreground">{orderTicket.acknowledgement.label}</span>
                    <span id={`${orderTicket.acknowledgement.id}-description`} className="mt-1 block text-xs leading-5 text-muted-foreground">
                      {orderTicket.acknowledgement.description}
                    </span>
                    <FieldSupportText
                      disabledReason={orderTicket.acknowledgement.disabledReason}
                      disabledReasonId={`${orderTicket.acknowledgement.id}-disabled-reason`}
                      disabledReasonClassName="mt-1 block"
                    />
                  </span>
                </label>

                {orderTicket.errorText && (
                  <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger flex items-center gap-2">
                    <XCircle className="h-4 w-4 shrink-0" />
                    {orderTicket.errorText}
                  </div>
                )}

                <div className="panel-action-zone justify-start">
                  <Button
                    type="submit"
                    size="sm"
                    disabled={!orderTicket.canSubmit}
                    disabledReason={orderTicket.submitDisabledReason}
                    busy={orderTicket.submitBusy}
                    busyLabel={orderTicket.submitBusyLabel}
                    aria-label={orderTicket.submitAriaLabel}
                    aria-describedby="order-ticket-requirements"
                  >
                    {orderTicket.submitButtonLabel}
                  </Button>
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    onClick={orderTicket.closeTicket}
                    disabled={!orderTicket.canClose}
                    disabledReason={orderTicket.closeDisabledReason}
                    aria-label={orderTicket.closeAriaLabel}
                  >
                    {orderTicket.closeButtonLabel}
                  </Button>
                </div>
              </form>
            </CardContent>
          )}
          {!orderTicket.open && (
            <OrderStatusBanner
              successText={orderTicket.successText}
              parkedText={orderTicket.parkedText}
              riskWarnings={orderTicket.riskWarnings}
            />
          )}
          <CardContent className="space-y-3">
            <DenseDataTable
              ariaLabel={blotterVm.ordersTableLabel}
              caption="Select an order to update the order detail status window."
              columns={orderColumns}
              rows={blotterVm.orderRows}
              getRowId={(order) => order.id}
              getRowAriaLabel={(order) => order.ariaLabel}
              getRowSelectAriaLabel={(order) => order.selectAriaLabel}
              getRowAriaControls={(order) => order.detailPanelId}
              getRowAriaExpanded={(order) => order.ariaExpanded}
              selectedRowId={blotterVm.selectedOrderRowId}
              onRowSelect={(order) => blotterVm.selectOrder(order.id)}
              emptyText={blotterVm.orderEmptyText}
            />
          </CardContent>
        </Card>

        <Card className="panel-surface">
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <CandlestickChart className="h-4 w-4 text-primary" />
                  Recent fills
                </CardTitle>
                <CardDescription className="mt-2">
                  Select a fill to inspect execution venue, price, and linked order context.
                </CardDescription>
              </div>
              <CockpitChip label="Rows" value={String(blotterVm.fillRows.length)} />
            </div>
          </CardHeader>
          <CardContent className="space-y-3">
            <DenseDataTable
              ariaLabel={blotterVm.fillsTableLabel}
              caption="Select a fill to update the fill detail status window."
              columns={fillColumns}
              rows={blotterVm.fillRows}
              getRowId={(fill) => fill.id}
              getRowAriaLabel={(fill) => fill.ariaLabel}
              getRowSelectAriaLabel={(fill) => fill.selectAriaLabel}
              getRowAriaControls={(fill) => fill.detailPanelId}
              getRowAriaExpanded={(fill) => fill.ariaExpanded}
              selectedRowId={blotterVm.selectedFillRowId}
              onRowSelect={(fill) => blotterVm.selectFill(fill.id)}
              emptyText={blotterVm.fillEmptyText}
            />
          </CardContent>
        </Card>
        </div>

        <div className="min-w-0 space-y-4 xl:sticky xl:top-4">
          <TradingBlotterDetailPanel id={blotterVm.orderDetailId} detail={blotterVm.selectedOrder} emptyText={blotterVm.orderEmptyText} selectedSourceLabel="Selected from open orders" />
          <TradingBlotterDetailPanel id={blotterVm.fillDetailId} detail={blotterVm.selectedFill} emptyText={blotterVm.fillEmptyText} selectedSourceLabel="Selected from recent fills" />
        </div>
      </section>
      </section>
      ) : null}

      {showOverview ? (
      <section id="trading-history" className="workspace-section-band" aria-labelledby="trading-history-heading">
        <div className="workspace-section-subheader">
          <div className="min-w-0">
            <p className="eyebrow-label">History</p>
            <h3 id="trading-history-heading" className="workspace-section-title">Paper session history and execution evidence</h3>
          </div>
          <a className="workspace-section-jump" href="#trading-posture">Posture</a>
        </div>
      <section className="grid gap-4 xl:grid-cols-2">
        {/* Paper session management */}
        <Card className="panel-surface">
          <CardHeader>
            <div className="flex items-center justify-between">
              <CardTitle className="flex items-center gap-2 text-base">
                <Layers className="h-4 w-4 text-primary" />
                Paper sessions
              </CardTitle>
              <Button
                size="sm"
                variant="outline"
                onClick={paperSessions.toggleCreateForm}
                aria-expanded={paperSessions.showCreateForm}
                aria-controls={paperSessions.formPanelId}
                aria-label={paperSessions.toggleCreateButtonAriaLabel}
                disabled={paperSessions.isBusy && !paperSessions.showCreateForm}
                disabledReason={paperSessions.toggleCreateButtonDisabledReason}
              >
                <PlusCircle className="mr-2 h-4 w-4" aria-hidden="true" />
                {paperSessions.toggleCreateButtonLabel}
              </Button>
            </div>
            <CardDescription>Manage paper trading sessions and initial capital allocation.</CardDescription>
          </CardHeader>

          <span className="sr-only" aria-live="polite">{paperSessions.statusAnnouncement}</span>

          {paperSessions.errorText && (
            <CardContent className="pt-0 pb-2">
              <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger flex items-center gap-2">
                <XCircle className="h-4 w-4 shrink-0" aria-hidden="true" />
                {paperSessions.errorText}
              </div>
            </CardContent>
          )}

          {paperSessions.showCreateForm && (
            <CardContent id={paperSessions.formPanelId} className="border-b border-border/60 pb-6">
              <form
                onSubmit={(event) => { event.preventDefault(); void paperSessions.createSession(); }}
                className="space-y-4"
                aria-describedby={paperSessions.formDescriptionId}
              >
                <div className="grid gap-3 sm:grid-cols-2">
                  <div className="space-y-1">
                    <label htmlFor={paperSessions.strategyIdField.id} className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                      {paperSessions.strategyIdField.label}
                    </label>
                    <Input
                      id={paperSessions.strategyIdField.id}
                      type={paperSessions.strategyIdField.type}
                      placeholder={paperSessions.strategyIdField.placeholder}
                      value={paperSessions.strategyIdField.value}
                      autoComplete={paperSessions.strategyIdField.autoComplete}
                      aria-label={paperSessions.strategyIdField.ariaLabel}
                      aria-describedby={joinDescribedByIds(
                        paperSessions.strategyIdField.describedBy,
                        `${paperSessions.strategyIdField.id}-disabled-reason`
                      )}
                      disabled={paperSessions.strategyIdField.disabled}
                      error={paperSessions.strategyIdField.invalid}
                      onChange={(e) => paperSessions.updateField(paperSessions.strategyIdField.field, e.target.value)}
                      className="font-mono"
                    />
                  </div>
                  <div className="space-y-1">
                    <label htmlFor={paperSessions.initialCashField.id} className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                      {paperSessions.initialCashField.label}
                    </label>
                    <Input
                      id={paperSessions.initialCashField.id}
                      type={paperSessions.initialCashField.type}
                      min={paperSessions.initialCashField.min}
                      step={paperSessions.initialCashField.step}
                      value={paperSessions.initialCashField.value}
                      autoComplete={paperSessions.initialCashField.autoComplete}
                      aria-label={paperSessions.initialCashField.ariaLabel}
                      aria-describedby={joinDescribedByIds(
                        paperSessions.initialCashField.describedBy,
                        `${paperSessions.initialCashField.id}-disabled-reason`
                      )}
                      disabled={paperSessions.initialCashField.disabled}
                      error={paperSessions.initialCashField.invalid}
                      onChange={(e) => paperSessions.updateField(paperSessions.initialCashField.field, e.target.value)}
                      className="font-mono"
                      required
                    />
                  </div>
                </div>
                <p id={paperSessions.formDescriptionId} className="text-xs text-muted-foreground">
                  {paperSessions.formRequirementText}
                </p>
                <div className="panel-action-zone justify-start">
                  <Button
                    type="submit"
                    size="sm"
                    disabled={!paperSessions.canSubmitCreate}
                    disabledReason={paperSessions.createButtonDisabledReason}
                    busy={paperSessions.createButtonBusy}
                    busyLabel={paperSessions.createButtonBusyLabel}
                    aria-label={paperSessions.createButtonAriaLabel}
                  >
                    {paperSessions.createButtonLabel}
                  </Button>
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    onClick={paperSessions.closeCreateForm}
                    disabled={!paperSessions.canCloseCreateForm}
                    disabledReason={paperSessions.cancelCreateButtonDisabledReason}
                  >
                    {paperSessions.cancelCreateButtonLabel}
                  </Button>
                </div>
              </form>
            </CardContent>
          )}

          <CardContent>
            {paperSessions.rows.length === 0 ? (
              <p className="text-sm text-muted-foreground py-4 text-center">
                {paperSessions.emptyText}
              </p>
            ) : (
              <div className="space-y-2">
                {paperSessions.rows.map((session) => (
                  <div
                    key={session.sessionId}
                    role="group"
                    aria-label={session.ariaLabel}
                    className={cn(
                      "flex items-center justify-between rounded-lg border px-4 py-3",
                      session.isSelected
                        ? "border-primary/40 bg-primary/10"
                        : "border-border/70 bg-secondary/20"
                    )}
                  >
                    <div className="min-w-0 flex-1">
                      <div className="font-mono text-sm text-foreground truncate">{session.sessionId}</div>
                      <div className="text-xs text-muted-foreground mt-0.5">
                        {session.strategyId} · {session.initialCashText} · {session.statusLabel}
                      </div>
                    </div>
                    <div className="panel-action-zone ml-4 shrink-0">
                      <Button
                        size="sm"
                        variant="outline"
                        onClick={() => { void paperSessions.restoreSession(session.sessionId); }}
                        disabled={!session.canRestore}
                        disabledReason={session.restoreDisabledReason}
                        aria-label={session.restoreAriaLabel}
                      >
                        {session.restoreButtonLabel}
                      </Button>
                      <Button
                        size="sm"
                        variant="outline"
                        onClick={() => { void paperSessions.verifySessionReplay(session.sessionId); }}
                        disabled={!session.canVerify}
                        disabledReason={session.verifyDisabledReason}
                        aria-label={session.verifyAriaLabel}
                      >
                        {session.verifyButtonLabel}
                      </Button>
                      {session.isActive && (
                        <Button
                          size="sm"
                          variant="destructive"
                          onClick={() => openSessionCloseConfirmation(session.sessionId)}
                          disabled={!session.canClose}
                          disabledReason={session.closeDisabledReason}
                          aria-label={session.closeAriaLabel}
                        >
                          {session.closeButtonLabel}
                        </Button>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            )}
            {paperSessions.selectedSessionLabel && (
              <p className="mt-3 text-xs text-muted-foreground">{paperSessions.selectedSessionLabel}</p>
            )}
            {paperSessions.detail && (
              <PaperSessionDetailPanelView detail={paperSessions.detail} />
            )}
            <div
              role="region"
              aria-label={executionEvidence.auditListLabel}
              className="mt-4 rounded-lg border border-border/70 bg-secondary/20 p-4"
            >
              <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                <p className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                  {executionEvidence.auditTitle}
                </p>
                <span className="font-mono text-[11px] text-muted-foreground">{executionEvidence.auditCountLabel}</span>
              </div>
              {executionEvidence.auditRows.length === 0 ? (
                <p className="text-xs text-muted-foreground">{executionEvidence.auditEmptyText}</p>
              ) : (
                <div className="space-y-2">
                  {executionEvidence.auditRows.map((entry) => (
                    <div
                      key={entry.id}
                      role="group"
                      aria-label={entry.ariaLabel}
                      className="rounded-lg border border-border/60 bg-background/70 px-3 py-2"
                    >
                      <div className="flex items-center justify-between gap-3 text-xs">
                        <span className="font-semibold text-foreground">{entry.action}</span>
                        <span
                          className={cn(
                            "font-mono",
                            entry.outcomeTone === "danger"
                              ? "text-danger"
                              : entry.outcomeTone === "success"
                                ? "text-success"
                                : "text-warning"
                          )}
                        >
                          {entry.outcome}
                        </span>
                      </div>
                      <p className="mt-1 text-xs text-muted-foreground">{entry.message}</p>
                      <p className="mt-1 font-mono text-[11px] text-muted-foreground">{entry.metadataText}</p>
                      {entry.technicalMetadataText ? (
                        <TechnicalDetails label="Execution references" className="mt-2">
                          <p className="break-all font-mono text-[11px] text-muted-foreground">
                            {entry.technicalMetadataText}
                          </p>
                        </TechnicalDetails>
                      ) : null}
                    </div>
                  ))}
                </div>
              )}
            </div>
          </CardContent>
        </Card>

        {/* Strategy lifecycle controls — moved to sheet; trigger in Workflow Tools strip */}
      </section>
      </section>
      ) : null}

      {/* ---- Strategy Controls Sheet ---- */}
      <Sheet open={shellVm.strategySheetOpen} onOpenChange={(open) => shellVm.setWorkflowPanelOpen("strategy", open)}>
        <SheetContent id="strategy-lifecycle-panel" aria-labelledby="strategy-lifecycle-title" aria-describedby="strategy-lifecycle-description">
          <SheetHeader>
            <SheetTitle id="strategy-lifecycle-title">
              <PlayCircle className="h-4 w-4 text-primary" />
              {strategyLifecycle.title}
            </SheetTitle>
            <SheetDescription id="strategy-lifecycle-description">{strategyLifecycle.description}</SheetDescription>
            <SheetCloseButton onClick={() => shellVm.closeWorkflowPanel("strategy")} />
          </SheetHeader>
          <SheetBody>
            <div className="space-y-1">
              <label htmlFor={strategyLifecycle.strategyIdInputId} className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                {strategyLifecycle.strategyIdLabel}
              </label>
              <input
                id={strategyLifecycle.strategyIdInputId}
                type="text"
                placeholder={strategyLifecycle.strategyIdPlaceholder}
                value={strategyLifecycle.strategyId}
                aria-describedby={`${strategyLifecycle.strategyIdHelpId} ${strategyLifecycle.strategyIdStatusId}`}
                onChange={(e) => strategyLifecycle.updateStrategyId(e.target.value)}
                className="w-full rounded-lg border border-border bg-background px-3 py-2 font-mono text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
              />
              <p id={strategyLifecycle.strategyIdHelpId} className="text-xs text-muted-foreground">
                {strategyLifecycle.helpText}
              </p>
              <p id={strategyLifecycle.strategyIdStatusId} role="status" className="text-xs text-muted-foreground">
                {strategyLifecycle.statusText}
              </p>
              <span className="sr-only" aria-live="polite">{strategyLifecycle.statusAnnouncement}</span>
            </div>
            <div className="flex flex-wrap gap-3">
              <Button
                size="sm"
                variant="outline"
                aria-label={strategyLifecycle.pauseAriaLabel}
                onClick={strategyLifecycle.openPauseConfirm}
                disabled={!strategyLifecycle.canPause}
                disabledReason={strategyLifecycle.pauseDisabledReason}
              >
                <PauseCircle className="mr-2 h-4 w-4" />
                {strategyLifecycle.pauseButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="destructive"
                aria-label={strategyLifecycle.stopAriaLabel}
                onClick={strategyLifecycle.openStopConfirm}
                disabled={!strategyLifecycle.canStop}
                disabledReason={strategyLifecycle.stopDisabledReason}
              >
                <StopCircle className="mr-2 h-4 w-4" />
                {strategyLifecycle.stopButtonLabel}
              </Button>
            </div>
          </SheetBody>
        </SheetContent>
      </Sheet>

      {/* ---- Session Replay Sheet ---- */}
      <Sheet open={shellVm.replaySheetOpen} onOpenChange={(open) => shellVm.setWorkflowPanelOpen("replay", open)}>
        <SheetContent id="session-replay-panel" aria-labelledby={sessionReplay.sectionTitleId} aria-describedby={sessionReplay.sectionDescriptionId}>
          <SheetHeader>
            <SheetTitle id={sessionReplay.sectionTitleId}>
              <RotateCcw className="h-4 w-4 text-primary" />
              {sessionReplay.sectionTitle}
            </SheetTitle>
            <SheetDescription id={sessionReplay.sectionDescriptionId}>{sessionReplay.sectionDescription}</SheetDescription>
            <SheetCloseButton onClick={() => shellVm.closeWorkflowPanel("replay")} />
          </SheetHeader>
          <SheetBody className="space-y-3">
            <div className="grid gap-2">
              <label htmlFor={sessionReplay.fileSelectId} className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                {sessionReplay.fileSelectLabel}
              </label>
              <Select
                id={sessionReplay.fileSelectId}
                aria-label={sessionReplay.fileSelectAriaLabel}
                value={sessionReplay.selectedFilePath}
                onChange={(e) => sessionReplay.selectReplayFile(e.target.value)}
                disabled={sessionReplay.loadingFiles || sessionReplay.fileOptions.length === 0}
                aria-describedby={sessionReplay.fileSelectDescribedBy}
              >
                {sessionReplay.fileOptions.length === 0 ? (
                  <option value="">{sessionReplay.fileEmptyOptionText}</option>
                ) : sessionReplay.fileOptions.map((file) => (
                  <option key={file.path} value={file.path} aria-label={file.ariaLabel}>
                    {file.name}
                  </option>
                ))}
              </Select>
            </div>

            <div className="grid gap-3 lg:grid-cols-[minmax(7rem,9rem)_1fr] lg:items-end">
              <div className="grid gap-1.5">
                <label htmlFor={sessionReplay.speedInputId} className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                  {sessionReplay.speedLabel}
                </label>
                <Input
                  id={sessionReplay.speedInputId}
                  aria-label={sessionReplay.speedAriaLabel}
                  value={sessionReplay.replaySpeed}
                  onChange={(e) => sessionReplay.updateReplaySpeed(e.target.value)}
                  aria-describedby={sessionReplay.speedDescribedBy}
                  inputMode="decimal"
                  error={Boolean(sessionReplay.speedValidationText)}
                />
                <span id={sessionReplay.speedHelpId} className="text-[11px] text-muted-foreground">
                  {sessionReplay.speedHelpText}
                </span>
              </div>
              <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap">
                <Button
                  size="sm"
                  onClick={sessionReplay.startReplay}
                  disabled={!sessionReplay.canStart}
                  disabledReason={sessionReplay.startDisabledReason}
                >
                  {sessionReplay.startButtonLabel}
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  onClick={sessionReplay.pauseReplay}
                  disabled={!sessionReplay.canPause}
                  disabledReason={sessionReplay.pauseDisabledReason}
                >
                  <PauseCircle className="mr-2 h-4 w-4" />
                  {sessionReplay.pauseButtonLabel}
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  onClick={sessionReplay.resumeReplay}
                  disabled={!sessionReplay.canResume}
                  disabledReason={sessionReplay.resumeDisabledReason}
                >
                  <PlayCircle className="mr-2 h-4 w-4" />
                  {sessionReplay.resumeButtonLabel}
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  onClick={sessionReplay.stopReplay}
                  disabled={!sessionReplay.canStop}
                  disabledReason={sessionReplay.stopDisabledReason}
                >
                  <StopCircle className="mr-2 h-4 w-4" />
                  {sessionReplay.stopButtonLabel}
                </Button>
              </div>
            </div>

            <div className="grid gap-3 lg:grid-cols-[minmax(8rem,10rem)_1fr] lg:items-end">
              <div className="grid gap-1.5">
                <label htmlFor={sessionReplay.seekInputId} className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                  {sessionReplay.seekLabel}
                </label>
                <Input
                  id={sessionReplay.seekInputId}
                  aria-label={sessionReplay.seekAriaLabel}
                  value={sessionReplay.seekMs}
                  onChange={(e) => sessionReplay.updateSeekMs(e.target.value)}
                  aria-describedby={sessionReplay.seekDescribedBy}
                  inputMode="numeric"
                  error={Boolean(sessionReplay.seekValidationText)}
                />
                <span id={sessionReplay.seekHelpId} className="text-[11px] text-muted-foreground">
                  {sessionReplay.seekHelpText}
                </span>
              </div>
              <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap">
                <Button
                  size="sm"
                  variant="outline"
                  onClick={sessionReplay.seekReplay}
                  disabled={!sessionReplay.canSeek}
                  disabledReason={sessionReplay.seekDisabledReason}
                >
                  {sessionReplay.seekButtonLabel}
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  onClick={sessionReplay.applyReplaySpeed}
                  disabled={!sessionReplay.canApplySpeed}
                  disabledReason={sessionReplay.applySpeedDisabledReason}
                >
                  <FastForward className="mr-2 h-4 w-4" />
                  {sessionReplay.applySpeedButtonLabel}
                </Button>
              </div>
            </div>

            <div
              id={sessionReplay.statusId}
              role={sessionReplay.statusPanel.role}
              aria-live={sessionReplay.statusPanel.ariaLive}
              aria-label={sessionReplay.statusPanel.ariaLabel}
              className={cn(
                "rounded-lg border px-3 py-2 text-xs",
                sessionReplayStatusPanelClass[sessionReplay.statusPanel.tone]
              )}
            >
              <div className="font-semibold">{sessionReplay.statusPanel.title}</div>
              <div className="mt-1">{sessionReplay.statusPanel.detail}</div>
            </div>
            {sessionReplay.activeErrorText && (
              <p id={sessionReplay.errorId} className="sr-only">
                {sessionReplay.activeErrorText}
              </p>
            )}
            <span className="sr-only" aria-live="polite">{sessionReplay.statusAnnouncement}</span>
          </SheetBody>
        </SheetContent>
      </Sheet>

      {/* ---- Promotion Gate Sheet ---- */}
      <Sheet open={shellVm.promotionSheetOpen} onOpenChange={(open) => shellVm.setWorkflowPanelOpen("promotion", open)}>
        <SheetContent id="promotion-gate-panel" aria-labelledby="promotion-gate-title" aria-describedby="promotion-gate-description">
          <SheetHeader>
            <SheetTitle id="promotion-gate-title">
              <FlaskConical className="h-4 w-4 text-primary" />
              Backtest → Paper promotion gate
            </SheetTitle>
            <SheetDescription id="promotion-gate-description">Requires eligibility check before confirmation and audit refresh.</SheetDescription>
            <SheetCloseButton onClick={() => shellVm.closeWorkflowPanel("promotion")} />
          </SheetHeader>
          <SheetBody className="space-y-3">
            <div id="promotion-action-state" className="rounded-lg border border-border/70 bg-secondary/25 px-4 py-3">
              <div className="eyebrow-label">Action state</div>
              <p className="mt-2 text-sm font-semibold text-foreground">{promotionGate.nextActionText}</p>
              <div className="mt-2 grid gap-2 text-xs leading-5 text-muted-foreground md:grid-cols-2">
                <p>{promotionGate.approvalRequirementText}</p>
                <p>{promotionGate.rejectionRequirementText}</p>
              </div>
            </div>

            <span className="sr-only" aria-live="polite">{promotionGate.statusAnnouncement}</span>

            <div className="grid gap-3 sm:grid-cols-2">
              <label htmlFor={promotionGate.fields.runId.id} className="grid gap-1 text-sm">
                <span className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">{promotionGate.fields.runId.label}</span>
                <input
                  id={promotionGate.fields.runId.id}
                  aria-label={promotionGate.fields.runId.ariaLabel}
                  placeholder={promotionGate.fields.runId.placeholder}
                  value={promotionGate.form.runId}
                  onChange={(e) => promotionGate.updateField(promotionGate.fields.runId.field, e.target.value)}
                  aria-describedby={promotionGate.fields.runId.describedBy ?? undefined}
                  disabled={promotionGate.busy}
                  className="w-full rounded-lg border border-border bg-background px-3 py-2 font-mono text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                />
                {promotionGate.fields.runId.helpText ? (
                  <span id={promotionGate.fields.runId.helpId ?? undefined} className="text-xs text-muted-foreground">{promotionGate.fields.runId.helpText}</span>
                ) : null}
              </label>
              <label htmlFor={promotionGate.fields.approvedBy.id} className="grid gap-1 text-sm">
                <span className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">{promotionGate.fields.approvedBy.label}</span>
                <input
                  id={promotionGate.fields.approvedBy.id}
                  aria-label={promotionGate.fields.approvedBy.ariaLabel}
                  placeholder={promotionGate.fields.approvedBy.placeholder}
                  value={promotionGate.form.approvedBy}
                  onChange={(e) => promotionGate.updateField(promotionGate.fields.approvedBy.field, e.target.value)}
                  aria-describedby={promotionGate.fields.approvedBy.describedBy ?? undefined}
                  disabled={promotionGate.busy}
                  className="w-full rounded-lg border border-border bg-background px-3 py-2 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                  required={promotionGate.fields.approvedBy.required}
                />
              </label>
            </div>
            <label htmlFor={promotionGate.fields.approvalReason.id} className="grid gap-1 text-sm">
              <span className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">{promotionGate.fields.approvalReason.label}</span>
              <input
                id={promotionGate.fields.approvalReason.id}
                aria-label={promotionGate.fields.approvalReason.ariaLabel}
                placeholder={promotionGate.fields.approvalReason.placeholder}
                value={promotionGate.form.approvalReason}
                onChange={(e) => promotionGate.updateField(promotionGate.fields.approvalReason.field, e.target.value)}
                aria-describedby={promotionGate.fields.approvalReason.describedBy ?? undefined}
                disabled={promotionGate.busy}
                className="w-full rounded-lg border border-border bg-background px-3 py-2 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                required={promotionGate.fields.approvalReason.required}
              />
            </label>
            <label htmlFor={promotionGate.fields.rejectionReason.id} className="grid gap-1 text-sm">
              <span className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">{promotionGate.fields.rejectionReason.label}</span>
              <input
                id={promotionGate.fields.rejectionReason.id}
                aria-label={promotionGate.fields.rejectionReason.ariaLabel}
                placeholder={promotionGate.fields.rejectionReason.placeholder}
                value={promotionGate.form.rejectionReason}
                onChange={(e) => promotionGate.updateField(promotionGate.fields.rejectionReason.field, e.target.value)}
                aria-describedby={promotionGate.fields.rejectionReason.describedBy ?? undefined}
                disabled={promotionGate.busy}
                className="w-full rounded-lg border border-border bg-background px-3 py-2 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
              />
            </label>
            <div className="grid gap-3 sm:grid-cols-2">
              <label htmlFor={promotionGate.fields.reviewNotes.id} className="grid gap-1 text-sm">
                <span className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">{promotionGate.fields.reviewNotes.label}</span>
                <input
                  id={promotionGate.fields.reviewNotes.id}
                  aria-label={promotionGate.fields.reviewNotes.ariaLabel}
                  placeholder={promotionGate.fields.reviewNotes.placeholder}
                  value={promotionGate.form.reviewNotes}
                  onChange={(e) => promotionGate.updateField(promotionGate.fields.reviewNotes.field, e.target.value)}
                  aria-describedby={promotionGate.fields.reviewNotes.describedBy ?? undefined}
                  disabled={promotionGate.busy}
                  className="w-full rounded-lg border border-border bg-background px-3 py-2 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                />
              </label>
              <label htmlFor={promotionGate.fields.manualOverrideId.id} className="grid gap-1 text-sm">
                <span className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">{promotionGate.fields.manualOverrideId.label}</span>
                <input
                  id={promotionGate.fields.manualOverrideId.id}
                  aria-label={promotionGate.fields.manualOverrideId.ariaLabel}
                  placeholder={promotionGate.fields.manualOverrideId.placeholder}
                  value={promotionGate.form.manualOverrideId}
                  onChange={(e) => promotionGate.updateField(promotionGate.fields.manualOverrideId.field, e.target.value)}
                  aria-describedby={promotionGate.fields.manualOverrideId.describedBy ?? undefined}
                  disabled={promotionGate.busy}
                  className="w-full rounded-lg border border-border bg-background px-3 py-2 font-mono text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                />
              </label>
            </div>
            <label htmlFor={promotionGate.fields.evidenceReferences.id} className="grid gap-1 text-sm">
              <span className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">{promotionGate.fields.evidenceReferences.label}</span>
              <textarea
                id={promotionGate.fields.evidenceReferences.id}
                aria-label={promotionGate.fields.evidenceReferences.ariaLabel}
                placeholder={promotionGate.fields.evidenceReferences.placeholder}
                value={promotionGate.form.evidenceReferences}
                onChange={(e) => promotionGate.updateField(promotionGate.fields.evidenceReferences.field, e.target.value)}
                aria-describedby={promotionGate.fields.evidenceReferences.describedBy ?? undefined}
                disabled={promotionGate.busy}
                className="min-h-24 w-full rounded-lg border border-border bg-background px-3 py-2 font-mono text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
              />
              {promotionGate.fields.evidenceReferences.helpText ? (
                <span id={promotionGate.fields.evidenceReferences.helpId ?? undefined} className="text-xs text-muted-foreground">{promotionGate.fields.evidenceReferences.helpText}</span>
              ) : null}
            </label>
            <div className="flex flex-wrap gap-2">
              <Button
                size="sm"
                variant="outline"
                onClick={() => void promotionGate.evaluateGateChecks()}
                disabled={promotionGate.evaluateCommand.disabled}
                disabledReason={promotionGate.evaluateCommand.disabledReason}
                busy={promotionGate.evaluateCommand.busy}
                busyLabel={promotionGate.evaluateCommand.busyLabel}
                aria-label={promotionGate.evaluateCommand.ariaLabel}
              >
                {promotionGate.evaluateCommand.label}
              </Button>
              <Button
                size="sm"
                onClick={() => void promotionGate.promoteToPaper()}
                disabled={promotionGate.promoteCommand.disabled}
                disabledReason={promotionGate.promoteCommand.disabledReason}
                busy={promotionGate.promoteCommand.busy}
                busyLabel={promotionGate.promoteCommand.busyLabel}
                aria-label={promotionGate.promoteCommand.ariaLabel}
              >
                {promotionGate.promoteCommand.label}
              </Button>
              <Button
                size="sm"
                variant="destructive"
                onClick={() => void promotionGate.rejectPromotion()}
                disabled={promotionGate.rejectCommand.disabled}
                disabledReason={promotionGate.rejectCommand.disabledReason}
                busy={promotionGate.rejectCommand.busy}
                busyLabel={promotionGate.rejectCommand.busyLabel}
                aria-label={promotionGate.rejectCommand.ariaLabel}
              >
                {promotionGate.rejectCommand.label}
              </Button>
            </div>
            {promotionGate.evaluationPanel && (
              <div className="space-y-3">
                <div
                  role={promotionGate.evaluationPanel.role}
                  aria-live={promotionGate.evaluationPanel.ariaLive}
                  aria-label={promotionGate.evaluationPanel.ariaLabel}
                  className={cn(
                    "rounded-lg border p-3 text-xs",
                    promotionEvaluationPanelTone[promotionGate.evaluationPanel.tone]
                  )}
                >
                  <p className="font-semibold">{promotionGate.evaluationPanel.title}</p>
                  <p className="mt-1">
                    <span className={promotionEvaluationTextTone[promotionGate.evaluationPanel.eligibleTone]}>
                      {promotionGate.evaluationPanel.eligibleLabel}
                    </span>
                  </p>
                  <dl className="mt-2 grid gap-2 sm:grid-cols-3">
                    {promotionGate.evaluationPanel.metrics.map((metric) => (
                      <div key={metric.id}>
                        <dt className="font-mono text-[10px] uppercase tracking-[0.14em] opacity-75">{metric.label}</dt>
                        <dd className="font-mono text-xs">{metric.value}</dd>
                      </div>
                    ))}
                  </dl>
                  {promotionGate.evaluationPanel.reason && (
                    <p className="mt-2 text-muted-foreground">{promotionGate.evaluationPanel.reason}</p>
                  )}
                  {promotionGate.evaluationPanel.warnings.map((warning) => (
                    <p key={warning.id} className="mt-1 text-warning">{warning.text}</p>
                  ))}
                  {promotionGate.evaluationPanel.blockingReasons.length > 0 && (
                    <div className="mt-2 rounded border border-danger/30 bg-danger/10 p-2">
                      <p className="font-semibold text-danger">Blocking reasons:</p>
                      <ul aria-label={promotionGate.evaluationPanel.blockingListLabel ?? undefined} className="mt-1 list-disc space-y-1 pl-4 text-danger">
                        {promotionGate.evaluationPanel.blockingReasons.map((reason) => (
                          <li key={reason.id}>{reason.text}</li>
                        ))}
                      </ul>
                    </div>
                  )}
                </div>
                <div className="rounded-lg border border-border/60 p-3 text-xs">
                  <p className="font-semibold">Approval checklist</p>
                  <ul className="mt-2 space-y-2">
                    {promotionGate.approvalChecklist.map((item) => (
                      <li key={item.id} className="flex items-start gap-2" aria-label={item.ariaLabel}>
                        <span className={cn(
                          "mt-0.5 inline-block h-2 w-2 rounded-full flex-shrink-0",
                          promotionChecklistDotTone[item.status]
                        )} />
                        <div>
                          <p className="font-medium">{item.label}</p>
                          {item.description && <p className="text-muted-foreground">{item.description}</p>}
                        </div>
                      </li>
                    ))}
                  </ul>
                </div>
              </div>
            )}
            {promotionGate.outcome && (
              <p role="status" className={cn("text-xs", promotionOutcomeTone[promotionGate.outcome.level])}>
                {promotionGate.outcome.message}
              </p>
            )}
            {promotionGate.errorText && <p role="alert" className="text-xs text-danger">{promotionGate.errorText}</p>}
            <div className="rounded-lg border border-border/60 p-3">
              <p className="mb-2 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Audit trail</p>
              <ul className="space-y-1 text-xs">
                {promotionGate.historyRows.length === 0 && (
                  <li className="text-muted-foreground">{promotionGate.historyEmptyText}</li>
                )}
                {promotionGate.historyRows.map((record) => (
                  <li key={record.id} className="font-mono" aria-label={record.ariaLabel}>
                    {record.label}
                  </li>
                ))}
              </ul>
            </div>
          </SheetBody>
        </SheetContent>
      </Sheet>

      <ConfirmActionDialog vm={confirmVm} />
      <SessionCloseConfirmationDialog
        sessionId={sessionCloseTarget}
        acknowledged={sessionCloseAcknowledged}
        busy={sessionCloseBusy}
        onAcknowledgedChange={setSessionCloseAcknowledged}
        onCancel={cancelSessionCloseConfirmation}
        onConfirm={confirmSessionClose}
      />
    </div>
  );
}

function buildCockpitAcceptance({
  operatorReadiness,
  sessions,
  selectedSessionDetail,
  sessionReplayVerification,
  executionAudit,
  executionControls,
  promotionEval,
  promotionHistory,
  promotionApprovedBy,
  promotionApprovalReason
}: {
  operatorReadiness: TradingOperatorReadiness | null;
  sessions: PaperSessionSummary[];
  selectedSessionDetail: PaperSessionDetail | null;
  sessionReplayVerification: PaperSessionReplayVerification | null;
  executionAudit: ExecutionAuditEntry[];
  executionControls: ExecutionControlSnapshot | null;
  promotionEval: PromotionEvaluationResult | null;
  promotionHistory: PromotionRecord[];
  promotionApprovedBy: string;
  promotionApprovalReason: string;
}): CockpitAcceptanceItem[] {
  if (operatorReadiness?.acceptanceGates?.length) {
    return operatorReadiness.acceptanceGates.map(mapAcceptanceGate);
  }

  const readinessSession = operatorReadiness?.activeSession ?? null;
  const sessionCount = Math.max(sessions.length, operatorReadiness?.sessions.length ?? 0);
  const readinessReplay = operatorReadiness?.replay ?? null;
  const replayEvidence = sessionReplayVerification
    ? {
        isConsistent: sessionReplayVerification.isConsistent,
        comparedFillCount: sessionReplayVerification.comparedFillCount,
        comparedOrderCount: sessionReplayVerification.comparedOrderCount,
        comparedLedgerEntryCount: sessionReplayVerification.comparedLedgerEntryCount,
        mismatchReasons: sessionReplayVerification.mismatchReasons
      }
    : readinessReplay;
  const readinessControls = operatorReadiness?.controls ?? null;
  const circuitBreakerOpen = readinessControls?.circuitBreakerOpen ?? executionControls?.circuitBreaker.isOpen ?? false;
  const circuitBreakerReason = readinessControls?.circuitBreakerReason ?? executionControls?.circuitBreaker.reason ?? null;
  const manualOverrideCount = readinessControls?.manualOverrideCount ?? executionControls?.manualOverrides.length ?? 0;
  const serverAuditEvidenceCount = (readinessReplay?.verificationAuditId ? 1 : 0) + (operatorReadiness?.promotion?.auditReference ? 1 : 0);
  const latestPromotion = promotionHistory[0];
  const latestPromotionHasRationale = Boolean(
    latestPromotion?.approvalReason || latestPromotion?.reviewNotes
  );
  const latestPromotionHasLineage = Boolean(
    latestPromotion?.sourceRunId || latestPromotion?.runId
  );
  const latestPromotionTraceComplete = Boolean(
    latestPromotion?.decision &&
    latestPromotion?.approvedBy &&
    latestPromotionHasRationale &&
    latestPromotionHasLineage &&
    latestPromotion?.auditReference
  );
  const promotionReviewPrepared = Boolean(
    promotionEval?.isEligible &&
    promotionApprovedBy.trim() &&
    promotionApprovalReason.trim()
  );
  const readinessPromotion = operatorReadiness?.promotion ?? null;
  const readinessPromotionTraceComplete = Boolean(
    readinessPromotion?.approvalStatus &&
    readinessPromotion?.approvedBy &&
    readinessPromotion?.reason &&
    readinessPromotion?.sourceRunId &&
    readinessPromotion?.auditReference &&
    !readinessPromotion?.requiresReview
  );

  return [
    selectedSessionDetail
      ? {
          label: "Session persistence",
          value: "Ready",
          detail: `Restored ${selectedSessionDetail.summary.sessionId} with ${selectedSessionDetail.orderHistory?.length ?? 0} retained orders.`,
          level: "ready"
        }
      : readinessSession
        ? {
            label: "Session persistence",
            value: readinessSession.isActive ? "Active" : "Restored",
            detail: `${readinessSession.sessionId} tracks ${readinessSession.orderCount} retained orders and ${readinessSession.positionCount} positions.`,
            level: readinessSession.isActive ? "ready" : "review"
          }
      : sessionCount > 0
        ? {
            label: "Session persistence",
            value: "Restore required",
            detail: "Restore a paper session before treating the cockpit as operator-ready.",
            level: "review"
          }
        : {
            label: "Session persistence",
            value: "No session",
            detail: "Create a paper session so orders, fills, and portfolio state can be retained.",
            level: "atRisk"
          },
    replayEvidence
      ? {
          label: "Replay confidence",
          value: replayEvidence.isConsistent ? "Ready" : "Mismatch detected",
          detail: replayEvidence.isConsistent
            ? `Compared ${replayEvidence.comparedFillCount} fills, ${replayEvidence.comparedOrderCount} orders, and ${replayEvidence.comparedLedgerEntryCount} ledger entries.`
            : replayEvidence.mismatchReasons[0] ?? "Replay output differs from current session state.",
          level: replayEvidence.isConsistent ? "ready" : "atRisk"
        }
      : {
          label: "Replay confidence",
          value: "Verify required",
          detail: "Run replay verification for the selected paper session before accepting cockpit readiness.",
          level: "review"
        },
    circuitBreakerOpen
      ? {
          label: "Audit + controls",
          value: "Circuit open",
          detail: circuitBreakerReason ?? "The execution circuit breaker must be resolved before acceptance.",
          level: "atRisk"
        }
      : executionAudit.length > 0 || serverAuditEvidenceCount > 0
        ? {
            label: "Audit + controls",
            value: "Ready",
            detail: `${executionAudit.length || serverAuditEvidenceCount} recent execution audit ${executionAudit.length + serverAuditEvidenceCount === 1 ? "entry is" : "entries are"} visible; ${manualOverrideCount} manual override(s) active.`,
            level: "ready"
          }
        : {
            label: "Audit + controls",
            value: "No entries",
            detail: "Execution actions need visible audit and control evidence for daily operation.",
            level: "review"
          },
    readinessPromotionTraceComplete
      ? {
          label: "Promotion review",
          value: "Ready",
          detail: `${readinessPromotion!.approvalStatus} by ${formatTradingOperatorLabel(readinessPromotion!.approvedBy)}: ${readinessPromotion!.reason}. Audit evidence retained.`,
          level: "ready"
        }
      : readinessPromotion
        ? {
            label: "Promotion review",
            value: "Trace incomplete",
            detail: readinessPromotion.reason || "Promotion decision is missing operator, lineage, rationale, or audit linkage.",
            level: "review"
          }
      : promotionEval
          ? {
              label: "Promotion review",
              value: promotionEval.isEligible
                ? promotionReviewPrepared ? "Trace pending" : "Rationale required"
                : "Gate blocked",
              detail: promotionEval.isEligible
                ? promotionReviewPrepared
                  ? "Confirm promotion to write the durable audit-linked decision record."
                  : "Add operator and approval reason before confirming promotion."
                : promotionEval.blockingReasons?.[0] ?? promotionEval.reason,
              level: promotionEval.isEligible ? "review" : "atRisk"
            }
      : latestPromotionTraceComplete
        ? {
            label: "Promotion review",
            value: "Ready",
            detail: `${latestPromotion!.decision} by ${formatTradingOperatorLabel(latestPromotion!.approvedBy)}: ${latestPromotion!.approvalReason ?? latestPromotion!.reviewNotes}. Audit evidence retained.`,
            level: "ready"
          }
        : latestPromotion && latestPromotionHasRationale
          ? {
              label: "Promotion review",
              value: "Trace incomplete",
              detail: "Latest promotion decision has rationale but is missing operator, lineage, or audit linkage.",
              level: "review"
            }
      : {
          label: "Promotion review",
          value: "Evaluate gate",
          detail: "Evaluate a backtest run before promoting it into paper operation.",
          level: "review"
        }
  ];
}

function formatTradingOperatorLabel(value: string | null | undefined): string {
  const normalized = value?.trim() ?? "";
  if (!normalized) {
    return "Unknown operator";
  }

  if (/^fixture[-_:]/i.test(normalized)) {
    return "Paper operator";
  }

  const words = normalized.replace(/[._:-]+/g, " ").replace(/\s+/g, " ").trim();
  return words
    .split(" ")
    .map((word) => word ? `${word.charAt(0).toUpperCase()}${word.slice(1)}` : word)
    .join(" ");
}

function PaperSessionDetailPanelView({ detail }: { detail: PaperSessionDetailPanel }) {
  return (
    <div
      className="mt-4 space-y-3 rounded-lg border border-border/70 bg-background/70 p-4"
      role="region"
      aria-label={detail.ariaLabel}
    >
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Session detail</p>
          <p className="mt-1 font-mono text-sm text-foreground">{detail.sessionId}</p>
        </div>
        <span className={cn("rounded-sm border px-2.5 py-1 font-mono text-[10px] font-medium uppercase tracking-[0.14em]", acceptanceTone[detail.statusTone])}>
          {detail.statusLabel}
        </span>
      </div>

      <div className="grid gap-3 sm:grid-cols-2">
        {detail.infoRows.map((row) => (
          <DataRow key={row.label} label={row.label} value={row.value} />
        ))}
      </div>

      {detail.metricRows.length > 0 && (
        <div className="grid gap-3 sm:grid-cols-3">
          {detail.metricRows.map((row) => (
            <DataRow key={row.label} label={row.label} value={row.value} />
          ))}
        </div>
      )}

      {detail.replay && <PaperSessionReplayPanelView panel={detail.replay} />}
    </div>
  );
}

function PaperSessionReplayPanelView({ panel }: { panel: PaperSessionReplayPanel }) {
  return (
    <div
      role="status"
      aria-label={panel.ariaLabel}
      className={cn(
        "rounded-lg border px-3 py-3 text-sm",
        panel.tone === "success"
          ? "border-success/30 bg-success/10"
          : "border-warning/30 bg-warning/10"
      )}
    >
      <div className="flex items-center justify-between gap-3">
        <span className="font-semibold text-foreground">Replay verification</span>
        <span className={panel.tone === "success" ? "text-success" : "text-warning"}>
          {panel.statusLabel}
        </span>
      </div>
      <p className="mt-1 text-xs text-muted-foreground">{panel.metadataText}</p>
      <div className="mt-2 grid gap-1 text-xs text-foreground sm:grid-cols-2">
        {panel.rows.map((row) => (
          <span key={row.label}>{row.label}: {row.value}</span>
        ))}
      </div>
      {panel.mismatchReasons.length > 0 && (
        <ul className="mt-2 space-y-1 text-xs text-foreground">
          {panel.mismatchReasons.map((reason) => (
            <li key={reason}>• {reason}</li>
          ))}
        </ul>
      )}
    </div>
  );
}

/** Shared label+value tile used in session info and session metric contexts. */
function DataRow({ label, value }: { label: string; value: string | null }) {
  return (
    <div className="data-grid-surface px-3 py-2">
      <div className="text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">{label}</div>
      <div className="mt-1 font-mono text-sm text-foreground">{value ?? "Unavailable"}</div>
    </div>
  );
}

function ConfirmActionDialog({ vm }: { vm: TradingConfirmViewModel }) {
  const isSuccess = vm.resultPanel?.tone === "success";

  return (
    <Dialog open={vm.open} onOpenChange={(open) => { if (!open) vm.closeConfirm(); }}>
      <DialogContent
        className="sm:max-w-md"
        aria-labelledby={vm.dialogTitleId}
        aria-describedby={vm.dialogDescriptionId}
      >
        <DialogHeader>
          <DialogTitle id={vm.dialogTitleId}>{vm.title}</DialogTitle>
          <DialogDescription id={vm.dialogDescriptionId}>{vm.description}</DialogDescription>
        </DialogHeader>
        <span className="sr-only" aria-live="polite">{vm.statusAnnouncement}</span>

        {vm.errorPanel && (
          <div role="alert" aria-label={vm.errorPanel.ariaLabel} className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger flex items-center gap-2">
            <XCircle className="h-4 w-4 shrink-0" />
            {vm.errorPanel.text}
          </div>
        )}

        {vm.resultPanel && (
          <div
            role="status"
            aria-label={vm.resultPanel.ariaLabel}
            className={cn(
              "rounded-lg border px-4 py-3 text-sm flex flex-col gap-1",
              isSuccess
                ? "border-success/30 bg-success/10 text-success"
                : "border-warning/30 bg-warning/10 text-warning"
            )}
          >
            <div className="flex items-center gap-2">
              {isSuccess ? (
                <CheckCircle className="h-4 w-4 shrink-0" />
              ) : (
                <AlertTriangle className="h-4 w-4 shrink-0" />
              )}
              <span className="font-semibold">{vm.resultPanel.status}</span>
            </div>
            <p>{vm.resultPanel.message}</p>
            <p className="mt-1 font-mono text-xs opacity-70">Action ID: {vm.resultPanel.actionId}</p>
          </div>
        )}

        {!vm.isCompleted && (
          <div className="space-y-3 pt-2">
            <label
              htmlFor={vm.acknowledgement.id}
              className="flex items-start gap-3 rounded-md border border-border/70 bg-secondary/20 px-3 py-2 text-sm"
            >
              <input
                id={vm.acknowledgement.id}
                type="checkbox"
                checked={vm.acknowledgement.checked}
                disabled={vm.acknowledgement.disabled}
                onChange={(event) => vm.setReviewAcknowledged(event.target.checked)}
                aria-describedby={joinDescribedByIds(
                  `${vm.acknowledgement.id}-description`,
                  `${vm.acknowledgement.id}-disabled-reason`
                )}
                className="mt-1 h-4 w-4 accent-primary"
              />
              <span>
                <span className="block font-medium text-foreground">{vm.acknowledgement.label}</span>
                <span id={`${vm.acknowledgement.id}-description`} className="mt-1 block text-xs leading-5 text-muted-foreground">
                  {vm.acknowledgement.description}
                </span>
                <FieldSupportText
                  disabledReason={vm.acknowledgement.disabledReason}
                  disabledReasonId={`${vm.acknowledgement.id}-disabled-reason`}
                  disabledReasonClassName="mt-1 block"
                />
              </span>
            </label>
            <div className="panel-action-zone">
              <Button variant="outline" onClick={vm.closeConfirm} disabled={!vm.canClose}>
                {vm.cancelButtonLabel}
              </Button>
              <Button
                variant={vm.confirmButtonVariant}
                onClick={() => { void vm.executeConfirm(); }}
                disabled={!vm.canConfirm}
                disabledReason={vm.confirmDisabledReason}
                aria-label={vm.confirmAriaLabel}
              >
                {vm.confirmButtonLabel}
              </Button>
            </div>
          </div>
        )}

        {vm.isCompleted && (
          <div className="panel-action-zone pt-2">
            <Button variant="outline" onClick={vm.closeConfirm} aria-label={vm.closeAriaLabel}>
              {vm.closeButtonLabel}
            </Button>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}

function SessionCloseConfirmationDialog({
  sessionId,
  acknowledged,
  busy,
  onAcknowledgedChange,
  onCancel,
  onConfirm
}: {
  sessionId: string | null;
  acknowledged: boolean;
  busy: boolean;
  onAcknowledgedChange: (acknowledged: boolean) => void;
  onCancel: () => void;
  onConfirm: () => Promise<void>;
}) {
  const titleId = "paper-session-close-title";
  const descriptionId = "paper-session-close-description";
  const acknowledgementId = "paper-session-close-acknowledgement";

  return (
    <Dialog open={sessionId !== null} onOpenChange={(open) => { if (!open) onCancel(); }}>
      <DialogContent
        className="sm:max-w-md"
        aria-labelledby={titleId}
        aria-describedby={descriptionId}
      >
        <DialogHeader>
          <DialogTitle id={titleId}>Close paper session {sessionId}</DialogTitle>
          <DialogDescription id={descriptionId}>
            Closing stops further execution for this paper session. It does not cancel working orders or flatten open positions.
          </DialogDescription>
        </DialogHeader>
        <label
          htmlFor={acknowledgementId}
          className="flex items-start gap-3 rounded-md border border-danger/30 bg-danger/10 px-3 py-3 text-sm"
        >
          <input
            id={acknowledgementId}
            type="checkbox"
            checked={acknowledged}
            disabled={busy}
            onChange={(event) => onAcknowledgedChange(event.target.checked)}
            className="mt-1 h-4 w-4 accent-danger"
          />
          <span>
            <span className="block font-medium text-foreground">I reviewed open orders and positions</span>
            <span className="mt-1 block text-xs leading-5 text-muted-foreground">
              Confirm any remaining exposure is intentional before closing the session.
            </span>
          </span>
        </label>
        <div className="panel-action-zone pt-2">
          <Button variant="outline" onClick={onCancel} disabled={busy}>Keep session open</Button>
          <Button
            variant="destructive"
            onClick={() => { void onConfirm(); }}
            disabled={!acknowledged || busy}
            disabledReason={!acknowledged ? "Review open orders and positions before closing this session." : undefined}
            busy={busy}
            busyLabel="Closing paper session"
            aria-label={sessionId ? `Confirm close paper session ${sessionId}` : "Confirm close paper session"}
          >
            Close session
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function TradingBlotterDetailPanel({
  id,
  detail,
  emptyText,
  selectedSourceLabel
}: {
  id: string;
  detail: TradingBlotterDetail | null;
  emptyText: string;
  selectedSourceLabel: string;
}) {
  return (
    <DenseRowDetailPanel
      id={id}
      ariaLabel={detail?.ariaLabel ?? "Trading blotter detail"}
      selectedSourceLabel={selectedSourceLabel}
      className={cn(
        "rounded-md border bg-background/70 p-3",
        detail ? dataTonePanelClass[detail.statusTone] : "border-border/70"
      )}
    >
      {detail ? (
        <>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="min-w-0">
              <div className="eyebrow-label">{detail.statusLabel}</div>
              <h3 className="mt-1 break-words text-sm font-semibold text-foreground">{detail.title}</h3>
              <p className="mt-1 font-mono text-xs text-muted-foreground">{detail.subtitle}</p>
            </div>
            <span className={cn("rounded-sm border px-2 py-1 font-mono text-[10px] font-medium uppercase tracking-[0.14em]", dataTonePanelClass[detail.statusTone], dataToneClass[detail.statusTone])}>
              Detail
            </span>
          </div>
          <p className="mt-2 text-xs leading-5 text-foreground/80">{detail.detail}</p>
          <dl className="mt-3 grid gap-2 sm:grid-cols-2">
            {detail.fields.map((field) => (
              <div key={field.label} className="grid grid-cols-[minmax(0,0.8fr)_minmax(0,1fr)] items-start gap-3 rounded-md border border-border/60 bg-secondary/20 px-3 py-2">
                <dt className="text-xs text-muted-foreground">{field.label}</dt>
                <dd className={cn("break-words text-right font-mono text-xs", dataToneClass[field.tone])}>{field.value}</dd>
              </div>
            ))}
          </dl>
        </>
      ) : (
        <div role="status" className="text-sm text-muted-foreground">{emptyText}</div>
      )}
    </DenseRowDetailPanel>
  );
}

function Stat({ label, value, tone }: { label: string; value: string; tone?: string }) {
  return (
    <div className="workspace-header-card p-4">
      <div className="text-xs font-medium uppercase tracking-[0.16em] text-muted-foreground">{label}</div>
      <div className={cn("mt-2 font-mono text-sm font-semibold text-foreground", tone)}>{value}</div>
    </div>
  );
}

/** Shared key-value row used in brokerage wiring and route context panels. */
function KeyValueRow({ label, value, tone }: { label: string; value: string; tone?: string }) {
  return (
    <div className="data-grid-surface flex items-center justify-between gap-4 px-3 py-2">
      <span className="text-muted-foreground">{label}</span>
      <span className={cn("font-mono text-foreground", tone)}>{value}</span>
    </div>
  );
}

function CockpitChip({ label, value }: { label: string; value: string }) {
  return (
    <span className="toolbar-chip" aria-label={`${label}: ${value}`}>
      <span className="text-muted-foreground">{label}</span>
      <span className="font-mono text-foreground">{value}</span>
    </span>
  );
}

function WorkflowPanelButton({
  command,
  onOpen
}: {
  command: TradingWorkflowCommandState;
  onOpen: () => void;
}) {
  const Icon = command.icon === "strategy" ? PlayCircle : command.icon === "replay" ? RotateCcw : FlaskConical;

  return (
    <Button
      size="sm"
      variant={command.active ? "secondary" : "outline"}
      aria-label={command.ariaLabel}
      aria-expanded={command.expanded}
      aria-controls={command.controlsId}
      onClick={onOpen}
    >
      <Icon className="mr-2 h-4 w-4" aria-hidden="true" />
      {command.label}
    </Button>
  );
}

const orderPreviewWarningTone: Record<OrderPreviewLevel, string> = {
  info: "border-border/70 bg-secondary/30 text-muted-foreground",
  warning: "border-warning/30 bg-warning/10 text-warning",
  danger: "border-danger/30 bg-danger/10 text-danger"
};

const orderPreviewEffectTone: Record<OrderPreviewEffect, string> = {
  "open-long": "text-success",
  "open-short": "text-warning",
  "add-long": "text-success",
  "add-short": "text-warning",
  "reduce-long": "text-muted-foreground",
  "reduce-short": "text-muted-foreground",
  "close-long": "text-foreground",
  "close-short": "text-foreground",
  "flip-to-short": "text-danger",
  "flip-to-long": "text-danger"
};

function OrderPreviewPanel({ preview }: { preview: OrderPreview }) {
  const effectTone = preview.effect ? orderPreviewEffectTone[preview.effect] : "text-muted-foreground";
  return (
    <section
      role="region"
      aria-label="Order impact preview"
      aria-live="polite"
      className="rounded-lg border border-border/60 bg-secondary/15 p-3 text-xs"
      data-testid="order-preview"
    >
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <div className="text-[11px] font-medium uppercase tracking-[0.14em] text-muted-foreground">
          Order impact preview
        </div>
        <div className={cn("font-semibold", effectTone)} data-testid="order-preview-effect">
          {preview.effectLabel}
        </div>
      </div>

      <p className="sr-only">{preview.ariaSummary}</p>

      <dl className="mt-2 grid gap-2 sm:grid-cols-3">
        <PreviewStat label="Estimated notional" value={preview.notionalLabel} mono />
        <PreviewStat
          label={preview.priceSourceLabel}
          value={preview.referencePriceLabel}
          mono
        />
        <PreviewStat label="Resulting position" value={preview.resultingPositionLabel} />
      </dl>

      <p className="mt-2 text-muted-foreground" data-testid="order-preview-detail">
        {preview.effectDetail}
      </p>

      {preview.riskNote && (
        <p className="mt-1 text-muted-foreground">{preview.riskNote}</p>
      )}

      {preview.warnings.length > 0 && (
        <ul className="mt-2 space-y-1" data-testid="order-preview-warnings">
          {preview.warnings.map((warning) => (
            <OrderPreviewWarningRow key={warning.id} warning={warning} />
          ))}
        </ul>
      )}
    </section>
  );
}

function PreviewStat({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div>
      <dt className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">{label}</dt>
      <dd className={cn("text-foreground", mono && "font-mono")}>{value}</dd>
    </div>
  );
}

function OrderPreviewWarningRow({ warning }: { warning: OrderPreviewWarning }) {
  return (
    <li
      className={cn("rounded-md border px-2 py-1.5 text-xs", orderPreviewWarningTone[warning.level])}
      role={warning.level === "danger" ? "alert" : undefined}
    >
      {warning.message}
    </li>
  );
}
