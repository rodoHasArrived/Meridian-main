import { useCallback, useEffect, useId, useRef, useState } from "react";
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
  supersedeReconciliationBreak, waiveReconciliationBreak, transitionReconciliationBreak,
  setReconciliationBreakRootCause, setReconciliationBreakResolution
} from "@/lib/api";
import { evidenceWorkbenchPath } from "@/lib/workspace";
import { reconciliationBreakAuditEndpoint } from "@/lib/workstation-endpoints";
import { sourceObservationLabel, sourceObservationAge } from "./reconciliation-source-observation";
import { buildReconciliationBreakRoutingHref } from "./accounting-screen.reconciliation.view-model";
import { caseMeasure, caseMeasureAmount, caseQueueResidual, caseResidual, caseResidualUnit, caseTolerance, reconciliationUnitLabel, reconciliationValueDecimals, reconciliationValueUnavailableReason } from "./reconciliation-case-values";
import { equalExactDecimals } from "@/lib/exact-financial-value";
import { ReconciliationCaseEvidence, ReconciliationMatchCandidate } from "./reconciliation-case-evidence";
import { ReconciliationCaseDiscussion } from "./accounting-screen.case-discussion";
import { BreakAuditTimeline, type BreakAuditLoader } from "./accounting-screen.reconciliation-audit-timeline";
import { isStatementHandoffMarker } from "./reconciliation-casework-command";
import { DenseDataTable } from "@/components/meridian/ui-kit-primitives";
import { Select } from "@/components/ui/select";
import { getReconciliationTaxonomy } from "@/lib/api/reconciliation-readiness.api";
import { useCaseCommandRecovery } from "./use-case-command-recovery";
import type { ReconciliationTaxonomySnapshot } from "@/types/reconciliation-readiness.types";
import type { ReconciliationBreakMeasure, ReconciliationBreakQueueItem, ReconciliationCaseworkAction, ReconciliationCaseworkCommand, ReconciliationCaseworkOperationResult } from "@/types";
import "@/styles/case-inspector.css";

import { isUnavailableLabel, VALUE_STATE_LABELS } from "@/lib/format";
type CaseDecision = ReconciliationCaseworkAction | "Dismiss";
const decisionLabels: Partial<Record<CaseDecision, string>> = {
  Resolve: "Resolve", Dismiss: "Dismiss", TransitionStatus: "Start review", Waive: "Waive", Assign: "Assign", SignOff: "Sign off", Reopen: "Reopen", Supersede: "Supersede",
  SetRootCause: "Update root cause", SetResolution: "Update resolution"
};
const decisionOrder: CaseDecision[] = ["Resolve", "TransitionStatus", "SignOff", "Reopen", "Assign", "Dismiss", "Waive", "Supersede", "SetRootCause", "SetResolution"];
const defaultServices = {
  read: getReconciliationBreakDetail,
  audit: getReconciliationBreakAudit,
  resolve: resolveReconciliationBreak,
  commands: { TransitionStatus: transitionReconciliationBreak, Assign: assignReconciliationBreak, Waive: waiveReconciliationBreak, SignOff: signOffReconciliationBreak,
    Reopen: reopenReconciliationBreak, Supersede: supersedeReconciliationBreak }
};
type CaseInspectorServices = typeof defaultServices & {
  taxonomy?: () => Promise<ReconciliationTaxonomySnapshot>;
  commands: { SetRootCause?: typeof setReconciliationBreakRootCause; SetResolution?: typeof setReconciliationBreakResolution };
};

