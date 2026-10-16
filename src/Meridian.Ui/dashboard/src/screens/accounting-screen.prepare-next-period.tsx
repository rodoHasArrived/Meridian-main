import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { describeApiError, isApiError } from "@/lib/api-errors";
import { captureClosePlanTemplate, createPreparedClosePlan, getClosePreparationBooks, getClosePreparationPeriods, getClosePreparationSource, listClosePlanTemplates, listClosePreparationSources, previewClosePreparation } from "@/lib/api/close-preparation.api";
import type { ClosePeriodPlan, LedgerBook, LedgerPeriod, OperationsContinuityWorkflowSummary } from "@/types";
import type { CaptureClosePlanTemplateRequest, ClosePlanTemplate, ClosePreparationHistory, ClosePreparationPreview, PreparedClosePlanResult } from "@/types/close-preparation";
import { ClosePreparationCalendarForm } from "./accounting-screen.prepare-next-period-calendar";

const message = (error: unknown) => describeApiError(error, "Close preparation is unavailable. Retry when the service is available.").summary;
const templateKey = (template: ClosePlanTemplate) => `${template.templateId}:${template.version}`;

interface PendingCloseCreation {
  version: 1;
  sourceWorkflowId: string;
  sourceLedgerBookId: string;
  fundProfileId: string;
  templateId: string;
  templateVersion: number;
  targetLedgerBookId: string;
  targetPeriodId: string;
  previewId: string;
  idempotencyKey: string;
}

const pendingCreationKey = (workflowId: string) => `meridian.close-preparation.pending.v1:${encodeURIComponent(workflowId.toLowerCase())}`;
const sameIdentity = (left: string, right: string) => left.toLowerCase() === right.toLowerCase();

/** Opaque command identity only. Fresh authorized reads and the create endpoint retain all authority. */
function readPendingCreation(sourceWorkflowId: string): PendingCloseCreation | null {
  try {
    const saved = JSON.parse(sessionStorage.getItem(pendingCreationKey(sourceWorkflowId)) ?? "null") as PendingCloseCreation | null;
    if (!saved || saved.version !== 1 || !Number.isSafeInteger(saved.templateVersion) || saved.templateVersion < 1
      || [saved.sourceWorkflowId, saved.sourceLedgerBookId, saved.fundProfileId, saved.templateId, saved.targetLedgerBookId,
        saved.targetPeriodId, saved.previewId, saved.idempotencyKey].some(value => typeof value !== "string" || !value.trim())
      || !sameIdentity(saved.sourceWorkflowId, sourceWorkflowId)) return null;
    return saved;
  } catch { return null; }
}

function matchingPendingCreation(template: ClosePlanTemplate, books: LedgerBook[]) {
  const saved = readPendingCreation(template.sourceWorkflowId);
  return saved && sameIdentity(saved.sourceLedgerBookId, template.sourceLedgerBookId)
    && saved.fundProfileId === template.fundProfileId && sameIdentity(saved.templateId, template.templateId)
    && saved.templateVersion === template.version
    && books.some(book => sameIdentity(book.ledgerBookId, saved.targetLedgerBookId) && book.fundProfileId === saved.fundProfileId)
    ? saved : null;
}

function persistPendingCreation(command: PendingCloseCreation) {
  const existing = readPendingCreation(command.sourceWorkflowId);
  if (existing && existing.fundProfileId === command.fundProfileId && sameIdentity(existing.sourceLedgerBookId, command.sourceLedgerBookId)
    && (existing.previewId !== command.previewId || existing.idempotencyKey !== command.idempotencyKey))
    throw new Error("A pending creation already exists for this source plan. Reopen preparation to recover its original request before creating another plan.");
  try { sessionStorage.setItem(pendingCreationKey(command.sourceWorkflowId), JSON.stringify(command)); }
  catch { throw new Error("The creation recovery request could not be saved. Enable browser session storage before creating the plan."); }
}

function clearPendingCreation(command: PendingCloseCreation) {
  try {
    const current = readPendingCreation(command.sourceWorkflowId);
    if (current?.previewId === command.previewId && current.idempotencyKey === command.idempotencyKey)
      sessionStorage.removeItem(pendingCreationKey(command.sourceWorkflowId));
  } catch { /* A retained identity can safely recover the same result again. */ }
}

