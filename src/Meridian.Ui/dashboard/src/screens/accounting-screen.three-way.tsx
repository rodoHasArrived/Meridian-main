import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { getLatestThreeWayReconciliation, runThreeWayReconciliation } from "@/lib/api/accounting-integration.api";
import type { AccountingIntegrationScope, ThreeWayReconciliation } from "@/types/accounting-integration";
import { IntegrationScopePicker, integrationError, scopeIdentity } from "./accounting-integration.scope";
import { ThreeWayAdjustmentProposalPanel } from "./accounting-screen.three-way-adjustment";

const comparisonColumns: DenseDataTableColumn<ThreeWayReconciliation["checks"][number]>[] = [
  { id: "measure", label: "Measure", rowHeader: true, render: check => check.measure },
  { id: "instrument", label: "Instrument", render: check => check.symbol ?? "Cash" },
  { id: "left", label: "Left source / amount", render: check => <span className="font-mono">{check.leftSource}: {check.leftAmount ?? "Unavailable"}</span> },
  { id: "right", label: "Right source / amount", render: check => <span className="font-mono">{check.rightSource}: {check.rightAmount ?? "Unavailable"}</span> },
  { id: "tolerance", label: "Tolerance", numeric: true, render: check => <span className="font-mono">{check.tolerance}</span> },
  { id: "status", label: "Status / explanation", render: check => <>{check.status} · {check.explanation}</> }
];

export function ThreeWayAccountingPanel() {
  const [scope, setScope] = useState<AccountingIntegrationScope | null>(null);
  return <Card aria-label="Ledger OMS broker reconciliation">
    <CardHeader><CardTitle>Ledger · OMS · Broker</CardTitle>
      <CardDescription>Compare independently retained cash, quantities and supported cost basis at one cutoff. Missing coverage remains blocked.</CardDescription></CardHeader>
    <CardContent className="space-y-4"><IntegrationScopePicker onSelect={setScope} />
      {scope ? <ThreeWayCommands key={scopeIdentity(scope)} scope={scope} /> : <p className="text-sm text-muted-foreground">Select the exact financial account, ledger book and accounting period.</p>}
    </CardContent>
  </Card>;
}

