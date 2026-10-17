import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { DenseDataTable } from "@/components/meridian/ui-kit-primitives";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { Tabs } from "@/components/ui/tabs";
import { TechnicalDetails } from "@/components/ui/technical-details";
import * as api from "@/lib/api/account-oversight.api";
import { isApiError } from "@/lib/api-errors";
import type {
  AccountCashBalanceSource, AccountCashReconciliationCase, AccountCashReconciliationRequest,
  AccountCashReconciliationRun, OversightContext, OversightScope
} from "@/types/account-oversight";

interface Props {
  scope: OversightScope;
  context: OversightContext;
  onDirtyChange?: (dirty: boolean) => void;
}

const sourceNames = ["Prime", "Custodian", "Administrator", "Internal ledger"];
const errorMessage = (reason: unknown) => reason instanceof Error ? reason.message : "The retained cash request failed.";
const scopeMatches = (item: { accountId: string; ledgerBookId: string }, scope: OversightScope) =>
  item.accountId.toLowerCase() === scope.accountId.toLowerCase() && item.ledgerBookId.toLowerCase() === scope.ledgerBookId.toLowerCase();

/** Scope changes remount the handoff and abort every outstanding request. */
export function AccountCashCasesPanel(props: Props) {
  return <AccountCashCasesScopePanel key={`${props.scope.ledgerBookId}:${props.scope.accountId}:${props.scope.baseCurrency}`} {...props} />;
}