export function caseChanges(before: ReconciliationBreakQueueItem, after: ReconciliationBreakQueueItem) {
  const components = (item: ReconciliationBreakQueueItem) => {
    if (item.statementEvidence) return item.statementEvidence.match.residuals.map((residual) => ({
      measure: residual.measure, unit: reconciliationUnitLabel(residual.unit),
      value: reconciliationValueUnavailableReason(residual.residual) ? null : residual.residual
    }));
    if (item.measures?.length) return item.measures.map((measure) => ({
      measure: measure.kind === "CostBasis" ? "Cost basis" : measure.kind,
      unit: reconciliationUnitLabel(measure.unit, measure.kind),
      value: caseResidual({ ...item, measures: [measure] })
    }));
    return [{ measure: "Variance", unit: "Unit unavailable", value: caseResidual(item) }];
  };
  const oldComponents = components(before);
  const newComponents = components(after);
  const unmatched = [...newComponents];
  // Match separate measure/unit components without treating decimal padding or row order as a change.
  const varianceChanged = oldComponents.length !== newComponents.length || oldComponents.some((previous) => {
    const match = unmatched.findIndex((current) => previous.measure === current.measure && previous.unit === current.unit
      && (previous.value == null && current.value == null || equalExactDecimals(previous.value, current.value)));
    if (match < 0) return true;
    unmatched.splice(match, 1);
    return false;
  });
  const showComponents = oldComponents.length !== 1 || newComponents.length !== 1
    || oldComponents[0].measure !== newComponents[0].measure || oldComponents[0].unit !== newComponents[0].unit;
  const describeComponents = (values: ReturnType<typeof components>) => values.length
    ? values.map((component) => `${component.measure}: ${component.value ?? VALUE_STATE_LABELS.unavailable} ${component.unit}`).join("; ")
    : VALUE_STATE_LABELS.unavailable;
  const fields = [
    ["Status", before.status, after.status],
    ["Owner", before.assigneeDisplayName ?? before.assignedTo, after.assigneeDisplayName ?? after.assignedTo],
    ...(varianceChanged ? [[showComponents ? "Variance components" : "Variance",
      showComponents ? describeComponents(oldComponents) : oldComponents[0].value,
      showComponents ? describeComponents(newComponents) : newComponents[0].value]] : []),
    ["Version", before.version, after.version],
    ["Root cause", before.rootCauseCode, after.rootCauseCode],
    ["Resolution", before.resolutionCode, after.resolutionCode]
  ];
  return fields.filter(([, oldValue, newValue]) => oldValue !== newValue).map(([label, oldValue, newValue]) => ({
    label: String(label), before: String(oldValue ?? VALUE_STATE_LABELS.unavailable), after: String(newValue ?? VALUE_STATE_LABELS.unavailable)
  }));
}

