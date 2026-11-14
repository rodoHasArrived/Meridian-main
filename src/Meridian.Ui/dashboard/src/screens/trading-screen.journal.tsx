import { useCallback, useEffect, useState } from "react";
import { getJournal, getJournalOrder, getJournalSummary, type JournalOrder, type JournalSummary } from "@/lib/api/execution-workflows.api";
import { formatCurrency, formatTimestamp } from "@/lib/format";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { TableWorkbench } from "@/components/meridian/table-workbench";

const money = (value: number | null | undefined) => value == null ? "Unavailable" : formatCurrency(value);
type OrderSelection = { orderId: string; attemptId?: string; fromLink?: boolean };
const matchesSelection = (order: JournalOrder, selected: OrderSelection) =>
  order.orderId === selected.orderId && (selected.attemptId === undefined || order.attemptId === selected.attemptId);

export function ExecutionJournalPanel({ accountId }: { accountId?: string | null }) {
  const [orders, setOrders] = useState<JournalOrder[]>([]);
  const [summary, setSummary] = useState<JournalSummary | null>(null);
  const [linked] = useState(() => new URLSearchParams(window.location.search));
  const [loadedAccount, setLoadedAccount] = useState<string | null>(null);
  const [from, setFrom] = useState(""); const [to, setTo] = useState(""); const [status, setStatus] = useState("");
  const [linkedOrder, setLinkedOrder] = useState<JournalOrder | null>(null);
  const [symbol, setSymbol] = useState(""); const [strategy, setStrategy] = useState(linked.get("runId") ?? ""); const [tag, setTag] = useState("");
  const [selected, setSelected] = useState<OrderSelection | null>(null);
  const [error, setError] = useState<string | null>(null); const [loading, setLoading] = useState(false);
  const [revision, setRevision] = useState(0);
  const refresh = useCallback(() => setRevision((value) => value + 1), []);
  useEffect(() => {
    const linkedOrderId = linked.get("orderId");
    setOrders([]); setSummary(null); setLinkedOrder(null); setError(null); setLoadedAccount(null);
    setSelected(linkedOrderId ? { orderId: linkedOrderId, attemptId: linked.get("attemptId") ?? undefined, fromLink: true } : null);
    if (!accountId) { setLoading(false); return; }
    const controller = new AbortController(); setLoading(true);
    const timer = window.setTimeout(() => {
      const range = { from: from ? new Date(`${from}T00:00:00Z`).toISOString() : undefined, to: to ? new Date(`${to}T00:00:00Z`).toISOString() : undefined };
      void Promise.all([getJournal(accountId, { symbol, strategy, tag, status, ...range }, { signal: controller.signal }), getJournalSummary(accountId, { signal: controller.signal }, range)])
        .then(([next, totals]) => { if (!controller.signal.aborted) { setOrders(next); setSummary(totals); setLoadedAccount(accountId); } })
        .catch((reason: unknown) => { if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : "Journal evidence is unavailable."); })
        .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    }, 200);
    return () => { controller.abort(); window.clearTimeout(timer); };
  }, [accountId, symbol, strategy, tag, from, to, status, revision, linked]);
  const visibleOrders = orders.filter((order) => order.accountId.toLowerCase() === accountId?.toLowerCase());
  const selectedInList = selected && (!selected.fromLink || selected.attemptId)
    ? visibleOrders.find((order) => matchesSelection(order, selected)) : undefined;
  useEffect(() => {
    if (!accountId || !selected || selectedInList) return;
    const controller = new AbortController();
    void getJournalOrder(accountId, selected.orderId, { signal: controller.signal }, selected.attemptId).then((order) => {
      if (!controller.signal.aborted) setLinkedOrder(order);
    }).catch(() => { /* List errors remain visible; a missing deep link grants no authority. */ });
    return () => controller.abort();
  }, [accountId, selected, selectedInList]);
  if (!accountId) {
    return <section aria-label="Operational trade journal" aria-busy="false" className="border-y border-border px-4 py-2 text-sm text-muted-foreground">Select an account to see its journal</section>;
  }
  const totals = loadedAccount === accountId ? summary : null;
  const detail = selectedInList ?? (selected && linkedOrder?.accountId.toLowerCase() === accountId?.toLowerCase()
    && linkedOrder && matchesSelection(linkedOrder, selected) ? linkedOrder : null);
  return <section aria-label="Operational trade journal" aria-busy={loading} className="panel-surface space-y-3 rounded-lg border border-border p-4">
    <div className="flex items-center justify-between gap-3"><div><h3 className="font-semibold">Trade journal</h3><p className="text-xs text-muted-foreground">Retained execution evidence and FIFO operational attribution. Accounting posting remains separate.</p></div><Button size="sm" variant="outline" onClick={refresh} disabled={loading || !accountId}>Refresh journal</Button></div>
      <div className="grid gap-2 md:grid-cols-3"><label className="text-xs">Symbol<Input value={symbol} onChange={(event) => setSymbol(event.target.value)} /></label><label className="text-xs">Strategy or run<Input value={strategy} onChange={(event) => setStrategy(event.target.value)} /></label><label className="text-xs">Tag<Input value={tag} onChange={(event) => setTag(event.target.value)} /></label></div>
      <div className="grid gap-2 md:grid-cols-3">
        <label className="text-xs">From date (UTC)<Input type="date" value={from} onChange={(event) => setFrom(event.target.value)} /></label>
        <label className="text-xs">Before date (UTC)<Input type="date" value={to} min={from || undefined} onChange={(event) => setTo(event.target.value)} /></label>
        <label className="text-xs">Status<Select value={status} onChange={(event) => setStatus(event.target.value)}><option value="">All statuses</option>{["PendingNew", "Accepted", "PartiallyFilled", "Filled", "PendingCancel", "Cancelled", "Rejected", "Expired"].map((value) => <option key={value}>{value}</option>)}</Select></label>
      </div>
      {error ? <p role="alert">{error}</p> : null}
      {totals ? <dl className="grid grid-cols-2 gap-2 text-xs md:grid-cols-4"><div><dt>Gross / net realized P&amp;L</dt><dd>{money(totals.realizedPnl)} / {money(totals.netRealizedPnl)}</dd></div><div><dt>Commissions / fees</dt><dd>{money(totals.commissions)} / {money(totals.fees)}</dd></div><div><dt>Execution slippage</dt><dd>{money(totals.slippage)}</dd></div><div><dt>Account coverage for selected dates</dt><dd>{totals.coverage}</dd></div></dl> : null}
      {totals?.attributions?.length ? <div className="text-xs"><h4 className="font-semibold">FIFO attribution to opening strategy and tag</h4><ul>{totals.attributions.map((row, index) => <li key={index}>{row.strategyId ?? "Unattributed"} · {row.runId ?? "No run"} · {row.tag ?? "No tag"}: gross {money(row.grossRealizedPnl)}; net {money(row.netRealizedPnl)}</li>)}</ul></div> : null}
      <p className="text-xs text-muted-foreground">Totals cover the account and date window. Symbol, strategy, tag and status filter the order list.</p>
      <TableWorkbench<JournalOrder>
        workbenchId="execution-journal"
        sourceKey={JSON.stringify([accountId, symbol, strategy, tag, status, from, to])}
        sourceDescription="Retained execution orders for the selected account and filters, limited to 500 results. Totals cover the account and date window."
        rows={visibleOrders}
        getRowId={(order) => JSON.stringify([order.scope, order.orderId, order.attemptId])}
        ariaLabel="Retained orders for the selected account"
        emptyText={loading ? "Loading retained orders…" : "No retained orders match these filters. This does not establish complete historical coverage."}
        columns={[
          { id: "order", label: "Order", render: (order) => <Button type="button" size="sm" variant="ghost" className="px-1" onClick={() => setSelected({ orderId: order.orderId, attemptId: order.attemptId })} aria-expanded={detail?.orderId === order.orderId && detail?.attemptId === order.attemptId}>{order.orderId}</Button>, exportValue: (order) => order.orderId },
          { id: "symbol", label: "Symbol", render: (order) => order.symbol, exportValue: (order) => order.symbol },
          { id: "side", label: "Side", render: (order) => order.side, exportValue: (order) => order.side },
          { id: "status", label: "Status", render: (order) => order.status, exportValue: (order) => order.status },
          { id: "quantity", label: "Filled / requested", numeric: true, render: (order) => `${order.filledQuantity} / ${order.quantity}`, exportValue: (order) => `${order.filledQuantity} / ${order.quantity}` },
          { id: "strategy", label: "Strategy / tag", render: (order) => `${order.strategyId ?? "Unattributed"} / ${order.tag ?? "—"}`, exportValue: (order) => `${order.strategyId ?? "Unattributed"} / ${order.tag ?? "—"}` },
          { id: "updated", label: "Updated", render: (order) => formatTimestamp(new Date(order.updatedAt)), exportValue: (order) => order.updatedAt }
        ]}
      />
      {!loading && !error && visibleOrders.length === 0 ? <p className="text-xs">No retained orders match these filters. This does not establish complete historical coverage.</p> : null}
      {detail ? <section aria-label={`Order ${detail.orderId} history`} className="border-t border-border pt-3"><h4 className="text-sm font-semibold">Order evidence: {detail.orderId}</h4><p className="break-all text-xs">Scope: {detail.scope} · Run: {detail.runId ?? "Unattributed"}</p><ol className="mt-2 space-y-2 text-xs">{detail.events.map((event) => <li key={event.eventId}><strong>{event.kind}</strong> · {formatTimestamp(new Date(event.recordedAt))}{event.report ? <p>Broker identity {event.report.gatewayOrderId ?? "unknown"}; predecessor {event.report.replacesOrderId ?? "none"}; cumulative fill {event.report.filledQuantity}; price {event.report.fillPrice ?? "unknown"}; commission {money(event.report.commission)}; fees {money(event.report.fees)}</p> : null}{event.activityFill ? <p>Provider fill {event.activityFill.fillId}: {event.activityFill.quantity} at {event.activityFill.price}; commission {money(event.activityFill.commission)}</p> : null}{event.detail ? <p>{event.detail}</p> : null}</li>)}</ol></section> : null}
  </section>;
}
