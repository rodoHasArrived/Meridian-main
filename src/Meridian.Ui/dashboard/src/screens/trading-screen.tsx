import { Activity, AlertTriangle, Cable, CandlestickChart, CheckCircle, ClipboardList, FastForward, Layers, PauseCircle, PlayCircle, PlusCircle, RadioTower, RotateCcw, ShieldCheck, StopCircle, Trash2, Wallet, XCircle } from "lucide-react";
import React, { useEffect, useMemo, useState } from "react";
import { useLocation } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle
} from "@/components/ui/dialog";
import { MetricCard } from "@/components/meridian/metric-card";
import { approvePromotion, cancelAllOrders, cancelOrder, closePosition, closePaperSession, createPaperSession, evaluatePromotion, getExecutionAudit, getExecutionSessions, getPaperSessionDetail, getPaperSessionReplayVerification, getPromotionHistory, getReplayFiles, getReplayStatus, pauseReplay, pauseStrategy, resumeReplay, seekReplay, setReplaySpeed as apiSetReplaySpeed, startReplay, stopReplay, stopStrategy, submitOrder } from "@/lib/api";
import { cn } from "@/lib/utils";
import type { ExecutionAuditEntry, OrderSubmitRequest, PaperSessionDetail, PaperSessionReplayVerification, PaperSessionSummary, PromotionEvaluationResult, PromotionRecord, ReplayFileRecord, ReplayStatus, TradingActionResult, TradingWorkspaceResponse } from "@/types";

interface TradingScreenProps {
  data: TradingWorkspaceResponse | null;
}

const riskTone: Record<TradingWorkspaceResponse["risk"]["state"], string> = {
  Healthy: "text-success",
  Observe: "text-warning",
  Constrained: "text-danger"
};

const wiringTone: Record<TradingWorkspaceResponse["brokerage"]["connection"], string> = {
  Connected: "text-success",
  Degraded: "text-warning",
  Disconnected: "text-danger"
};