function AccountCashCasesScopePanel({ scope: incomingScope, context, onDirtyChange }: Props) {
  // The outer key owns scope lifetime; parent render identities must not restart reads.
  const [scope] = useState(incomingScope);
  const id = useId();
  const [cases, setCases] = useState<AccountCashReconciliationCase[]>([]);
  const [selectedId, setSelectedId] = useState("");
  const [runs, setRuns] = useState<Record<string, AccountCashReconciliationRun>>({});
  const [runErrors, setRunErrors] = useState<Record<string, string>>({});
  const [loading, setLoading] = useState(true);
  const [historyUnavailable, setHistoryUnavailable] = useState(false);
  const [busy, setBusy] = useState(false);
  const [readingFile, setReadingFile] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [reload, setReload] = useState(0);
  const [preview, setPreview] = useState<AccountCashReconciliationRequest | null>(null);
  const [fileName, setFileName] = useState("");
  const [assignmentDirty, setAssignmentDirty] = useState(false);
  const [assignmentConflict, setAssignmentConflict] = useState(false);
  const active = useRef<AbortController | null>(null);
  const mutation = useRef(false);
  const fileSequence = useRef(0);
  const dirty = preview !== null || assignmentDirty || busy || readingFile;
  useEffect(() => { onDirtyChange?.(dirty); }, [dirty, onDirtyChange]);
  useEffect(() => () => onDirtyChange?.(false), [onDirtyChange]);
  useEffect(() => {
    const controller = new AbortController();
    active.current = controller;
    return () => controller.abort();
  }, []);
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    api.listCashCases(scope, { signal: controller.signal }).then(async next => {
      if (controller.signal.aborted) return;
      if (next.some(item => !scopeMatches(item.scope, scope))) throw new Error("Cash cases belong to a different account or ledger book. Refresh the selected scope.");
      setCases(next);
      setHistoryUnavailable(false);
      setSelectedId(current => next.some(item => item.caseId === current) ? current : "");
      setAssignmentConflict(false);
      setLoading(false);
      const latestIds = [...new Set(next.flatMap(item => item.history.at(-1)?.runId ?? []))];
      await Promise.allSettled(latestIds.map(async runId => {
        try {
          const run = await api.replayCashRun(scope, runId, { signal: controller.signal });
          if (controller.signal.aborted) return;
          if (!scopeMatches(run.input.scope, scope) || run.input.runId !== runId) throw new Error("A retained comparison returned a different scope or run identity.");
          setRuns(current => ({ ...current, [runId]: run }));
          setRunErrors(current => { const nextErrors = { ...current }; delete nextErrors[runId]; return nextErrors; });
        } catch (reason) {
          if (!controller.signal.aborted) setRunErrors(current => ({ ...current, [runId]: errorMessage(reason) }));
        }
      }));
    }).catch(reason => { if (!controller.signal.aborted) { setHistoryUnavailable(true); setError(errorMessage(reason)); } })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [scope, reload]);

  async function importHandoff(file: File | undefined) {
    if (!file || mutation.current) return;
    const sequence = ++fileSequence.current;
    const signal = active.current!.signal;
    setReadingFile(true); setError(""); setMessage(""); setPreview(null); setFileName("");
    try {
      if (!file.name.toLowerCase().endsWith(".json")) throw new Error("Select a typed reconciliation handoff .json file.");
      if (file.size > 1024 * 1024) throw new Error("The reconciliation handoff must be no larger than 1 MiB.");
      const text = await readFile(file, signal);
      const request = api.parseCashComparisonImport(text, scope);
      if (signal.aborted || sequence !== fileSequence.current) return;
      setPreview(request); setFileName(file.name);
    } catch (reason) { if (!signal.aborted && sequence === fileSequence.current) setError(errorMessage(reason)); }
    finally { if (!signal.aborted && sequence === fileSequence.current) setReadingFile(false); }
  }

  async function capture() {
    // A capture refreshes case versions and remounts the selected case, so an unsaved owner assignment would be lost.
    if (!preview || !context.canWrite || mutation.current || readingFile || assignmentDirty) return;
    mutation.current = true;
    const signal = active.current!.signal;
    setBusy(true); setError(""); setMessage("");
    try {
      const run = await api.runCashComparison(scope, preview, { signal });
      if (signal.aborted) return;
      if (!scopeMatches(run.input.scope, scope) || run.input.runId !== preview.runId) throw new Error("The captured comparison returned a different scope or run identity. Refresh before retrying.");
      setRuns(current => ({ ...current, [run.input.runId]: run }));
      setPreview(null); setFileName("");
      setMessage(`Comparison ${run.input.runId} retained. Cash cases are refreshing.`);
      setReload(value => value + 1);
    } catch (reason) {
      if (!signal.aborted) setError(`${errorMessage(reason)} The imported run identity is retained for a retry; check the retained history before replacing this handoff.`);
    } finally { if (!signal.aborted) { mutation.current = false; setBusy(false); } }
  }

  async function assign(item: AccountCashReconciliationCase, owner: string, reason: string) {
    if (!context.canWrite || mutation.current || assignmentConflict || !owner.trim() || !reason.trim()) return;
    mutation.current = true;
    const signal = active.current!.signal;
    setBusy(true); setError(""); setMessage("");
    try {
      const next = await api.assignCashCase(scope, item.caseId, { expectedVersion: item.version, owner: owner.trim(), reason: reason.trim() }, { signal });
      if (signal.aborted) return;
      if (!scopeMatches(next.scope, scope) || next.caseId !== item.caseId) throw new Error("The assignment returned a different case or account scope. Refresh retained cases.");
      setCases(current => current.map(entry => entry.caseId === item.caseId ? next : entry));
      setAssignmentDirty(false); setMessage(`Owner assignment retained for ${next.caseId}.`);
    } catch (reason) {
      if (!signal.aborted) {
        const conflict = isApiError(reason) && reason.status === 409;
        setAssignmentConflict(conflict);
        setError(conflict ? "This case changed after it was loaded. Refresh retained cases, inspect the current version, then submit the assignment again." : errorMessage(reason));
      }
    } finally { if (!signal.aborted) { mutation.current = false; setBusy(false); } }
  }

  async function replay(runId: string) {
    if (mutation.current) return;
    mutation.current = true;
    const signal = active.current!.signal;
    setBusy(true); setError(""); setMessage("");
    try {
      const run = await api.replayCashRun(scope, runId, { signal });
      if (signal.aborted) return;
      if (!scopeMatches(run.input.scope, scope) || run.input.runId !== runId) throw new Error("The replay returned a different account scope or run identity.");
      setRuns(current => ({ ...current, [runId]: run }));
      setRunErrors(current => { const next = { ...current }; delete next[runId]; return next; });
      setMessage(`Retained run ${runId} reproduced from its frozen source rows and tolerance version.`);
    } catch (reason) { if (!signal.aborted) setError(errorMessage(reason)); }
    finally { if (!signal.aborted) { mutation.current = false; setBusy(false); } }
  }

  const selected = cases.find(item => item.caseId === selectedId);
  return <Card className="panel-surface min-w-0" role="region" aria-label="Account cash reconciliation cases" aria-busy={loading || busy}>
    <CardHeader><CardTitle>Cash reconciliation continuity</CardTitle><CardDescription>Retain one cash-balance case per native currency and independent source pair. A corrected source updates the same case; source clearance remains separate from governed close sign-off.</CardDescription></CardHeader>
    <CardContent className="space-y-4">
      {!context.canWrite ? <StatusBanner title={<span className="text-foreground">Read-only cash oversight</span>} detail="You can inspect retained history and reproduce comparisons. Capturing comparisons and assigning owners require ledger management permission." /> : null}
      {error ? <StatusBanner role="alert" tone="danger" title={<span className="text-foreground">Cash oversight needs attention</span>} detail={error} /> : null}
      {message ? <StatusBanner role="status" tone="success" title={<span className="text-foreground">{message}</span>} /> : null}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-muted-foreground">{loading ? "Loading authoritative retained cases…" : historyUnavailable ? "Retained cash history is unavailable. Refresh to retry; previously loaded cases remain visible." : `${cases.length} retained cash ${cases.length === 1 ? "case" : "cases"}`}</p>
        <Button variant="outline" disabled={loading || busy || readingFile} onClick={() => { setError(""); setMessage(""); setReload(value => value + 1); }}>Refresh retained cases</Button>
      </div>
      <DenseDataTable<AccountCashReconciliationCase>
        ariaLabel="Retained account cash cases" rows={cases} getRowId={item => item.caseId}
        selectedRowId={selectedId} onRowSelect={item => { if (!busy && !assignmentDirty) setSelectedId(item.caseId); }}
        getRowAriaLabel={item => `${item.currency} cash case ${item.caseId}, ${item.state}, owner ${item.owner}`}
        emptyText={loading ? "Loading retained cash cases…" : historyUnavailable ? "Retained cash cases could not be loaded. Refresh retained cases to retry." : "No retained cash cases for this account and book. Import a validated source handoff to capture the first comparison."}
        columns={[
          { id: "case", label: "Stable case", render: item => <span className="break-all font-mono">{item.caseId}</span> },
          { id: "currency", label: "Native currency", render: item => item.currency },
          { id: "state", label: "Source state", render: item => item.state },
          { id: "owner", label: "Owner", render: item => item.owner },
          { id: "aging", label: "Aging", render: item => `${item.agingDays} ${item.agingDays === 1 ? "day" : "days"}` },
          { id: "tolerance", label: "Executed tolerance", render: item => {
            const runId = item.history.at(-1)?.runId;
            const run = runId ? runs[runId] : null;
            return run ? `${run.input.tolerance.profileId} · v${run.input.tolerance.version}` : runId && runErrors[runId] ? "Unavailable — inspect observation" : "Loading retained version…";
          } }
        ]}
      />
      {assignmentDirty ? <p role="status" className="text-sm text-warning">Save or discard the owner assignment before selecting another case or capturing a comparison.</p> : null}
      {selected ? <CashCaseDetail key={`${selected.caseId}:${selected.version}`} item={selected} runs={runs} runErrors={runErrors} busy={busy} canWrite={context.canWrite} conflict={assignmentConflict} onAssign={(owner, reason) => void assign(selected, owner, reason)} onReplay={runId => void replay(runId)} onDirtyChange={setAssignmentDirty} /> : null}
      <TechnicalDetails label="Import next cash comparison" open={preview !== null}>
        <div className="space-y-4">
          <p className="text-sm text-muted-foreground">Import a typed handoff with two complete independent cash-balance sources, original row payloads, source versions, evidence references and the executed tolerance profile. Decimal amounts must be strings. The authenticated server binds tenant, company and actor; the selected account and book must match every row.</p>
          <FormRow label="Reconciliation handoff (.json)" labelFor={`${id}-file`} hint="Preview validates the handoff locally. Capture submits it to the authoritative service; no case changes before capture.">
            <Input id={`${id}-file`} type="file" accept=".json,application/json" disabled={!context.canWrite || busy || readingFile} onChange={event => { const file = event.target.files?.[0]; event.target.value = ""; void importHandoff(file); }} />
          </FormRow>
          {readingFile ? <p role="status" className="text-sm">Validating the reconciliation handoff…</p> : null}
          {preview ? <section aria-label="Cash comparison preview" className="space-y-4">
            <p className="text-sm font-semibold">Validated handoff: {fileName}</p>
            <dl className="grid grid-cols-1 gap-3 text-sm md:grid-cols-3">
              <Fact label="Run identity" value={preview.runId} /><Fact label="Business date" value={preview.businessDate} /><Fact label="Observed at (UTC)" value={preview.observedAtUtc} />
              <Fact label="Initial owner" value={preview.initialOwner} /><Fact label="Tolerance" value={`${preview.tolerance.profileId} · v${preview.tolerance.version}`} /><Fact label="Account / book" value={`${scope.accountId} / ${scope.ledgerBookId}`} />
            </dl>
            <CashSource source={preview.left} side="Left" /><CashSource source={preview.right} side="Right" />
            <Tolerance run={preview} />
            <div className="flex flex-wrap gap-2"><Button disabled={!context.canWrite || busy || readingFile || loading || assignmentDirty} onClick={() => void capture()}>{busy ? "Capturing comparison…" : "Capture cash comparison"}</Button><Button variant="outline" disabled={busy} onClick={() => { setPreview(null); setFileName(""); }}>Discard handoff</Button></div>
          </section> : null}
        </div>
      </TechnicalDetails>
    </CardContent>
  </Card>;
}