function isStalePreviewError(error: unknown) {
  if (!isApiError(error)) return false;
  // Unclaimed expired previews may be pruned; previews referenced by a retained claim are kept.
  if (error.status === 404 || error.status === 410 || error.status === 412) return true;
  if (error.status !== 409 || !error.responseBody) return false;
  try { return (JSON.parse(error.responseBody) as { code?: string }).code === "PREPARATION_PREVIEW_STALE"; }
  catch { return false; }
}

/** Entry point stays visible in the close workspace; authoritative reads begin on opening. */
export function PrepareNextPeriodPanel({ initialWorkflowId }: { initialWorkflowId?: string }) {
  const [open, setOpen] = useState(false);
  return <section className="panel-surface space-y-4 p-4" aria-label="Prepare next period">
    <div className="flex flex-wrap items-center justify-between gap-3">
      <div><h3 className="workspace-section-title">Prepare next period</h3><p className="text-sm text-muted-foreground">Reuse a plan's tasks, dependencies and requirements with fresh evidence and sign-offs.</p></div>
      <Button variant={open ? "outline" : "default"} aria-expanded={open} aria-controls="close-preparation-workflow" onClick={() => setOpen(current => !current)}>{open ? "Hide preparation" : "Prepare next period"}</Button>
    </div>
    {open ? <div id="close-preparation-workflow"><ClosePreparationSources key={initialWorkflowId ?? "unselected"} initialWorkflowId={initialWorkflowId} /></div> : null}
  </section>;
}

function ClosePreparationSources({ initialWorkflowId }: { initialWorkflowId?: string }) {
  const [sources, setSources] = useState<OperationsContinuityWorkflowSummary[]>([]);
  const [selected, setSelected] = useState(initialWorkflowId ?? "");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    const request = new AbortController();
    setLoading(true); setError(null); setSources([]);
    void listClosePreparationSources(request.signal).then(rows => {
      if (!request.signal.aborted) {
        setSources(rows);
        // Only discover pending requests through freshly authorized source identities.
        const pendingSource = rows.find(row => readPendingCreation(row.workflowId));
        if (pendingSource) setSelected(pendingSource.workflowId);
      }
    }).catch(reason => { if (!request.signal.aborted) setError(message(reason)); })
      .finally(() => { if (!request.signal.aborted) setLoading(false); });
    return () => request.abort();
  }, [revision]);
  const source = sources.find(row => row.workflowId === selected);
  return <div className="space-y-4">
    <p className="text-sm text-muted-foreground">Completions, approvals, reviewed evidence, journal references and period locks stay with their original period. This preparation creates a new plan.</p>
    {loading ? <p role="status">Loading source plans…</p> : null}
    {error ? <StatusBanner tone="danger" title="Source plans unavailable" detail={error} role="alert" /> : null}
    {error ? <Button variant="outline" onClick={() => setRevision(value => value + 1)}>Retry source plans</Button> : null}
    {!loading && !error && sources.length === 0 ? <p role="status">No source plans are available. Create and configure a close plan first.</p> : null}
    {sources.length > 0 ? <FormRow label="Source plan" labelFor="close-preparation-source">
      <Select id="close-preparation-source" value={source?.workflowId ?? ""} placeholder="Select a source plan" onChange={event => setSelected(event.target.value)}>
        {sources.map(row => <option key={row.workflowId} value={row.workflowId}>{row.periodId} · {row.fundAccountId} · {row.status}</option>)}
      </Select>
    </FormRow> : null}
    {source ? <ClosePreparationTemplateEditor key={source.workflowId} workflowId={source.workflowId} /> : null}
  </div>;
}

