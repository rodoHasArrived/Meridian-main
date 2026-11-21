import { useEffect, useRef, useState } from "react";
import { Link, useLocation } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { FinancialValue } from "@/components/meridian/financial-value";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { ApiError, describeApiError } from "@/lib/api-errors";
import {
  assignReconciliationBreak, getReconciliationBreakAudit, getReconciliationBreakDetail,
  reopenReconciliationBreak, resolveReconciliationBreak, signOffReconciliationBreak,
  supersedeReconciliationBreak, waiveReconciliationBreak, transitionReconciliationBreak
} from "@/lib/api";
import { evidenceWorkbenchPath } from "@/lib/workspace";
import { reconciliationBreakAuditEndpoint } from "@/lib/workstation-endpoints";
import { sourceObservationLabel, sourceObservationAge } from "./reconciliation-source-observation";
import { buildReconciliationBreakRoutingHref } from "./accounting-screen.reconciliation.view-model";
import { caseMeasure, caseMeasureAmount, caseResidual, caseTolerance } from "./reconciliation-case-values";
import { equalExactDecimals } from "@/lib/exact-financial-value";
import type { ReconciliationBreakQueueItem, ReconciliationCaseworkAction, ReconciliationCaseworkCommand, ReconciliationCaseworkOperationResult } from "@/types";

type CaseDecision = ReconciliationCaseworkAction | "Dismiss";
const decisionLabels: Partial<Record<CaseDecision, string>> = {
  Resolve: "Resolve", Dismiss: "Dismiss", TransitionStatus: "Start review", Waive: "Waive", Assign: "Assign", SignOff: "Sign off", Reopen: "Reopen", Supersede: "Supersede"
};
const decisionOrder: CaseDecision[] = ["Resolve", "TransitionStatus", "SignOff", "Reopen", "Assign", "Dismiss", "Waive", "Supersede"];
const defaultServices = {
  read: getReconciliationBreakDetail,
  audit: getReconciliationBreakAudit,
  resolve: resolveReconciliationBreak,
  commands: { TransitionStatus: transitionReconciliationBreak, Assign: assignReconciliationBreak, Waive: waiveReconciliationBreak, SignOff: signOffReconciliationBreak,
    Reopen: reopenReconciliationBreak, Supersede: supersedeReconciliationBreak }
};

export function caseChanges(before: ReconciliationBreakQueueItem, after: ReconciliationBreakQueueItem) {
  const fields = [
    ["Status", before.status, after.status],
    ["Owner", before.assigneeDisplayName ?? before.assignedTo, after.assigneeDisplayName ?? after.assignedTo],
    ["Variance", caseResidual(before), caseResidual(after)],
    ["Version", before.version, after.version],
    ["Root cause", before.rootCauseCode, after.rootCauseCode],
    ["Resolution", before.resolutionCode, after.resolutionCode]
  ];
  return fields.filter(([label, oldValue, newValue]) => label === "Variance"
    ? !(oldValue == null && newValue == null) && !equalExactDecimals(oldValue, newValue)
    : oldValue !== newValue).map(([label, oldValue, newValue]) => ({
    label: String(label), before: String(oldValue ?? "Not recorded"), after: String(newValue ?? "Not recorded")
  }));
}

