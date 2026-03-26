import { Activity, AlertTriangle, Cable, CandlestickChart, CheckCircle, ClipboardList, PlusCircle, Wallet, XCircle } from "lucide-react";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { MetricCard } from "@/components/meridian/metric-card";
import { cancelOrder, submitOrder } from "@/lib/api";
import { cn } from "@/lib/utils";
import type { OrderSubmitRequest, TradingWorkspaceResponse } from "@/types";

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
  const [cancelError, setCancelError] = useState<string | null>(null);

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

  async function handleCancelOrder(orderId: string) {
    setCancelError(null);
    try {
      await cancelOrder(orderId);
    } catch (err) {
      setCancelError(
        `Cancel failed for ${orderId}: ${err instanceof Error ? err.message : "unknown error"}`
      );
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
            <TradingTable
              columns={["Symbol", "Side", "Qty", "Avg", "Mark", "Day P&L", "Unrealized", "Exposure"]}
              rows={data.positions.map((position) => [
                position.symbol,
                position.side,
                position.quantity,
                position.averagePrice,
                position.markPrice,
                position.dayPnl,
                position.unrealizedPnl,
                position.exposure
              ])}
            />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <div className="flex items-center justify-between">
              <CardTitle className="flex items-center gap-2 text-base">
                <ClipboardList className="h-4 w-4 text-primary" />
                Open orders
              </CardTitle>
              <Button size="sm" variant="outline" onClick={() => setShowOrderForm((prev) => !prev)}>
                <PlusCircle className="mr-2 h-4 w-4" />
                New order
              </Button>
            </div>
          </CardHeader>
          {cancelError && (
            <CardContent className="pt-0 pb-2">
              <div className="rounded-lg border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive flex items-center gap-2">
                <XCircle className="h-4 w-4 shrink-0" />
                {cancelError}
              </div>
            </CardContent>
          )}
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
                          onClick={() => handleCancelOrder(order.orderId)}
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
    </div>
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