function ClosePreparationTemplateEditor({ workflowId }: { workflowId: string }) {
  const [loaded, setLoaded] = useState<{ plan: ClosePeriodPlan; templates: ClosePlanTemplate[]; books: LedgerBook[] } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [revision, setRevision] = useState(0);
  const [selected, setSelected] = useState("");
  const [previous, setPrevious] = useState<ClosePlanTemplate | null>(null);
  const [busy, setBusy] = useState(false);
  const [hasPending, setHasPending] = useState(false);
  const controller = useRef<AbortController | null>(null);
  useEffect(() => {
    const request = new AbortController();
    controller.current = request;
    setLoaded(null); setError(null); setSelected(""); setPrevious(null); setHasPending(false);
    void Promise.all([getClosePreparationSource(workflowId, request.signal), listClosePlanTemplates(workflowId, request.signal)])
      .then(async ([plan, templates]) => {
        const books = await getClosePreparationBooks(plan.ledgerBookId!, request.signal);
        if (!request.signal.aborted) {
          setLoaded({ plan, templates, books });
          const pendingTemplate = templates.find(item => matchingPendingCreation(item, books));
          setHasPending(Boolean(pendingTemplate));
          if (pendingTemplate) setSelected(templateKey(pendingTemplate));
        }
      }).catch(reason => { if (!request.signal.aborted) setError(message(reason)); });
    return () => { request.abort(); controller.current?.abort(); };
  }, [workflowId, revision]);
  const capture = async (request: CaptureClosePlanTemplateRequest) => {
    if (busy || !loaded) return;
    const abort = new AbortController(); controller.current = abort;
    setBusy(true); setError(null);
    try {
      const retained = await captureClosePlanTemplate(request, abort.signal);
      if (!abort.signal.aborted) {
        if (retained.sourceWorkflowId.toLowerCase() !== workflowId.toLowerCase()) throw new Error("Captured template does not match the selected source plan.");
        setLoaded(current => current ? { ...current, templates: [...current.templates.filter(item => templateKey(item) !== templateKey(retained)), retained] } : null);
        setSelected(templateKey(retained)); setPrevious(null);
      }
    } catch (reason) { if (!abort.signal.aborted) setError(message(reason)); }
    finally { if (!abort.signal.aborted) setBusy(false); }
  };
  const template = loaded?.templates.find(item => templateKey(item) === selected) ?? null;
  return <div className="space-y-4 border-t border-border pt-4">
    {!loaded && !error ? <p role="status">Loading selected plan and retained templates…</p> : null}
    {error ? <StatusBanner tone="danger" title="Preparation unavailable" detail={error} role="alert" /> : null}
    {!loaded && error ? <Button variant="outline" onClick={() => setRevision(value => value + 1)}>Retry selected plan</Button> : null}
    {loaded ? <>
      <div className="flex flex-wrap items-center justify-between gap-3"><div className="flex flex-wrap gap-3 text-sm"><Badge variant="outline">{loaded.plan.periodStart} – {loaded.plan.periodEnd}</Badge><span>{loaded.plan.tasks.length} tasks</span><span>{loaded.plan.isPeriodLocked ? "Source period locked" : "Source period open"}</span></div><Button size="sm" variant="outline" disabled={busy || hasPending} onClick={() => setRevision(value => value + 1)}>Refresh source plan</Button></div>
      {loaded.plan.configuration?.preparation ? <section aria-label="Retained plan creation" className="space-y-2 rounded border border-border p-3 text-sm">
        <h4 className="font-semibold">Retained plan creation</h4>
        <p className="break-words">Template {loaded.plan.configuration.preparation.templateId} · v{loaded.plan.configuration.preparation.templateVersion}</p>
        <p>Created by {loaded.plan.configuration.preparation.createdBy} at {loaded.plan.configuration.preparation.createdAtUtc} for {loaded.plan.configuration.preparation.periodStart} – {loaded.plan.configuration.preparation.periodEnd}.</p>
        <ClosePreparationHistoryList history={loaded.plan.configuration.preparation.history} label="Retained creation history" />
      </section> : null}
      {loaded.templates.length > 0 ? <FormRow label="Retained template" labelFor="close-retained-template"><Select id="close-retained-template" disabled={busy || hasPending} value={selected} onChange={event => { setSelected(event.target.value); setPrevious(null); setError(null); }}>
        <option value="">Capture configuration from source</option>
        {loaded.templates.map(item => <option key={templateKey(item)} value={templateKey(item)}>{item.name} · v{item.version}</option>)}
      </Select></FormRow> : null}
      {!template ? <ClosePreparationCalendarForm key={previous ? templateKey(previous) : "new"} plan={loaded.plan} previous={previous} busy={busy} onCapture={request => void capture(request)} /> : <>
        <div className="flex flex-wrap items-center justify-between gap-3"><p className="text-sm">Template <strong>{template.name} · v{template.version}</strong> captured by {template.capturedBy} on {template.capturedAtUtc}.</p><Button size="sm" variant="outline" disabled={hasPending} onClick={() => { setPrevious(template); setSelected(""); }}>Capture another version</Button></div>
        <ClosePreparationHistoryList history={template.history} label="Template history" />
        <ClosePreparationTarget key={templateKey(template)} template={template} books={loaded.books} onPendingChange={setHasPending} />
      </>}
    </> : null}
  </div>;
}