const focusCopy: Record<string, { title: string; description: string }> = {
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

type OrderPhase = "idle" | "submitting" | "submitted" | "error";

interface OrderState {
  phase: OrderPhase;
  orderId: string | null;
  error: string | null;
}

// --- Shared confirmation state for all write actions ---

type ConfirmActionType =
  | { kind: "cancel-order"; orderId: string }
  | { kind: "cancel-all" }
  | { kind: "close-position"; symbol: string }
  | { kind: "pause-strategy"; strategyId: string }
  | { kind: "stop-strategy"; strategyId: string };

interface ConfirmState {
  action: ConfirmActionType | null;
  busy: boolean;
  result: TradingActionResult | null;
  error: string | null;
}

export function TradingScreen({ data }: TradingScreenProps) {
  const { pathname } = useLocation();
  const workstream = useMemo(() => {
    if (pathname.includes("/positions")) return "positions";
    if (pathname.includes("/risk")) return "risk";
    return "orders";
  }, [pathname]);

  const [showOrderForm, setShowOrderForm] = useState(false);
  const [orderForm, setOrderForm] = useState<OrderSubmitRequest>({
    symbol: "",
    side: "Buy",
    type: "Market",
    quantity: 0,
    limitPrice: null
  });
  const [orderState, setOrderState] = useState<OrderState>({ phase: "idle", orderId: null, error: null });

  // --- Shared confirmation dialog state ---
  const [confirm, setConfirm] = useState<ConfirmState>({
    action: null,
    busy: false,
    result: null,
    error: null
  });

  function openConfirm(action: ConfirmActionType) {
    setConfirm({ action, busy: false, result: null, error: null });
  }

  function closeConfirm() {
    if (confirm.busy) return;
    setConfirm({ action: null, busy: false, result: null, error: null });
  }

  async function executeConfirmedAction() {
    const { action } = confirm;
    if (!action) return;
    setConfirm((prev) => ({ ...prev, busy: true, result: null, error: null }));

    try {
      let result: TradingActionResult;
      if (action.kind === "cancel-order") {
        result = await cancelOrder(action.orderId);
      } else if (action.kind === "cancel-all") {
        result = await cancelAllOrders();
      } else if (action.kind === "close-position") {
        result = await closePosition(action.symbol);
      } else if (action.kind === "pause-strategy") {
        const raw = await pauseStrategy(action.strategyId);
        result = {
          actionId: `act-${Date.now()}`,
          status: raw.success ? "Completed" : "Rejected",
          message: raw.reason ?? (raw.success ? "Strategy paused." : "Pause rejected."),
          occurredAt: new Date().toISOString()
        };
      } else {
        const raw = await stopStrategy(action.strategyId);
        result = {
          actionId: `act-${Date.now()}`,
          status: raw.success ? "Completed" : "Rejected",
          message: raw.reason ?? (raw.success ? "Strategy stopped." : "Stop rejected."),
          occurredAt: new Date().toISOString()
        };
      }
      setConfirm((prev) => ({ ...prev, busy: false, result }));
    } catch (err) {
      setConfirm((prev) => ({
        ...prev,
        busy: false,
        error: err instanceof Error ? err.message : "Action failed."
      }));
    }
  }

  // --- Paper session management ---
  const [sessions, setSessions] = useState<PaperSessionSummary[]>([]);
  const [sessionLoading, setSessionLoading] = useState(false);
  const [sessionError, setSessionError] = useState<string | null>(null);
  const [showSessionForm, setShowSessionForm] = useState(false);
  const [newSessionStrategyId, setNewSessionStrategyId] = useState("");
  const [newSessionCash, setNewSessionCash] = useState("100000");

  // --- Strategy lifecycle ---
  const [strategyId, setStrategyId] = useState("");
  const [selectedSessionId, setSelectedSessionId] = useState<string | null>(null);
  const [selectedSessionDetail, setSelectedSessionDetail] = useState<PaperSessionDetail | null>(null);
  const [sessionReplayVerification, setSessionReplayVerification] = useState<PaperSessionReplayVerification | null>(null);
  const [executionAudit, setExecutionAudit] = useState<ExecutionAuditEntry[]>([]);

  const [replayFiles, setReplayFiles] = useState<ReplayFileRecord[]>([]);
  const [selectedReplayFile, setSelectedReplayFile] = useState("");
  const [replayStatus, setReplayStatus] = useState<ReplayStatus | null>(null);
  const [replayError, setReplayError] = useState<string | null>(null);
  const [replaySpeed, setReplaySpeed] = useState("1");
  const [seekMs, setSeekMs] = useState("0");

  const [promotionRunId, setPromotionRunId] = useState("");
  const [promotionApprovedBy, setPromotionApprovedBy] = useState("");
  const [promotionApprovalReason, setPromotionApprovalReason] = useState("");
  const [promotionReviewNotes, setPromotionReviewNotes] = useState("");
  const [promotionManualOverrideId, setPromotionManualOverrideId] = useState("");
  const [promotionEval, setPromotionEval] = useState<PromotionEvaluationResult | null>(null);
  const [promotionHistory, setPromotionHistory] = useState<PromotionRecord[]>([]);
  const [promotionError, setPromotionError] = useState<string | null>(null);
  const [promotionBusy, setPromotionBusy] = useState(false);
  const [promotionResult, setPromotionResult] = useState<string | null>(null);

  async function refreshExecutionAudit() {
    try {
      const entries = await getExecutionAudit(8);
      setExecutionAudit(entries);
    } catch {
      setExecutionAudit([]);
    }
  }

  useEffect(() => {
    getExecutionSessions()
      .then(setSessions)
      .catch(() => { /* sessions unavailable — silently skip */ });
    getReplayFiles()
      .then((result) => {
        setReplayFiles(result.files);
        if (result.files.length > 0) {
          setSelectedReplayFile(result.files[0].path);
        }
      })
      .catch(() => { /* replay unavailable */ });
    getPromotionHistory()
      .then(setPromotionHistory)
      .catch(() => { /* history unavailable */ });
    void refreshExecutionAudit();
  }, []);

  async function handleSubmitOrder(e: React.FormEvent) {
    e.preventDefault();
    setOrderState({ phase: "submitting", orderId: null, error: null });
    try {
      const result = await submitOrder(orderForm);
      if (result.success) {
        setOrderState({ phase: "submitted", orderId: result.orderId, error: null });
        setShowOrderForm(false);
        setOrderForm({ symbol: "", side: "Buy", type: "Market", quantity: 0, limitPrice: null });
      } else {
        setOrderState({ phase: "error", orderId: null, error: result.reason ?? "Order failed." });
      }
    } catch (err) {
      setOrderState({
        phase: "error",
        orderId: null,
        error: err instanceof Error ? err.message : "Order submission failed."
      });
    }
  }

  async function handleCreateSession(e: React.FormEvent) {
    e.preventDefault();
    setSessionLoading(true);
    setSessionError(null);
    try {
      const sid = newSessionStrategyId.trim() || `strat-${Date.now()}`;
      const cash = parseFloat(newSessionCash) || 100_000;
      const summary = await createPaperSession(sid, null, cash);
      setSessions((prev) => [summary, ...prev]);
      setShowSessionForm(false);
      setNewSessionStrategyId("");
      setNewSessionCash("100000");
    } catch (err) {
      setSessionError(err instanceof Error ? err.message : "Failed to create session.");
    } finally {
      setSessionLoading(false);
    }
  }

  async function handleCloseSession(sessionId: string) {
    setSessionError(null);
    try {
      const result = await closePaperSession(sessionId);
      setSessions((prev) => prev.map((session) => (
        session.sessionId === sessionId
          ? { ...session, closedAt: result.occurredAt, isActive: false }
          : session
      )));
      setSelectedSessionDetail((prev) => {
        if (!prev || prev.summary.sessionId !== sessionId) {
          return prev;
        }

        return {
          ...prev,
          summary: {
            ...prev.summary,
            closedAt: result.occurredAt,
            isActive: false
          }
        };
      });
      await refreshExecutionAudit();
    } catch (err) {
      setSessionError(err instanceof Error ? err.message : "Failed to close session.");
    }
  }

  async function handleRestoreSession(sessionId: string) {
    setSessionError(null);
    try {
      const detail = await getPaperSessionDetail(sessionId);
      setSelectedSessionId(sessionId);
      setSelectedSessionDetail(detail);
      setSessionReplayVerification(null);
    } catch (err) {
      setSessionError(err instanceof Error ? err.message : "Failed to restore session.");
    }
  }

  async function handleVerifySessionReplay(sessionId: string) {
    setSessionError(null);
    try {
      const [detail, verification] = await Promise.all([
        getPaperSessionDetail(sessionId),
        getPaperSessionReplayVerification(sessionId)
      ]);
      setSelectedSessionId(sessionId);
      setSelectedSessionDetail(detail);
      setSessionReplayVerification(verification);
      await refreshExecutionAudit();
    } catch (err) {
      setSessionError(err instanceof Error ? err.message : "Failed to verify session replay.");
    }
  }

  async function handleStartReplay() {
    if (!selectedReplayFile) return;
    setReplayError(null);
    try {
      const started = await startReplay(selectedReplayFile, Number(replaySpeed) || 1);
      const status = await getReplayStatus(started.sessionId);
      setReplayStatus(status);
    } catch (err) {
      setReplayError(err instanceof Error ? err.message : "Failed to start replay.");
    }
  }

  async function handleReplayControl(action: "pause" | "resume" | "stop" | "seek" | "speed") {
    if (!replayStatus) return;
    setReplayError(null);
    try {
      if (action === "pause") await pauseReplay(replayStatus.sessionId);
      if (action === "resume") await resumeReplay(replayStatus.sessionId);
      if (action === "stop") await stopReplay(replayStatus.sessionId);
      if (action === "seek") await seekReplay(replayStatus.sessionId, Number(seekMs) || 0);
      if (action === "speed") await apiSetReplaySpeed(replayStatus.sessionId, Number(replaySpeed) || 1);
      if (action === "stop") {
        setReplayStatus(null);
      } else {
        const status = await getReplayStatus(replayStatus.sessionId);
        setReplayStatus(status);
      }
    } catch (err) {
      setReplayError(err instanceof Error ? err.message : "Replay action failed.");
    }
  }

  async function handleEvaluatePromotion() {
    if (!promotionRunId.trim()) return;
    setPromotionBusy(true);
    setPromotionError(null);
    setPromotionResult(null);
    try {
      const evaluation = await evaluatePromotion(promotionRunId.trim());
      setPromotionEval(evaluation);
    } catch (err) {
      setPromotionError(err instanceof Error ? err.message : "Evaluation failed.");
    } finally {
      setPromotionBusy(false);
    }
  }

  async function handlePromoteToPaper() {
    if (!promotionEval?.isEligible || !promotionRunId.trim() || !promotionApprovedBy.trim() || !promotionApprovalReason.trim()) {
      setPromotionError("Approver and approval reason are required.");
      return;
    }
    setPromotionBusy(true);
    setPromotionError(null);
    try {
      const result = await approvePromotion({
        runId: promotionRunId.trim(),
        approvedBy: promotionApprovedBy.trim(),
        approvalReason: promotionApprovalReason.trim(),
        reviewNotes: promotionReviewNotes.trim() || undefined,
        manualOverrideId: promotionManualOverrideId.trim() || undefined
      });
      setPromotionResult(result.success ? `Promoted. Promotion ID: ${result.promotionId ?? "n/a"}` : result.reason);
      const history = await getPromotionHistory();
      setPromotionHistory(history);
    } catch (err) {
      setPromotionError(err instanceof Error ? err.message : "Promotion approval failed.");
    } finally {
      setPromotionBusy(false);
    }
  }

  if (!data) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Loading trading cockpit</CardTitle>
          <CardDescription>Waiting for paper-trading state, order flow, and brokerage wiring snapshots.</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  return (
    <div className="space-y-8">
      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {data.metrics.map((metric) => (
          <MetricCard key={metric.id} {...metric} />
        ))}
      </section>

      <section className="grid gap-4 xl:grid-cols-[1.25fr_0.75fr]">
        <Card>
          <CardHeader>
            <div className="eyebrow-label">Trading Lane</div>
            <CardTitle className="flex items-center gap-2">
              <RadioTower className="h-5 w-5 text-primary" />
              {focusCopy[workstream].title}
            </CardTitle>
            <CardDescription>{focusCopy[workstream].description}</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 md:grid-cols-3">
            <TradingHighlight
              icon={ClipboardList}
              title="Blotter management"
              description="Working and partial orders stay visible so you can act on fill progress without context-switching."
            />
            <TradingHighlight
              icon={Wallet}
              title="Position exposure"
              description="Live exposure, marks, and unrealized P&L for every open position in the active paper session."
            />
            <TradingHighlight
              icon={ShieldCheck}
              title="Guardrail state"
              description="Paper thresholds and drawdown limits are evaluated on every order and surfaced here for review."
            />
          </CardContent>
        </Card>

        <Card className="bg-panel-strong text-slate-50">
          <CardHeader>
            <div className="eyebrow-label">Route Context</div>
            <CardTitle>Current workstream</CardTitle>
            <CardDescription className="text-slate-300">
              Deep links under{" "}
              <code className="rounded bg-white/10 px-1 py-0.5 text-xs text-foreground">{pathname}</code>{" "}
              reuse the same prefetched cockpit payload.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-3 text-sm text-slate-200">
            <ContextRow label="Open positions" value={String(data.positions.length)} />
            <ContextRow label="Working orders" value={String(data.openOrders.length)} />
            <ContextRow label="Completed fills" value={String(data.fills.length)} />
            <ContextRow label="Risk state" value={data.risk.state} />
          </CardContent>
        </Card>
      </section>

      <section className="grid gap-4 xl:grid-cols-[1.1fr_0.9fr]">
        <Card>
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
              <ul className="list-disc space-y-1 pl-6 text-sm text-foreground">
                {data.risk.activeGuardrails.map((guardrail) => (
                  <li key={guardrail}>{guardrail}</li>
                ))}
              </ul>
            </div>
          </CardContent>
        </Card>

        <Card className="bg-panel-strong text-slate-50">
          <CardHeader>
            <div className="eyebrow-label">Brokerage Wiring</div>
            <CardTitle className="flex items-center gap-2">
              <Cable className="h-5 w-5 text-primary" />
              Execution adapter health
            </CardTitle>
            <CardDescription className="text-slate-300">{data.brokerage.notes}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3 text-sm">
            <WiringRow label="Provider" value={data.brokerage.provider} />
            <WiringRow label="Account" value={data.brokerage.account} />
            <WiringRow label="Environment" value={data.brokerage.environment.toUpperCase()} />
            <WiringRow label="Connection" value={data.brokerage.connection} tone={wiringTone[data.brokerage.connection]} />
            <WiringRow label="Last heartbeat" value={data.brokerage.lastHeartbeat} />
            <WiringRow label="Order ingress" value={data.brokerage.orderIngress} />
            <WiringRow label="Fill feed" value={data.brokerage.fillFeed} />
          </CardContent>
        </Card>
      </section>

      <section className="grid gap-4 xl:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <Wallet className="h-4 w-4 text-primary" />
              Live positions
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="overflow-x-auto rounded-xl border border-border/70">
              <table className="min-w-full divide-y divide-border/60 text-left text-xs sm:text-sm">
                <thead className="bg-secondary/30">
                  <tr>
                    {["Symbol", "Side", "Qty", "Avg", "Mark", "Day P&L", "Unrealized", "Exposure", ""].map((col) => (
                      <th key={col} className="px-3 py-2 font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                        {col}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-border/50">
                  {data.positions.map((position, i) => (
                    <tr key={`pos-${i}`} className="bg-background/20">
                      <td className="px-3 py-2 font-mono text-foreground">{position.symbol}</td>
                      <td className="px-3 py-2 font-mono text-foreground">{position.side}</td>
                      <td className="px-3 py-2 font-mono text-foreground">{position.quantity}</td>
                      <td className="px-3 py-2 font-mono text-foreground">{position.averagePrice}</td>
                      <td className="px-3 py-2 font-mono text-foreground">{position.markPrice}</td>
                      <td className="px-3 py-2 font-mono text-foreground">{position.dayPnl}</td>
                      <td className="px-3 py-2 font-mono text-foreground">{position.unrealizedPnl}</td>
                      <td className="px-3 py-2 font-mono text-foreground">{position.exposure}</td>
                      <td className="px-3 py-2">
                        <button
                          type="button"
                          onClick={() => openConfirm({ kind: "close-position", symbol: position.symbol })}
                          className="rounded px-2 py-1 text-xs text-muted-foreground hover:text-danger hover:bg-danger/10 transition-colors"
                          title="Close position"
                        >
                          Close
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <div className="flex items-center justify-between gap-3">
              <CardTitle className="flex items-center gap-2 text-base">
                <ClipboardList className="h-4 w-4 text-primary" />
                Open orders
              </CardTitle>
              <div className="flex items-center gap-2">
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() => openConfirm({ kind: "cancel-all" })}
                  disabled={data.openOrders.length === 0}
                  title="Cancel all open orders"
                >
                  <Trash2 className="mr-2 h-4 w-4" />
                  Cancel all
                </Button>
                <Button size="sm" variant="outline" onClick={() => setShowOrderForm((prev) => !prev)}>
                  <PlusCircle className="mr-2 h-4 w-4" />
                  New order
                </Button>
              </div>
            </div>
          </CardHeader>
          {showOrderForm && (
            <CardContent className="border-b border-border/60 pb-6">
              <form onSubmit={handleSubmitOrder} className="space-y-4">
                <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
                  <div className="space-y-1">
                    <label className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">Symbol</label>
                    <input
                      type="text"
                      placeholder="AAPL"
                      value={orderForm.symbol}
                      onChange={(e) => setOrderForm((prev) => ({ ...prev, symbol: e.target.value }))}
                      onBlur={(e) => setOrderForm((prev) => ({ ...prev, symbol: e.target.value.toUpperCase() }))}
                      className="w-full rounded-lg border border-border bg-background px-3 py-2 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-primary/40"
                      required
                    />
                  </div>
                  <div className="space-y-1">
                    <label className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">Side</label>
                    <select
                      value={orderForm.side}
                      onChange={(e) => setOrderForm((prev) => ({ ...prev, side: e.target.value as "Buy" | "Sell" }))}
                      className="w-full rounded-lg border border-border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary/40"
                    >
                      <option value="Buy">Buy</option>
                      <option value="Sell">Sell</option>
                    </select>
                  </div>
                  <div className="space-y-1">
                    <label className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">Type</label>
                    <select
                      value={orderForm.type}
                      onChange={(e) => setOrderForm((prev) => ({ ...prev, type: e.target.value as "Market" | "Limit" | "Stop" }))}
                      className="w-full rounded-lg border border-border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary/40"
                    >
                      <option value="Market">Market</option>
                      <option value="Limit">Limit</option>
                      <option value="Stop">Stop</option>
                    </select>
                  </div>
                  <div className="space-y-1">
                    <label className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">Quantity</label>
                    <input
                      type="number"
                      min={1}
                      step={1}
                      value={orderForm.quantity || ""}
                      onChange={(e) => setOrderForm((prev) => ({ ...prev, quantity: Number(e.target.value) }))}
                      className="w-full rounded-lg border border-border bg-background px-3 py-2 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-primary/40"
                      required
                    />
                  </div>
                  {(orderForm.type === "Limit" || orderForm.type === "Stop") && (
                    <div className="space-y-1">
                      <label className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                        {orderForm.type} Price
                      </label>
                      <input
                        type="number"
                        min={0}
                        step={0.01}
                        value={orderForm.limitPrice ?? ""}
                        onChange={(e) => setOrderForm((prev) => ({ ...prev, limitPrice: e.target.value ? Number(e.target.value) : null }))}
                        className="w-full rounded-lg border border-border bg-background px-3 py-2 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-primary/40"
                        required
                      />
                    </div>
                  )}
                </div>

                {orderState.error && (
                  <div className="rounded-lg border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive flex items-center gap-2">
                    <XCircle className="h-4 w-4 shrink-0" />
                    {orderState.error}
                  </div>
                )}

                {orderState.phase === "submitted" && (
                  <div className="rounded-lg border border-success/30 bg-success/10 px-4 py-3 text-sm text-success flex items-center gap-2">
                    <CheckCircle className="h-4 w-4 shrink-0" />
                    Order submitted{orderState.orderId ? ` — ${orderState.orderId}` : ""}.
                  </div>
                )}

                <div className="flex gap-3">
                  <Button type="submit" size="sm" disabled={orderState.phase === "submitting"}>
                    {orderState.phase === "submitting" ? "Submitting…" : "Submit order"}
                  </Button>
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    onClick={() => {
                      setShowOrderForm(false);
                      setOrderState({ phase: "idle", orderId: null, error: null });
                    }}
                  >
                    Cancel
                  </Button>
                </div>
              </form>
            </CardContent>
          )}
          <CardContent>
            <div className="overflow-x-auto rounded-xl border border-border/70">
              <table className="min-w-full divide-y divide-border/60 text-left text-xs sm:text-sm">
                <thead className="bg-secondary/30">
                  <tr>
                    {["Order", "Symbol", "Side", "Type", "Qty", "Limit", "Status", "Submitted", ""].map((col) => (
                      <th key={col} className="px-3 py-2 font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                        {col}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-border/50">
                  {data.openOrders.map((order, i) => (
                    <tr key={`order-${i}`} className="bg-background/20">
                      <td className="px-3 py-2 font-mono text-foreground">{order.orderId}</td>
                      <td className="px-3 py-2 font-mono text-foreground">{order.symbol}</td>
                      <td className="px-3 py-2 font-mono text-foreground">{order.side}</td>
                      <td className="px-3 py-2 font-mono text-foreground">{order.type}</td>
                      <td className="px-3 py-2 font-mono text-foreground">{order.quantity}</td>
                      <td className="px-3 py-2 font-mono text-foreground">{order.limitPrice}</td>
                      <td className="px-3 py-2 font-mono text-foreground">{order.status}</td>
                      <td className="px-3 py-2 font-mono text-foreground">{order.submittedAt}</td>
                      <td className="px-3 py-2">
                        <button
                          type="button"
                          onClick={() => openConfirm({ kind: "cancel-order", orderId: order.orderId })}
                          className="rounded px-2 py-1 text-xs text-muted-foreground hover:text-danger hover:bg-danger/10 transition-colors"
                          title="Cancel order"
                        >
                          Cancel
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </CardContent>
        </Card>
      </section>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <CandlestickChart className="h-4 w-4 text-primary" />
            Recent fills
          </CardTitle>
        </CardHeader>
        <CardContent>
          <TradingTable
            columns={["Fill", "Order", "Symbol", "Side", "Qty", "Price", "Venue", "Timestamp"]}
            rows={data.fills.map((fill) => [
              fill.fillId,
              fill.orderId,
              fill.symbol,
              fill.side,
              fill.quantity,
              fill.price,
              fill.venue,
              fill.timestamp
            ])}
          />
        </CardContent>
      </Card>

      <section className="grid gap-4 xl:grid-cols-2">
        {/* Paper session management */}
        <Card>
          <CardHeader>
            <div className="flex items-center justify-between">
              <CardTitle className="flex items-center gap-2 text-base">
                <Layers className="h-4 w-4 text-primary" />
                Paper sessions
              </CardTitle>
              <Button size="sm" variant="outline" onClick={() => setShowSessionForm((prev) => !prev)}>
                <PlusCircle className="mr-2 h-4 w-4" />
                New session
              </Button>
            </div>
            <CardDescription>Manage paper trading sessions and initial capital allocation.</CardDescription>
          </CardHeader>

          {sessionError && (
            <CardContent className="pt-0 pb-2">
              <div className="rounded-lg border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive flex items-center gap-2">
                <XCircle className="h-4 w-4 shrink-0" />
                {sessionError}
              </div>
            </CardContent>
          )}

          {showSessionForm && (
            <CardContent className="border-b border-border/60 pb-6">
              <form onSubmit={handleCreateSession} className="space-y-4">
                <div className="grid gap-3 sm:grid-cols-2">
                  <div className="space-y-1">
                    <label className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                      Strategy ID
                    </label>
                    <input
                      type="text"
                      placeholder="my-strategy-01"
                      value={newSessionStrategyId}
                      onChange={(e) => setNewSessionStrategyId(e.target.value)}
                      className="w-full rounded-lg border border-border bg-background px-3 py-2 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-primary/40"
                    />
                  </div>
                  <div className="space-y-1">
                    <label className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                      Initial cash ($)
                    </label>
                    <input
                      type="number"
                      min={1000}
                      step={1000}
                      value={newSessionCash}
                      onChange={(e) => setNewSessionCash(e.target.value)}
                      className="w-full rounded-lg border border-border bg-background px-3 py-2 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-primary/40"
                      required
                    />
                  </div>
                </div>
                <div className="flex gap-3">
                  <Button type="submit" size="sm" disabled={sessionLoading}>
                    {sessionLoading ? "Creating…" : "Create session"}
                  </Button>
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    onClick={() => setShowSessionForm(false)}
                  >
                    Cancel
                  </Button>
                </div>
              </form>
            </CardContent>
          )}

          <CardContent>
            {sessions.length === 0 ? (
              <p className="text-sm text-muted-foreground py-4 text-center">
                No paper sessions active. Create one above to start tracking execution.
              </p>
            ) : (
              <div className="space-y-2">
                {sessions.map((session) => (
                  <div
                    key={session.sessionId}
                    className="flex items-center justify-between rounded-lg border border-border/70 bg-secondary/20 px-4 py-3"
                  >
                    <div className="min-w-0 flex-1">
                      <div className="font-mono text-sm text-foreground truncate">{session.sessionId}</div>
                      <div className="text-xs text-muted-foreground mt-0.5">
                        {session.strategyId} · {formatUsd(session.initialCash)} · {getSessionStatus(session)}
                      </div>
                    </div>
                    <div className="ml-4 flex shrink-0 gap-2">
                      <Button size="sm" variant="outline" onClick={() => handleRestoreSession(session.sessionId)}>
                        Restore
                      </Button>
                      <Button size="sm" variant="outline" onClick={() => handleVerifySessionReplay(session.sessionId)}>
                        Verify replay
                      </Button>
                      {session.isActive && (
                        <Button
                          size="sm"
                          variant="outline"
                          onClick={() => handleCloseSession(session.sessionId)}
                        >
                          Close
                        </Button>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            )}
            {selectedSessionId && (
              <p className="mt-3 text-xs text-muted-foreground">Selected session: {selectedSessionId}</p>
            )}
            {selectedSessionDetail && (
              <div className="mt-4 space-y-3 rounded-lg border border-border/70 bg-background/70 p-4">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Session detail</p>
                    <p className="mt-1 font-mono text-sm text-foreground">{selectedSessionDetail.summary.sessionId}</p>
                  </div>
                  <span
                    className={cn(
                      "rounded-full px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.14em]",
                      selectedSessionDetail.summary.isActive
                        ? "bg-success/10 text-success"
                        : "bg-secondary text-muted-foreground"
                    )}
                  >
                    {getSessionStatus(selectedSessionDetail.summary)}
                  </span>
                </div>

                <div className="grid gap-3 sm:grid-cols-2">
                  <SessionInfoRow label="Strategy" value={selectedSessionDetail.summary.strategyId} />
                  <SessionInfoRow label="Initial cash" value={formatUsd(selectedSessionDetail.summary.initialCash)} />
                  <SessionInfoRow
                    label="Tracked symbols"
                    value={selectedSessionDetail.symbols.length > 0 ? selectedSessionDetail.symbols.join(", ") : "None"}
                  />
                  <SessionInfoRow
                    label="Orders retained"
                    value={String(selectedSessionDetail.orderHistory?.length ?? 0)}
                  />
                </div>

                {selectedSessionDetail.portfolio && (
                  <div className="grid gap-3 sm:grid-cols-3">
                    <SessionMetric label="Cash" value={formatUsd(selectedSessionDetail.portfolio.cash)} />
                    <SessionMetric label="Portfolio value" value={formatUsd(selectedSessionDetail.portfolio.portfolioValue)} />
                    <SessionMetric label="Open positions" value={String(selectedSessionDetail.portfolio.positions.length)} />
                  </div>
                )}

                {sessionReplayVerification && sessionReplayVerification.summary.sessionId === selectedSessionDetail.summary.sessionId && (
                  <div
                    className={cn(
                      "rounded-lg border px-3 py-3 text-sm",
                      sessionReplayVerification.isConsistent
                        ? "border-success/30 bg-success/10"
                        : "border-warning/30 bg-warning/10"
                    )}
                  >
                    <div className="flex items-center justify-between gap-3">
                      <span className="font-semibold text-foreground">Replay verification</span>
                      <span className={sessionReplayVerification.isConsistent ? "text-success" : "text-warning"}>
                        {sessionReplayVerification.isConsistent ? "Matched current state" : "Mismatch detected"}
                      </span>
                    </div>
                    <p className="mt-1 text-xs text-muted-foreground">
                      Source: {sessionReplayVerification.replaySource} · Verified at {sessionReplayVerification.verifiedAt}
                    </p>
                    {sessionReplayVerification.mismatchReasons.length > 0 && (
                      <ul className="mt-2 space-y-1 text-xs text-foreground">
                        {sessionReplayVerification.mismatchReasons.slice(0, 3).map((reason) => (
                          <li key={reason}>• {reason}</li>
                        ))}
                      </ul>
                    )}
                  </div>
                )}
              </div>
            )}
            <div className="mt-4 rounded-lg border border-border/70 bg-secondary/20 p-4">
              <p className="mb-2 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Recent execution audit</p>
              {executionAudit.length === 0 ? (
                <p className="text-xs text-muted-foreground">No execution audit entries available.</p>
              ) : (
                <div className="space-y-2">
                  {executionAudit.map((entry) => (
                    <div key={entry.auditId} className="rounded-lg border border-border/60 bg-background/70 px-3 py-2">
                      <div className="flex items-center justify-between gap-3 text-xs">
                        <span className="font-semibold text-foreground">{entry.action}</span>
                        <span className="font-mono text-muted-foreground">{entry.outcome}</span>
                      </div>
                      <p className="mt-1 text-xs text-muted-foreground">
                        {entry.message ?? "No operator message recorded."}
                      </p>
                      <p className="mt-1 font-mono text-[11px] text-muted-foreground">
                        {entry.occurredAt}
                        {entry.metadata?.sessionId ? ` · session ${entry.metadata.sessionId}` : ""}
                      </p>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </CardContent>
        </Card>

        {/* Strategy lifecycle controls */}
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <PlayCircle className="h-4 w-4 text-primary" />
              Strategy lifecycle
            </CardTitle>
            <CardDescription>
              Pause or stop a running strategy by its registered ID. Changes take effect immediately.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-1">
              <label className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                Strategy ID
              </label>
              <input
                type="text"
                placeholder="e.g. mean-reversion-fx-01"
                value={strategyId}
                onChange={(e) => setStrategyId(e.target.value)}
                className="w-full rounded-lg border border-border bg-background px-3 py-2 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-primary/40"
              />
            </div>
            <div className="flex gap-3">
              <Button
                size="sm"
                variant="outline"
                onClick={() => openConfirm({ kind: "pause-strategy", strategyId: strategyId.trim() })}
                disabled={!strategyId.trim()}
              >
                <PauseCircle className="mr-2 h-4 w-4" />
                Pause
              </Button>
              <Button
                size="sm"
                variant="outline"
                onClick={() => openConfirm({ kind: "stop-strategy", strategyId: strategyId.trim() })}
                disabled={!strategyId.trim()}
              >
                <StopCircle className="mr-2 h-4 w-4" />
                Stop
              </Button>
            </div>
          </CardContent>
        </Card>
      </section>

      <section className="grid gap-4 xl:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <RotateCcw className="h-4 w-4 text-primary" />
              Session replay controls
            </CardTitle>
            <CardDescription>Start and control replay for reconnect/resume validation.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            <select
              aria-label="Replay file"
              value={selectedReplayFile}
              onChange={(e) => setSelectedReplayFile(e.target.value)}
              className="w-full rounded-lg border border-border bg-background px-3 py-2 text-sm"
            >
              {replayFiles.map((file) => (
                <option key={file.path} value={file.path}>{file.name}</option>
              ))}
            </select>
            <div className="flex gap-2">
              <input aria-label="Replay speed" value={replaySpeed} onChange={(e) => setReplaySpeed(e.target.value)} className="w-28 rounded-lg border border-border bg-background px-2 py-1 text-sm" />
              <Button size="sm" onClick={handleStartReplay} disabled={!selectedReplayFile}>Start</Button>
              <Button size="sm" variant="outline" onClick={() => handleReplayControl("pause")} disabled={!replayStatus}><PauseCircle className="mr-2 h-4 w-4" />Pause</Button>
              <Button size="sm" variant="outline" onClick={() => handleReplayControl("resume")} disabled={!replayStatus}><PlayCircle className="mr-2 h-4 w-4" />Resume</Button>
              <Button size="sm" variant="outline" onClick={() => handleReplayControl("stop")} disabled={!replayStatus}><StopCircle className="mr-2 h-4 w-4" />Stop</Button>
            </div>
            <div className="flex gap-2">
              <input aria-label="Seek ms" value={seekMs} onChange={(e) => setSeekMs(e.target.value)} className="w-32 rounded-lg border border-border bg-background px-2 py-1 text-sm" />
              <Button size="sm" variant="outline" onClick={() => handleReplayControl("seek")} disabled={!replayStatus}>Seek</Button>
              <Button size="sm" variant="outline" onClick={() => handleReplayControl("speed")} disabled={!replayStatus}><FastForward className="mr-2 h-4 w-4" />Apply speed</Button>
            </div>
            {replayStatus && <p className="text-xs text-muted-foreground">Replay {replayStatus.status} · {replayStatus.eventsProcessed}/{replayStatus.totalEvents} ({replayStatus.progressPercent}%)</p>}
            {replayError && <p className="text-xs text-destructive">{replayError}</p>}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Backtest → Paper promotion gate</CardTitle>
            <CardDescription>Requires eligibility check before confirmation and audit refresh.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            <input
              aria-label="Run id"
              placeholder="backtest run id"
              value={promotionRunId}
              onChange={(e) => setPromotionRunId(e.target.value)}
              className="w-full rounded-lg border border-border bg-background px-3 py-2 font-mono text-sm"
            />
            <input
              aria-label="Approved by"
              placeholder="operator id"
              value={promotionApprovedBy}
              onChange={(e) => setPromotionApprovedBy(e.target.value)}
              className="w-full rounded-lg border border-border bg-background px-3 py-2 text-sm"
              required
            />
            <input
              aria-label="Approval reason"
              placeholder="why this promotion is approved"
              value={promotionApprovalReason}
              onChange={(e) => setPromotionApprovalReason(e.target.value)}
              className="w-full rounded-lg border border-border bg-background px-3 py-2 text-sm"
              required
            />
            <input
              aria-label="Review notes"
              placeholder="optional review notes"
              value={promotionReviewNotes}
              onChange={(e) => setPromotionReviewNotes(e.target.value)}
              className="w-full rounded-lg border border-border bg-background px-3 py-2 text-sm"
            />
            <input
              aria-label="Manual override id"
              placeholder="optional manual override id"
              value={promotionManualOverrideId}
              onChange={(e) => setPromotionManualOverrideId(e.target.value)}
              className="w-full rounded-lg border border-border bg-background px-3 py-2 font-mono text-sm"
            />
            <div className="flex gap-2">
              <Button size="sm" variant="outline" onClick={handleEvaluatePromotion} disabled={promotionBusy || !promotionRunId.trim()}>Evaluate gate checks</Button>
              <Button size="sm" onClick={handlePromoteToPaper} disabled={promotionBusy || !promotionEval?.isEligible || !promotionApprovedBy.trim() || !promotionApprovalReason.trim()}>Confirm promote</Button>
            </div>
            {promotionEval && (
              <div className="rounded-lg border border-border/60 p-3 text-xs">
                <p>Eligible: {promotionEval.isEligible ? "Yes" : "No"}</p>
                <p>Sharpe: {promotionEval.sharpeRatio} · Max DD: {promotionEval.maxDrawdownPercent}% · Return: {promotionEval.totalReturn}%</p>
                <p>{promotionEval.reason}</p>
              </div>
            )}
            {promotionResult && <p className="text-xs text-success">{promotionResult}</p>}
            {promotionError && <p className="text-xs text-destructive">{promotionError}</p>}
            <div className="rounded-lg border border-border/60 p-3">
              <p className="mb-2 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Audit trail</p>
              <ul className="space-y-1 text-xs">
                {promotionHistory.slice(0, 4).map((record) => (
                  <li key={record.promotionId} className="font-mono">
                    {record.promotedAt} · {record.strategyId} · {record.sourceRunType}→{record.targetRunType}
                    {record.approvedBy ? ` · by ${record.approvedBy}` : ""}
                    {record.approvalReason ? ` · reason: ${record.approvalReason}` : ""}
                    {record.manualOverrideId ? ` · override: ${record.manualOverrideId}` : ""}
                    {record.reviewNotes ? ` · notes: ${record.reviewNotes}` : ""}
                  </li>
                ))}
              </ul>
            </div>
          </CardContent>
        </Card>
      </section>

      <ConfirmActionDialog
        confirm={confirm}
        onClose={closeConfirm}
        onConfirm={executeConfirmedAction}
      />
    </div>
  );
}

function actionLabel(action: ConfirmActionType): string {
  switch (action.kind) {
    case "cancel-order": return `Cancel order ${action.orderId}`;
    case "cancel-all":   return "Cancel all open orders";
    case "close-position": return `Close position — ${action.symbol}`;
    case "pause-strategy": return `Pause strategy — ${action.strategyId}`;
    case "stop-strategy":  return `Stop strategy — ${action.strategyId}`;
  }
}

function actionCopy(action: ConfirmActionType): string {
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
  }
}

function getSessionStatus(session: PaperSessionSummary): string {
  return session.isActive ? "Active" : "Closed";
}

function formatUsd(value: number): string {
  return value.toLocaleString(undefined, {
    style: "currency",
    currency: "USD",
    maximumFractionDigits: 2
  });
}

function SessionInfoRow({ label, value }: { label: string; value: string | null }) {
  return (
    <div className="rounded-lg border border-border/60 bg-secondary/20 px-3 py-2">
      <div className="text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">{label}</div>
      <div className="mt-1 font-mono text-sm text-foreground">{value ?? "Unavailable"}</div>
    </div>
  );
}

function SessionMetric({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-border/60 bg-secondary/20 px-3 py-2">
      <div className="text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">{label}</div>
      <div className="mt-1 font-mono text-sm text-foreground">{value}</div>
    </div>
  );
}

function ConfirmActionDialog({
  confirm,
  onClose,
  onConfirm
}: {
  confirm: ConfirmState;
  onClose: () => void;
  onConfirm: () => Promise<void>;
}) {
  const { action, busy, result, error } = confirm;

  const title = action ? actionLabel(action) : "";
  const copy = action ? actionCopy(action) : "";

  const isCompleted = result !== null;
  const isSuccess = result?.status === "Accepted" || result?.status === "Completed";

  return (
    <Dialog open={action !== null} onOpenChange={(open) => { if (!open) onClose(); }}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{copy}</DialogDescription>
        </DialogHeader>

        {error && (
          <div className="rounded-lg border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive flex items-center gap-2">
            <XCircle className="h-4 w-4 shrink-0" />
            {error}
          </div>
        )}

        {result && (
          <div
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
              <span className="font-semibold">{result.status}</span>
            </div>
            <p>{result.message}</p>
            <p className="mt-1 font-mono text-xs opacity-70">Action ID: {result.actionId}</p>
          </div>
        )}

        {!isCompleted && (
          <div className="flex justify-end gap-3 pt-2">
            <Button variant="outline" onClick={onClose} disabled={busy}>
              Cancel
            </Button>
            <Button onClick={onConfirm} disabled={busy}>
              {busy ? "Processing…" : "Confirm"}
            </Button>
          </div>
        )}

        {isCompleted && (
          <div className="flex justify-end pt-2">
            <Button variant="outline" onClick={onClose}>
              Close
            </Button>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}

function TradingTable({ columns, rows }: { columns: string[]; rows: string[][] }) {
  return (
    <div className="overflow-x-auto rounded-xl border border-border/70">
      <table className="min-w-full divide-y divide-border/60 text-left text-xs sm:text-sm">
        <thead className="bg-secondary/30">
          <tr>
            {columns.map((column) => (
              <th key={column} className="px-3 py-2 font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                {column}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-border/50">
          {rows.map((row, rowIndex) => (
            <tr key={`row-${rowIndex}`} className="bg-background/20">
              {row.map((value, valueIndex) => (
                <td key={`cell-${rowIndex}-${valueIndex}`} className="px-3 py-2 font-mono text-foreground">
                  {value}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function Stat({ label, value, tone }: { label: string; value: string; tone?: string }) {
  return (
    <div className="rounded-xl border border-border/70 bg-secondary/30 p-4">
      <div className="text-xs font-medium uppercase tracking-[0.16em] text-muted-foreground">{label}</div>
      <div className={cn("mt-2 font-mono text-sm font-semibold text-foreground", tone)}>{value}</div>
    </div>
  );
}

function WiringRow({ label, value, tone }: { label: string; value: string; tone?: string }) {
  return (
    <div className="flex items-center justify-between gap-4 rounded-lg bg-white/10 px-3 py-2">
      <span className="text-slate-300">{label}</span>
      <span className={cn("font-mono text-slate-100", tone)}>{value}</span>
    </div>
  );
}

function TradingHighlight({ icon: Icon, title, description }: { icon: React.ElementType; title: string; description: string }) {
  return (
    <div className="rounded-xl border border-border/70 bg-secondary/30 p-4">
      <div className="flex items-center gap-2 text-sm font-semibold text-foreground">
        <Icon className="h-4 w-4 text-primary shrink-0" />
        {title}
      </div>
      <p className="mt-2 text-xs leading-5 text-muted-foreground">{description}</p>
    </div>
  );
}

function ContextRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between gap-4 rounded-lg bg-white/10 px-3 py-2">
      <span className="text-slate-300">{label}</span>
      <span className="font-mono text-slate-100">{value}</span>
    </div>
  );
}
