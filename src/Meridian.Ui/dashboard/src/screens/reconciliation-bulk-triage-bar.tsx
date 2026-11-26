import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { describeApiError, isApiError } from "@/lib/api-errors";
import { dryRunReconciliationBreakBulkAction, executeReconciliationBreakBulkAction } from "@/lib/api";
import { getReconciliationBreakBulkActionResult } from "@/lib/api/statement-connectors.api";
import { reconciliationBreakBulkResultEndpoint } from "@/lib/workstation-endpoints";
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
import { Select } from "@/components/ui/select";

export interface BulkTriageServices {
  dryRun: (request: ReconciliationBulkCaseworkRequest) => Promise<ReconciliationBulkCaseworkResult>;
  execute: (request: ReconciliationBulkCaseworkRequest) => Promise<ReconciliationBulkCaseworkResult>;
  getTaxonomy: () => Promise<ReconciliationTaxonomySnapshot>;
  readResult?: (commandId: string) => Promise<ReconciliationBulkCaseworkResult | null>;
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
  bulkActionId: string;
  breakIds: string[];
  /** The draft the dry run checked; the execute sends exactly this. */
  draft: BulkTriageDraft;
  eligible: Array<{ breakId: string; item?: ReconciliationBreakQueueItem | null }>;
}

interface BulkReceipt {
  commandId: string;
  breakIds: string[];
  result: ReconciliationBulkCaseworkResult | null;
  queueOnly?: boolean;
  reviewed?: { request: ReconciliationBulkCaseworkRequest; kind: BulkTriageKind };
}

function readBatchIdentity(key: string | null): BulkReceipt | null {
  if (!key) return null;
  try {
    const raw = sessionStorage.getItem(key);
    const receipt = raw ? JSON.parse(raw) as Partial<BulkReceipt> : null;
    if (typeof receipt?.commandId !== "string" || !Array.isArray(receipt.breakIds)
      || !receipt.breakIds.every((id) => typeof id === "string")) return null;
    return { commandId: receipt.commandId, breakIds: receipt.breakIds, result: null };
  } catch { return null; }
}

function matchesResult(result: ReconciliationBulkCaseworkResult, commandId: string, breakIds: string[], dryRun: boolean): boolean {
  const returnedIds = new Set(result.results.map((entry) => entry.breakId));
  return result.bulkActionId === commandId && result.dryRun === dryRun
    && result.results.length === breakIds.length && breakIds.every((id) => returnedIds.has(id))
    && result.results.every((entry) => !entry.item || entry.item.breakId === entry.breakId);
}

function hasPendingStatementHandoff(result: ReconciliationBulkCaseworkResult): boolean {
  const prefix = "urn:meridian:statement-casework-handoff:";
  return result.results.some((entry) => entry.succeeded && entry.item?.sourceType === "statement"
    && entry.item.evidenceLinks?.some((reference) => reference.startsWith(`${prefix}pending:`)
      && !entry.item?.evidenceLinks?.includes(reference.replace(`${prefix}pending:`, `${prefix}completed:`))));
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
  busy: "checking" | "executing" | "checkingOutcome" | null;
  preview: CheckedPreview | null;
  previewIsCurrent: boolean;
  previewStale: boolean;
  receipt: BulkReceipt | null;
  checkOutcome: () => Promise<void>;
  resume: () => Promise<void>;
  message: BulkTriageMessage | null;
  taxonomy: ReconciliationTaxonomySnapshot | null;
  taxonomyError: string | null;
}

