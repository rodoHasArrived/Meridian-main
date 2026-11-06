import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { describeApiError } from "@/lib/api-errors";
import { dryRunReconciliationBreakBulkAction, executeReconciliationBreakBulkAction } from "@/lib/api";
import { getReconciliationTaxonomy } from "@/lib/api/reconciliation-readiness.api";
import type { ReconciliationBreakQueueItem, ReconciliationBulkCaseworkRequest, ReconciliationBulkCaseworkResult } from "@/types";
import type { ReconciliationTaxonomySnapshot } from "@/types/reconciliation-readiness.types";
import {
  BULK_TRIAGE_LABELS,
  bulkTriageDraftProblem,
  buildBulkCaseworkRequest,
  emptyBulkTriageDraft,
  newBulkOperationId,
  summarizeBulkDryRun,
  summarizeBulkExecute,
  type BulkTriageDraft,
  type BulkTriageKind,
  type BulkTriagePreview
} from "./reconciliation-bulk-triage";

export interface BulkTriageServices {
  dryRun: (request: ReconciliationBulkCaseworkRequest) => Promise<ReconciliationBulkCaseworkResult>;
  execute: (request: ReconciliationBulkCaseworkRequest) => Promise<ReconciliationBulkCaseworkResult>;
  getTaxonomy: () => Promise<ReconciliationTaxonomySnapshot>;
}

export const defaultBulkTriageServices: BulkTriageServices = {
  dryRun: dryRunReconciliationBreakBulkAction,
  execute: executeReconciliationBreakBulkAction,
  getTaxonomy: () => getReconciliationTaxonomy()
};

interface BulkTriageMessage {
  tone: "success" | "danger";
  text: string;
  details: string[];
}

interface CheckedPreview extends BulkTriagePreview {
  operationId: string;
  breakIds: string[];
  /** The draft the dry run checked; the execute sends exactly this. */
  draft: BulkTriageDraft;
}

export interface ReconciliationBulkTriage {
  selectedIds: string[];
  isSelected: (breakId: string) => boolean;
  toggle: (breakId: string) => void;
  selectAll: () => void;
  clear: () => void;
  draft: BulkTriageDraft | null;
  start: (kind: BulkTriageKind) => void;
  updateDraft: (patch: Partial<BulkTriageDraft>) => void;
  cancel: () => void;
  check: () => Promise<void>;
  execute: () => Promise<void>;
  busy: "checking" | "executing" | null;
  preview: CheckedPreview | null;
  previewIsCurrent: boolean;
  message: BulkTriageMessage | null;
  taxonomy: ReconciliationTaxonomySnapshot | null;
  taxonomyError: string | null;
}

