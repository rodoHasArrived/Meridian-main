import { useEffect, useId, useState } from "react";
import {
  getReconciliationBreakAudit,
  getReconciliationBreakDetail,
  reopenReconciliationBreak,
  setReconciliationBreakResolution,
  setReconciliationBreakRootCause,
  signOffReconciliationBreak,
  supersedeReconciliationBreak,
  waiveReconciliationBreak
} from "@/lib/api";
import { getReconciliationTaxonomy } from "@/lib/api/reconciliation-readiness.api";
import { Button } from "@/components/ui/button";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { TextArea } from "@/components/ui/text-area";
import { buildBreakLifecycleModel, lifecycleStateLabel, type BreakLifecycleAction, type BreakLifecycleActionId } from "./reconciliation-break-lifecycle";
import { createVersionedCaseworkCommand, isStatementHandoffMarker, runReconciliationCaseworkCommand } from "./reconciliation-casework-command";
import type {
  ReconciliationBreakQueueItem,
  ReconciliationBreakQueueAuditEvent,
  ReconciliationCaseworkCommand,
  ReconciliationCaseworkOperationResult
} from "@/types";
import type { ReconciliationTaxonomySnapshot } from "@/types/reconciliation-readiness.types";

type Send = (command: ReconciliationCaseworkCommand) => Promise<ReconciliationCaseworkOperationResult>;

export interface BreakLifecycleServices {
  read: (breakId: string) => Promise<ReconciliationBreakQueueItem>;
  audit?: (breakId: string) => Promise<ReconciliationBreakQueueAuditEvent[]>;
  taxonomy: () => Promise<ReconciliationTaxonomySnapshot>;
  setRootCause: Send;
  setResolution: Send;
  waive: Send;
  supersede?: Send;
  signOff: Send;
  reopen: Send;
}

export const defaultBreakLifecycleServices: BreakLifecycleServices = {
  read: getReconciliationBreakDetail,
  audit: getReconciliationBreakAudit,
  taxonomy: () => getReconciliationTaxonomy(),
  setRootCause: setReconciliationBreakRootCause,
  setResolution: setReconciliationBreakResolution,
  waive: waiveReconciliationBreak,
  supersede: supersedeReconciliationBreak,
  signOff: signOffReconciliationBreak,
  reopen: reopenReconciliationBreak
};

const SOURCE = "browser-reconciliation-inspector";

type DraftAction = BreakLifecycleActionId | "rootCause" | "resolution";

interface PendingDecision {
  action: DraftAction;
  commandId: string;
  command?: ReconciliationCaseworkCommand;
}

const COMMAND_ACTION: Record<DraftAction, ReconciliationCaseworkCommand["action"]> = {
  rootCause: "SetRootCause",
  resolution: "SetResolution",
  waive: "Waive",
  supersede: "Supersede",
  signOff: "SignOff",
  reopen: "Reopen"
};

const ACTION_EVENT: Record<DraftAction, string> = {
  rootCause: "RootCauseSet",
  resolution: "ResolutionSet",
  waive: "Waived",
  supersede: "Superseded",
  signOff: "SignedOff",
  reopen: "Reopen"
};

const SUCCESS_TEXT: Record<DraftAction, string> = {
  rootCause: "Root cause retained in case history.",
  resolution: "Resolution retained in case history.",
  waive: "Waiver retained in case history. The break is resolved.",
  supersede: "Supersession retained in case history.",
  signOff: "Sign-off retained in case history.",
  reopen: "Break reopened. The reason is retained in case history."
};

function splitEvidenceLinks(value: string): string[] {
  return [...new Set(value.split(/[\n,]/).map((link) => link.trim()).filter((link) => link && !isStatementHandoffMarker(link)))];
}

function readPendingDecision(key: string | null, memory?: Map<string, ReconciliationCaseworkCommand>): PendingDecision | null {
  if (!key) return null;
  try {
    const raw = sessionStorage.getItem(key);
    const pending = raw ? JSON.parse(raw) as Pick<PendingDecision, "commandId" | "action"> : null;
    const command = memory?.get(key);
    return pending && Object.hasOwn(COMMAND_ACTION, pending.action) && typeof pending.commandId === "string"
      ? { ...pending, command: command?.commandId === pending.commandId ? command : undefined }
      : null;
  } catch { return null; }
}

