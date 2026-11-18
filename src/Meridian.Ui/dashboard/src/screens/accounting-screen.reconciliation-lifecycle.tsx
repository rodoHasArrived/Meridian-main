import { useEffect, useId, useState } from "react";
import {
  getReconciliationBreakDetail,
  reopenReconciliationBreak,
  setReconciliationBreakResolution,
  setReconciliationBreakRootCause,
  signOffReconciliationBreak,
  waiveReconciliationBreak
} from "@/lib/api";
import { getReconciliationTaxonomy } from "@/lib/api/reconciliation-readiness.api";
import { Button } from "@/components/ui/button";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { TextArea } from "@/components/ui/text-area";
import { buildBreakLifecycleModel, type BreakLifecycleAction, type BreakLifecycleActionId } from "./reconciliation-break-lifecycle";
import { createVersionedCaseworkCommand, runReconciliationCaseworkCommand } from "./reconciliation-casework-command";
import type {
  ReconciliationBreakQueueItem,
  ReconciliationCaseworkCommand,
  ReconciliationCaseworkOperationResult
} from "@/types";
import type { ReconciliationTaxonomySnapshot } from "@/types/reconciliation-readiness.types";

type Send = (command: ReconciliationCaseworkCommand) => Promise<ReconciliationCaseworkOperationResult>;

export interface BreakLifecycleServices {
  read: (breakId: string) => Promise<ReconciliationBreakQueueItem>;
  taxonomy: () => Promise<ReconciliationTaxonomySnapshot>;
  setRootCause: Send;
  setResolution: Send;
  waive: Send;
  signOff: Send;
  reopen: Send;
}

export const defaultBreakLifecycleServices: BreakLifecycleServices = {
  read: getReconciliationBreakDetail,
  taxonomy: () => getReconciliationTaxonomy(),
  setRootCause: setReconciliationBreakRootCause,
  setResolution: setReconciliationBreakResolution,
  waive: waiveReconciliationBreak,
  signOff: signOffReconciliationBreak,
  reopen: reopenReconciliationBreak
};

const SOURCE = "browser-reconciliation-inspector";

type DraftAction = BreakLifecycleActionId | "rootCause" | "resolution";

const SUCCESS_TEXT: Record<DraftAction, string> = {
  rootCause: "Root cause retained in case history.",
  resolution: "Resolution retained in case history.",
  waive: "Waiver retained in case history. The break is resolved.",
  signOff: "Sign-off retained in case history.",
  reopen: "Break reopened. The reason is retained in case history."
};

function splitEvidenceLinks(value: string): string[] {
  return [...new Set(value.split(/[\n,]/).map((link) => link.trim()).filter(Boolean))];
}

/**
 * Lifecycle decisions for the selected break: root cause, resolution, waive, sign-off and reopen.
 *
 * Every change is a versioned command. The server re-checks eligibility, approval, taxonomy and
 * role, and answers with the new version, which is handed to `onUpdated` for the queue.
 */
