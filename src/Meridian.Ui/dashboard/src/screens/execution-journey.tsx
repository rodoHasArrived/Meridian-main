import { useEffect, useRef, useState } from "react";
import { Link, useLocation } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { searchExecutionAuditTrail } from "@/lib/api/execution-audit.api";
import { normalizeLocalWorkstationRoute } from "@/lib/workspace";
import { DenseDataTable } from "@/components/meridian/ui-kit-primitives";
import { formatNumber } from "@/lib/format";
import type { AuditTrailExplorerResult, AuditTrailTimelineEntry } from "@/types/execution-audit.types";

export function chronologicalJourney(entries: AuditTrailTimelineEntry[]) {
  const seen = new Set<string>();
  return entries.filter((entry) => { if (seen.has(entry.auditId)) return false; seen.add(entry.auditId); return true; }).sort((a, b) => {
    const at = Date.parse(a.occurredAt); const bt = Date.parse(b.occurredAt);
    if (!Number.isFinite(at)) return Number.isFinite(bt) ? 1 : a.auditId.localeCompare(b.auditId);
    if (!Number.isFinite(bt)) return -1;
    return at - bt || a.auditId.localeCompare(b.auditId);
  });
}

export function journeyStage(entry: AuditTrailTimelineEntry) {
  if (entry.category === "Fill" || /^(Partial)?Fill$/.test(entry.action)) return "Fill report";
  if (/operator|manualoverride|approved|denied|approvalgranted/i.test(entry.category + entry.action)) return "Operator action";
  if (/risk|control|check/i.test(entry.category + entry.action)) return "Control / decision";
  if (/submit|submission/i.test(entry.action)) return "Submission";
  if (/operator|override|approval/i.test(entry.category + entry.action)) return "Operator action";
  return "Recorded outcome";
}

function eventTime(entry: AuditTrailTimelineEntry) {
  return entry.metadata?.reportTimestampUtc ?? entry.metadata?.fillOccurredAtUtc ?? (entry.metadata?.timestampBasis === "execution-report-event" ? entry.occurredAt : null);
}

/** Exact source order identity, not symbol/text coincidence, binds this evidence trace. */
export function ExecutionJourney({ recent }: { recent: AuditTrailExplorerResult | null }) {
  const location = useLocation();
  return <ExecutionJourneyContent key={`${location.pathname}${location.search}`} recent={recent} />;
}