function ClosePreparationTarget({ template, books, onPendingChange }: { template: ClosePlanTemplate; books: LedgerBook[]; onPendingChange: (pending: boolean) => void }) {
  const [pending, setPending] = useState(() => matchingPendingCreation(template, books));
  useEffect(() => { onPendingChange(Boolean(pending)); }, [pending, onPendingChange]);
  const [bookId, setBookId] = useState(pending?.targetLedgerBookId ?? "");
  const [periodId, setPeriodId] = useState(pending?.targetPeriodId ?? "");
  const [periods, setPeriods] = useState<LedgerPeriod[]>([]);
  const [periodLoading, setPeriodLoading] = useState(false);
  const [periodError, setPeriodError] = useState<string | null>(null);
  const [periodRevision, setPeriodRevision] = useState(0);
  const owners = [...new Set(template.tasks.map(task => task.configuration.owner ?? ""))];
  const [mappings, setMappings] = useState<Record<string, string>>({});
  const [policyAcknowledged, setPolicyAcknowledged] = useState(false);
  const [preview, setPreview] = useState<ClosePreparationPreview | null>(null);
  const [result, setResult] = useState<PreparedClosePlanResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<"preview" | "create" | null>(null);
  const [expired, setExpired] = useState(false);
  const [retryCreate, setRetryCreate] = useState(false);
  const generation = useRef(0);
  const controller = useRef<AbortController | null>(null);
  const creationKey = useRef<string | null>(null);
  useEffect(() => () => { ++generation.current; controller.current?.abort(); }, []);
  useEffect(() => {
    const request = new AbortController();
    setPeriods([]); setPeriodError(null); setPeriodLoading(Boolean(bookId));
    if (bookId) void getClosePreparationPeriods(bookId, request.signal).then(rows => {
      if (!request.signal.aborted) setPeriods(rows);
    }).catch(reason => { if (!request.signal.aborted) setPeriodError(message(reason)); })
      .finally(() => { if (!request.signal.aborted) setPeriodLoading(false); });
    return () => request.abort();
  }, [bookId, periodRevision]);
  useEffect(() => {
    if (!preview) return;
    const remaining = Date.parse(preview.expiresAtUtc) - Date.now();
    setExpired(!Number.isFinite(remaining) || remaining <= 0);
    if (!Number.isFinite(remaining) || remaining <= 0) return;
    const timer = window.setTimeout(() => setExpired(true), Math.min(remaining, 2_147_483_647));
    return () => window.clearTimeout(timer);
  }, [preview]);
  const invalidate = () => {
    if (pending) return;
    ++generation.current; controller.current?.abort(); creationKey.current = null;
    setPreview(null); setResult(null); setError(null); setExpired(false); setRetryCreate(false); setBusy(null);
  };
  const runPreview = async () => {
    if (!bookId || !periodId || busy || pending) return;
    invalidate();
    const requestGeneration = generation.current;
    const request = new AbortController(); controller.current = request;
    setBusy("preview");
    try {
      const next = await previewClosePreparation({ templateId: template.templateId, templateVersion: template.version, targetLedgerBookId: bookId, targetPeriodId: periodId,
        ownerMappings: owners.map(sourceOwner => ({ sourceOwner, targetOwner: mappings[sourceOwner]?.trim() ?? "" })), acknowledgePolicyChange: policyAcknowledged }, request.signal);
      if (requestGeneration === generation.current && !request.signal.aborted) {
        if (next.sourceWorkflowId.toLowerCase() !== template.sourceWorkflowId.toLowerCase()) throw new Error("The preview does not match the template's source plan.");
        setPreview(next); creationKey.current = crypto.randomUUID();
      }
    } catch (reason) { if (requestGeneration === generation.current && !request.signal.aborted) setError(message(reason)); }
    finally { if (requestGeneration === generation.current && !request.signal.aborted) setBusy(null); }
  };
  const create = async () => {
    if (busy || result) return;
    if (!pending && (!preview?.canCreate || !creationKey.current || (expired && !retryCreate))) return;
    const command: PendingCloseCreation = pending ?? { version: 1, sourceWorkflowId: template.sourceWorkflowId,
      sourceLedgerBookId: template.sourceLedgerBookId, fundProfileId: template.fundProfileId,
      templateId: template.templateId, templateVersion: template.version, targetLedgerBookId: bookId, targetPeriodId: periodId,
      previewId: preview!.previewId, idempotencyKey: creationKey.current! };
    // Persist before the first POST, including a navigation or reload while it is in flight.
    try { persistPendingCreation(command); }
    catch (reason) { setError(message(reason)); return; }
    setPending(command);
    const requestGeneration = generation.current;
    const request = new AbortController(); controller.current = request;
    setBusy("create"); setError(null);
    try {
      const next = await createPreparedClosePlan({ previewId: command.previewId, idempotencyKey: command.idempotencyKey }, request.signal);
      if (requestGeneration === generation.current && !request.signal.aborted) {
        if (!sameIdentity(next.templateId, command.templateId) || next.templateVersion !== command.templateVersion
          || !sameIdentity(next.targetLedgerBookId, command.targetLedgerBookId) || !sameIdentity(next.targetPeriodId, command.targetPeriodId)
          || !sameIdentity(next.sourceWorkflowId, command.sourceWorkflowId)) throw new Error("Created plan response does not match the reviewed preparation. Retry the same request to recover its result.");
        clearPendingCreation(command); setPending(null); setResult(next); setRetryCreate(false);
      }
    } catch (reason) {
      if (requestGeneration === generation.current && !request.signal.aborted) {
        if (isStalePreviewError(reason)) {
          clearPendingCreation(command); setPending(null);
          setPreview(null); creationKey.current = null; setRetryCreate(false);
          setError(`${message(reason)} Refresh the preview before creating the plan.`);
        } else { setRetryCreate(true); setError(`${message(reason)} Retry creation to recover the same plan; the retry keeps the original request identity.`); }
      }
    } finally { if (requestGeneration === generation.current && !request.signal.aborted) setBusy(null); }
  };
  const selectedBook = books.find(book => book.ledgerBookId === bookId);
  const visiblePeriods = periods.filter(period => period.ledgerBookId.toLowerCase() === bookId.toLowerCase());
  const selectedPeriod = visiblePeriods.find(period => period.periodId === periodId);
  const configurationDisabled = busy === "create" || result !== null || pending !== null;
  const selectedPolicyChanged = selectedBook && (selectedBook.accountingPolicyId !== template.sourceAccountingPolicyId || selectedBook.accountingPolicyVersion !== template.sourceAccountingPolicyVersion);
  return <div className="space-y-4 border-t border-border pt-4">
    {pending && !preview && !result ? <div className="space-y-3">
      <StatusBanner tone="warning" title="Unfinished creation request" detail="Recover the original request before preparing another period. The service will return its existing plan or recheck the original inputs." role="status" />
      <Button disabled={Boolean(busy)} onClick={() => void create()}>{busy === "create" ? "Recovering creation…" : "Recover pending creation"}</Button>
    </div> : null}
    <fieldset disabled={configurationDisabled} className="space-y-4">
      <legend className="mb-3 font-semibold">2. Select target and resolve mappings</legend>
      <FormGrid columns={2}>
        <FormRow label="Target book" labelFor="close-target-book"><Select id="close-target-book" value={bookId} placeholder="Select an authoritative book" onChange={event => { invalidate(); setBookId(event.target.value); setPeriodId(""); setPolicyAcknowledged(false); }}>
          {books.map(book => <option key={book.ledgerBookId} value={book.ledgerBookId}>{book.displayName} · {book.accountingBasis} · {book.baseCurrency}</option>)}
        </Select></FormRow>
        <FormRow label="Target period" labelFor="close-target-period"><Select id="close-target-period" value={periodId} disabled={!bookId || periodLoading || Boolean(periodError)} placeholder={periodLoading ? "Loading periods…" : "Select an authoritative period"} onChange={event => { invalidate(); setPeriodId(event.target.value); setPolicyAcknowledged(false); }}>
          {visiblePeriods.map(period => <option key={period.periodId} value={period.periodId}>{period.label} · {period.startDate} to {period.endDate} · {period.status}</option>)}
        </Select></FormRow>
      </FormGrid>
      {books.length === 0 ? <p role="status">No authoritative target books are available for this source.</p> : null}
      {periodError ? <StatusBanner tone="danger" title="Target periods unavailable" detail={periodError} role="alert" /> : null}
      {periodError ? <Button variant="outline" onClick={() => { invalidate(); setPeriodRevision(value => value + 1); }}>Retry target periods</Button> : null}
      {bookId && !periodLoading && !periodError && visiblePeriods.length === 0 ? <p role="status">No authoritative periods are configured for this book.</p> : null}
      {selectedPeriod ? <p className="text-sm">Target period: <strong>{selectedPeriod.label}</strong>, {selectedPeriod.startDate} – {selectedPeriod.endDate}. Status: {selectedPeriod.status}.</p> : null}
      <p className="text-sm text-muted-foreground">Assign each source owner explicitly, including owners who remain the same. Missing assignments block creation.</p>
      <FormGrid columns={2}>{owners.map((owner, index) => <FormRow key={owner} label={`Target owner for ${owner || "unassigned source owner"}`} labelFor={`close-owner-${index}`}><Input id={`close-owner-${index}`} value={mappings[owner] ?? ""} onChange={event => { invalidate(); setMappings(current => ({ ...current, [owner]: event.target.value })); }} /></FormRow>)}</FormGrid>
      {selectedBook ? <p className="text-sm"><strong>{selectedPolicyChanged ? "Policy changed" : "Book policy"}:</strong> {template.sourceAccountingPolicyId} · {template.sourceAccountingPolicyVersion} → {selectedBook.accountingPolicyId} · {selectedBook.accountingPolicyVersion}</p> : null}
      <Checkbox label="I have reviewed policy changes for this target book and period" checked={policyAcknowledged} onCheckedChange={checked => { invalidate(); setPolicyAcknowledged(checked); }} />
    </fieldset>
    {!result ? <Button variant="outline" disabled={!bookId || !periodId || Boolean(busy) || Boolean(pending)} onClick={() => void runPreview()}>{busy === "preview" ? "Calculating preview…" : preview || error ? "Refresh preview" : "Preview next period"}</Button> : null}
    {error ? <StatusBanner tone="danger" title="Preparation needs attention" detail={error} role="alert" /> : null}
    {preview ? <ClosePreparationPreviewDetails preview={preview} /> : null}
    {preview && !result ? <>
      {expired ? <StatusBanner tone="warning" title="Preview expired" detail="Refresh the preview to recheck the source, target period, calendar and policies." role="status" /> : null}
      <p className="text-xs text-muted-foreground">Preview valid until {preview.expiresAtUtc}. Creation rechecks current source and target versions.</p>
      <Button disabled={!preview.canCreate || preview.issues.some(issue => issue.isBlocking) || Boolean(busy) || (expired && !retryCreate)} onClick={() => void create()}>{busy === "create" ? "Creating fresh plan…" : retryCreate ? "Retry creation" : "Create next-period plan"}</Button>
    </> : null}
    {result ? <div className="space-y-3" role="status">
      <StatusBanner tone="success" title={result.wasAlreadyCreated ? "Existing preparation recovered" : "Next-period plan created"} detail={`${preview?.targetPeriod.label ?? selectedPeriod?.label ?? result.plan.periodId} · Template ${template.name} v${result.templateVersion} · Created by ${result.createdBy} at ${result.createdAtUtc}`} />
      <p className="text-sm">{result.plan.tasks.length} fresh tasks. All required sign-offs and evidence reviews must be completed for this period.</p>
      <Link className="text-sm font-semibold text-primary underline" to={`/accounting?${new URLSearchParams({ ledgerBookId: result.targetLedgerBookId, periodId: result.plan.periodId, ...(result.plan.fundAccountId ? { fundAccountId: result.plan.fundAccountId } : {}) })}`}>Open prepared close plan</Link>
      <ClosePreparationHistoryList history={result.history} label="Creation history" />
    </div> : null}
  </div>;
}

