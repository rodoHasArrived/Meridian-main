import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { getCorporateActionCaseDeadlines, importCorporateActionDeadlines, previewCorporateActionDeadlineImport, recordCorporateActionDeadline } from "@/lib/api/corporate-action-calendar.api";
import { CORPORATE_ACTION_DATE_KINDS } from "@/screens/corporate-action-calendar.view-state";
import type { CorporateActionCaseDeadlines, CorporateActionDeadlineHistory, CorporateActionDeadlineImportPreview, CorporateActionDeadlineImportRequest, CorporateActionElectionDeadline, RecordCorporateActionDeadlineRequest } from "@/types/corporate-action-calendar.types";
import { DenseDataTable } from "@/components/meridian/ui-kit-primitives";
import { isApiError } from "@/lib/api-errors";

export const DEADLINE_CSV_HEADER = "seriesId,kind,sourceParty,date,at,timeZoneId,financialAccountId,custodyAccountId,evidenceReference,evidenceHash,rationale,supersedesEvidenceId,withdrawn,sourceText";
type Command = { kind: "manual"; request: RecordCorporateActionDeadlineRequest } | { kind: "csv"; request: CorporateActionDeadlineImportRequest };
const message = (error: unknown) => error instanceof Error ? error.message : "The deadline operation could not be completed.";
const newDeadline = (): CorporateActionElectionDeadline => ({ seriesId: crypto.randomUUID(), kind: "IssuerElection", sourceParty: "", date: "", at: null, timeZoneId: "", financialAccountId: "", custodyAccountId: "", withdrawn: false, sourceText: "" });