function Fact({ label, value }: { label: string; value: string }) {
  return <div className="min-w-0"><dt className="text-xs text-muted-foreground">{label}</dt><dd className="break-all font-mono">{value}</dd></div>;
}

function CashCaseDetail({ item, runs, runErrors, busy, canWrite, conflict, onAssign, onReplay, onDirtyChange }: {
  item: AccountCashReconciliationCase; runs: Record<string, AccountCashReconciliationRun>; runErrors: Record<string, string>;
  busy: boolean; canWrite: boolean; conflict: boolean; onAssign: (owner: string, reason: string) => void;
  onReplay: (runId: string) => void; onDirtyChange: (dirty: boolean) => void;
}) {
  const id = useId();
  const [runId, setRunId] = useState(item.history.at(-1)?.runId ?? "");
  const [owner, setOwner] = useState(item.owner);
  const [reason, setReason] = useState("");
  const run = runs[runId];
  const dirty = owner !== item.owner || reason !== "";
  useEffect(() => { onDirtyChange(dirty); }, [dirty, onDirtyChange]);
  useEffect(() => () => onDirtyChange(false), [onDirtyChange]);
  function submit(event: FormEvent) { event.preventDefault(); onAssign(owner, reason); }
  return <section className="space-y-4 rounded-[2px] border border-border p-4" aria-label={`Cash case ${item.caseId}`}>
    <dl className="grid grid-cols-1 gap-3 text-sm md:grid-cols-3">
      <Fact label="Stable case identity" value={item.caseId} /><Fact label="Source pair" value={`${item.leftSourceId} / ${item.rightSourceId}`} /><Fact label="Current source state" value={item.state} />
      <Fact label="First observed (UTC)" value={item.firstObservedAtUtc} /><Fact label="Last observed (UTC)" value={item.lastObservedAtUtc} /><Fact label="Retained resolution" value={item.resolvedAtUtc ? `${item.resolvedAtUtc} · run ${item.resolutionRunId ?? "Unavailable"}` : "Unresolved"} />
    </dl>
    <form onSubmit={submit} aria-label="Assign cash case owner" className="space-y-3">
      <FormGrid columns={2}>
        <FormRow label="Case owner" labelFor={`${id}-owner`}><Input id={`${id}-owner`} value={owner} onChange={event => setOwner(event.target.value)} required disabled={!canWrite || busy || conflict} /></FormRow>
        <FormRow label="Assignment rationale" labelFor={`${id}-reason`} hint={`Assignment requires retained case version ${item.version}.`}><Input id={`${id}-reason`} value={reason} onChange={event => setReason(event.target.value)} required disabled={!canWrite || busy || conflict} /></FormRow>
      </FormGrid>
      <div className="flex flex-wrap gap-2"><Button type="submit" disabled={!canWrite || busy || conflict || !owner.trim() || !reason.trim()}>Retain owner assignment</Button>{dirty ? <Button variant="outline" disabled={busy} onClick={() => { setOwner(item.owner); setReason(""); }}>Discard assignment</Button> : null}</div>
    </form>
    <Tabs tabs={[{ id: `${id}-observations`, label: "Observation history" }, { id: `${id}-assignments`, label: "Owner history" }]}>
      <div className="space-y-4">
        <ol aria-label="Cash case observation history" className="space-y-2 text-sm">{item.history.map((observation, index) => <li key={`${observation.runId}:${index}`} className="rounded-[2px] border border-border px-3 py-2"><span className="font-semibold">{observation.state}</span> · {observation.observedAtUtc} · run <span className="break-all font-mono">{observation.runId}</span><TechnicalDetails label={`Result hash for ${observation.runId}`}><code className="break-all text-xs">{observation.resultHash}</code></TechnicalDetails></li>)}</ol>
        <FormRow label="Retained observation" labelFor={`${id}-run`}><Select id={`${id}-run`} value={runId} disabled={busy} onChange={event => setRunId(event.target.value)}>{item.history.map(observation => <option key={observation.runId} value={observation.runId}>{observation.observedAtUtc} · {observation.state} · {observation.runId}</option>)}</Select></FormRow>
        <Button variant="outline" disabled={busy || !runId} onClick={() => onReplay(runId)}>Reproduce retained run</Button>
        {runErrors[runId] ? <StatusBanner tone="warning" title={<span className="text-foreground">Retained comparison could not be reproduced</span>} detail={runErrors[runId]} /> : null}
        {run ? <RetainedRun run={run} /> : <p className="text-sm text-muted-foreground">Reproduce this observation to inspect its frozen source rows, evidence and executed tolerance.</p>}
      </div>
      <ol aria-label="Cash case owner history" className="space-y-2 text-sm">{item.assignments.map((assignment, index) => <li key={`${assignment.assignedAtUtc}:${index}`} className="rounded-[2px] border border-border px-3 py-2"><span className="font-semibold">{assignment.owner}</span> · {assignment.assignedAtUtc} · by {assignment.actor}<p>{assignment.reason}</p></li>)}{item.assignments.length === 0 ? <li>No owner changes retained. Initial owner: {item.owner}.</li> : null}</ol>
    </Tabs>
  </section>;
}