/**
 * Lifecycle decisions for the selected break: root cause, resolution, waive, sign-off and reopen.
 *
 * Every change is a versioned command. The server re-checks eligibility, approval, taxonomy and
 * role, and answers with the new version, which is handed to `onUpdated` for the queue.
 */
export function BreakLifecyclePanel({ item, onUpdated, operatorId, commandMemory, readOnlyReason, services = defaultBreakLifecycleServices }: {
  item: ReconciliationBreakQueueItem;
  onUpdated?: (item: ReconciliationBreakQueueItem) => void;
  operatorId?: string | null;
  commandMemory?: Map<string, ReconciliationCaseworkCommand>;
  readOnlyReason?: string | null;
  services?: BreakLifecycleServices;
}) {
  const id = useId();
  const model = buildBreakLifecycleModel(item);
  const recoveryKey = operatorId && item.tenantId && item.companyId
    ? `meridian:reconciliation:decision:${JSON.stringify([operatorId, item.tenantId, item.companyId, item.breakId])}`
    : null;
  const [taxonomy, setTaxonomy] = useState<ReconciliationTaxonomySnapshot | null>(null);
  const [taxonomyError, setTaxonomyError] = useState<string | null>(null);
  const [rootCause, setRootCause] = useState(item.rootCauseCode ?? "");
  const [resolution, setResolution] = useState(item.resolutionCode ?? "");
  const [decision, setDecision] = useState<{ action: BreakLifecycleAction; reviewed: ReconciliationBreakQueueItem } | null>(null);
  const [reason, setReason] = useState("");
  const [evidence, setEvidence] = useState((item.evidenceLinks ?? []).join("\n"));
  const [approver, setApprover] = useState("");
  const [approvalReference, setApprovalReference] = useState("");
  const [successor, setSuccessor] = useState("");
  const [pending, setPending] = useState(() => readPendingDecision(recoveryKey, commandMemory));
  const [canResume, setCanResume] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [requiresReload, setRequiresReload] = useState(false);
  const [periodLock, setPeriodLock] = useState<string | null>(null);

  const hasLifecycle = Boolean(model);
  useEffect(() => {
    if (!hasLifecycle) return;
    let cancelled = false;
    services.taxonomy()
      .then((loaded) => { if (!cancelled) setTaxonomy(loaded); })
      .catch((failure: unknown) => {
        if (!cancelled) setTaxonomyError(failure instanceof Error ? failure.message : "The reconciliation taxonomy could not be loaded.");
      });
    return () => { cancelled = true; };
  }, [hasLifecycle, services]);

  // A new version from anywhere (this panel, the queue, another tab) resets the classification drafts.
  useEffect(() => {
    setRootCause(item.rootCauseCode ?? "");
    setResolution(item.resolutionCode ?? "");
  }, [item.breakId, item.version, item.rootCauseCode, item.resolutionCode]);

  if (!model) return null;
  const lifecycle = model;

  const editable = !busy && !pending && !requiresReload && !readOnlyReason && !periodLock;
  const stalePreview = decision !== null && (decision.reviewed.version !== item.version
    || decision.reviewed.lineage?.lastObservedRunId !== item.lineage?.lastObservedRunId
    || decision.reviewed.sourceFingerprint !== item.sourceFingerprint);
  const rootCauses = taxonomy?.rootCauses.filter((value) => value.isActive || value.code === item.rootCauseCode) ?? [];
  const resolutions = taxonomy?.resolutionCodes.filter((value) => value.isActive || value.code === item.resolutionCode) ?? [];
  const selectedResolution = resolutions.find((value) => value.code === resolution);
  const evidenceLinks = splitEvidenceLinks(evidence);

  function retainPending(value: PendingDecision | null) {
    setPending(value);
    if (!recoveryKey) return;
    if (value?.command) commandMemory?.set(recoveryKey, value.command);
    else commandMemory?.delete(recoveryKey);
    try {
      if (value) sessionStorage.setItem(recoveryKey, JSON.stringify({ action: value.action, commandId: value.commandId }));
      else sessionStorage.removeItem(recoveryKey);
    } catch { /* The command ID also remains available in this panel. */ }
  }

  async function send(action: DraftAction, command: ReconciliationCaseworkCommand, resuming = false) {
    const sendCommand = {
      rootCause: services.setRootCause,
      resolution: services.setResolution,
      waive: services.waive,
      supersede: services.supersede,
      signOff: services.signOff,
      reopen: services.reopen
    }[action];
    if (!sendCommand) return;
    retainPending({ action, commandId: command.commandId, command });
    setCanResume(false);
    setBusy(true); setError(null); setStatus(null);
    const outcome = await runReconciliationCaseworkCommand(command, sendCommand);
    setBusy(false);
    if (outcome.kind === "succeeded") {
      retainPending(null);
      setDecision(null);
      setStatus(SUCCESS_TEXT[action]);
      onUpdated?.(outcome.item);
      return;
    }
    if (outcome.kind === "conflict" && !resuming) {
      retainPending(null);
      setRequiresReload(true);
    } else if (outcome.kind === "refused") {
      // The server checked and declined this command; the same identity would be declined again.
      if (!resuming) retainPending(null);
      if (outcome.errorCode === "AccountingPeriodHardClosed") setPeriodLock(outcome.message);
    }
    setError(outcome.kind === "failed" ? "The request ended before its outcome could be confirmed. Check retained case history." : outcome.message);
  }

  function start(action: DraftAction, fields: Partial<ReconciliationCaseworkCommand>, reviewed = item) {
    if (!editable) return;
    const command = createVersionedCaseworkCommand(reviewed, COMMAND_ACTION[action], SOURCE, fields);
    if (command) void send(action, command);
  }

  async function reload() {
    setBusy(true);
    try {
      const fresh = await services.read(item.breakId);
      if (fresh.breakId !== item.breakId) throw new Error("The reloaded break does not match the selected break.");
      const reviewed = decision?.reviewed ?? item;
      setDecision(null);
      setRequiresReload(false);
      setError(null);
      setStatus(`Case version ${reviewed.version ?? "unavailable"} → ${fresh.version ?? "unavailable"}. ${lifecycleStateLabel(reviewed.lifecycleState)} → ${lifecycleStateLabel(fresh.lifecycleState)}. Review the current evidence before opening a new preview.`);
      onUpdated?.(fresh);
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The break could not be reloaded.");
    } finally {
      setBusy(false);
    }
  }

  async function checkOutcome() {
    if (!pending || busy) return;
    setBusy(true); setError(null); setStatus(null);
    setCanResume(false);
    try {
      const history = (await (services.audit ?? getReconciliationBreakAudit)(item.breakId)).filter((event) => event.breakId === item.breakId);
      const commandId = pending.commandId;
      const recorded = history.some((event) => event.commandId === commandId && event.eventType === ACTION_EVENT[pending.action]);
      if (!recorded) {
        setStatus("Outcome still unconfirmed. The command is absent from the retained audit history.");
        setCanResume(true);
        return;
      }
      const needsHandoff = item.sourceType === "statement" && pending.action !== "rootCause" && pending.action !== "resolution";
      if (needsHandoff && !history.some((event) => event.eventType === "EvidenceLinked"
        && event.causationId === commandId && event.source === "statement-casework-handoff")) {
        setStatus("The case action is recorded. Source and downstream handoff are still unconfirmed.");
        setCanResume(true);
        return;
      }
      const fresh = await services.read(item.breakId);
      if (fresh.breakId !== item.breakId) throw new Error("The returned case does not match this command.");
      onUpdated?.(fresh);
      retainPending(null);
      setDecision(null);
      setStatus("Command confirmed in retained case history.");
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The command outcome could not be checked.");
    } finally {
      setBusy(false);
    }
  }

  function decisionReady(action: BreakLifecycleAction): { ready: boolean; reason: string | null } {
    if (action.id === "waive" || action.id === "supersede") {
      if (!reason.trim()) return { ready: false, reason: "Give a reason for the disposition." };
      if (evidenceLinks.length === 0) return { ready: false, reason: "Add at least one evidence link." };
      if (!approver.trim() || !approvalReference.trim()) return { ready: false, reason: "Name the approver and the approval reference." };
      if (action.id === "supersede" && (!successor.trim() || successor.trim().toLowerCase() === item.breakId.toLowerCase())) {
        return { ready: false, reason: "Enter a different retained case ID as the successor." };
      }
      return { ready: true, reason: null };
    }
    if (action.id === "reopen" && !reason.trim()) return { ready: false, reason: "Give a reason for reopening." };
    return { ready: true, reason: null };
  }

  function confirm() {
    if (!decision || stalePreview) return;
    const action = decision.action.id;
    const trimmed = reason.trim();
    if (action === "waive" || action === "supersede") {
      start(action, {
        reason: trimmed,
        evidenceLinks,
        approvalActor: approver.trim(),
        approvalReference: approvalReference.trim(),
        ...(action === "supersede" ? { supersedingBreakId: successor.trim() } : {})
      }, decision.reviewed);
    } else if (action === "signOff") {
      start("signOff", trimmed ? { reason: trimmed, note: trimmed } : {}, decision.reviewed);
    } else {
      start("reopen", { reason: trimmed }, decision.reviewed);
    }
  }

  const activeAction = decision?.action ?? null;
  const readiness = activeAction ? decisionReady(activeAction) : null;

  return (
    <section aria-label="Break lifecycle" className="grid gap-3 border-t border-border pt-3" aria-busy={busy}>
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h3 className="text-sm font-semibold">Lifecycle</h3>
        <span className="text-sm text-muted-foreground">State: <span className="font-medium text-foreground">{lifecycle.lifecycleLabel}</span></span>
      </div>

      {error ? <StatusBanner role="alert" tone="warning" title={pending ? "Command outcome unconfirmed" : "Lifecycle change needs review"} detail={error} /> : null}
      {readOnlyReason || periodLock ? <p className="text-sm text-muted-foreground">{readOnlyReason ?? periodLock}</p> : null}
      {status ? <p role="status" className="text-sm">{status}</p> : null}
      {stalePreview ? <StatusBanner role="alert" tone="warning" title="Case changed after preview" detail={`Reviewed version ${decision?.reviewed.version} · current version ${item.version}. Reload to review the current case.`} /> : null}
      {requiresReload || stalePreview ? <Button size="sm" variant="outline" className="w-fit" disabled={busy || Boolean(pending)} onClick={() => void reload()}>Reload this break</Button> : null}
      {pending ? (
        <div className="grid gap-2">
          <p className="break-all text-xs text-muted-foreground">Command ID <code>{pending.commandId}</code></p>
          <Button size="sm" variant="outline" className="w-fit" disabled={busy} onClick={() => void checkOutcome()}>Check outcome</Button>
          {canResume && pending.command ? <Button size="sm" variant="outline" className="w-fit" disabled={busy || Boolean(readOnlyReason)}
            onClick={() => { if (pending.command) void send(pending.action, pending.command, true); }}>Resume same command</Button> : null}
        </div>
      ) : null}

      <div className="grid gap-2" role="group" aria-labelledby={`${id}-decision`}>
        <p id={`${id}-decision`} className="text-xs font-semibold text-muted-foreground">Decision</p>
        <div className="panel-action-zone justify-start">
          {lifecycle.actions.filter((action) => action.id !== "supersede" || services.supersede).map((action) => (
            <Button
              key={action.id}
              size="sm"
              variant={action.id === "waive" ? "outline" : "secondary"}
              disabled={!action.enabled || !editable || stalePreview || activeAction?.id === action.id}
              disabledReason={action.disabledReason ?? undefined}
              onClick={() => {
                setDecision({ action, reviewed: item });
                setReason(""); setApprover(""); setApprovalReference(""); setSuccessor("");
                setEvidence((item.evidenceLinks ?? []).filter((reference) => !isStatementHandoffMarker(reference)).join("\n"));
                setError(null); setStatus(null);
              }}
            >
              {action.label}
            </Button>
          ))}
        </div>
      </div>

      {activeAction && readiness ? (
        <form
          aria-label={`${activeAction.label} this break`}
          className="grid gap-2 rounded-lg border border-border/50 bg-secondary/20 p-3"
          onSubmit={(event) => { event.preventDefault(); if (readiness.ready && editable) confirm(); }}
        >
          <p className="text-sm">
            <span className="font-medium">Preview: </span>
            Lifecycle {activeAction.preview}. Reviewed case version {decision?.reviewed.version}.
          </p>
          {activeAction.id === "supersede" ? <p className="text-sm text-muted-foreground">Retains this case and records which case takes over.</p> : null}
          {activeAction.id === "waive" ? <p className="text-sm text-muted-foreground">Records an approved exception against this case.</p> : null}
          <FormRow
            label={activeAction.id === "signOff" ? "Sign-off note (required if you resolved this break)" : "Reason"}
            labelFor={`${id}-reason`}
          >
            <Input id={`${id}-reason`} value={reason} autoFocus required={activeAction.id !== "signOff"} disabled={!editable} onChange={(event) => setReason(event.target.value)} />
          </FormRow>
          {activeAction.id === "waive" || activeAction.id === "supersede" ? (
            <>
              {activeAction.id === "supersede" ? <FormRow label="Successor case ID" labelFor={`${id}-successor`}>
                <Input id={`${id}-successor`} value={successor} disabled={!editable} onChange={(event) => setSuccessor(event.target.value)} />
              </FormRow> : null}
              <FormRow label="Evidence links (one per line)" labelFor={`${id}-evidence`}>
                <TextArea id={`${id}-evidence`} rows={2} className="min-h-16" value={evidence} disabled={!editable} onChange={(event) => setEvidence(event.target.value)} />
              </FormRow>
              <FormRow label="Approver operator ID" labelFor={`${id}-approver`} hint="The approver has to be someone other than you.">
                <Input id={`${id}-approver`} value={approver} disabled={!editable} onChange={(event) => setApprover(event.target.value)} />
              </FormRow>
              <FormRow label="Approval reference" labelFor={`${id}-approval-reference`}>
                <Input id={`${id}-approval-reference`} value={approvalReference} disabled={!editable} onChange={(event) => setApprovalReference(event.target.value)} />
              </FormRow>
            </>
          ) : null}
          <div className="panel-action-zone justify-start">
            <Button type="submit" size="sm" disabled={!readiness.ready || !editable || stalePreview} disabledReason={readiness.reason ?? undefined}>
              Confirm {activeAction.label.toLowerCase()}
            </Button>
            <Button type="button" size="sm" variant="ghost" onClick={() => setDecision(null)}>Cancel</Button>
          </div>
        </form>
      ) : null}

      <div className="grid gap-2" role="group" aria-labelledby={`${id}-classification`}>
        <p id={`${id}-classification`} className="text-xs font-semibold text-muted-foreground">Classification</p>
        {taxonomyError ? <p className="text-sm text-muted-foreground">Root cause and resolution codes are unavailable: {taxonomyError}</p> : null}
        {!lifecycle.canClassify && lifecycle.classifyDisabledReason ? <p className="text-sm text-muted-foreground">{lifecycle.classifyDisabledReason}</p> : null}
        <FormRow label="Root cause" labelFor={`${id}-root-cause`}>
          <div className="flex flex-wrap gap-2">
            <Select
              id={`${id}-root-cause`}
              value={rootCause}
              placeholder={taxonomy ? "Choose a root cause" : "Loading codes…"}
              disabled={!taxonomy || !lifecycle.canClassify || !editable}
              onChange={(event) => setRootCause(event.target.value)}
            >
              {rootCauses.map((value) => <option key={value.code} value={value.code}>{value.displayName}</option>)}
            </Select>
            <Button
              size="sm"
              variant="outline"
              disabled={!rootCause || rootCause === (item.rootCauseCode ?? "") || !lifecycle.canClassify || !editable}
              disabledReason={lifecycle.classifyDisabledReason ?? "Choose a different root cause first."}
              onClick={() => start("rootCause", { rootCauseCode: rootCause })}
            >
              Set root cause
            </Button>
          </div>
        </FormRow>
        <FormRow
          label="Resolution"
          labelFor={`${id}-resolution`}
          hint={selectedResolution?.requiredEvidencePrefixes?.length
            ? `Needs evidence starting with ${selectedResolution.requiredEvidencePrefixes.join(" or ")}.`
            : undefined}
        >
          <div className="flex flex-wrap gap-2">
            <Select
              id={`${id}-resolution`}
              value={resolution}
              placeholder={taxonomy ? "Choose a resolution" : "Loading codes…"}
              disabled={!taxonomy || !lifecycle.canClassify || !editable}
              onChange={(event) => setResolution(event.target.value)}
            >
              {resolutions.map((value) => <option key={value.code} value={value.code}>{value.displayName}</option>)}
            </Select>
            <Button
              size="sm"
              variant="outline"
              disabled={!resolution || resolution === (item.resolutionCode ?? "") || !lifecycle.canClassify || !editable}
              disabledReason={lifecycle.classifyDisabledReason ?? "Choose a different resolution first."}
              onClick={() => start("resolution", { resolutionCode: resolution,
                evidenceLinks: (item.evidenceLinks ?? []).filter((reference) => !isStatementHandoffMarker(reference)) })}
            >
              Set resolution
            </Button>
          </div>
        </FormRow>
      </div>
    </section>
  );
}