/** One subject-addressed case surface. Server capabilities inform discovery; writes recheck policy and versions. */
export function CaseInspector({ caseId, initialCase, onChanged, services = defaultServices, relatedCases,
  comparisonBreakId, onComparisonChange, onOpenCase, requestedDecision, onDecisionRequestHandled, operatorId, commandMemory }: {
  caseId: string | null;
  initialCase?: ReconciliationBreakQueueItem | null;
  onChanged?: (item: ReconciliationBreakQueueItem) => void;
  services?: CaseInspectorServices;
  operatorId?: string | null;
  commandMemory?: Map<string, ReconciliationCaseworkCommand>;
  relatedCases?: readonly ReconciliationBreakQueueItem[];
  comparisonBreakId?: string | null;
  onComparisonChange?: (breakId: string | null) => void;
  onOpenCase?: (breakId: string) => void;
  requestedDecision?: "Resolve" | "Dismiss" | null;
  onDecisionRequestHandled?: () => void;
}) {
  const location = useLocation();
  const comparisonConditionId = useId();
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
  const [revision, setRevision] = useState(0);
  const [taxonomy, setTaxonomy] = useState<ReconciliationTaxonomySnapshot | null>(null);
  const [taxonomyError, setTaxonomyError] = useState<string | null>(null);
  const [taxonomyRevision, setTaxonomyRevision] = useState(0);
  const [legacyUnconfirmed, setLegacyUnconfirmed] = useState(false);
  const localCommandMemory = useRef(new Map<string, ReconciliationCaseworkCommand>());
  const formRef = useRef<HTMLFormElement>(null);
  const triggerRef = useRef<HTMLElement | null>(null);
  const receiptRef = useRef<HTMLElement>(null);
  const pendingDecisionFocus = useRef<{ context: string; trigger: HTMLElement | null } | null>(null);
  const contextKey = `${caseId}:${scopeKey}`;
  const currentContext = useRef(contextKey);
  currentContext.current = contextKey;
  const item = record?.breakId === caseId ? record : initialCase?.breakId === caseId ? initialCase : null;
  const needsRootCause = action === "Resolve" || action === "SetRootCause";
  const needsResolution = action === "Resolve" || action === "SetResolution";
  const needsTaxonomy = needsRootCause || needsResolution;
  const classificationReady = !needsTaxonomy || !!taxonomy && (!needsRootCause || !!rootCause) && (!needsResolution || !!resolution);

  useEffect(() => {
    let active = true;
    setLoading(true); setBusy(false); setError(null); setAction(null); setConflict(null); setReceipt(null); setReason(""); setLegacyUnconfirmed(false);
    if (!caseId) { setRecord(null); setLoading(false); return; }
    services.read(caseId).then((value) => {
      if (!active) return;
      if (value.breakId !== caseId) throw new Error("The case response does not match the selected case.");
      setRecord(value);
    }).catch((err) => { if (active) setError(describeApiError(err, "Case unavailable.").summary); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [caseId, scopeKey, revision, services]);

  useEffect(() => { if (action) formRef.current?.querySelector<HTMLElement>("input, select, textarea")?.focus(); }, [action]);
  useEffect(() => {
    const pending = pendingDecisionFocus.current;
    if (!pending) return;
    if (pending.context !== contextKey || pending.context !== currentContext.current) { pendingDecisionFocus.current = null; return; }
    if (loading || busy || action) return;
    pendingDecisionFocus.current = null;
    if (pending.trigger?.isConnected && !pending.trigger.matches(":disabled")) pending.trigger.focus();
    else receiptRef.current?.focus();
  }, [action, busy, contextKey, loading]);
  useEffect(() => {
    if (!needsTaxonomy) return;
    let active = true; setTaxonomy(null); setTaxonomyError(null);
    (services.taxonomy ?? getReconciliationTaxonomy)().then((value) => { if (active) setTaxonomy(value); })
      .catch((failure) => { if (active) setTaxonomyError(describeApiError(failure, "The reconciliation taxonomy is unavailable.").summary); });
    return () => { active = false; };
  }, [action, needsTaxonomy, services, taxonomyRevision]);

  function closeDecision() {
    setAction(null); setConflict(null); setReason(""); setError(null);
    requestAnimationFrame(() => triggerRef.current?.focus());
  }
  const choose = useCallback((next: CaseDecision) => {
    triggerRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    setAction(next); setReason(""); setRootCause(item?.rootCauseCode ?? ""); setResolution(item?.resolutionCode ?? "");
    setAssignee(item?.assignedTo ?? ""); setSuccessor(""); setApprovalActor(""); setApprovalReference("");
    setEvidenceReferences((item?.evidenceLinks ?? []).filter((value) => !isStatementHandoffMarker(value)).join(", ")); setReceipt(null); setConflict(null); setError(null);
  }, [item]);
  const acceptExternalUpdate = useCallback((updated: ReconciliationBreakQueueItem) => {
    if (currentContext.current !== contextKey || updated.breakId !== caseId) return;
    setRecord((current) => current?.version != null && (updated.version == null || updated.version < current.version) ? current : updated);
    onChanged?.(updated);
    setLoading(true);
    services.read(caseId).then((latest) => {
      if (currentContext.current === contextKey) { setRecord(latest); onChanged?.(latest); }
    }).catch(() => {
      if (currentContext.current === contextKey) setError("Reload the case to retrieve its latest permitted decisions.");
    }).finally(() => { if (currentContext.current === contextKey) setLoading(false); });
  }, [caseId, contextKey, onChanged, services]);
  const recovery = useCaseCommandRecovery({ operatorId, item, memory: commandMemory ?? localCommandMemory.current,
    read: services.read, audit: services.audit, onUpdated: acceptExternalUpdate });
  function commandSender(command: ReconciliationCaseworkCommand) {
    return services.commands[command.action as keyof typeof services.commands]
      ?? (services === defaultServices && command.action === "SetRootCause" ? setReconciliationBreakRootCause : undefined)
      ?? (services === defaultServices && command.action === "SetResolution" ? setReconciliationBreakResolution : undefined);
  }
  useEffect(() => {
    if (!requestedDecision || loading || busy) return;
    if (!recovery.pending && item?.permittedCommands?.some((entry) => entry.action === requestedDecision && entry.enabled)) choose(requestedDecision);
    onDecisionRequestHandled?.();
  }, [requestedDecision, loading, busy, item, choose, onDecisionRequestHandled, recovery.pending]);
  async function submit(resumeCommand?: ReconciliationCaseworkCommand) {
    if (!item || busy || loading || conflict) return;
    const decision = resumeCommand?.action ?? action;
    if (!decision) return;
    if (resumeCommand) {
      if (!recovery.canResume || recovery.pending?.commandId !== resumeCommand.commandId) return;
    } else if (!action || recovery.pending || error || !reason.trim() || item.version == null
      || !classificationReady
      || !item.permittedCommands?.some((entry) => entry.action === action && entry.enabled)) return;
    const before = item;
    const requestContext = currentContext.current;
    const requestId = crypto.randomUUID();
    const legacy = decision === "Resolve" || decision === "Dismiss";
    const command: ReconciliationCaseworkCommand = resumeCommand ?? { breakId: caseId!, action: decision === "Dismiss" ? "Resolve" : decision,
      actor: "", commandId: requestId, correlationId: requestId, source: "browser-case-inspector", expectedVersion: before.version!,
      reason: reason.trim(), note: reason.trim(), rootCauseCode: rootCause.trim() || undefined,
      resolutionCode: resolution.trim() || undefined, assignee: assignee.trim() || undefined,
      supersedingBreakId: successor.trim() || undefined, status: decision === "TransitionStatus" ? "Investigating" : undefined,
      approvalActor: approvalActor.trim() || undefined, approvalReference: approvalReference.trim() || undefined,
      evidenceLinks: evidenceReferences.split(",").map((value) => value.trim()).filter((value) => value && !isStatementHandoffMarker(value)) };
    if (!legacy) recovery.begin(command);
    setBusy(true); setError(null);
    try {
      const result = legacy
        ? await services.resolve({ breakId: caseId!, status: decision === "Dismiss" ? "Dismissed" : "Resolved", expectedVersion: before.version,
          resolutionNote: reason.trim(), operatorRationale: reason.trim(),
          rootCauseCode: rootCause.trim() || undefined, resolutionCode: resolution.trim() || undefined })
        : await commandSender(command)!(command);
      if (currentContext.current !== requestContext) return;
      if (result.item && result.item.breakId !== before.breakId) throw new Error("The result belongs to another case. Check retained history before continuing.");
      if (result.transitionStatus === "Conflict" || result.errorCode === "ConcurrencyConflict") {
        if (!resumeCommand) recovery.clear();
        const latest = result.item ?? await services.read(before.breakId);
        if (currentContext.current === requestContext) setConflict(latest);
        return;
      }
      const links = result.item?.evidenceLinks ?? [];
      const handoffPending = links.some((reference) => reference.startsWith("urn:meridian:statement-casework-handoff:pending:")
        && !links.includes(reference.replace(":pending:", ":completed:")));
      if (!handoffPending) recovery.clear();
      setReceipt(result);
      if (result.item) { setRecord(result.item); onChanged?.(result.item); }
      if (!handoffPending && (result.outcome.state === "Succeeded" || result.outcome.state === "CompletedWithWarnings")) {
        pendingDecisionFocus.current = { context: requestContext, trigger: triggerRef.current };
        setAction(null); setReason("");
      }
      setLoading(true);
      try {
        const latest = await services.read(before.breakId);
        if (currentContext.current === requestContext) { setRecord(latest); onChanged?.(latest); }
      } catch {
        if (currentContext.current === requestContext) setError("The decision receipt is retained below. Reload the case to retrieve its latest state and permitted decisions.");
      } finally { if (currentContext.current === requestContext) setLoading(false); }
    } catch (err) {
      if (currentContext.current !== requestContext) return;
      const knownRefusal = err instanceof ApiError && [400, 401, 403, 404, 409].includes(err.status);
      if (knownRefusal && !resumeCommand) recovery.clear();
      if (err instanceof ApiError && err.status === 409) {
        try {
          const latest = await services.read(before.breakId);
          if (currentContext.current === requestContext) setConflict(latest);
        } catch { if (currentContext.current === requestContext) setError("This case changed. Reload the case before reviewing and confirming again."); }
      } else if (legacy && !knownRefusal) {
        setLegacyUnconfirmed(true);
        setError("Decision outcome unconfirmed. Review the current record and retained audit before opening another decision.");
        try {
          const latest = await services.read(before.breakId);
          if (currentContext.current === requestContext) { setRecord(before); setConflict(latest); }
        } catch { /* Reload remains required; the legacy request is never retried automatically. */ }
      } else setError(describeApiError(err, "The request ended before its outcome was confirmed. Check retained case history.").summary);
    } finally { if (currentContext.current === requestContext) setBusy(false); }
  }

  if (!caseId) return <p role="status">Select a case to inspect its facts, evidence and decisions.</p>;
  const capabilities = item?.permittedCommands ?? [];
  const decisions = decisionOrder.filter((name) => capabilities.some((entry) => entry.action === name));
  const primaryDecisions = decisions.filter((name) => name !== "SetRootCause" && name !== "SetResolution");
  const primary = primaryDecisions.find((name) => capabilities.find((entry) => entry.action === name)?.enabled) ?? primaryDecisions[0];
  const activeCapability = capabilities.find((entry) => entry.action === action);
  async function reviewLatest() {
    if (!caseId) return;
    const requestContext = currentContext.current;
    setLoading(true);
    try { const latest = await services.read(caseId); if (currentContext.current === requestContext) { setRecord(latest); setConflict(null); setError(null);
      if (legacyUnconfirmed) { setLegacyUnconfirmed(false); setAction(null); setReason(""); } } }
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
    const disabledReason = recovery.pending ? "Check the retained outcome of the pending command first." : action ? "Complete or cancel the current decision first." : loading ? "Loading the latest case and permitted decisions." : error ? "Reload this case before making a decision."
      : item?.version == null ? "A retained case version is required." : capability.disabledReason;
    const descriptionId = `case-${caseId}-${name}-disabled`;
    return <span key={name} className="inline-flex flex-col items-start gap-1"><Button type="button" size="sm" variant={name === primary ? "default" : "outline"}
      disabled={busy || loading || !!recovery.pending || !!action || !!error || item?.version == null || !capability.enabled}
      aria-describedby={disabledReason ? descriptionId : undefined}
      disabledReason={disabledReason} onClick={() => choose(name)}>{decisionLabels[name]}</Button>
      {disabledReason ? <span id={descriptionId} className="max-w-64 text-xs text-muted-foreground">{disabledReason}</span> : null}</span>;
  };
  return <section className="case-inspector min-w-0 space-y-4 break-words" aria-label="Case inspector" aria-busy={loading || busy} data-case-id={item?.breakId}>
    {loading ? <p role="status">Loading latest case…</p> : null}
    {error ? <div role="alert"><p>{error}</p><Button type="button" variant="outline" onClick={() => setRevision((value) => value + 1)}>Reload case</Button></div> : null}
    {item ? <div className="space-y-4">
      <header><div className="flex flex-wrap items-start justify-between gap-2"><h3 className="text-base font-semibold">{item.reason || item.strategyName}</h3><Badge>{item.status}</Badge></div>
        {item.recommendedAction ? <p className="mt-2 text-sm">{item.recommendedAction}</p> : null}
        <dl className="mt-3 grid gap-2 sm:grid-cols-2"><Fact label="Owner" value={item.assigneeDisplayName ?? item.assignedTo ?? "Unassigned"} />
          <Fact label="Due" value={item.slaDueAt ?? VALUE_STATE_LABELS.unavailable} /><Fact label="Version" value={String(item.version ?? VALUE_STATE_LABELS.unavailable)} /></dl></header>
      <TechnicalDetails label="Case identifiers">
        <dl className="grid gap-2 sm:grid-cols-2"><Fact label="Case ID" value={item.breakId} /><Fact label="Run" value={item.runId} />
          <Fact label="Fund account" value={item.fundAccountId ?? VALUE_STATE_LABELS.unavailable} /><Fact label="Accounting period" value={item.accountingPeriodId ?? VALUE_STATE_LABELS.unavailable} /></dl>
      </TechnicalDetails>
      <section aria-label="Comparison" className="rounded border border-border p-3">
        {item.statementEvidence || (item.measures?.length ?? 0) > 1 ? <ReconciliationCaseEvidence item={item} relatedRows={relatedCases} onOpenCase={onOpenCase}
          comparisonBreakId={comparisonBreakId} onComparisonChange={onComparisonChange} /> : <>
        <h4 className="text-sm font-semibold">Disagreement</h4>
        {!hasComparison ? <p className="my-2 text-sm">Comparison unavailable: {missingComparison}</p> : null}
        <p id={comparisonConditionId} className="mt-2 text-xs text-muted-foreground">Data condition unavailable. {dataReason}</p>
        <dl className="mt-3 grid gap-3 sm:grid-cols-2">
          <ValueFact label="Expected" value={expected} unit={measure?.unit} kind={measure?.kind} reason={dataReason} conditionContextId={comparisonConditionId} />
          <ValueFact label="Actual" value={actual} unit={measure?.unit} kind={measure?.kind} reason={dataReason} conditionContextId={comparisonConditionId} />
          <div><dt className="text-xs text-muted-foreground">Residual</dt><dd><CaseVariance item={item} conditionContextId={comparisonConditionId} /></dd></div>
          <ValueFact label="Tolerance" value={caseTolerance(item)} unit={measure?.unit} kind={measure?.kind} reason={dataReason} conditionContextId={comparisonConditionId} />
        </dl><p className="mt-2 text-xs text-muted-foreground">Basis: Not recorded; the source has not identified the comparison sides or subtraction convention.</p>
        {item.breakExplanation?.summary ? <p className="mt-2 text-sm">{item.breakExplanation.summary}</p> : null}
        <TechnicalDetails label="Retained comparison and lineage"><ReconciliationCaseEvidence item={item} relatedRows={relatedCases}
          onOpenCase={onOpenCase} comparisonBreakId={comparisonBreakId} onComparisonChange={onComparisonChange} /></TechnicalDetails>
        </>}
      </section>
      <ReconciliationMatchCandidate item={item} />
      <section aria-label="Case decisions" className="space-y-3">
        {!capabilities.length ? <p role="status">Decisions unavailable: the server has not supplied permitted commands for this case and your role.</p> : null}
        <div className="flex flex-wrap items-start gap-2">{primary ? renderDecision(primary) : null}
          {decisions.some((name) => name !== primary) ? <details><summary className="cursor-pointer rounded border border-border px-3 py-2 text-sm">Other decisions</summary><div className="mt-2 flex flex-wrap gap-2">{decisions.filter((name) => name !== primary).map(renderDecision)}</div></details> : null}</div>
        {action ? <form ref={formRef} aria-label={`${decisionLabels[action]} case`} className="space-y-3 rounded border border-border p-3" onSubmit={(event) => { event.preventDefault(); void submit(); }} onKeyDown={(event) => {
            if (event.key === "Escape" && !busy) { event.preventDefault(); event.stopPropagation(); closeDecision(); }
          }}><fieldset disabled={busy || loading || !!recovery.pending} className="space-y-3">
          <h4 className="font-semibold">Review {decisionLabels[action]?.toLowerCase()}</h4><p className="text-xs text-muted-foreground">Case {item.breakId} · version {item.version} · period {item.accountingPeriodId ?? "not recorded"}. The server validates the decision against this version.</p>
          {needsTaxonomy ? <>
            {needsRootCause ? <label className="block text-sm">Root cause code<Select required value={rootCause} onChange={(event) => setRootCause(event.target.value)}><option value="">Choose a root cause</option>{taxonomy?.rootCauses.filter((value) => value.isActive || value.code === item.rootCauseCode).map((value) => <option key={value.code} value={value.code}>{value.displayName}</option>)}</Select></label> : null}
            {needsResolution ? <label className="block text-sm">Resolution code<Select required value={resolution} onChange={(event) => setResolution(event.target.value)}><option value="">Choose a resolution</option>{taxonomy?.resolutionCodes.filter((value) => value.isActive || value.code === item.resolutionCode).map((value) => <option key={value.code} value={value.code}>{value.displayName}</option>)}</Select></label> : null}
            {taxonomyError ? <div role="alert"><p>{taxonomyError}</p><Button type="button" variant="outline" onClick={() => setTaxonomyRevision((value) => value + 1)}>Reload decision choices</Button></div> : !taxonomy ? <p role="status">Loading root causes and resolutions…</p> : null}
            {needsResolution && taxonomy?.resolutionCodes.find((value) => value.code === resolution)?.requiredEvidencePrefixes?.length ? <p className="text-sm text-muted-foreground">Retained evidence required: {taxonomy.resolutionCodes.find((value) => value.code === resolution)!.requiredEvidencePrefixes!.join(", ")}. Add missing support in discussion before confirming.</p> : null}
          </> : null}
          {action === "Assign" || action === "TransitionStatus" ? <label className="block text-sm">Assignee<Input required value={assignee} onChange={(event) => setAssignee(event.target.value)} /></label> : null}
          {action === "Supersede" ? <label className="block text-sm">Superseding case ID<Input required value={successor} onChange={(event) => setSuccessor(event.target.value)} /></label> : null}
          {action === "Waive" || action === "Supersede" ? <>
            <label className="block text-sm">Independent approver<Input required value={approvalActor} onChange={(event) => setApprovalActor(event.target.value)} /></label>
            <label className="block text-sm">Retained approval reference<Input required value={approvalReference} onChange={(event) => setApprovalReference(event.target.value)} /></label>
            <label className="block text-sm">Retained evidence references (comma separated)<Input required value={evidenceReferences} onChange={(event) => setEvidenceReferences(event.target.value)} /></label>
          </> : null}
          <label className="block text-sm">{decisionLabels[action]} rationale<Input required value={reason} onChange={(event) => setReason(event.target.value)} /></label>
          {conflict ? <section role="alert" aria-label="Case changed"><h4 className="font-semibold">Changed since you loaded it</h4>
            <DenseDataTable columns={[
              { id: "field", label: "Field", rowHeader: true, render: (change) => change.label },
              { id: "before", label: "Your version", render: (change) => change.before },
              { id: "after", label: "Current version", render: (change) => change.after }
            ]} rows={caseChanges(item, conflict)} getRowId={(change) => change.label}
              ariaLabel="Changes since the reviewed case" emptyText="The record version changed; no displayed field changed." />
            <p>Review the latest case before confirming again.</p><Button type="button" variant="outline" onClick={() => void reviewLatest()}>Review latest version</Button><Button type="button" variant="ghost" onClick={closeDecision}>Discard decision</Button>
          </section> : null}
          <div className="flex flex-wrap gap-2"><Button type="submit" disabled={busy || loading || !!recovery.pending || !!error || !!conflict || !reason.trim() || !activeCapability?.enabled || !classificationReady}
            disabledReason={!activeCapability?.enabled ? activeCapability?.disabledReason ?? "This decision is no longer permitted. Review the latest case." : undefined}>{busy ? "Recording decision…" : `Confirm ${decisionLabels[action]?.toLowerCase()}`}</Button><Button type="button" variant="ghost" disabled={busy} onClick={closeDecision}>Cancel</Button></div>
        </fieldset></form> : null}
      </section>
      {recovery.pending ? <section aria-label="Pending case command" className="space-y-2 rounded border border-border p-3">
        <h4 className="font-semibold">Command outcome unconfirmed</h4><p className="break-all text-xs">Command ID {recovery.pending.commandId}</p>
        <p>Check retained case history before any further decision. Retrying uses the identical reviewed payload and command identity.</p>
        <Button type="button" variant="outline" disabled={busy || recovery.checking} onClick={() => void recovery.check()}>Check command outcome</Button>
        {recovery.canResume && recovery.pending.command && commandSender(recovery.pending.command) ? <Button type="button" variant="outline" disabled={busy || loading || recovery.checking} onClick={() => void submit(recovery.pending!.command)}>Resume same command</Button> : null}
      </section> : null}
      {recovery.message ? <p role="status">{recovery.message}</p> : null}
      {receipt ? <section ref={receiptRef} tabIndex={-1} role="status" aria-label="Decision receipt" className="rounded border border-border p-3"><h4 className="font-semibold">{receipt.outcome.state}</h4><p>{receipt.error ?? `${receipt.outcome.isSuccessful ? "Decision recorded" : "Decision not completed"} for ${item.breakId}, version ${receipt.item?.version ?? "not supplied"}.`}</p><p className="text-xs">Receipt {receipt.outcome.operationId}</p>{receipt.outcome.issues?.map((issue) => <p key={issue.code}>{issue.message}</p>)}<a className="underline" href={reconciliationBreakAuditEndpoint(item.breakId)}>Review audit entry</a></section> : null}
      <div className="flex flex-wrap gap-2"><Button asChild size="sm" variant="outline"><Link to={evidenceWorkbenchPath("reconciliation-break", item.breakId)}>Retained evidence</Link></Button>
        {routingHref ? <Button asChild size="sm" variant="outline"><Link to={routingHref} aria-label={`Open routing target for reconciliation break ${item.breakId}`}>Open routing target</Link></Button> : null}</div>
      <TechnicalDetails label="Timeline and discussion">
        <dl className="my-3 grid gap-2 sm:grid-cols-2"><Fact label="Source observation" value={`${sourceObservationLabel(item.lineage)} · ${sourceObservationAge(item.lineage)}`} />
          <Fact label="Updated" value={item.lastUpdatedAt} /><Fact label="Source system" value={item.sourceSystem ?? VALUE_STATE_LABELS.unavailable} /><Fact label="Source reference" value={item.sourceReference ?? VALUE_STATE_LABELS.unavailable} />
          <Fact label="Required sign-off" value={item.requiredSignoffRole ?? VALUE_STATE_LABELS.unavailable} /><Fact label="Sign-off status" value={item.signoffStatus ?? VALUE_STATE_LABELS.unavailable} />
          <Fact label="Root cause code" value={item.rootCauseCode ?? VALUE_STATE_LABELS.unavailable} /><Fact label="Resolution code" value={item.resolutionCode ?? VALUE_STATE_LABELS.unavailable} />
          <Fact label="Decision note" value={item.resolutionNote ?? VALUE_STATE_LABELS.unavailable} /><Fact label="Ledger impact" value={item.breakExplanation?.ledgerImpact ?? VALUE_STATE_LABELS.unavailable} /></dl>
        <ReconciliationCaseDiscussion key={item.breakId} breakId={item.breakId} onUpdated={acceptExternalUpdate} />
        <BreakAuditTimeline key={`audit:${item.breakId}`} breakId={item.breakId} refreshKey={item.version} load={services.audit as BreakAuditLoader} />
      </TechnicalDetails>
    </div> : !loading ? <p role="status">This case is unavailable in the current scope.</p> : null}
  </section>;
}

function Fact({ label, value }: { label: string; value: string }) { return <div><dt className="text-xs text-muted-foreground">{label}</dt><dd className="text-sm">{value}</dd></div>; }
function ValueFact({ label, value, unit, kind, reason, signed, conditionContextId }: { label: string; value?: string | number | null; unit?: string; kind?: ReconciliationBreakMeasure["kind"]; reason: string; signed?: boolean; conditionContextId?: string }) {
  const declared = unit?.trim();
  const unavailableReason = reconciliationValueUnavailableReason(value);
  return <div><dt className="text-xs text-muted-foreground">{label}</dt><dd>{unavailableReason
    ? <span className="inline-flex flex-col" aria-describedby={conditionContextId}>{VALUE_STATE_LABELS.unavailable}<span className="text-xs text-muted-foreground">{unavailableReason}</span></span>
    : <FinancialValue value={value} currency={declared && /^[A-Z]{3}$/.test(declared) ? declared : undefined} unit={reconciliationUnitLabel(declared, kind)} decimals={reconciliationValueDecimals(value, declared)} reason={reason} signed={signed} conditionContextId={conditionContextId} />}</dd></div>;
}

export function CaseVariance({ item, conditionContextId }: { item: ReconciliationBreakQueueItem; conditionContextId?: string }) {
  const residual = caseQueueResidual(item);
  const unit = caseResidualUnit(item);
  if (residual.amount === "Multiple" || residual.amount === "Not recorded" || isUnavailableLabel(residual.amount)) return <span>{residual.amount}</span>;
  return <FinancialValue value={caseResidual(item)} signed
    currency={unit && /^[A-Z]{3}$/.test(unit) ? unit : undefined} unit={residual.unit ?? "Unit unavailable"}
    decimals={reconciliationValueDecimals(caseResidual(item), unit)}
    conditionContextId={conditionContextId} className="whitespace-nowrap"
    reason="A verified data condition was not supplied for this case measure." />;
}