/** Each instance belongs to exactly one loaded case/scope. Writes retain their original command for retries. */
export function CorporateActionDeadlinePanel(props: { caseId: string; onChanged: () => void }) {
  return <DeadlineCaseEditor key={props.caseId} {...props} />;
}
function DeadlineCaseEditor({ caseId, onChanged }: { caseId: string; onChanged: () => void }) {
  const [data, setData] = useState<CorporateActionCaseDeadlines | null>(null);
  const [loading, setLoading] = useState(true);
  const [readError, setReadError] = useState<string | null>(null);
  const [revision, setRevision] = useState(0);
  const [deadline, setDeadline] = useState(newDeadline);
  const [reference, setReference] = useState("");
  const [digest, setDigest] = useState("");
  const [rationale, setRationale] = useState("");
  const [csv, setCsv] = useState(`${DEADLINE_CSV_HEADER}\n`);
  const [preview, setPreview] = useState<{ request: CorporateActionDeadlineImportRequest; result: CorporateActionDeadlineImportPreview } | null>(null);
  const [command, setCommand] = useState<Command | null>(null);
  const [busy, setBusy] = useState(false);
  const [dispatched, setDispatched] = useState(false);
  const [inspected, setInspected] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const active = useRef(true);
  const latestRead = useRef(0);
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  useEffect(() => {
    const requestRevision = ++latestRead.current;
    const controller = new AbortController();
    setLoading(true); setReadError(null);
    getCorporateActionCaseDeadlines(caseId, { signal: controller.signal }).then((result) => {
      if (active.current && latestRead.current === requestRevision) {
        if (result.case.caseId !== caseId) throw new Error("The deadline response identifies a different case.");
        setData(result); setLoading(false);
      }
    }).catch((error: unknown) => {
      if (active.current && latestRead.current === requestRevision) { setReadError(message(error)); setLoading(false); }
    });
    return () => controller.abort();
  }, [caseId, revision]);
  const locked = busy || Boolean(command);
  const canDraft = Boolean(data?.canRecord && !loading && !readError && !locked);
  const baseRequest = () => {
    if (!data) throw new Error("Load the authoritative case before preparing deadline evidence.");
    return { caseId, expectedVersion: data.case.version, idempotencyKey: crypto.randomUUID(), tenantId: data.case.scope.tenantId, companyId: data.case.scope.companyId, scopeAssertion: data.case.scope };
  };
  const updateDeadline = (values: Partial<CorporateActionElectionDeadline>) => { setDeadline((current) => ({ ...current, ...values })); setNotice(null); };
  const seedHistory = (history: CorporateActionDeadlineHistory, withdrawn: boolean) => {
    setDeadline({ ...history.deadline, supersedesEvidenceId: history.evidenceId, withdrawn });
    setReference(""); setDigest(""); setRationale(""); setNotice(withdrawn ? "Review and record the withdrawal with its own supporting source evidence." : "Correction retains the same series and applicability. Supply the corrected deadline and supporting evidence.");
  };
  async function send(exact: Command) {
    if (!data || busy || loading) return;
    const retryingUnresolvedCommand = dispatched;
    setCommand(exact); setBusy(true); setDispatched(true); setInspected(false); setNotice(null);
    try {
      if (exact.kind === "manual") {
        const result = await recordCorporateActionDeadline(exact.request);
        if (!active.current) return;
        if (result.case.caseId !== caseId || Object.entries(exact.request.scopeAssertion).some(([field, value]) => Reflect.get(result.case.scope, field) !== value)) throw new Error("The mutation receipt identifies a different case or scope; inspect retained history before retrying.");
        setNotice(`Deadline evidence ${result.evidence.evidenceId} retained at case version ${result.case.version}${result.replayed ? " (replayed)" : ""}.`);
      } else {
        const result = await importCorporateActionDeadlines(exact.request);
        if (!active.current) return;
        if (!Array.isArray(result.results) || result.committedCount !== result.results.length || result.results.some((row) => row.case.caseId !== caseId || Object.entries(exact.request.scopeAssertion).some(([field, value]) => Reflect.get(row.case.scope, field) !== value)) || result.complete && preview && result.committedCount !== preview.result.rows.length)
          throw new Error("The import receipt does not match the reviewed case, scope or row population. Inspect retained history before retrying.");
        if (!result.complete) { setNotice(`${result.committedCount} rows retained; import stopped at row ${result.failedRow ?? "unknown"}: ${result.error ?? "Inspect retained history."} Retry uses the original CSV and keys.`); return; }
        setNotice(`${result.committedCount} deadline rows retained. Replayed rows reuse their original evidence identities.`);
      }
      setCommand(null); setDispatched(false); setPreview(null); setDeadline(newDeadline()); setReference(""); setDigest(""); setRationale("");
      setRevision((value) => value + 1); onChanged();
    } catch (error) {
      if (active.current) {
        if (!retryingUnresolvedCommand && isApiError(error) && [400, 403, 404].includes(error.status)) {
          setCommand(null); setDispatched(false); setNotice(`Deadline rejected: ${message(error)} No evidence was committed by this request. The source draft remains available for correction.`);
        } else setNotice(`Outcome requires inspection: ${message(error)} The exact request and idempotency key remain available for retry.`);
      }
    } finally { if (active.current) setBusy(false); }
  }
  async function inspect() {
    setBusy(true);
    try {
      const result = await getCorporateActionCaseDeadlines(caseId);
      if (!active.current) return;
      if (result.case.caseId !== caseId) throw new Error("A different case was returned.");
      setData(result); setInspected(true); setNotice("Fresh retained history loaded. Compare the original request with retained evidence before releasing the local retry."); onChanged();
    } catch (error) { if (active.current) setNotice(message(error)); }
    finally { if (active.current) setBusy(false); }
  }
  async function previewCsv() {
    if (!canDraft) return;
    const request = { ...baseRequest(), csv };
    setBusy(true); setNotice(null); setPreview(null);
    try {
      const result = await previewCorporateActionDeadlineImport(request);
      if (active.current) setPreview({ request, result });
    } catch (error) { if (active.current) setNotice(message(error)); }
    finally { if (active.current) setBusy(false); }
  }
  return <section className="space-y-3 rounded-sm border border-border p-3" aria-label={`Election deadlines for case ${caseId}`}>
    <div className="flex flex-wrap items-center justify-between gap-2"><h4 className="text-sm font-semibold">Sourced election deadlines</h4><Button size="sm" variant="outline" disabled={busy || Boolean(command)} onClick={() => { setPreview(null); setRevision((value) => value + 1); }}>Refresh deadlines</Button></div>
    <p className="break-all font-mono text-xs">Case {caseId}{data ? ` · ${data.case.state} · version ${data.case.version} · ${data.case.scope.tenantId} / ${data.case.scope.companyId}` : ""}</p>
    <p className="text-xs text-muted-foreground">Issuer, broker and custodian cutoffs remain distinct. Captured evidence is operator-recorded; the provided digest does not verify external source bytes.</p>
    {loading ? <p role="status">Loading authoritative deadline history…</p> : null}
    {readError ? <p role="status">Deadline history unavailable: {readError}{data ? " Prior retained history remains visible; recording requires a successful refresh." : ""}</p> : null}
    {data && !data.canRecord ? <p role="status">{data.blocker ?? "Deadline recording is unavailable for this case."}</p> : null}
    {notice ? <p role="status" className="text-sm">{notice}</p> : null}
    {data ? <div className="space-y-2">
      <details><summary className="cursor-pointer text-xs font-medium">Case source snapshot</summary><p className="mt-1 break-words text-xs">{data.case.sourceSnapshot ? `${data.case.sourceSnapshot.providerIdentity.providerId} · ${data.case.sourceSnapshot.providerIdentity.sourceEventId} · version ${data.case.sourceSnapshot.providerIdentity.sourceEventVersion} · observed ${data.case.sourceSnapshot.providerIdentity.observedAtUtc}` : "No retained source snapshot was supplied."}</p></details>
      <details open><summary className="cursor-pointer text-xs font-medium">Retained deadline history ({data.history.length})</summary>{data.history.length === 0 ? <p className="mt-2 text-xs">No election deadlines have been recorded for this case.</p> : <ul className="mt-2 space-y-2">{data.history.map((history) => <li key={history.evidenceId} className="rounded-sm border border-border p-2 text-xs">
        <p className="font-medium">{CORPORATE_ACTION_DATE_KINDS[history.deadline.kind]?.label ?? history.deadline.kind} · {history.deadline.sourceParty} · {history.deadline.withdrawn ? "Withdrawn" : history.deadline.date ?? history.deadline.at} {history.deadline.timeZoneId}</p>
        <p>{history.isCurrent ? "Current series entry" : "Superseded evidence"} · Operator-recorded · {history.recordedBy} · {history.recordedAtUtc}</p>
        <p className="break-all font-mono">Evidence {history.evidenceId} · series {history.deadline.seriesId} · {history.evidenceReference} · claimed SHA-256 {history.evidenceHash ?? "unreported"}</p>
        <p className="whitespace-pre-wrap">Original source text: {history.deadline.sourceText ?? "Not retained"}</p><p>{history.rationale}</p>
        <p>Financial account {history.deadline.financialAccountId ?? "unscoped"} · custody account {history.deadline.custodyAccountId ?? "unscoped"}</p>
        {history.isCurrent && !history.deadline.withdrawn ? <div className="mt-1 flex gap-2"><Button size="sm" variant="outline" disabled={!canDraft} onClick={() => seedHistory(history, false)}>Prepare correction</Button><Button size="sm" variant="outline" disabled={!canDraft} onClick={() => seedHistory(history, true)}>Prepare withdrawal</Button></div> : null}
      </li>)}</ul>}</details>
      {command ? <div className="space-y-2 rounded-sm border border-primary/40 p-3">
        <p className="text-xs font-medium">{dispatched ? "Original request retained for inspection / retry" : "Review exact deadline request"} · version {command.request.expectedVersion} · key {command.request.idempotencyKey}</p>
        <pre className="max-h-52 overflow-auto whitespace-pre-wrap break-all text-xs">{JSON.stringify(command.request, null, 2)}</pre>
        <div className="flex flex-wrap gap-2"><Button size="sm" disabled={busy || loading || !data.canRecord} onClick={() => void send(command)}>{dispatched ? "Retry same request" : "Record reviewed deadline"}</Button>{!dispatched ? <Button size="sm" variant="outline" disabled={busy} onClick={() => setCommand(null)}>Edit draft</Button> : <Button size="sm" variant="outline" disabled={busy} onClick={() => void inspect()}>Inspect retained case</Button>}{inspected ? <Button size="sm" variant="outline" disabled={busy} onClick={() => { setCommand(null); setDispatched(false); setPreview(null); setNotice("Local retry released after inspection. Retained rows remain in history; prepare only unresolved evidence in a new draft."); }}>Release local retry after inspection</Button> : null}</div>
      </div> : null}
      <details><summary className="cursor-pointer text-sm font-medium">Capture a sourced deadline</summary>
        <form className="mt-3 space-y-3" onSubmit={(event) => { event.preventDefault(); if (canDraft) { setCommand({ kind: "manual", request: { ...baseRequest(), deadline, evidenceReference: reference, evidenceHash: digest, rationale } }); setDispatched(false); } }}>
          <fieldset disabled={!canDraft} className="grid gap-3 md:grid-cols-2">
            <label className="grid gap-1 text-xs">Deadline authority<Select aria-label="Deadline authority" value={deadline.kind} disabled={Boolean(deadline.supersedesEvidenceId)} onChange={(event) => updateDeadline({ kind: event.target.value as CorporateActionElectionDeadline["kind"] })}><option value="IssuerElection">Issuer election</option><option value="BrokerElection">Broker election</option><option value="CustodianElection">Custodian election</option></Select></label>
            <label className="grid gap-1 text-xs">Source party<Input required value={deadline.sourceParty} disabled={Boolean(deadline.supersedesEvidenceId)} onChange={(event) => updateDeadline({ sourceParty: event.target.value })} /></label>
            <label className="grid gap-1 text-xs">Date representation<Select aria-label="Date representation" value={deadline.at != null ? "instant" : "date"} disabled={deadline.withdrawn} onChange={(event) => updateDeadline(event.target.value === "date" ? { date: "", at: null } : { date: null, at: "" })}><option value="date">Source date only</option><option value="instant">Source timestamp with offset</option></Select></label>
            {deadline.at != null ? <label className="grid gap-1 text-xs">Source timestamp with explicit offset<Input required={!deadline.withdrawn} disabled={deadline.withdrawn} placeholder="2026-11-12T17:00:00-07:00" value={deadline.at} onChange={(event) => updateDeadline({ at: event.target.value })} /></label> : <label className="grid gap-1 text-xs">Source deadline date<Input type="date" required={!deadline.withdrawn} disabled={deadline.withdrawn} value={deadline.date ?? ""} onChange={(event) => updateDeadline({ date: event.target.value })} /></label>}
            <label className="grid gap-1 text-xs">Source timezone (optional)<Input value={deadline.timeZoneId ?? ""} onChange={(event) => updateDeadline({ timeZoneId: event.target.value })} placeholder="America/Phoenix" /></label>
            <label className="grid gap-1 text-xs">Financial account applicability<Input value={deadline.financialAccountId ?? ""} disabled={Boolean(deadline.supersedesEvidenceId)} onChange={(event) => updateDeadline({ financialAccountId: event.target.value })} /></label>
            <label className="grid gap-1 text-xs">Custody account applicability<Input value={deadline.custodyAccountId ?? ""} disabled={Boolean(deadline.supersedesEvidenceId)} onChange={(event) => updateDeadline({ custodyAccountId: event.target.value })} /></label>
            <label className="grid gap-1 text-xs">Evidence URI<Input required value={reference} onChange={(event) => setReference(event.target.value)} placeholder="document://source-reference" /></label>
            <label className="grid gap-1 text-xs">Operator-provided SHA-256 digest<Input required pattern="[0-9a-f]{64}" value={digest} onChange={(event) => setDigest(event.target.value)} /></label>
            <label className="grid gap-1 text-xs">Source review rationale<Input required value={rationale} onChange={(event) => setRationale(event.target.value)} /></label>
            <label className="grid gap-1 text-xs md:col-span-2">Original source text<textarea aria-label="Original source text" required maxLength={8192} rows={3} className="w-full rounded-sm border border-border bg-background p-2 text-sm" value={deadline.sourceText ?? ""} onChange={(event) => updateDeadline({ sourceText: event.target.value })} /></label>
          </fieldset>
          <p className="text-xs">Series {deadline.seriesId}{deadline.supersedesEvidenceId ? ` · supersedes ${deadline.supersedesEvidenceId}${deadline.withdrawn ? " · withdrawal" : " · correction"}` : " · new source series"}. Dates and amounts of the corporate action are unchanged.</p>
          <div className="flex gap-2"><Button type="submit" size="sm" disabled={!canDraft}>Review deadline</Button><Button type="button" size="sm" variant="outline" disabled={!canDraft} onClick={() => setDeadline(newDeadline())}>Start new deadline series</Button></div>
        </form>
      </details>
      <details><summary className="cursor-pointer text-sm font-medium">Import sourced deadline CSV</summary>
        <div className="mt-3 space-y-2"><p className="text-xs">One case; at most 100 rows. The server validates the canonical header, exact source dates, evidence and correction lineage before acceptance.</p><label className="grid gap-1 text-xs">Deadline CSV<textarea aria-label="Deadline CSV" rows={5} disabled={!canDraft} className="w-full rounded-sm border border-border bg-background p-2 font-mono text-xs" value={csv} onChange={(event) => { setCsv(event.target.value); setPreview(null); }} /></label>
          <Button size="sm" variant="outline" disabled={!canDraft} onClick={() => void previewCsv()}>Preview deadline CSV</Button>
          {preview ? <div className="space-y-2"><p className="break-all text-xs">Case version {preview.result.expectedVersion} · preview {preview.result.previewHash}</p>{preview.result.errors.map((error, index) => <p key={index} role="status" className="text-xs text-danger">{error}</p>)}<DenseDataTable ariaLabel="Deadline CSV preview" rows={preview.result.rows} getRowId={(row) => String(row.row)} emptyText="No valid rows in this preview." maxVisibleRows={null} columns={[
            { id: "row", label: "Row", numeric: true, render: (row) => row.row },
            { id: "authority", label: "Authority / source", render: (row) => `${row.deadline.kind} · ${row.deadline.sourceParty}` },
            { id: "date", label: "Source date", render: (row) => row.deadline.withdrawn ? "Withdrawal" : row.deadline.date ?? row.deadline.at },
            { id: "evidence", label: "Original text / evidence", className: "max-w-80 break-all whitespace-normal", render: (row) => `${row.deadline.sourceText} · ${row.evidenceReference} · claimed digest ${row.evidenceHash}` }
          ]} /><Button size="sm" disabled={!canDraft || preview.result.errors.length > 0 || preview.result.rows.length === 0} onClick={() => void send({ kind: "csv", request: { ...preview.request, previewHash: preview.result.previewHash } })}>Accept reviewed deadline CSV</Button></div> : null}
        </div>
      </details>
    </div> : null}
  </section>;
}