export function useReconciliationBulkTriage({
  visibleBreakIds,
  visibleVersions,
  beginItemsUpdate,
  receiptKey = null,
  services = defaultBulkTriageServices
}: {
  visibleBreakIds: readonly string[];
  visibleVersions: ReadonlyMap<string, number | undefined>;
  receiptKey?: string | null;
  /** Called as the execute starts; the returned function applies its committed cases, if still in scope. */
  beginItemsUpdate?: () => (items: ReconciliationBreakQueueItem[]) => void;
  services?: BulkTriageServices;
}): ReconciliationBulkTriage {
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [draft, setDraft] = useState<BulkTriageDraft | null>(null);
  const [busy, setBusy] = useState<ReconciliationBulkTriage["busy"]>(null);
  const [preview, setPreview] = useState<CheckedPreview | null>(null);
  const [message, setMessage] = useState<BulkTriageMessage | null>(null);
  const [receiptState, setReceiptState] = useState<{ key: string | null; value: BulkReceipt | null }>(() => ({ key: receiptKey, value: readBatchIdentity(receiptKey) }));
  const receipt = receiptState.key === receiptKey ? receiptState.value : null;
  const activeReceiptKey = useRef(receiptKey);
  activeReceiptKey.current = receiptKey;
  const [taxonomy, setTaxonomy] = useState<ReconciliationTaxonomySnapshot | null>(null);
  const [taxonomyError, setTaxonomyError] = useState<string | null>(null);
  const taxonomyRequested = useRef(false);
  // Bumped whenever the draft or its check changes, so a dry run that returns late cannot stand in
  // for a draft it did not check.
  const checkRevision = useRef(0);
  // Read by start and cancel, which keyboard shortcuts can call while the buttons are disabled.
  const busyRef = useRef(busy);
  busyRef.current = busy;

  useEffect(() => {
    setReceiptState((current) => current.key === receiptKey ? current : { key: receiptKey, value: readBatchIdentity(receiptKey) });
  }, [receiptKey]);

  const retainReceipt = useCallback((value: BulkReceipt | null) => {
    if (receiptKey) {
      // The tab retains only the lookup identity. The authoritative receipt stays on the server.
      try {
        if (!value || (value.result && !value.queueOnly)) sessionStorage.removeItem(receiptKey);
        else sessionStorage.setItem(receiptKey, JSON.stringify({ commandId: value.commandId, breakIds: value.breakIds }));
      }
      catch { /* The lookup remains available in the current queue. */ }
    }
    if (activeReceiptKey.current === receiptKey) setReceiptState({ key: receiptKey, value });
  }, [receiptKey]);

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
  const previewStale = preview?.eligible.some(({ breakId, item }) => {
    const currentVersion = visibleVersions.get(breakId);
    return currentVersion != null && item?.version != null && currentVersion !== item.version;
  }) ?? false;
  const hasUnconfirmed = receipt !== null && (receipt.result === null || receipt.queueOnly);
  const previewIsCurrent = preview !== null && !previewStale && preview.draft === draft
    && preview.breakIds.join("\u0000") === selectedKey && preview.breakIds.every((id) => visibleVersions.has(id));

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
      const request = buildBulkCaseworkRequest(nextDraft, breakIds, { operationId, dryRun: true, operator: null });
      const result = await services.dryRun(request);
      if (checkRevision.current !== revision) return;
      if (!matchesResult(result, request.commandId, breakIds, true)) {
        throw new Error("The returned check does not cover the selected break IDs.");
      }
      if (result.reviewProtocol !== 1) throw new Error("The server did not retain a version-bound bulk preview.");
      const eligible = result.results.filter((entry) => entry.wouldSucceed && !entry.error);
      if (eligible.some((entry) => !Number.isSafeInteger(entry.item?.version) || (entry.item?.version ?? 0) <= 0)) {
        throw new Error("The bulk preview is missing a reviewed case version.");
      }
      setPreview({ ...summarizeBulkDryRun(nextDraft.kind, result), operationId, bulkActionId: result.bulkActionId, breakIds, draft: nextDraft, eligible });
    } catch (err) {
      if (checkRevision.current !== revision) return;
      const display = describeApiError(err, "The bulk check failed. Nothing was changed.");
      setMessage({ tone: "danger", text: display.summary, details: display.details });
    } finally {
      if (checkRevision.current === revision) setBusy(null);
    }
  }, [services]);

  const start = useCallback((kind: BulkTriageKind) => {
    // One bulk action at a time: a running check or execute is never replaced mid-flight.
    if (busyRef.current !== null || hasUnconfirmed) return;
    const next = emptyBulkTriageDraft(kind);
    checkRevision.current += 1;
    setBusy(null);
    setDraft(next);
    setPreview(null);
    setMessage(null);
    if (kind === "resolve") loadTaxonomy();
    // Assign to me needs no input, so it checks straight away; executing still takes a confirm.
    if (kind === "assign") void runCheck(next, selectedIds);
  }, [hasUnconfirmed, loadTaxonomy, runCheck, selectedIds]);

  const updateDraft = useCallback((patch: Partial<BulkTriageDraft>) => {
    checkRevision.current += 1;
    setBusy((current) => current === "checking" ? null : current);
    setDraft((current) => current ? { ...current, ...patch } : current);
    setPreview(null);
  }, []);

  const cancel = useCallback(() => {
    // An execute already sent cannot be withdrawn; its result clears the draft when it lands.
    if (busyRef.current === "executing") return;
    checkRevision.current += 1;
    setBusy((current) => current === "checking" ? null : current);
    setDraft(null);
    setPreview(null);
    setMessage(null);
  }, []);

  const check = useCallback(async () => {
    if (busyRef.current !== null || hasUnconfirmed) return;
    if (draft) await runCheck(draft, selectedIds);
  }, [draft, hasUnconfirmed, runCheck, selectedIds]);

  const sendReviewed = useCallback(async (request: ReconciliationBulkCaseworkRequest, kind: BulkTriageKind, resuming = false) => {
    if (busyRef.current !== null) return;
    const applyUpdate = beginItemsUpdate?.();
    const pending = { commandId: request.commandId, breakIds: request.breakIds, result: null, reviewed: { request, kind } };
    busyRef.current = "executing";
    setBusy("executing");
    setMessage(null);
    retainReceipt(pending);
    try {
      const result = await services.execute(request);
      if (!matchesResult(result, request.commandId, request.breakIds, false)) {
        throw new Error("The result does not cover the submitted break IDs.");
      }
      const outcome = summarizeBulkExecute(kind, result);
      if (outcome.updatedItems.length > 0) applyUpdate?.(outcome.updatedItems);
      retainReceipt({ ...pending, result });
      if (activeReceiptKey.current !== receiptKey) return;
      const changed = new Set(outcome.updatedItems.map((item) => item.breakId));
      setSelectedIds((current) => current.filter((id) => !changed.has(id)));
      setMessage({
        tone: outcome.failed.length === 0 ? "success" : "danger",
        text: outcome.summary,
        details: outcome.failed.map((entry) => `${entry.breakId}: ${entry.error}`)
      });
    } catch (err) {
      if (activeReceiptKey.current !== receiptKey) return;
      if (!resuming && isApiError(err) && [400, 401, 403, 404].includes(err.status)) {
        retainReceipt(null);
        setMessage({ tone: "danger", text: err.message, details: [] });
        return;
      }
      const display = describeApiError(err, "The request ended before the result was confirmed.");
      setMessage({ tone: "danger", text: "Batch outcome unconfirmed", details: [display.summary, ...display.details] });
    } finally {
      setDraft(null);
      setPreview(null);
      setBusy(null);
    }
  }, [beginItemsUpdate, receiptKey, retainReceipt, services]);

  const execute = async () => {
    if (hasUnconfirmed || !preview || !previewIsCurrent || preview.passedIds.length === 0) return;
    await sendReviewed(buildBulkCaseworkRequest(preview.draft, preview.passedIds, {
      operationId: preview.operationId, dryRun: false, operator: null, previewBulkActionId: preview.bulkActionId
    }), preview.draft.kind);
  };

  const resume = async () => {
    if (receipt?.queueOnly && receipt.reviewed) await sendReviewed(receipt.reviewed.request, receipt.reviewed.kind, true);
  };

  const checkOutcome = useCallback(async () => {
    if (!receipt || busyRef.current !== null) return;
    busyRef.current = "checkingOutcome";
    setBusy("checkingOutcome");
    try {
      const result = await (services.readResult ?? getReconciliationBreakBulkActionResult)(receipt.commandId);
      if (activeReceiptKey.current !== receiptKey) return;
      if (!result || !matchesResult(result, receipt.commandId, receipt.breakIds, false)) {
        setMessage({ tone: "danger", text: "Batch outcome still unconfirmed", details: ["A retained result for this batch is unavailable."] });
        return;
      }
      retainReceipt({ ...receipt, result, queueOnly: hasPendingStatementHandoff(result) });
      setMessage(null);
    } catch (err) {
      if (activeReceiptKey.current !== receiptKey) return;
      const display = describeApiError(err, "The retained batch result could not be loaded.");
      setMessage({ tone: "danger", text: display.summary, details: display.details });
    } finally {
      setBusy(null);
    }
  }, [receipt, receiptKey, retainReceipt, services]);

  const selectedSet = useMemo(() => new Set(selectedIds), [selectedIds]);
  const isSelected = useCallback((breakId: string) => selectedSet.has(breakId), [selectedSet]);

  return {
    selectedIds, isSelected, toggle, selectAll, clear,
    draft, start, updateDraft, cancel, check, execute,
    busy, preview, previewIsCurrent, previewStale, receipt, checkOutcome, resume, message, taxonomy, taxonomyError
  };
}