export function BreakLifecyclePanel({ item, onUpdated, services = defaultBreakLifecycleServices }: {
  item: ReconciliationBreakQueueItem;
  onUpdated?: (item: ReconciliationBreakQueueItem) => void;
  services?: BreakLifecycleServices;
}) {
  const id = useId();
  const model = buildBreakLifecycleModel(item);
  const [taxonomy, setTaxonomy] = useState<ReconciliationTaxonomySnapshot | null>(null);
  const [taxonomyError, setTaxonomyError] = useState<string | null>(null);
  const [rootCause, setRootCause] = useState(item.rootCauseCode ?? "");
  const [resolution, setResolution] = useState(item.resolutionCode ?? "");
  const [openAction, setOpenAction] = useState<BreakLifecycleActionId | null>(null);
  const [reason, setReason] = useState("");
  const [evidence, setEvidence] = useState((item.evidenceLinks ?? []).join("\n"));
  const [approver, setApprover] = useState("");
  const [approvalReference, setApprovalReference] = useState("");
  const [pending, setPending] = useState<{ action: DraftAction; command: ReconciliationCaseworkCommand } | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [requiresReload, setRequiresReload] = useState(false);

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

  const editable = !busy && !pending && !requiresReload;
  const rootCauses = taxonomy?.rootCauses.filter((value) => value.isActive || value.code === item.rootCauseCode) ?? [];
  const resolutions = taxonomy?.resolutionCodes.filter((value) => value.isActive || value.code === item.resolutionCode) ?? [];
  const selectedResolution = resolutions.find((value) => value.code === resolution);
  const evidenceLinks = splitEvidenceLinks(evidence);

  async function send(action: DraftAction, command: ReconciliationCaseworkCommand) {
    const sender: Record<DraftAction, Send> = {
      rootCause: services.setRootCause,
      resolution: services.setResolution,
      waive: services.waive,
      signOff: services.signOff,
      reopen: services.reopen
    };
    setPending({ action, command }); setBusy(true); setError(null); setStatus(null);
    const outcome = await runReconciliationCaseworkCommand(command, sender[action]);
    setBusy(false);
    if (outcome.kind === "succeeded") {
      setPending(null);
      setOpenAction(null);
      setReason(""); setApprover(""); setApprovalReference("");
      setStatus(SUCCESS_TEXT[action]);
      onUpdated?.(outcome.item);
      return;
    }
    if (outcome.kind === "conflict") {
      setPending(null);
      setRequiresReload(true);
    } else if (outcome.kind === "refused") {
      // The server checked and declined this command; the same identity would be declined again.
      setPending(null);
    }
    setError(outcome.message);
  }

  function start(action: DraftAction, fields: Partial<ReconciliationCaseworkCommand>) {
    if (!editable) return;
    const actionName: Record<DraftAction, ReconciliationCaseworkCommand["action"]> = {
      rootCause: "SetRootCause",
      resolution: "SetResolution",
      waive: "Waive",
      signOff: "SignOff",
      reopen: "Reopen"
    };
    const command = createVersionedCaseworkCommand(item, actionName[action], SOURCE, fields);
    if (command) void send(action, command);
  }

  async function reload() {
    setBusy(true);
    try {
      const fresh = await services.read(item.breakId);
      if (fresh.breakId !== item.breakId) throw new Error("The reloaded break does not match the selected break.");
      setRequiresReload(false);
      setError(null);
      setStatus("Reloaded the current version of this break.");
      onUpdated?.(fresh);
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The break could not be reloaded.");
    } finally {
      setBusy(false);
    }
  }

  function decisionReady(action: BreakLifecycleAction): { ready: boolean; reason: string | null } {
    if (action.id === "waive") {
      if (!reason.trim()) return { ready: false, reason: "Give a reason for the waiver." };
      if (evidenceLinks.length === 0) return { ready: false, reason: "Add at least one evidence link." };
      if (!approver.trim() || !approvalReference.trim()) return { ready: false, reason: "Name the approver and the approval reference." };
      return { ready: true, reason: null };
    }
    if (action.id === "reopen" && !reason.trim()) return { ready: false, reason: "Give a reason for reopening." };
    return { ready: true, reason: null };
  }

  function confirm(action: BreakLifecycleAction) {
    const trimmed = reason.trim();
    if (action.id === "waive") {
      start("waive", {
        reason: trimmed,
        evidenceLinks,
        approvalActor: approver.trim(),
        approvalReference: approvalReference.trim()
      });
    } else if (action.id === "signOff") {
      start("signOff", trimmed ? { reason: trimmed, note: trimmed } : {});
    } else {
      start("reopen", { reason: trimmed });
    }
  }

  const activeAction = lifecycle.actions.find((action) => action.id === openAction) ?? null;
  const readiness = activeAction ? decisionReady(activeAction) : null;

  return (
    <section aria-label="Break lifecycle" className="grid gap-3 border-t border-border pt-3" aria-busy={busy}>
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h3 className="text-sm font-semibold">Lifecycle</h3>
        <span className="text-sm text-muted-foreground">State: <span className="font-medium text-foreground">{lifecycle.lifecycleLabel}</span></span>
      </div>

      {error ? <StatusBanner role="alert" tone="warning" title="Lifecycle change not applied" detail={error} /> : null}
      {status ? <p role="status" className="text-sm">{status}</p> : null}
      {requiresReload ? <Button size="sm" variant="outline" className="w-fit" disabled={busy} onClick={() => void reload()}>Reload this break</Button> : null}
      {pending && !busy ? (
        <Button size="sm" variant="outline" className="w-fit" onClick={() => void send(pending.action, pending.command)}>Retry the same command</Button>
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
              onClick={() => start("resolution", { resolutionCode: resolution, evidenceLinks: item.evidenceLinks ?? [] })}
            >
              Set resolution
            </Button>
          </div>
        </FormRow>
      </div>

      <div className="grid gap-2" role="group" aria-labelledby={`${id}-decision`}>
        <p id={`${id}-decision`} className="text-xs font-semibold text-muted-foreground">Decision</p>
        <div className="panel-action-zone justify-start">
          {lifecycle.actions.map((action) => (
            <Button
              key={action.id}
              size="sm"
              variant={action.id === "waive" ? "outline" : "secondary"}
              disabled={!action.enabled || !editable || openAction === action.id}
              disabledReason={action.disabledReason ?? undefined}
              onClick={() => { setOpenAction(action.id); setReason(""); setError(null); setStatus(null); }}
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
          onSubmit={(event) => { event.preventDefault(); if (readiness.ready && editable) confirm(activeAction); }}
        >
          <p className="text-sm">
            <span className="font-medium">Preview: </span>
            Lifecycle {activeAction.preview}. The decision is retained in the audit history.
          </p>
          <FormRow
            label={activeAction.id === "signOff" ? "Sign-off note (required if you resolved this break)" : "Reason"}
            labelFor={`${id}-reason`}
          >
            <Input id={`${id}-reason`} value={reason} autoFocus required={activeAction.id !== "signOff"} disabled={!editable} onChange={(event) => setReason(event.target.value)} />
          </FormRow>
          {activeAction.id === "waive" ? (
            <>
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
            <Button type="submit" size="sm" disabled={!readiness.ready || !editable} disabledReason={readiness.reason ?? undefined}>
              Confirm {activeAction.label.toLowerCase()}
            </Button>
            <Button type="button" size="sm" variant="ghost" onClick={() => setOpenAction(null)}>Cancel</Button>
          </div>
        </form>
      ) : null}
    </section>
  );
}