function ThreeWayCommands({ scope }: { scope: AccountingIntegrationScope }) {
  const [result, setResult] = useState<ThreeWayReconciliation | null>(null);
  const [operationId, setOperationId] = useState(() => crypto.randomUUID());
  const [busy, setBusy] = useState(false);
  const [draftBusy, setDraftBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const alive = useRef(true);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  async function load(run: boolean) {
    setBusy(true); setError(null); setNotice(null);
    try {
      const next = run ? await runThreeWayReconciliation(scope.accountId, scope.book.ledgerBookId, scope.period.periodId, operationId)
        : await getLatestThreeWayReconciliation(scope.accountId, scope.book.ledgerBookId, scope.period.periodId);
      if (!alive.current) return;
      if (next && (next.scope.financialAccountId !== scope.accountId || next.scope.ledgerBookId !== scope.book.ledgerBookId
        || next.scope.accountingPeriodId !== scope.period.periodId)) throw new Error("The returned comparison does not match the selected account, book and period.");
      setResult(next);
      if (!next) setNotice("No retained three-way comparison exists for this exact scope.");
      if (run && next) setOperationId(crypto.randomUUID());
    } catch (reason) { if (alive.current) setError(integrationError(reason)); }
    finally { if (alive.current) setBusy(false); }
  }
  return <div className="space-y-4">
    <div className="flex flex-wrap gap-2">
      <Button size="sm" disabled={busy || draftBusy} onClick={() => void load(true)}>Run three-way comparison</Button>
      <Button size="sm" variant="outline" disabled={busy || draftBusy} onClick={() => void load(false)}>Load latest comparison</Button>
    </div>
    {busy ? <p role="status" className="text-sm">Reading independent source evidence…</p> : null}
    {notice ? <p role="status" className="text-sm text-muted-foreground">{notice}</p> : null}
    {error ? <p role="alert" className="text-sm text-danger">{error} The operation identity is retained for retry.</p> : null}
    {result ? <div className="space-y-4" aria-label="Retained three-way comparison">
      <div className="flex flex-wrap items-center gap-2"><Badge variant="outline">{result.status}</Badge>
        <Badge variant="outline">{provenanceLabel(result.dataProvenanceToken)}</Badge>
        <span className="text-xs">{result.scope.currency} · Cutoff {result.cutoffUtc} · Recorded {result.recordedAtUtc}</span></div>
      <div className="grid gap-3 lg:grid-cols-3">
        {result.sources.map(source => <div key={source.source} className="min-w-0 rounded border border-border p-3 text-xs">
          <p className="font-semibold">{source.source} <span className="font-normal text-muted-foreground">· {provenanceLabel(source.dataProvenanceToken)}</span></p>
          <dl className="mt-2 space-y-1"><div><dt>Cash ({result.scope.currency})</dt><dd className="font-mono">{source.cash ?? "Unavailable"}</dd></div>
            <div><dt>Source time</dt><dd>{source.asOfUtc}</dd></div>
            <div><dt>Age at cutoff</dt><dd>{sourceAge(source.asOfUtc, result.cutoffUtc)}</dd></div>
            <div><dt>Cost basis convention</dt><dd>{source.costBasisConvention || "Unavailable"}</dd></div></dl>
          <p className="mt-2 font-medium">{source.coverageFailures.length ? "Coverage blocked" : "Source coverage retained"}</p>
          {source.coverageFailures.length ? <ul className="mt-1 list-disc pl-4 text-warning">{source.coverageFailures.map((failure, index) => <li key={index}>{failure}</li>)}</ul> : null}
          <details className="mt-2"><summary className="cursor-pointer">Source evidence and positions</summary>
            <p className="mt-2 break-all font-mono">{source.evidenceReference}</p>
            {source.positions.map(position => <p key={position.securityId} className="mt-1 font-mono">{position.symbol}: quantity {position.quantity}; basis {position.costBasis ?? "Unavailable"}</p>)}
          </details>
        </div>)}
      </div>
      <DenseDataTable columns={comparisonColumns} rows={result.checks} getRowId={check => check.checkId}
        ariaLabel="Retained ledger OMS broker comparison operands" emptyText="No comparison checks were retained."
        caption="Retained comparison operands; amounts are displayed as supplied by the server." />
      {result.adjustmentProposals?.length ? <section className="space-y-3" aria-label="Evidence-backed fee adjustment drafts">
        <h4 className="text-sm font-semibold">Fee adjustment drafts</h4>
        {result.adjustmentProposals.map(proposal => <ThreeWayAdjustmentProposalPanel
          key={`${result.runId}:${proposal.checkId}:${proposal.evidenceHash}:${result.accountingPeriodVersion ?? scope.period.version}`}
          scope={scope} comparison={result} proposal={proposal} disabled={busy || draftBusy} onBusyChange={setDraftBusy} />)}
      </section> : null}
      <div className="text-xs"><p className="font-medium">Break casework and correction status</p>
        <p className="text-muted-foreground">Inspect first-seen age, assignment, evidence and approved corrections in each retained case. A comparison does not post an adjustment.</p>
        <div className="mt-2 flex flex-wrap gap-3">{result.breakIds.map(breakId => <Link key={breakId} className="text-primary underline"
          to={`/accounting/reconciliation?${new URLSearchParams({ fundAccountId: scope.accountId, accountId: scope.accountId, ledgerBookId: scope.book.ledgerBookId, periodId: scope.period.periodId, reconciliationBreak: breakId }).toString()}`}>Open break {breakId}</Link>)}</div>
        {!result.breakIds.length ? <p className="mt-2">No retained breaks in this comparison.</p> : null}
      </div>
    </div> : null}
  </div>;
}

function sourceAge(asOf: string, cutoff: string): string {
  const seconds = (Date.parse(cutoff) - Date.parse(asOf)) / 1000;
  if (!Number.isFinite(seconds) || seconds < 0) return "Outside cutoff";
  const value = seconds < 60 ? Math.floor(seconds) : Math.floor(seconds / 60);
  const unit = seconds < 60 ? "second" : "minute";
  return `${value} ${unit}${value === 1 ? "" : "s"}`;
}

function provenanceLabel(value: string | undefined): string {
  return value && ["real", "simulated", "seeded", "sample"].includes(value.toLowerCase())
    ? value.toUpperCase() : "Provenance unavailable";
}