function ExecutionJourneyContent({ recent }: { recent: AuditTrailExplorerResult | null }) {
  const [orderInput, setOrderInput] = useState("");
  const [order, setOrder] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [result, setResult] = useState<AuditTrailExplorerResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const controller = useRef<AbortController | null>(null);
  useEffect(() => () => controller.current?.abort(), []);
  const ambiguous = result?.orderIdentityStatus === "Ambiguous";
  const rows = chronologicalJourney(ambiguous ? [] : result?.entries ?? []);
  const suggestions = [...new Set((recent?.entries ?? []).flatMap((entry) => entry.orderId ? [entry.orderId] : entry.objectKind === "Order" ? [entry.objectId] : []))];
  const load = async () => {
    controller.current?.abort();
    const current = new AbortController(); controller.current = current;
    setOrder(orderInput.trim()); setResult(null); setError(null); setLoading(true);
    if ((from && !Number.isFinite(Date.parse(`${from}Z`))) || (to && !Number.isFinite(Date.parse(`${to}Z`))) || (from && to && from > to)) { setError("Choose a valid UTC range with the start before the end."); setLoading(false); return; }
    try {
      const value = await searchExecutionAuditTrail({ orderId: orderInput.trim(), limit: 1000, fromUtc: from ? `${from}Z` : undefined, toUtc: to ? `${to}Z` : undefined }, { signal: current.signal, allowDevelopmentFallback: false });
      if (!current.signal.aborted) setResult(value);
    } catch (reason) { if (!current.signal.aborted) setError(reason instanceof Error ? reason.message : "Order evidence could not be loaded."); }
    finally { if (!current.signal.aborted) setLoading(false); }
  };
  const invalidate = () => { controller.current?.abort(); setLoading(false); setResult(null); setError(null); setOrder(""); };
  return <section aria-label="Execution journey" className="min-w-0 scroll-mt-16 space-y-3 rounded-[2px] border border-border p-3 text-xs">
    <h3 className="text-sm font-semibold">Execution journey</h3>
    <p className="text-muted-foreground">Trace recorded evidence for one exact canonical client/order ID. A broker ID is accepted only when it resolves uniquely; a coinciding canonical ID takes precedence. Missing stages remain unknown. A submission acknowledgement, a parked approval and a fill are different facts.</p>
    <form className="flex flex-wrap items-end gap-3" onSubmit={(event) => { event.preventDefault(); void load(); }}>
      <label>Journey order ID<Input className="mt-1 w-60" list="journey-order-suggestions" value={orderInput} onChange={(event) => { invalidate(); setOrderInput(event.target.value); }} /></label>
      <datalist id="journey-order-suggestions">{suggestions.map((id) => <option key={id} value={id} />)}</datalist>
      <label>From (UTC)<Input className="mt-1" type="datetime-local" value={from} onChange={(event) => { invalidate(); setFrom(event.target.value); }} /></label>
      <label>To (UTC)<Input className="mt-1" type="datetime-local" value={to} onChange={(event) => { invalidate(); setTo(event.target.value); }} /></label>
      <Button type="submit" size="sm" variant="outline" disabled={!orderInput.trim() || loading}>{loading ? "Loading journey…" : "Trace order"}</Button>
    </form>
    {error ? <p role="alert">Journey unavailable: {error}</p> : null}
    {result ? <>
      <p role="status">Order {order} · {ambiguous ? "identity unresolved; events withheld" : `${result.returned} of ${result.totalMatched} recorded events`} · as of {result.asOf}</p>
      {result.orderIdentityStatus === "Ambiguous" ? <p role="alert">Order identity is ambiguous across orders or source contexts. No events are displayed. Use the canonical client/order ID and resolve conflicting source evidence.</p> : null}
      {result.returned < result.totalMatched ? <p role="alert">Journey truncated. Earlier events may be absent. Narrow the UTC range to inspect them; a truncated trace cannot establish the start of this journey.</p> : null}
      <p>{result.coverageNote ?? "Source coverage is not supplied by this server. A complete order history cannot be established."}</p>
      <p>Sources: {result.sources?.map((source) => `${source.source}: ${source.state}`).join(" · ") || "availability unreported"}. Passed checks and individual fills outside these sources may be unavailable.</p>
      <div className="flex flex-wrap gap-2" aria-label="Journey stage coverage">{["Control / decision", "Submission", "Recorded outcome", "Fill report", "Operator action"].map((stage) => <span key={stage} className="rounded border border-border px-2 py-1">{stage}: {rows.filter((row) => journeyStage(row) === stage).length || "not recorded / unknown"}</span>)}</div>
      <p className="text-muted-foreground">Chronological source timestamps. Audit entries generally use recording time; fill reports use the execution report event time. Intervening activity is unknown; elapsed time alone does not establish missing evidence.</p>
      <ol className="ml-2 space-y-3 border-l-2 border-border pl-4" aria-label="Chronological order events">{rows.map((entry, index) => {
        const previous = rows[index - 1]; const elapsed = previous ? Date.parse(entry.occurredAt) - Date.parse(previous.occurredAt) : 0;
        return <li key={entry.auditId} className="relative"><span aria-hidden="true" className="absolute -left-[22px] top-2 h-2.5 w-2.5 rounded-full border-2 border-background bg-primary" />
          {elapsed >= 60000 ? <p className="mb-2 text-muted-foreground">Elapsed {formatNumber(elapsed / 60000, { minimumFractionDigits: 1, maximumFractionDigits: 1 })} minutes; intervening evidence unknown.</p> : null}
          <details className="rounded border border-border p-2"><summary className="cursor-pointer focus-visible:ring-2 focus-visible:ring-primary"><time>{entry.occurredAt}</time> · {journeyStage(entry)} · <strong>{entry.action}</strong> · {entry.outcome}</summary>
            <dl className="mt-2 grid gap-1"><div><dt className="inline font-semibold">Actor: </dt><dd className="inline">{entry.actor ?? "not attributed"}</dd></div><div><dt className="inline font-semibold">Source: </dt><dd className="inline">{entry.actionLedgerSource ?? "unreported"} · {entry.actionLedgerStatus ?? "integrity unreported"}</dd></div><div><dt className="inline font-semibold">Event time: </dt><dd className="inline">{eventTime(entry) ?? "not separately retained"}</dd></div><div><dt className="inline font-semibold">Recording time: </dt><dd className="inline">{entry.metadata?.timestampBasis === "execution-report-event" ? "not exposed" : entry.occurredAt}</dd></div><div><dt className="inline font-semibold">Identity: </dt><dd className="inline break-all">{entry.auditId} · order {entry.orderId ?? entry.objectId} · correlation {entry.correlationId ?? "unreported"}</dd></div><div><dt className="inline font-semibold">Reason: </dt><dd className="inline">{entry.reason ?? entry.message ?? "not reported"}</dd></div></dl>
            {entry.metadata ? <dl className="mt-2 grid gap-1">{Object.entries(entry.metadata).map(([name, value]) => <div key={name} className="break-all"><dt className="inline font-semibold">{name}: </dt><dd className="inline">{value}</dd></div>)}</dl> : null}
            {normalizeLocalWorkstationRoute(entry.evidenceRoute) ? <Link className="mt-2 inline-block text-primary underline" to={normalizeLocalWorkstationRoute(entry.evidenceRoute)!}>Open source evidence</Link> : entry.evidenceRoute ? <p className="mt-2 break-all">Source reference: {entry.evidenceRoute}</p> : null}
          </details>
        </li>;
      })}</ol>
      {rows.length === 0 && !ambiguous ? <p>No matching recorded events. This does not establish that the order never existed or that no activity occurred.</p> : null}
      <details><summary className="cursor-pointer">Event table</summary><DenseDataTable rows={rows} getRowId={(entry) => entry.auditId}
        maxVisibleRows={null} ariaLabel="Journey event table" emptyText="No attributable event evidence is available."
        columns={[
          { id: "timestamp", label: "Source timestamp", render: (entry) => entry.occurredAt },
          { id: "stage", label: "Stage", render: (entry) => journeyStage(entry) },
          { id: "action", label: "Action", render: (entry) => entry.action },
          { id: "outcome", label: "Outcome", render: (entry) => entry.outcome },
          { id: "actor", label: "Actor", render: (entry) => entry.actor ?? "unattributed" }
        ]} /></details>
    </> : loading ? <p role="status">Loading recorded order evidence…</p> : <p className="text-muted-foreground">Enter an order ID to inspect its recorded journey.</p>}
  </section>;
}
