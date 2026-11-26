import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { FormGrid } from "@/components/ui/form";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { formatTimestamp } from "@/lib/format";
import type { EntityOperationScope } from "@/types/entity-operations";
import type { AgreementWorkItem, AgreementWorkItemAction, AgreementWorkQueue, AgreementWorkQueueScope, ObserveAgreementWorkItem, UpdateAgreementWorkItem } from "@/types/intercompany-work-queue";
import * as api from "@/lib/api/intercompany-work-queue.api";
import { EntityOperationTable, OperationError, OperationField } from "./accounting-screen.entity-operations.shared";

type Services = typeof api;
const agreementLink = (id: string) => `/accounting/configure?agreementId=${encodeURIComponent(id)}#configure-section-agreements`;
const errorText = (error: unknown) => error instanceof Error ? error.message : "The lifecycle queue is unavailable.";
const stateText = (value: string) => value === "AwaitingReview" ? "Awaiting review" : value;
const timeText = (value: string | null) => value ? value.replace("T", " ").replace(/Z$/, " UTC") : "Not yet tracked";

/** Scope-keyed state prevents an earlier book/date response or draft input appearing in a new scope. */
export function IntercompanyAgreementWorkQueue({ scope, asOf, expiryHorizonDays, services = api }: {
  scope: EntityOperationScope; asOf: string; expiryHorizonDays: number; services?: Services;
}) {
  return <ScopedWorkQueue key={`${scope.book.ledgerBookId}:${scope.book.fundProfileId}:${asOf}:${expiryHorizonDays}`}
    scope={scope} asOf={asOf} expiryHorizonDays={expiryHorizonDays} services={services} />;
}