const NO_SELECTION_REASON = "Select breaks first: tick their boxes, or press x on a row.";
const NO_SELECTION_HINT = "Tick breaks, or press x on a row, to assign, route or resolve them together.";

export function ReconciliationBulkTriageBar({ triage, visibleCount }: { triage: ReconciliationBulkTriage; visibleCount: number }) {
  const count = triage.selectedIds.length;
  const draft = triage.draft;
  const labels = draft ? BULK_TRIAGE_LABELS[draft.kind] : null;
  const problem = draft ? bulkTriageDraftProblem(draft, count) : null;
  const preview = triage.previewIsCurrent ? triage.preview : null;
  const breaks = (n: number) => `${n} ${n === 1 ? "break" : "breaks"}`;
  // An open draft keeps its controls even if the selection empties, so it can still be cancelled.
  const showActions = count > 0 || draft !== null;

  return (
    <section role="region" aria-label="Bulk break actions" className="space-y-2 rounded-lg border border-border/60 px-3 py-2">
      <div className="flex flex-wrap items-center gap-2">
        <p role="status" className="text-sm text-foreground">{count === 0 ? "No breaks selected" : `${breaks(count)} selected`}</p>
        {showActions ? null : <p className="text-xs text-muted-foreground">{NO_SELECTION_HINT}</p>}
        <Button type="button" size="sm" variant="ghost" disabled={visibleCount === 0} onClick={triage.selectAll}>
          Select all {visibleCount}
        </Button>
        {/* Selection-only controls appear once there is a selection, instead of sitting disabled. */}
        {showActions ? (
          <>
            <Button type="button" size="sm" variant="ghost" disabled={count === 0} onClick={triage.clear}>Clear selection</Button>
            <span className="flex-1" />
            {(["assign", "route", "resolve"] as const).map((kind) => (
              <Button
                key={kind}
                type="button"
                size="sm"
                variant="outline"
                disabled={count === 0 || triage.busy !== null || Boolean(triage.receipt && (!triage.receipt.result || triage.receipt.queueOnly))}
                disabledReason={count === 0 ? NO_SELECTION_REASON : null}
                aria-pressed={draft?.kind === kind}
                onClick={() => triage.start(kind)}
              >
                {BULK_TRIAGE_LABELS[kind].action}
              </Button>
            ))}
          </>
        ) : null}
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
                <FormRow label="Note for the new owner (optional)" labelFor="bulk-route-note">
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
                  <Select id="bulk-resolve-root-cause" value={draft.rootCauseCode} onChange={(event) => triage.updateDraft({ rootCauseCode: event.target.value })}>
                    <option value="">Choose a root cause</option>
                    {(triage.taxonomy?.rootCauses ?? []).filter((value) => value.isActive).map((value) => (
                      <option key={value.code} value={value.code}>{value.displayName}</option>
                    ))}
                  </Select>
                </FormRow>
                <FormRow label="Resolution" labelFor="bulk-resolve-resolution">
                  <Select id="bulk-resolve-resolution" value={draft.resolutionCode} onChange={(event) => triage.updateDraft({ resolutionCode: event.target.value })}>
                    <option value="">Choose a resolution</option>
                    {(triage.taxonomy?.resolutionCodes ?? []).filter((value) => value.isActive).map((value) => (
                      <option key={value.code} value={value.code}>{value.displayName}</option>
                    ))}
                  </Select>
                </FormRow>
                {triage.taxonomyError ? <p role="alert" className="text-sm text-danger sm:col-span-2">{triage.taxonomyError}</p> : null}
              </div>
            ) : null}
          </fieldset>

          {preview ? (
            <div role="status" className="space-y-1 text-sm">
              <p className="text-foreground">{preview.summary}</p>
              {preview.eligible.length > 0 ? <TechnicalDetails label={`Eligible preview (${Math.min(preview.eligible.length, 5)} of ${preview.eligible.length})`}>
                <ul className="space-y-1 break-all text-xs text-muted-foreground" aria-label="Sample of eligible breaks">
                  {preview.eligible.slice(0, 5).map(({ breakId, item }) => (
                    <li key={breakId}><code>{breakId}</code>{item ? ` · ${item.status}${item.version != null ? ` · Version ${item.version}` : ""}` : null}</li>
                  ))}
                </ul>
              </TechnicalDetails> : null}
              {preview.blocked.length > 0 ? (
                <ul aria-label="Breaks the check blocked" className="list-disc space-y-0.5 pl-5 text-xs text-muted-foreground">
                  {preview.blocked.map((entry) => <li key={entry.breakId}><span className="font-mono">{entry.breakId}</span>: {entry.error}</li>)}
                </ul>
              ) : null}
            </div>
          ) : null}
          {triage.previewStale ? <p role="alert" className="text-sm text-muted-foreground">A case version changed. Refresh the queue and check the batch again.</p> : null}

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
      {triage.receipt ? <div className="grid gap-2 border-t border-border pt-2">
        <p className="break-all text-xs text-muted-foreground">Batch command ID <code>{triage.receipt.commandId}</code></p>
        {triage.receipt.result ? <section aria-label="Retained batch queue result" className="space-y-2">
          <h4 className="text-sm font-semibold">Retained batch queue result</h4>
          <ul aria-label="Per-case batch results" className="space-y-1 break-all text-xs">
            {[...triage.receipt.result.results].sort((left, right) => Number(!right.succeeded) - Number(!left.succeeded)).map((entry) => <li key={entry.breakId}>
              <code>{entry.breakId}</code> · {entry.succeeded ? `Recorded${entry.item ? ` · ${entry.item.status}` : ""}` : entry.error ?? "Declined"}
            </li>)}
          </ul>
          <Button asChild size="sm" variant="outline" className="mt-2">
            <a href={reconciliationBreakBulkResultEndpoint(triage.receipt.commandId)} target="_blank" rel="noreferrer">Open batch receipt</a>
          </Button>
        </section> : null}
        {triage.receipt.queueOnly ? <p role="status" className="text-sm">The queue result is retained. Statement handoff is still unconfirmed.</p> : null}
        {!triage.receipt.result || triage.receipt.queueOnly ? <Button size="sm" variant="outline" className="w-fit" disabled={triage.busy !== null} onClick={() => void triage.checkOutcome()}>Check batch outcome</Button> : null}
        {triage.receipt.queueOnly && triage.receipt.reviewed ? <Button size="sm" variant="outline" className="w-fit" disabled={triage.busy !== null} onClick={() => void triage.resume()}>Resume same batch</Button> : null}
      </div> : null}
    </section>
  );
}