/** One subject-addressed case surface. Server capabilities inform discovery; writes recheck policy and versions. */
export function CaseInspector({ caseId, initialCase, onChanged, services = defaultServices }: {
  caseId: string | null;
  initialCase?: ReconciliationBreakQueueItem | null;
  onChanged?: (item: ReconciliationBreakQueueItem) => void;
  services?: typeof defaultServices;
}) {
  const location = useLocation();
  const params = new URLSearchParams(location.search);
  const scopeKey = JSON.stringify(["fundId", "fundProfileId", "fundAccountId", "entityId", "companyId", "organizationId",
    "ledgerBookId", "bookId", "periodId", "accountingPeriodId", "asOf", "date", "from", "to"].map((name) => params.get(name)));
  const [record, setRecord] = useState<ReconciliationBreakQueueItem | null>(initialCase ?? null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [action, setAction] = useState<CaseDecision | null>(null);
  const [reason, setReason] = useState("");
  const [rootCause, setRootCause] = useState("");
  const [resolution, setResolution] = useState("");
  const [assignee, setAssignee] = useState("");
  const [successor, setSuccessor] = useState("");
  const [approvalActor, setApprovalActor] = useState("");
  const [approvalReference, setApprovalReference] = useState("");
  const [evidenceReferences, setEvidenceReferences] = useState("");
  const [busy, setBusy] = useState(false);
  const [receipt, setReceipt] = useState<ReconciliationCaseworkOperationResult | null>(null);
  const [conflict, setConflict] = useState<ReconciliationBreakQueueItem | null>(null);
  const [audit, setAudit] = useState<unknown[]>([]);
  const [auditError, setAuditError] = useState(false);
  const [revision, setRevision] = useState(0);
  const formRef = useRef<HTMLFormElement>(null);
  const triggerRef = useRef<HTMLElement | null>(null);
  const contextKey = `${caseId}:${scopeKey}`;
  const currentContext = useRef(contextKey);
  currentContext.current = contextKey;
  const item = record?.breakId === caseId ? record : initialCase?.breakId === caseId ? initialCase : null;

  useEffect(() => {
    let active = true;
    setLoading(true); setBusy(false); setError(null); setAction(null); setConflict(null); setReceipt(null); setReason(""); setAudit([]); setAuditError(false);
    if (!caseId) { setRecord(null); setLoading(false); return; }
    services.read(caseId).then((value) => {
      if (!active) return;
      if (value.breakId !== caseId) throw new Error("The case response does not match the selected case.");
      setRecord(value);
    }).catch((err) => { if (active) setError(describeApiError(err, "Case unavailable.").summary); })
      .finally(() => { if (active) setLoading(false); });
    services.audit(caseId).then((value) => { if (active) setAudit(Array.isArray(value) ? value : []); })
      .catch(() => { if (active) setAuditError(true); });
    return () => { active = false; };
  }, [caseId, scopeKey, revision, services]);

  useEffect(() => { if (action) formRef.current?.querySelector<HTMLElement>("input, textarea")?.focus(); }, [action]);

  function closeDecision() {
    setAction(null); setConflict(null); setReason(""); setError(null);
    requestAnimationFrame(() => triggerRef.current?.focus());
  }
  function choose(next: CaseDecision) {
    triggerRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    setAction(next); setReason(""); setRootCause(item?.rootCauseCode ?? ""); setResolution(item?.resolutionCode ?? "");
    setAssignee(item?.assignedTo ?? ""); setSuccessor(""); setApprovalActor(""); setApprovalReference("");
    setEvidenceReferences((item?.evidenceLinks ?? []).join(", ")); setReceipt(null); setConflict(null); setError(null);
  }
  async function submit() {
    if (!item || !action || busy || loading || conflict || !reason.trim() || item.version == null
      || !item.permittedCommands?.some((entry) => entry.action === action && entry.enabled)) return;
    const before = item;
    const requestContext = currentContext.current;
    const requestId = crypto.randomUUID();
    setBusy(true); setError(null);
    try {
      const command: ReconciliationCaseworkCommand = { breakId: caseId!, action: action === "Dismiss" ? "Resolve" : action, actor: "", commandId: requestId,
        correlationId: requestId, source: "browser-case-inspector", expectedVersion: before.version,
        reason: reason.trim(), note: reason.trim(), rootCauseCode: rootCause.trim() || undefined,
        resolutionCode: resolution.trim() || undefined, assignee: assignee.trim() || undefined,
        supersedingBreakId: successor.trim() || undefined,
        status: action === "TransitionStatus" ? "Investigating" : undefined,
        approvalActor: approvalActor.trim() || undefined, approvalReference: approvalReference.trim() || undefined,
        evidenceLinks: evidenceReferences.split(",").map((value) => value.trim()).filter(Boolean) };
      const result = action === "Resolve" || action === "Dismiss"
        ? await services.resolve({ breakId: caseId!, status: action === "Dismiss" ? "Dismissed" : "Resolved", expectedVersion: before.version,
          resolutionNote: reason.trim(), operatorRationale: reason.trim(),
          rootCauseCode: rootCause.trim() || undefined, resolutionCode: resolution.trim() || undefined })
        : await services.commands[action as keyof typeof services.commands](command);
      if (currentContext.current !== requestContext) return;
      if (result.transitionStatus === "Conflict" || result.errorCode === "ConcurrencyConflict") {
        const latest = result.item ?? await services.read(before.breakId);
        if (currentContext.current === requestContext) setConflict(latest);
        return;
      }
      setReceipt(result);
      if (result.item) { setRecord(result.item); onChanged?.(result.item); }
      if (result.outcome.state === "Succeeded" || result.outcome.state === "CompletedWithWarnings") {
        setAction(null); setReason(""); requestAnimationFrame(() => triggerRef.current?.focus());
      }
      // Capabilities are caller-specific projections; mutation receipts are not capability grants.
      setLoading(true);
      try {
        const latest = await services.read(before.breakId);
        if (currentContext.current === requestContext) { setRecord(latest); onChanged?.(latest); }
      } catch {
        if (currentContext.current === requestContext) setError("The decision receipt is retained below. Reload the case to retrieve its latest state and permitted decisions.");
      } finally { if (currentContext.current === requestContext) setLoading(false); }
    } catch (err) {
      if (currentContext.current !== requestContext) return;
      if (err instanceof ApiError && err.status === 409) {
        try {
          const latest = await services.read(before.breakId);
          if (currentContext.current === requestContext) setConflict(latest);
        }
        catch { if (currentContext.current === requestContext) setError("This case changed. Reload the case before reviewing and confirming again."); }
      } else setError(describeApiError(err, "The decision could not be completed.").summary);
    } finally { if (currentContext.current === requestContext) setBusy(false); }
  }

  if (!caseId) return <p role="status">Select a case to inspect its facts, evidence and decisions.</p>;
  const capabilities = item?.permittedCommands ?? [];
  const decisions = decisionOrder.filter((name) => capabilities.some((entry) => entry.action === name));
  const primary = decisions.find((name) => capabilities.find((entry) => entry.action === name)?.enabled) ?? decisions[0];
  const activeCapability = capabilities.find((entry) => entry.action === action);
  async function reviewLatest() {
    if (!caseId) return;
    const requestContext = currentContext.current;
    setLoading(true);
    try { const latest = await services.read(caseId); if (currentContext.current === requestContext) { setRecord(latest); setConflict(null); } }
    catch { if (currentContext.current === requestContext) setError("The latest case could not be loaded. The decision remains unconfirmed."); }
    finally { if (currentContext.current === requestContext) setLoading(false); }
  }
  const measure = item ? caseMeasure(item) : undefined;
  const expected = measure ? caseMeasureAmount(measure, "expected") : undefined;
  const actual = measure ? caseMeasureAmount(measure, "actual") : undefined;
  const missingComparison = measure?.unavailableReason ?? "No retained comparison pair is recorded for this case.";
  const hasComparison = expected != null && actual != null;
  const dataReason = measure?.unavailableReason ?? "A verified data condition was not supplied for this case measure.";
  const routingHref = buildReconciliationBreakRoutingHref(item?.routingTarget);
  const renderDecision = (name: CaseDecision) => {
    const capability = capabilities.find((entry) => entry.action === name)!;
    const disabledReason = action ? "Complete or cancel the current decision first." : loading ? "Loading the latest case and permitted decisions." : error ? "Reload this case before making a decision."
      : item?.version == null ? "A retained case version is required." : capability.disabledReason;
    const descriptionId = `case-${caseId}-${name}-disabled`;
    return <span key={name} className="inline-flex flex-col items-start gap-1"><Button type="button" size="sm" variant={name === primary ? "default" : "outline"}
      disabled={busy || loading || !!action || !!error || item?.version == null || !capability.enabled}
      aria-describedby={disabledReason ? descriptionId : undefined}
      disabledReason={disabledReason} onClick={() => choose(name)}>{decisionLabels[name]}</Button>
      {disabledReason ? <span id={descriptionId} className="max-w-64 text-xs text-muted-foreground">{disabledReason}</span> : null}</span>;
  };
  return <section className="min-w-0 space-y-4 break-words" aria-label="Case inspector" aria-busy={loading || busy}>
    {loading ? <p role="status">Loading latest case…</p> : null}
    {error ? <div role="alert"><p>{error}</p><Button type="button" variant="outline" onClick={() => setRevision((value) => value + 1)}>Reload case</Button></div> : null}
    {item ? <div className={loading ? "space-y-4 opacity-60" : "space-y-4"}>
      <header><div className="flex flex-wrap items-start justify-between gap-2"><h3 className="text-base font-semibold">{item.reason || item.strategyName}</h3><Badge>{item.status}</Badge></div>
        {item.recommendedAction ? <p className="mt-2 text-sm">{item.recommendedAction}</p> : null}
        <dl className="mt-3 grid gap-2 sm:grid-cols-2"><Fact label="Owner" value={item.assigneeDisplayName ?? item.assignedTo ?? "Unassigned"} />
          <Fact label="Due" value={item.slaDueAt ?? "Not recorded"} /><Fact label="Case" value={item.breakId} /><Fact label="Version" value={String(item.version ?? "Not recorded")} />
          <Fact label="Fund account" value={item.fundAccountId ?? "Not recorded"} /><Fact label="Period" value={item.accountingPeriodId ?? "Not recorded"} /></dl></header>
      <section aria-label="Comparison" className="rounded border border-border p-3">
        <h4 className="text-sm font-semibold">Disagreement</h4>
        {!hasComparison ? <p className="my-2 text-sm">Comparison unavailable: {missingComparison}</p> : null}
        <dl className="mt-3 grid gap-3 sm:grid-cols-2">
          <ValueFact label="Expected" value={expected} unit={measure?.unit} reason={dataReason} />
          <ValueFact label="Actual" value={actual} unit={measure?.unit} reason={dataReason} />
          <ValueFact label="Residual" value={caseResidual(item)} unit={measure?.unit} reason={dataReason} signed />
          <ValueFact label="Tolerance" value={caseTolerance(item)} unit={measure?.unit} reason={dataReason} />
        </dl><p className="mt-2 text-xs text-muted-foreground">Basis: Not recorded; the source has not identified the comparison sides or subtraction convention.</p>
        {item.breakExplanation?.summary ? <p className="mt-2 text-sm">{item.breakExplanation.summary}</p> : null}
      </section>
      <section aria-label="Case decisions" className="space-y-3">
        {!capabilities.length ? <p role="status">Decisions unavailable: the server has not supplied permitted commands for this case and your role.</p> : null}
        <div className="flex flex-wrap items-start gap-2">{primary ? renderDecision(primary) : null}
          {decisions.length > 1 ? <details><summary className="cursor-pointer rounded border border-border px-3 py-2 text-sm">Other decisions</summary><div className="mt-2 flex flex-wrap gap-2">{decisions.filter((name) => name !== primary).map(renderDecision)}</div></details> : null}</div>
        {action ? <form ref={formRef} aria-label={`${decisionLabels[action]} case`} className="space-y-3 rounded border border-border p-3" onSubmit={(event) => { event.preventDefault(); void submit(); }} onKeyDown={(event) => {
            if (event.key === "Escape" && !busy) { event.preventDefault(); event.stopPropagation(); closeDecision(); }
          }}><fieldset disabled={busy || loading} className="space-y-3">
          <h4 className="font-semibold">Review {decisionLabels[action]?.toLowerCase()}</h4><p className="text-xs text-muted-foreground">Case {item.breakId} · version {item.version} · period {item.accountingPeriodId ?? "not recorded"}. The server validates the decision against this version.</p>
          {action === "Resolve" ? <><label className="block text-sm">Root cause code<Input value={rootCause} onChange={(event) => setRootCause(event.target.value)} /></label><label className="block text-sm">Resolution code<Input value={resolution} onChange={(event) => setResolution(event.target.value)} /></label></> : null}
          {action === "Assign" || action === "TransitionStatus" ? <label className="block text-sm">Assignee<Input required value={assignee} onChange={(event) => setAssignee(event.target.value)} /></label> : null}
          {action === "Supersede" ? <label className="block text-sm">Superseding case ID<Input required value={successor} onChange={(event) => setSuccessor(event.target.value)} /></label> : null}
          {action === "Waive" || action === "Supersede" ? <>
            <label className="block text-sm">Independent approver<Input required value={approvalActor} onChange={(event) => setApprovalActor(event.target.value)} /></label>
            <label className="block text-sm">Retained approval reference<Input required value={approvalReference} onChange={(event) => setApprovalReference(event.target.value)} /></label>
            <label className="block text-sm">Retained evidence references (comma separated)<Input required value={evidenceReferences} onChange={(event) => setEvidenceReferences(event.target.value)} /></label>
          </> : null}
          <label className="block text-sm">{decisionLabels[action]} rationale<Input required value={reason} onChange={(event) => setReason(event.target.value)} /></label>
          {conflict ? <section role="alert" aria-label="Case changed"><h4 className="font-semibold">Changed since you loaded it</h4>
            <table className="w-full text-left text-sm"><thead><tr><th>Field</th><th>Your version</th><th>Current version</th></tr></thead><tbody>{caseChanges(item, conflict).map((change) => <tr key={change.label}><th>{change.label}</th><td>{change.before}</td><td>{change.after}</td></tr>)}</tbody></table>
            <p>Review the latest case before confirming again.</p><Button type="button" variant="outline" onClick={() => void reviewLatest()}>Review latest version</Button><Button type="button" variant="ghost" onClick={closeDecision}>Discard decision</Button>
          </section> : null}
          <div className="flex flex-wrap gap-2"><Button type="submit" disabled={busy || loading || !!conflict || !reason.trim() || !activeCapability?.enabled}
            disabledReason={!activeCapability?.enabled ? activeCapability?.disabledReason ?? "This decision is no longer permitted. Review the latest case." : undefined}>{busy ? "Recording decision…" : `Confirm ${decisionLabels[action]?.toLowerCase()}`}</Button><Button type="button" variant="ghost" disabled={busy} onClick={closeDecision}>Cancel</Button></div>
        </fieldset></form> : null}
      </section>
      {receipt ? <section role="status" aria-label="Decision receipt" className="rounded border border-border p-3"><h4 className="font-semibold">{receipt.outcome.state}</h4><p>{receipt.error ?? `${receipt.outcome.isSuccessful ? "Decision recorded" : "Decision not completed"} for ${item.breakId}, version ${receipt.item?.version ?? "not supplied"}.`}</p><p className="text-xs">Receipt {receipt.outcome.operationId}</p>{receipt.outcome.issues?.map((issue) => <p key={issue.code}>{issue.message}</p>)}<a className="underline" href={reconciliationBreakAuditEndpoint(item.breakId)}>Review audit entry</a></section> : null}
      <div className="flex flex-wrap gap-2"><Button asChild size="sm" variant="outline"><Link to={evidenceWorkbenchPath("reconciliation-break", item.breakId)}>Retained evidence</Link></Button>
        {routingHref ? <Button asChild size="sm" variant="outline"><Link to={routingHref} aria-label={`Open routing target for reconciliation break ${item.breakId}`}>Open routing target</Link></Button> : null}</div>
      <TechnicalDetails label="Timeline and discussion"><ul className="space-y-2">{item.comments?.map((comment) => <li key={comment.commentId}><strong>{comment.authorDisplayName}</strong>: {comment.deletedAt ? "Comment removed; audit retained" : comment.body}</li>)}</ul>
        <dl className="my-3 grid gap-2 sm:grid-cols-2"><Fact label="Source observation" value={`${sourceObservationLabel(item.lineage)} · ${sourceObservationAge(item.lineage)}`} />
          <Fact label="Updated" value={item.lastUpdatedAt} /><Fact label="Source system" value={item.sourceSystem ?? "Not recorded"} /><Fact label="Source reference" value={item.sourceReference ?? "Not recorded"} />
          <Fact label="Required sign-off" value={item.requiredSignoffRole ?? "Not recorded"} /><Fact label="Sign-off status" value={item.signoffStatus ?? "Not recorded"} />
          <Fact label="Decision note" value={item.resolutionNote ?? "Not recorded"} /><Fact label="Ledger impact" value={item.breakExplanation?.ledgerImpact ?? "Not recorded"} /></dl>
        {auditError ? <p>Audit timeline unavailable. Retained evidence remains accessible.</p> : null}
        {!audit.length && !item.comments?.length ? <p>No timeline entries loaded.</p> : null}
        <ul className="mt-2 space-y-2">{audit.map((entry, index) => { const event = entry as Record<string, unknown>; return <li key={String(event.eventId ?? index)} className="text-sm">{String(event.occurredAt ?? "Date not recorded")} · {String(event.eventType ?? "Case event")} · {String(event.actor ?? "Actor not recorded")}</li>; })}</ul>
      </TechnicalDetails>
    </div> : !loading ? <p role="status">This case is unavailable in the current scope.</p> : null}
  </section>;
}

function Fact({ label, value }: { label: string; value: string }) { return <div><dt className="text-xs text-muted-foreground">{label}</dt><dd className="text-sm">{value}</dd></div>; }
function ValueFact({ label, value, unit, reason, signed }: { label: string; value?: string | number | null; unit?: string; reason: string; signed?: boolean }) {
  return <div><dt className="text-xs text-muted-foreground">{label}</dt><dd><FinancialValue value={value} currency={unit && /^[A-Z]{3}$/.test(unit) ? unit : undefined} unit={unit || "Unit unavailable"} reason={reason} signed={signed} /></dd></div>;
}

export function CaseVariance({ item }: { item: ReconciliationBreakQueueItem }) {
  const measure = caseMeasure(item);
  const unit = measure?.unit;
  return <FinancialValue value={caseResidual(item)} signed
    currency={unit && /^[A-Z]{3}$/.test(unit) ? unit : undefined} unit={unit || "Unit unavailable"}
    reason="A verified data condition was not supplied for this case measure." />;
}