function CashSource({ source, side }: { source: AccountCashBalanceSource; side: string }) {
  return <section aria-label={`${side} cash source`} className="space-y-3">
    <h4 className="text-sm font-semibold">{side}: {source.sourceId} · {sourceNames[source.sourceType] ?? source.sourceType} · {source.version}</h4>
    <dl className="grid grid-cols-1 gap-3 text-sm md:grid-cols-3"><Fact label="Source captured at (UTC)" value={source.capturedAtUtc} /><Fact label="Evidence reference" value={source.evidenceReference} /><Fact label="Population" value={source.completePopulation ? "Complete" : "Incomplete"} /></dl>
    <DenseDataTable ariaLabel={`${side} source cash rows`} rows={source.rows} getRowId={entry => entry.row.cashEntryId} emptyText="No source cash rows." columns={[
      { id: "id", label: "Source row", render: entry => entry.row.cashEntryId },
      { id: "account", label: "Account", render: entry => <span className="break-all font-mono">{entry.row.accountId}</span> },
      { id: "native", label: "Native balance", align: "right", render: entry => <span className="font-mono">{entry.row.amount} {entry.row.currency}</span> },
      { id: "base", label: "Base balance", align: "right", render: entry => <span className="font-mono">{entry.row.amountBase} {entry.row.baseCurrency}</span> },
      { id: "date", label: "Accounting date / posted UTC", render: entry => `${entry.row.accountingPeriod} / ${entry.row.postedAtUtc}` }
    ]} />
    <TechnicalDetails label={`${side} original source payloads and row references`}>{source.rows.map(entry => <div key={entry.row.cashEntryId} className="mb-3 space-y-2 text-xs"><p className="break-all">{entry.row.cashEntryId} · {entry.row.sourceReference}</p><pre className="max-h-64 overflow-auto whitespace-pre-wrap break-all rounded-[2px] border border-border p-3">{entry.originalPayload}</pre><pre className="max-h-64 overflow-auto whitespace-pre-wrap break-all">{JSON.stringify(entry.row, null, 2)}</pre></div>)}</TechnicalDetails>
  </section>;
}