function ScopedWorkQueue({ scope, asOf, expiryHorizonDays, services }: {
  scope: EntityOperationScope; asOf: string; expiryHorizonDays: number; services: Services;
}) {
  const request: AgreementWorkQueueScope = { ledgerBookId: scope.book.ledgerBookId, fundProfileId: scope.book.fundProfileId, asOf, expiryHorizonDays };
  const [result, setResult] = useState<AgreementWorkQueue | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState("");
  const [requiresRefresh, setRequiresRefresh] = useState(false);
  const [action, setAction] = useState<AgreementWorkItemAction>("AssignOwner");
  const [owner, setOwner] = useState("");
  const [reason, setReason] = useState("");
  const [note, setNote] = useState("");
  const [evidence, setEvidence] = useState("");
  const active = useRef<AbortController | null>(null);
  const command = useRef<{ key: string; id: string } | null>(null);
  useEffect(() => () => active.current?.abort(), []);
  const selected = result?.cases.find(item => item.caseId === selectedId) ?? null;
  const current = result?.isComplete === true && result.status === "Current" && !!result.sourceFingerprint;
  const canManage = result?.canManage === true && scope.capabilities.canManageLedger;
  const canAct = !busy && !requiresRefresh && current && canManage;
  const validScope = !!asOf && Number.isInteger(expiryHorizonDays) && expiryHorizonDays >= 0 && expiryHorizonDays <= 3660;
  const entityName = (id: string) => scope.graph?.entities.find(entity => entity.entityId === id)?.name ?? id;
  function identity(body: unknown) {
    const key = JSON.stringify([request, body]);
    if (command.current?.key !== key) command.current = { key, id: crypto.randomUUID() };
    return command.current.id;
  }
  async function load() {
    if (active.current) return;
    const controller = new AbortController(); active.current = controller;
    setBusy(true); setError(null); setResult(null); setNotice("");
    try {
      const value = await services.getAgreementWorkQueue(request, { signal: controller.signal });
      if (!controller.signal.aborted) { setResult(value); setRequiresRefresh(false); }
    } catch (failure) { if (!controller.signal.aborted) { setError(errorText(failure)); setRequiresRefresh(true); } }
    finally { if (!controller.signal.aborted) { setBusy(false); active.current = null; } }
  }
  function select(item: AgreementWorkItem) {
    setSelectedId(item.caseId); setOwner(item.owner ?? ""); setReason(""); setNote(""); setEvidence("");
    setAction(item.state === "Resolved" ? "Reopen" : "AssignOwner");
  }
  async function mutate(observe: boolean) {
    if (!selected || !result || !canAct || !reason.trim() || active.current) return;
    const controller = new AbortController(); active.current = controller;
    let retained = false;
    setBusy(true); setError(null); setNotice("");
    try {
      const common = { caseId: selected.caseId, expectedSourceFingerprint: result.sourceFingerprint, reason: reason.trim(), expectedVersion: selected.version, expectedRecordHash: selected.recordHash };
      let item: AgreementWorkItem;
      if (observe) {
        const body: ObserveAgreementWorkItem = { ...common, commandId: identity(["observe", common]) };
        item = await services.observeAgreementWorkItem(request, body, { signal: controller.signal });
      } else {
        const values = { ...common, expectedRecordHash: selected.recordHash!, action,
          owner: action === "AssignOwner" ? owner.trim() || null : null, note: note.trim() || null,
          evidenceReferences: evidence.split("\n").map(value => value.trim()).filter(Boolean) };
        const body: UpdateAgreementWorkItem = { ...values, commandId: identity(["update", values]) };
        item = await services.updateAgreementWorkItem(request, body, { signal: controller.signal });
      }
      if (controller.signal.aborted) return;
      retained = true;
      command.current = null; setReason(""); setNote(""); setEvidence("");
      setNotice(`Case action retained at version ${item.version}.`);
      // Only a fresh queue observation can authorize the next action, including after a replayed receipt.
      setResult(null); setSelectedId(null); setRequiresRefresh(true);
      const latest = await services.getAgreementWorkQueue(request, { signal: controller.signal });
      if (!controller.signal.aborted) {
        setResult(latest); setSelectedId(item.caseId); setOwner(latest.cases.find(value => value.caseId === item.caseId)?.owner ?? "");
        setRequiresRefresh(false);
      }
    } catch (failure) {
      if (!controller.signal.aborted) {
        setError(retained
          ? `${errorText(failure)} The case action was retained, but the current queue could not be reloaded. Load the queue before another action.`
          : `${errorText(failure)} Reload the queue before another action; your reason and evidence remain available.`);
        setRequiresRefresh(true);
      }
    } finally { if (!controller.signal.aborted) { active.current = null; setBusy(false); } }
  }

  const actionDisabled = !canAct || !selected?.isTracked || !reason.trim()
    || (action === "Resolve" && !evidence.trim()) || (action === "AddNote" && !note.trim() && !evidence.trim())
    || (action === "Reopen" && selected.state !== "Resolved") || (action === "AwaitReview" && selected.state === "Resolved")
    || (action === "Resolve" && selected.state === "Resolved");
  return <section className="space-y-4 border-t border-border pt-4" aria-label="Intercompany lifecycle work queue">
    <div className="space-y-1"><h3 className="font-semibold">Agreement lifecycle work queue</h3>
      <p className="text-sm text-muted-foreground">Track follow-up work on the selected book. Assignments and case decisions retain their history; they do not amend an agreement, settle a balance, or post a journal.</p></div>
    <div className="flex flex-wrap items-center gap-3"><Button type="button" variant="outline" disabled={busy || !validScope || !scope.capabilities.canViewLedger} onClick={() => void load()}>Load lifecycle queue</Button>
      <p className="text-sm">{scope.book.displayName} · as of {asOf} · expiry horizon {Number.isInteger(expiryHorizonDays) ? `${expiryHorizonDays} days` : "not selected"}</p></div>
    <OperationError error={error} />
    {busy ? <p role="status" className="text-sm">Reading or retaining the scoped lifecycle case…</p> : null}
    {notice ? <p role="status" className="text-sm">{notice}</p> : null}
    {result ? <>
      <StatusBanner tone={current ? "info" : "warning"} title={`${result.status} lifecycle observation`}
        detail={`${result.isComplete ? "Complete evidence" : "Incomplete evidence"} · evaluated ${formatTimestamp(result.evaluatedAtUtc)}. A missing finding never resolves a retained case.`} />
      {result.diagnostics.length ? <ul className="list-disc space-y-1 pl-5 text-sm">{result.diagnostics.map((value, index) => <li key={index}>{value}</li>)}</ul> : null}
      {!canManage ? <p className="text-sm text-muted-foreground">Read-only access. Ledger management permission is required to retain observations or change cases.</p> : null}
      {!current ? <p className="text-sm text-warning">Case actions require a current, complete source observation. Retained case states are unchanged.</p> : null}
      <EntityOperationTable label="Agreement lifecycle cases" rows={result.cases} getRowId={item => item.caseId} emptyText="No current agreement findings or retained cases for this book." columns={[
        { id: "case", label: "Finding", rowHeader: true, render: item => <Button variant="ghost" disabled={busy} onClick={() => select(item)} className="h-auto max-w-[30rem] whitespace-normal text-left">{item.message}</Button> },
        { id: "state", label: "Case / observation", render: item => <>{item.isTracked ? stateText(item.state) : "Not tracked"}<br />{item.isCurrentlyObserved ? "Observed now" : "Not observed at this cutoff"}</> },
        { id: "owner", label: "Owner", render: item => item.owner ?? "Unassigned" },
        { id: "age", label: "First seen / age", render: item => <>{item.firstSeenAtUtc?.slice(0, 10) ?? "Not tracked"}<br />{item.ageDays == null ? "Age unavailable" : `${item.ageDays} calendar days`}</> },
        { id: "due", label: "Obligation boundary", render: item => <>{item.dueDate ?? "Not dated"}{item.daysOverdue != null && item.daysOverdue > 0 ? <><br />{item.daysOverdue} days overdue</> : null}</> }
      ]} />
      {selected ? <section aria-label="Selected lifecycle case" className="space-y-3 rounded border border-border p-4">
        <h4 className="font-semibold">{selected.message}</h4><p className="text-sm">Suggested action: {selected.suggestedAction}</p>
        <Link className="text-sm underline underline-offset-4" to={agreementLink(selected.agreementId)}>Open agreement amendment and termination workflow</Link>
        <p className="text-sm">First seen {timeText(selected.firstSeenAtUtc)} · last observed {timeText(selected.lastObservedAtUtc)} · version {selected.version}</p>
        <FormGrid columns={2}>
          <OperationField label="Lifecycle action reason" value={reason} onChange={setReason} required disabled={busy} />
          <OperationField label="Lifecycle owner" value={owner} onChange={setOwner} disabled={busy} hint="Operator identifier. Leave blank to remove the assignment." />
        </FormGrid>
        <Button type="button" variant="outline" disabled={!canAct || !selected.isCurrentlyObserved || !reason.trim()} onClick={() => void mutate(true)}>{selected.isTracked ? "Retain current observation" : "Track this finding"}</Button>
        {selected.isTracked ? <form onSubmit={event => { event.preventDefault(); if (!actionDisabled) void mutate(false); }} className="space-y-3">
          <FormGrid columns={2}>
            <OperationField label="Lifecycle case action" value={action} onChange={value => setAction(value as AgreementWorkItemAction)} disabled={busy}>
              <option value="AssignOwner">Assign or clear owner</option><option value="AddNote">Add note or evidence</option><option value="AwaitReview">Await review</option><option value="Resolve">Resolve with evidence</option><option value="Reopen">Reopen resolved case</option>
            </OperationField>
            <OperationField label="Lifecycle case note" value={note} onChange={setNote} disabled={busy} />
          </FormGrid>
          <label className="block space-y-1 text-sm"><span>Lifecycle evidence references (one per line)</span><textarea value={evidence} onChange={event => setEvidence(event.target.value)} rows={3} disabled={busy} className="w-full rounded border border-input bg-background px-3 py-2" /></label>
          <p className="text-sm text-muted-foreground">Resolving a case requires evidence and records a follow-up decision. It does not establish that the underlying agreement or posted balance changed.</p>
          <Button type="submit" disabled={actionDisabled}>Retain case action</Button>
        </form> : <p className="text-sm text-muted-foreground">Track the finding before assigning an owner or recording a case decision.</p>}
        <TechnicalDetails label="Lifecycle case history and source evidence"><p className="break-all text-sm">Case {selected.caseId} · retained hash {selected.recordHash ?? "None"}</p>
          <p className="break-all text-sm">Current observation {result.sourceFingerprint}</p>
          {selected.history.length ? <ol className="space-y-3 text-sm">{selected.history.map(step => <li key={step.commandId} className="border-t border-border pt-2">
            <p>{step.action} · {stateText(step.state)} · {step.actor} · {formatTimestamp(step.recordedAtUtc)} · version {step.version}</p><p>{step.reason}</p>
            {step.note ? <p>{step.note}</p> : null}<p>Owner: {step.owner ?? "Unassigned"}</p>
            {step.evidenceReferences.length ? <ul className="list-disc space-y-1 break-all pl-5">{step.evidenceReferences.map((value, index) => <li key={index}>{value}</li>)}</ul> : null}
            <p className="break-all">Source as of {step.observedAsOf}: {step.sourceFingerprint}</p>
            {step.sourceVersions?.length ? <ul className="list-disc space-y-1 break-all pl-5" aria-label={`Retained source versions for case version ${step.version}`}>
              {step.sourceVersions.map((source, index) => <li key={index}>{source.sourceKind} · {source.sourceId} · version {source.version} · {source.contentHash}</li>)}
            </ul> : null}
          </li>)}</ol> : <p className="text-sm">No retained history yet.</p>}
        </TechnicalDetails>
      </section> : null}
      <section className="space-y-3" aria-label="Guarantee register"><h4 className="font-semibold">Guarantee register</h4>
        <p className="text-sm text-muted-foreground">Declared contractual limits and measured exposure are separate. No reviewed underlying exposure mapping is available; a guarantee cap is not a balance or liability.</p>
        <EntityOperationTable label="Approved guarantee disclosures" rows={result.guarantees} getRowId={item => item.agreementId} emptyText="No approved guarantee disclosures in this observation." columns={[
          { id: "agreement", label: "Agreement", rowHeader: true, render: item => <Link className="underline underline-offset-4" to={agreementLink(item.agreementId)}>{item.code} · {item.name}</Link> },
          { id: "parties", label: "Legal parties", render: item => <>{item.partyOneRole}: {entityName(item.partyOneId)}<br />{item.partyTwoRole}: {entityName(item.partyTwoId)}</> },
          { id: "cap", label: "Declared cap", render: item => item.limitAmount == null ? "Not declared" : `${item.limitAmount} ${item.currency}` },
          { id: "exposure", label: "Measured exposure", render: item => <>{item.currentExposure == null ? "Unavailable" : `${item.currentExposure} ${item.currency}`}<p className="max-w-[24rem] text-sm text-muted-foreground">{item.exposureUnavailableReason}</p></> },
          { id: "dates", label: "Legal coverage", render: item => <>{item.effectiveFrom} – {item.effectiveTo ?? "open ended"}<br />{item.effectiveStatus}{item.terminationEffectiveDate ? ` · terminated ${item.terminationEffectiveDate}` : ""}</> },
          { id: "evidence", label: "Approved evidence", render: item => <TechnicalDetails label={`Version ${item.agreementVersion}`}><p className="text-sm">Beneficiary: {item.beneficiaryReference ?? "Not declared"}</p><p className="break-all text-sm">{item.payloadHash}</p><p className="text-sm">Expiry is exclusive.</p></TechnicalDetails> }
        ]} />
      </section>
    </> : !busy ? <p className="text-sm text-muted-foreground">Load the queue to inspect current findings and retained follow-up history.</p> : null}
  </section>;
}
