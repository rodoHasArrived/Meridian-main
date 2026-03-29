import { Activity, AlertTriangle, Cable, CandlestickChart, CheckCircle, ClipboardList, Layers, PauseCircle, PlayCircle, PlusCircle, StopCircle, Trash2, Wallet, XCircle } from "lucide-react";
import { useEffect, useState } from "react";
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
import { cancelAllOrders, cancelOrder, closePosition, closePaperSession, createPaperSession, getExecutionSessions, pauseStrategy, stopStrategy, submitOrder } from "@/lib/api";
import { cn } from "@/lib/utils";
import type { OrderSubmitRequest, PaperSessionSummary, TradingActionResult, TradingWorkspaceResponse } from "@/types";

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

  useEffect(() => {
    getExecutionSessions()
      .then(setSessions)
      .catch(() => { /* sessions unavailable — silently skip */ });
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
      await closePaperSession(sessionId);
      setSessions((prev) => prev.map((s) => s.sessionId === sessionId ? { ...s, status: "Closed" } : s));
    } catch (err) {
      setSessionError(err instanceof Error ? err.message : "Failed to close session.");
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
                        {session.strategyId} · ${session.initialCash.toLocaleString()} · {session.status}
                      </div>
                    </div>
                    {session.status !== "Closed" && (
                      <Button
                        size="sm"
                        variant="outline"
                        className="ml-4 shrink-0"
                        onClick={() => handleCloseSession(session.sessionId)}
                      >
                        Close
                      </Button>
                    )}
                  </div>
                ))}
              </div>
            )}
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