function ClosePreparationPreviewDetails({ preview }: { preview: ClosePreparationPreview }) {
  const names = new Map(preview.tasks.map(task => [task.taskId, task.displayName]));
  return <section className="space-y-3" aria-label="Next-period preview">
    <div className="flex flex-wrap items-center justify-between gap-3"><h4 className="font-semibold">3. Review {preview.targetPeriod.label}</h4><Badge variant={preview.canCreate ? "success" : "warning"}>{preview.canCreate ? "Ready to create" : "Mappings or policies need attention"}</Badge></div>
    <p className="text-sm">{preview.targetBook.displayName} · {preview.targetPeriod.startDate} – {preview.targetPeriod.endDate} · Calendar {preview.calendar.calendarId} v{preview.calendar.version}</p>
    {preview.policyChanged ? <StatusBanner tone="warning" title="Policy changed for the target" detail={`Target policy: ${preview.targetPeriod.accountingPolicyId ?? preview.targetBook.accountingPolicyId} · ${preview.targetPeriod.accountingPolicyVersion ?? preview.targetBook.accountingPolicyVersion}`} /> : null}
    {preview.issues.length > 0 ? <ul aria-label="Preparation issues" className="space-y-2">{preview.issues.map((issue, index) => <li key={`${issue.code}-${index}`} className="text-sm"><Badge variant={issue.isBlocking ? "danger" : "warning"}>{issue.isBlocking ? "Blocked" : "Review"}</Badge> {issue.taskId ? `${names.get(issue.taskId) ?? issue.taskId}: ` : ""}{issue.message}</li>)}</ul> : null}
    <div className="overflow-x-auto"><table className="w-full text-left text-sm" aria-label="Prepared task deadlines and requirements">
      <thead><tr className="border-b border-border"><th className="p-2">Task / dependencies</th><th className="p-2">Owner</th><th className="p-2">Deadline</th><th className="p-2">Fresh sign-offs and evidence</th></tr></thead>
      <tbody>{preview.tasks.map(task => <tr key={task.taskId} className="border-b border-border align-top">
        <th scope="row" className="min-w-40 p-2 font-medium">{task.displayName}<div className="mt-1 text-xs font-normal text-muted-foreground">{task.dependencies.length ? `After: ${task.dependencies.map(dependency => names.get(dependency.dependsOnTaskId) ?? dependency.dependsOnTaskId).join(", ")}` : "No dependencies"}</div></th>
        <td className="p-2">{task.ownerChanged ? <><Badge variant="warning">Owner changed</Badge><div>{task.sourceOwner} → {task.owner || "Unresolved"}</div></> : task.owner || "Unresolved owner"}</td>
        <td className="min-w-48 p-2"><div>{task.sourceDueDate} → <strong>{task.dueDate}</strong></div><div className="mt-1 text-xs text-muted-foreground">{task.deadlineRule.anchor === "PeriodEnd" ? "Period end" : "Period start"} {task.deadlineRule.offsetDays >= 0 ? "+" : ""}{task.deadlineRule.offsetDays} {task.deadlineRule.dayCount === "BusinessDays" ? "business" : "calendar"} days; {task.deadlineRule.adjustment === "None" ? "no adjustment" : task.deadlineRule.adjustment === "FollowingBusinessDay" ? "following business day" : "preceding business day"}.</div></td>
        <td className="min-w-48 p-2">{task.signOffRequirements.map((requirement, index) => <div key={`${requirement.role}-${index}`} className="mb-2"><div>{requirement.role}: {requirement.requiredApprovalCount} new approval(s)</div><div className="text-xs text-muted-foreground">{requirement.evidenceRequirement || "Fresh supporting evidence required"}</div></div>)}</td>
      </tr>)}</tbody>
    </table></div>
    <p className="text-sm text-muted-foreground">Task structure and dependency links are retained. Prior task completion, approvals, reviewed evidence, journal references and locks are excluded from the new plan.</p>
  </section>;
}

function ClosePreparationHistoryList({ history, label }: { history: ClosePreparationHistory[]; label: string }) {
  return <details className="text-sm"><summary className="cursor-pointer font-medium">{label} ({history.length})</summary><ol className="mt-2 space-y-2">{history.map((entry, index) => <li key={`${entry.occurredAtUtc}-${index}`}><p>{entry.description}</p><p className="text-xs text-muted-foreground">{entry.actor} · {entry.occurredAtUtc} · {entry.eventType}</p></li>)}</ol></details>;
}
