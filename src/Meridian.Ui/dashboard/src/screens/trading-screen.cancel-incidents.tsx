import { useEffect, useState } from "react";
import { TableWorkbench } from "@/components/meridian/table-workbench";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { PanelReload } from "@/components/ui/panel-reload";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { TimeText } from "@/components/ui/time-text";
import { getCancelAllIncident, getCancelAllIncidents } from "@/lib/api/cancel-incidents.api";
import type { CancelAllIncident, CancelAllIncidentHistory, CancelAllOrderOutcome } from "@/types/cancel-incidents.types";

const outcomeLabels: Record<string, string> = {
  ConfirmedCancelled: "Confirmed cancelled",
  FilledDuringCancel: "Filled during cancellation",
  AlreadyTerminal: "Already terminal",
  StillWorking: "Still working",
  WithdrawnBeforeDispatch: "Withdrawn before dispatch",
  Unknown: "Unknown"
};

export function CancelAllIncidentPanel({ fundAccountId }: { fundAccountId?: string | null }) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <CardTitle>Cancel-all incident history</CardTitle>
        <CardDescription>Retained cancellation outcomes and remaining order quantities for the selected execution account.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {fundAccountId ? <AccountIncidents key={fundAccountId} fundAccountId={fundAccountId} /> :
          <StatusBanner tone="info" title="Select an execution account" detail="Choose an account in Trading to inspect its retained cancel-all outcomes." />}
      </CardContent>
    </Card>
  );
}

function AccountIncidents({ fundAccountId }: { fundAccountId: string }) {
  const [history, setHistory] = useState<CancelAllIncidentHistory | null>(null);
  const [selectedId, setSelectedId] = useState("");
  const [lookupId, setLookupId] = useState("");
  const [revision, setRevision] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    async function load() {
      setLoading(true);
      setError(null);
      setHistory(null);
      try {
        const result = await getCancelAllIncidents(fundAccountId, { signal: controller.signal });
        if (controller.signal.aborted) return;
        setHistory(result);
        setSelectedId(current => current || result.incidents[0]?.incidentId || "");
      } catch (reason) {
        if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : "Retained incident history could not be loaded.");
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    }
    void load();
    return () => controller.abort();
  }, [fundAccountId, revision]);

  return (
    <div className="space-y-3" aria-busy={loading}>
      <div className="flex flex-wrap items-end justify-between gap-3">
        <label className="min-w-0 flex-1 space-y-1 text-sm">
          <span>Incident</span>
          <Select aria-label="Cancel-all incident" value={selectedId} disabled={loading || !history?.incidents.length}
            onChange={event => setSelectedId(event.target.value)}>
            {!history?.incidents.length ? <option value="">{loading ? "Loading incidents…" : "No incident selected"}</option> : null}
            {selectedId && !history?.incidents.some(incident => incident.incidentId === selectedId)
              ? <option value={selectedId}>Requested retained incident</option> : null}
            {history?.incidents.map(incident => <option key={incident.incidentId} value={incident.incidentId}>
              {incident.startedAt.replace("T", " ")} · {incident.orderCount} orders · {incident.unresolvedOrderCount} unresolved
            </option>)}
          </Select>
        </label>
        <PanelReload reload={() => setRevision(value => value + 1)} failed={error !== null} busy={loading} what="cancel-all incident history" />
      </div>
      {error ? <StatusBanner tone="danger" title="Cancel-all history unavailable" detail={error} /> : null}
      {history ? <p className="text-sm text-muted-foreground">{history.coverageNote}</p> : null}
      {history?.hasMore ? <StatusBanner tone="warning" title="Recent incidents shown" detail={`Showing the most recent ${history.returned} retained incidents. Use Find an older incident to reopen a specific record.`} /> : null}
      {history ? <details className="text-sm"><summary className="cursor-pointer">Find an older incident</summary>
        <form className="mt-2 flex flex-wrap items-end gap-2" onSubmit={event => { event.preventDefault(); if (lookupId.trim()) setSelectedId(lookupId.trim()); }}>
          <label className="min-w-0 flex-1 space-y-1"><span>Retained incident ID</span><Input aria-label="Retained incident ID" value={lookupId} onChange={event => setLookupId(event.target.value)} /></label>
          <Button type="submit" size="sm" disabled={!lookupId.trim()}>Open incident</Button>
        </form>
      </details> : null}
      {history?.incidents.length === 0 ? <StatusBanner tone="info" title="No retained incidents for this account" detail="This does not establish that the account has no working orders or exposure." /> : null}
      {!loading && history && selectedId ? <IncidentDetail key={`${selectedId}:${revision}`} incidentId={selectedId} fundAccountId={fundAccountId} /> : null}
    </div>
  );
}