function Tolerance({ run }: { run: AccountCashReconciliationRequest }) {
  return <TechnicalDetails label={`Executed tolerance ${run.tolerance.profileId} · version ${run.tolerance.version}`}>
    <DenseDataTable ariaLabel="Executed cash tolerance rules" rows={run.tolerance.cashRules} getRowId={rule => rule.ruleId} emptyText="No cash tolerance rules retained." columns={[
      { id: "rule", label: "Rule", render: rule => rule.ruleId },
      { id: "absolute", label: "Absolute tolerance", render: rule => rule.absoluteCashTolerance },
      { id: "bps", label: "Basis points", render: rule => rule.basisPointCashTolerance ?? "Not applied" },
      { id: "settlement", label: "Settlement date tolerance", render: rule => rule.settlementDateTolerance }
    ]} />
    <TechnicalDetails label="Complete frozen tolerance profile"><pre className="max-h-64 overflow-auto whitespace-pre-wrap break-all text-xs">{JSON.stringify(run.tolerance, null, 2)}</pre></TechnicalDetails>
  </TechnicalDetails>;
}

function RetainedRun({ run }: { run: AccountCashReconciliationRun }) {
  return <section aria-label={`Retained comparison ${run.input.runId}`} className="space-y-4">
    <dl className="grid grid-cols-1 gap-3 text-sm md:grid-cols-3"><Fact label="Business date" value={run.input.businessDate} /><Fact label="Input hash" value={run.inputHash} /><Fact label="Result hash" value={run.resultHash} /></dl>
    {run.comparisons.map(comparison => <section key={comparison.currency} className="space-y-3" aria-label={`${comparison.currency} comparison evidence`}>
      <h4 className="text-sm font-semibold">{comparison.currency} · {comparison.observationState}</h4>
      {comparison.breaks.map(item => <StatusBanner key={item.breakId} tone="warning" title={<span className="text-foreground">{item.reason}</span>} detail={`Break ${item.breakId} · rule ${item.ruleId}`} />)}
      <DenseDataTable ariaLabel={`${comparison.currency} matching evidence`} rows={comparison.evidence} getRowId={evidence => evidence.evidenceId} emptyText="No matching evidence retained." columns={[
        { id: "rule", label: "Stage / rule", render: evidence => `${evidence.stage} / ${evidence.ruleId}` },
        { id: "narrative", label: "Evidence", render: evidence => evidence.narrative },
        { id: "delta", label: "Retained delta", align: "right", render: evidence => evidence.numericDelta ?? "Not applicable" }
      ]} />
      <TechnicalDetails label={`${comparison.currency} complete match and break evidence`}><pre className="max-h-64 overflow-auto whitespace-pre-wrap break-all text-xs">{JSON.stringify(comparison, null, 2)}</pre></TechnicalDetails>
    </section>)}
    <Tolerance run={run.input} /><CashSource source={run.input.left} side="Left" /><CashSource source={run.input.right} side="Right" />
  </section>;
}

function readFile(file: File, signal: AbortSignal): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    const abort = () => { reader.abort(); reject(new DOMException("File import aborted.", "AbortError")); };
    signal.addEventListener("abort", abort, { once: true });
    reader.onload = () => { signal.removeEventListener("abort", abort); resolve(String(reader.result ?? "")); };
    reader.onerror = () => { signal.removeEventListener("abort", abort); reject(new Error("The reconciliation handoff could not be read.")); };
    reader.onabort = () => signal.removeEventListener("abort", abort);
    reader.readAsText(file);
  });
}