export function useReconciliationBulkTriage({
  visibleBreakIds,
  beginItemsUpdate,
  services = defaultBulkTriageServices
}: {
  visibleBreakIds: readonly string[];
  /** Called as the execute starts; the returned function applies its committed cases, if still in scope. */
  beginItemsUpdate?: () => (items: ReconciliationBreakQueueItem[]) => void;
  services?: BulkTriageServices;
}): ReconciliationBulkTriage {
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [draft, setDraft] = useState<BulkTriageDraft | null>(null);
  const [busy, setBusy] = useState<"checking" | "executing" | null>(null);
  const [preview, setPreview] = useState<CheckedPreview | null>(null);
  const [message, setMessage] = useState<BulkTriageMessage | null>(null);
  const [taxonomy, setTaxonomy] = useState<ReconciliationTaxonomySnapshot | null>(null);
  const [taxonomyError, setTaxonomyError] = useState<string | null>(null);
  const taxonomyRequested = useRef(false);
  // Bumped whenever the draft or its check changes, so a dry run that returns late cannot stand in
  // for a draft it did not check.
  const checkRevision = useRef(0);

  // Act only on what the operator can see: a break that leaves the filtered view leaves the selection.
  const visibleKey = visibleBreakIds.join("\u0000");
  useEffect(() => {
    const visible = new Set(visibleBreakIds);
    setSelectedIds((current) => {
      const next = current.filter((id) => visible.has(id));
      return next.length === current.length ? current : next;
    });
    // visibleKey stands in for the array identity.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [visibleKey]);

  const selectedKey = selectedIds.join("\u0000");
  const previewIsCurrent = preview !== null && preview.draft === draft && preview.breakIds.join("\u0000") === selectedKey;

  const toggle = useCallback((breakId: string) => {
    setSelectedIds((current) => current.includes(breakId) ? current.filter((id) => id !== breakId) : [...current, breakId]);
  }, []);
  const selectAll = useCallback(() => setSelectedIds([...visibleBreakIds]), [visibleBreakIds]);
  const clear = useCallback(() => setSelectedIds([]), []);

  const loadTaxonomy = useCallback(() => {
    if (taxonomyRequested.current) return;
    taxonomyRequested.current = true;
    services.getTaxonomy()
      .then((snapshot) => setTaxonomy(snapshot))
      .catch((err) => {
        taxonomyRequested.current = false;
        setTaxonomyError(describeApiError(err, "Root causes and resolutions could not be loaded.").summary);
      });
  }, [services]);

  const runCheck = useCallback(async (nextDraft: BulkTriageDraft, breakIds: string[]) => {
    const problem = bulkTriageDraftProblem(nextDraft, breakIds.length);
    if (problem) {
      setMessage({ tone: "danger", text: problem, details: [] });
      return;
    }
    const operationId = newBulkOperationId();
    const revision = ++checkRevision.current;
    setBusy("checking");
    setMessage(null);
    try {
      const result = await services.dryRun(buildBulkCaseworkRequest(nextDraft, breakIds, { operationId, dryRun: true, operator: null }));
      if (checkRevision.current !== revision) return;
      setPreview({ ...summarizeBulkDryRun(nextDraft.kind, result), operationId, breakIds, draft: nextDraft });
    } catch (err) {
      if (checkRevision.current !== revision) return;
      const display = describeApiError(err, "The bulk check failed. Nothing was changed.");
      setMessage({ tone: "danger", text: display.summary, details: display.details });
    } finally {
      if (checkRevision.current === revision) setBusy(null);
    }
  }, [services]);

  const start = useCallback((kind: BulkTriageKind) => {
    const next = emptyBulkTriageDraft(kind);
    checkRevision.current += 1;
    setBusy(null);
    setDraft(next);
    setPreview(null);
    setMessage(null);
    if (kind === "resolve") loadTaxonomy();
    // Assign to me needs no input, so it checks straight away; executing still takes a confirm.
    if (kind === "assign") void runCheck(next, selectedIds);
  }, [loadTaxonomy, runCheck, selectedIds]);

  const updateDraft = useCallback((patch: Partial<BulkTriageDraft>) => {
    checkRevision.current += 1;
    setBusy((current) => current === "checking" ? null : current);
    setDraft((current) => current ? { ...current, ...patch } : current);
    setPreview(null);
  }, []);

  const cancel = useCallback(() => {
    checkRevision.current += 1;
    setBusy((current) => current === "checking" ? null : current);
    setDraft(null);
    setPreview(null);
    setMessage(null);
  }, []);

  const check = useCallback(async () => {
    if (draft) await runCheck(draft, selectedIds);
  }, [draft, runCheck, selectedIds]);

  const execute = useCallback(async () => {
    if (!preview || !previewIsCurrent || preview.passedIds.length === 0) return;
    const checked = preview.draft;
    const applyUpdate = beginItemsUpdate?.();
    setBusy("executing");
    setMessage(null);
    try {
      const result = await services.execute(buildBulkCaseworkRequest(checked, preview.passedIds, {
        operationId: preview.operationId,
        dryRun: false,
        operator: null
      }));
      const outcome = summarizeBulkExecute(checked.kind, result);
      if (outcome.updatedItems.length > 0) applyUpdate?.(outcome.updatedItems);
      const changed = new Set(outcome.updatedItems.map((item) => item.breakId));
      setSelectedIds((current) => current.filter((id) => !changed.has(id)));
      setMessage({
        tone: outcome.failed.length === 0 ? "success" : "danger",
        text: outcome.summary,
        details: outcome.failed.map((entry) => `${entry.breakId}: ${entry.error}`)
      });
      setDraft(null);
      setPreview(null);
    } catch (err) {
      const display = describeApiError(err, "The bulk action failed. Check the queue before trying again.");
      setMessage({ tone: "danger", text: display.summary, details: display.details });
    } finally {
      setBusy(null);
    }
  }, [beginItemsUpdate, preview, previewIsCurrent, services]);

  const selectedSet = useMemo(() => new Set(selectedIds), [selectedIds]);
  const isSelected = useCallback((breakId: string) => selectedSet.has(breakId), [selectedSet]);

  return {
    selectedIds, isSelected, toggle, selectAll, clear,
    draft, start, updateDraft, cancel, check, execute,
    busy, preview, previewIsCurrent, message, taxonomy, taxonomyError
  };
}

const NO_SELECTION_REASON = "Select breaks first: tick their boxes, or press x on a row.";

export function ReconciliationBulkTriageBar({ triage, visibleCount }: { triage: ReconciliationBulkTriage; visibleCount: number }) {
  const count = triage.selectedIds.length;
  const draft = triage.draft;
  const labels = draft ? BULK_TRIAGE_LABELS[draft.kind] : null;
  const problem = draft ? bulkTriageDraftProblem(draft, count) : null;
  const preview = triage.previewIsCurrent ? triage.preview : null;
  const breaks = (n: number) => `${n} ${n === 1 ? "break" : "breaks"}`;

  return (
    <section role="region" aria-label="Bulk break actions" className="space-y-2 rounded-lg border border-border/60 px-3 py-2">
      <div className="flex flex-wrap items-center gap-2">
        <p role="status" className="text-sm text-foreground">{count === 0 ? "No breaks selected" : `${breaks(count)} selected`}</p>
        <Button type="button" size="sm" variant="ghost" disabled={visibleCount === 0} onClick={triage.selectAll}>
          Select all {visibleCount}
        </Button>
        <Button type="button" size="sm" variant="ghost" disabled={count === 0} onClick={triage.clear}>Clear selection</Button>
        <span className="flex-1" />
        {(["assign", "route", "resolve"] as const).map((kind) => (
          <Button
            key={kind}
            type="button"
            size="sm"
            variant="outline"
            disabled={count === 0 || triage.busy !== null}
            disabledReason={count === 0 ? NO_SELECTION_REASON : null}
            aria-pressed={draft?.kind === kind}
            onClick={() => triage.start(kind)}
          >
            {BULK_TRIAGE_LABELS[kind].action}
          </Button>
        ))}
      </div>

      {draft && labels ? (
        <form
          aria-label={`${labels.action} for ${breaks(count)}`}
          className="space-y-2"
          onSubmit={(event) => {
            event.preventDefault();
            void (preview ? triage.execute() : triage.check());
          }}
        >
          {/* The draft is frozen while its check or execute runs, so a result always matches it. */}
          <fieldset disabled={triage.busy !== null} className="m-0 min-w-0 border-0 p-0">
            {draft.kind === "route" ? (
              <div className="grid gap-2 sm:grid-cols-2">
                <FormRow label="Owner" labelFor="bulk-route-owner">
                  <Input id="bulk-route-owner" value={draft.assignee} onChange={(event) => triage.updateDraft({ assignee: event.target.value })} placeholder="Operator or desk" />
                </FormRow>
                <FormRow label="Handoff note (optional)" labelFor="bulk-route-note">
                  <Input id="bulk-route-note" value={draft.reason} onChange={(event) => triage.updateDraft({ reason: event.target.value })} />
                </FormRow>
              </div>
            ) : null}
            {draft.kind === "resolve" ? (
              <div className="grid gap-2 sm:grid-cols-2">
                <FormRow label="Reason" labelFor="bulk-resolve-reason" hint="Recorded on every break as its resolution note.">
                  <Input id="bulk-resolve-reason" value={draft.reason} onChange={(event) => triage.updateDraft({ reason: event.target.value })} />
                </FormRow>
                <FormRow label="Evidence reference (optional)" labelFor="bulk-resolve-evidence" hint="For example journal:…, statement:… or evidence:…">
                  <Input id="bulk-resolve-evidence" value={draft.evidenceLink} onChange={(event) => triage.updateDraft({ evidenceLink: event.target.value })} />
                </FormRow>
                <FormRow label="Root cause" labelFor="bulk-resolve-root-cause">
                  <select id="bulk-resolve-root-cause" value={draft.rootCauseCode} onChange={(event) => triage.updateDraft({ rootCauseCode: event.target.value })}>
                    <option value="">Choose a root cause</option>
                    {(triage.taxonomy?.rootCauses ?? []).filter((value) => value.isActive).map((value) => (
                      <option key={value.code} value={value.code}>{value.displayName}</option>
                    ))}
                  </select>
                </FormRow>
                <FormRow label="Resolution" labelFor="bulk-resolve-resolution">
                  <select id="bulk-resolve-resolution" value={draft.resolutionCode} onChange={(event) => triage.updateDraft({ resolutionCode: event.target.value })}>
                    <option value="">Choose a resolution</option>
                    {(triage.taxonomy?.resolutionCodes ?? []).filter((value) => value.isActive).map((value) => (
                      <option key={value.code} value={value.code}>{value.displayName}</option>
                    ))}
                  </select>
                </FormRow>
                {triage.taxonomyError ? <p role="alert" className="text-sm text-danger sm:col-span-2">{triage.taxonomyError}</p> : null}
              </div>
            ) : null}
          </fieldset>

          {preview ? (
            <div role="status" className="space-y-1 text-sm">
              <p className="text-foreground">{preview.summary}</p>
              {preview.blocked.length > 0 ? (
                <ul aria-label="Breaks the check blocked" className="list-disc space-y-0.5 pl-5 text-xs text-muted-foreground">
                  {preview.blocked.map((entry) => <li key={entry.breakId}><span className="font-mono">{entry.breakId}</span>: {entry.error}</li>)}
                </ul>
              ) : null}
            </div>
          ) : null}

          <div className="panel-action-zone justify-start">
            {preview ? (
              <>
                <Button
                  type="submit"
                  size="sm"
                  disabled={preview.passedIds.length === 0 || triage.busy !== null}
                  disabledReason={preview.passedIds.length === 0 ? "No selected break passed the check." : null}
                >
                  {triage.busy === "executing" ? "Applying…" : `${labels.verb} ${breaks(preview.passedIds.length)}`}
                </Button>
                <Button type="button" size="sm" variant="outline" disabled={triage.busy !== null} onClick={() => void triage.check()}>Check again</Button>
              </>
            ) : (
              <Button type="submit" size="sm" disabled={problem !== null || triage.busy !== null} disabledReason={problem}>
                {triage.busy === "checking" ? "Checking…" : `Check ${breaks(count)}`}
              </Button>
            )}
            <Button type="button" size="sm" variant="ghost" onClick={triage.cancel}>Cancel</Button>
          </div>
        </form>
      ) : null}

      {triage.message ? (
        <StatusBanner
          role={triage.message.tone === "danger" ? "alert" : "status"}
          tone={triage.message.tone}
          title={triage.message.text}
          detail={triage.message.details.length > 0 ? (
            <ul className="mt-1 list-disc space-y-0.5 pl-5 text-xs leading-5">
              {triage.message.details.map((detail) => <li key={detail}>{detail}</li>)}
            </ul>
          ) : null}
        />
      ) : null}
    </section>
  );
}