function IncidentDetail({ incidentId, fundAccountId }: { incidentId: string; fundAccountId: string }) {
  const [incident, setIncident] = useState<CancelAllIncident | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [revision, setRevision] = useState(0);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const controller = new AbortController();
    async function load() {
      setLoading(true);
      setIncident(null);
      setError(null);
      try {
        const result = await getCancelAllIncident(incidentId, fundAccountId, { signal: controller.signal });
        if (!controller.signal.aborted) setIncident(result);
      } catch (reason) {
        if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : "The incident could not be reopened.");
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    }
    void load();
    return () => controller.abort();
  }, [incidentId, fundAccountId, revision]);

  if (error) return <div className="space-y-2"><StatusBanner tone="danger" title="Incident evidence unavailable" detail={error} />
    <PanelReload reload={() => setRevision(value => value + 1)} failed busy={loading} what="the selected incident" /></div>;
  if (!incident) return <p role="status" className="text-sm text-muted-foreground">Loading retained order outcomes…</p>;

  const summary = incident.summary;
  return (
    <section className="space-y-3" aria-label="Retained cancel-all incident">
      <div className="flex flex-wrap items-center gap-2 text-sm">
        <Badge variant={summary.evidenceStatus === "Complete" && !summary.evidenceError ? "default" : "warning"}>Evidence {summary.evidenceStatus.toLowerCase()}</Badge>
        <Badge variant={summary.unresolvedOrderCount ? "warning" : "default"}>{summary.unresolvedOrderCount} unresolved {summary.unresolvedOrderCount === 1 ? "order" : "orders"}</Badge>
        <span>{summary.executionMode} · {summary.gatewayId} · {summary.actor ?? "Unattributed actor"}</span>
        <span>Started <TimeText value={summary.startedAt} precision="second" /></span>
        <span>{summary.completedAt ? <>Completed <TimeText value={summary.completedAt} precision="second" /></> : "No retained completion"}</span>
      </div>
      {summary.evidenceError ? <StatusBanner tone="warning" title="Evidence may be incomplete" detail={summary.evidenceError} /> : null}
      {summary.brokerViewUnavailable ? <StatusBanner tone="warning" title="Broker coverage unresolved" detail={incident.brokerViewError ?? "The broker's complete open-order book was not established. Reconcile broker state before resuming execution."} /> : null}
      <p className="text-sm text-muted-foreground">Quantities reflect each order’s observation time. Filled quantities remain executed exposure. Residual notional is shown only with retained valuation coverage.</p>
      <TableWorkbench<CancelAllOrderOutcome> workbenchId="trading-cancel-incident-orders" sourceKey={`${fundAccountId}:${incidentId}`}
        sourceDescription="Retained account-scoped order observations from one cancel-all incident; not a current exposure valuation."
        completePopulation={summary.evidenceStatus === "Complete" && !summary.evidenceError}
        exportContext={{ fundAccountId, incidentId, evidenceStatus: summary.evidenceStatus }}
        rows={incident.orders} getRowId={row => row.observationKey} ariaLabel="Cancel-all order outcomes" maxVisibleRows={null}
        emptyText="No account-attributed orders were retained in this incident. Broker coverage must be assessed separately."
        columns={[
          { id: "order", label: "Order", wrap: true, render: row => <div><span className="font-medium">{row.symbol ?? "Unknown instrument"}</span><div className="break-all text-muted-foreground">{row.orderId}</div></div>, exportValue: row => `${row.symbol ?? "Unknown instrument"} · ${row.orderId}` },
          { id: "outcome", label: "Outcome", wrap: true, render: row => <div><Badge variant={needsReconciliation(row) ? "warning" : "default"}>{outcomeLabels[row.disposition] ?? row.disposition}</Badge><div className="text-muted-foreground">{row.status ?? "Status unknown"}{row.requiresReconciliation ? " · Reconciliation required" : ""}</div></div>, exportValue: row => outcomeLabels[row.disposition] ?? row.disposition },
          { id: "filled", label: "Filled quantity", align: "right", render: row => <span className="font-mono tabular-nums">{row.filledQuantity ?? "Unknown"}</span>, exportValue: row => row.filledQuantity, exportDisplay: row => row.filledQuantity ?? "Unknown", exportType: "decimal", exportNumberFormat: "0.############################" },
          { id: "remaining", label: "Possible remaining", align: "right", render: row => <span className="font-mono tabular-nums">{row.remainingQuantity ?? "Unknown"}</span>, exportValue: row => row.remainingQuantity, exportDisplay: row => row.remainingQuantity ?? "Unknown", exportType: "decimal", exportNumberFormat: "0.############################" },
          { id: "notional", label: "Residual notional", priority: "medium", wrap: true, render: row => row.residualNotional !== null && row.residualNotional !== undefined ? <span className="font-mono tabular-nums">{row.residualNotional} {row.currency ?? "Currency unknown"}</span> : <span className="text-muted-foreground">Unavailable · {row.valuationCoverage}</span>, exportValue: row => row.residualNotional != null ? `${row.residualNotional} ${row.currency ?? "Currency unknown"}` : `Unavailable · ${row.valuationCoverage}` },
          { id: "evidence", label: "Evidence", wrap: true, render: row => <OrderObservation row={row} />, exportValue: row => row.detail }
        ]} />
      <details className="text-sm"><summary className="cursor-pointer text-muted-foreground">Incident identifiers</summary>
        <dl className="mt-2 grid gap-1 break-all"><dt>Incident</dt><dd className="font-mono">{summary.incidentId}</dd><dt>Account</dt><dd className="font-mono">{fundAccountId}</dd>
          <dt>Correlation</dt><dd>{incident.correlationId ?? "Not retained"}</dd><dt>Broker scope</dt><dd>{incident.brokerScopeIdentity ?? "Not retained"}</dd></dl>
      </details>
    </section>
  );
}

function needsReconciliation(row: CancelAllOrderOutcome) {
  return row.requiresReconciliation || row.hasUnconfirmedReplacement || row.disposition === "Unknown" || row.disposition === "StillWorking";
}

function OrderObservation({ row }: { row: CancelAllOrderOutcome }) {
  return <details><summary className="cursor-pointer">Observation details</summary>
    <div className="space-y-1 break-words text-sm">
      <p>{row.detail ?? "No additional outcome detail retained."}</p>
      <p>Observed <TimeText value={row.observedAt} precision="second" /></p>
      <p>Cancel {row.cancellationAttempted ? "attempted" : "not attempted"} · {row.side ?? "Side unknown"}</p>
      <p>Initial {row.initialStatus ?? "unknown"} · quantity {row.initialQuantity ?? "unknown"} · filled {row.initialFilledQuantity ?? "unknown"}</p>
      <p>Observed quantity {row.quantity ?? "unknown"}</p>
      {row.hasUnconfirmedReplacement ? <p className="text-warning">Replacement unconfirmed · broker {row.candidateBrokerOrderId ?? "unknown"} · client {row.candidateClientOrderId ?? "unknown"}</p> : null}
      <p className="break-all">Broker order {row.brokerOrderId ?? "not retained"} · account {row.brokerAccountId ?? "not retained"}</p>
      <p>Valuation: {row.valuationCoverage}{row.valuationPrice ? ` · ${row.valuationPrice} ${row.currency ?? "currency unknown"}` : ""}{row.valuationSource ? ` · ${row.valuationSource}` : ""}</p>
      {row.valuationObservedAt ? <p>Price observed <TimeText value={row.valuationObservedAt} precision="second" /></p> : null}
    </div>
  </details>;
}
