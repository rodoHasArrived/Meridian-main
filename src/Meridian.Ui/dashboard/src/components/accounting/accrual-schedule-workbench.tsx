import { useCallback, useEffect, useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Checkbox } from "@/components/ui/checkbox";
import { LocalDateText } from "@/components/ui/local-date-text";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Select } from "@/components/ui/select";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import { ApiError, describeApiError } from "@/lib/api-errors";
import { getAccrualSchedules, previewAccrualSchedule, configureAccrualSchedule, runAccrualSchedule,
  type AccrualScheduleInput, type AccrualSchedulePreview, type AccrualScheduleRegistry,
  type AccrualCadence, type AccrualAccountType } from "@/lib/api/accrual-schedules.api";
import type { RecurringJournalScope } from "@/lib/api/recurring-journals.api";
import { RetainedEvidenceFields, emptyRetainedEvidence, retainedEvidenceReference } from "@/components/accounting/retained-evidence-fields";
import { RecurringJournalQueue } from "@/components/accounting/RecurringJournalQueue";

interface Props { scope: RecurringJournalScope | null; availableDraftIds: string[]; onSelectDraft: (id: string) => void; onDraftsGenerated?: () => Promise<void> | void }
const initial = { scheduleId: "", description: "", currency: "", cadence: "Monthly" as AccrualCadence,
  anchorDate: "", endsOn: "", amount: "", debitAccount: "", debitAccountType: "Expense" as AccrualAccountType,
  creditAccount: "", creditAccountType: "Liability" as AccrualAccountType, expectedScheduleVersion: 0, expectedTemplateVersion: 0 };
const accountTypes: AccrualAccountType[] = ["Asset", "Liability", "Equity", "Revenue", "Expense"];
const cadences: AccrualCadence[] = ["Daily", "Weekly", "Monthly", "Quarterly", "SemiAnnually", "Annually"];
type AccrualOccurrence = AccrualSchedulePreview["occurrences"][number];
const occurrenceColumns: DenseDataTableColumn<AccrualOccurrence>[] = [
  { id: "date", label: "Date", rowHeader: true, render: row => <LocalDateText value={row.effectiveDate} /> },
  { id: "amount", label: "Amount", numeric: true, render: row => <span className="font-mono">{row.amount}</span> },
  { id: "debit", label: "Debit account", render: row => row.debitAccount },
  { id: "credit", label: "Credit account", render: row => row.creditAccount },
  { id: "period", label: "Period / status", render: row => <>{row.periodId} · {row.isOpen ? "Open" : row.blocker ?? "Closed"}{row.lockOwner ? ` · Lock: ${row.lockOwner}` : ""}</> }
];
export function AccrualScheduleWorkbench(props: Props) {
  return <AccrualScopeWorkbench key={JSON.stringify(props.scope)} {...props} />;
}
function AccrualScopeWorkbench({ scope, availableDraftIds, onSelectDraft, onDraftsGenerated }: Props) {
  const id = useId();
  const [registry, setRegistry] = useState<AccrualScheduleRegistry | null>(null);
  const [form, setForm] = useState(initial);
  const [evidence, setEvidence] = useState(emptyRetainedEvidence);
  const [preview, setPreview] = useState<{ input: AccrualScheduleInput; result: AccrualSchedulePreview } | null>(null);
  const [confirmed, setConfirmed] = useState(false);
  const [saved, setSaved] = useState<{ scheduleId: string; version: number } | null>(null);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState<string | null>(null);
  const [uncertain, setUncertain] = useState(false);
  const [queueVersion, setQueueVersion] = useState(0);
  const controller = useRef<AbortController | null>(null);
  const canConfigure = registry?.canConfigure === true;
  const locked = busy === "save" || busy === "run" || uncertain;
  const fundProfileId = scope?.fundProfileId;
  const ledgerBookId = scope?.ledgerBookId;
  const entityId = scope?.entityId;
  const load = useCallback(async () => {
    if (!fundProfileId || !ledgerBookId || !entityId || controller.current) return;
    const request = new AbortController(); controller.current = request; setBusy("load"); setError(""); setRegistry(null);
    try { const result = await getAccrualSchedules({ fundProfileId, ledgerBookId, entityId }, { signal: request.signal }); if (!request.signal.aborted) setRegistry(result); }
    catch (cause) { if (!request.signal.aborted) setError(describeApiError(cause, "Accrual schedule registry unavailable.").summary); }
    finally { if (controller.current === request) { controller.current = null; setBusy(null); } }
  }, [fundProfileId, ledgerBookId, entityId]);
  useEffect(() => { void load(); return () => { controller.current?.abort(); controller.current = null; }; }, [load]);
  function edit<K extends keyof typeof initial>(key: K, value: typeof initial[K]) {
    controller.current?.abort(); controller.current = null; setBusy(null);
    setForm(current => ({ ...current, [key]: value })); setPreview(null); setSaved(null); setConfirmed(false); setError("");
  }
  async function prepare() {
    if (!scope || controller.current || uncertain) return;
    setError(""); setPreview(null); setConfirmed(false); setSaved(null);
    const request = new AbortController(); controller.current = request; setBusy("preview");
    try {
      const references = [retainedEvidenceReference(evidence)];
      if (!/^\d+(?:\.\d+)?$/.test(form.amount)) throw new Error("Enter an exact decimal amount without exponents or separators.");
      const input = { ...form, ...scope, evidence: references } as AccrualScheduleInput;
      const result = await previewAccrualSchedule(input, { signal: request.signal });
      if (!request.signal.aborted) { setPreview({ input, result }); setMessage("Review every occurrence and its authoritative period before configuring."); }
    } catch (cause) { if (!request.signal.aborted) setError(describeApiError(cause, "Accrual preview unavailable.").summary); }
    finally { if (controller.current === request) { controller.current = null; setBusy(null); } }
  }
  async function save() {
    if (!preview || !confirmed || !canConfigure || controller.current) return;
    const request = new AbortController(); controller.current = request; setBusy("save"); setError("");
    try {
      const result = await configureAccrualSchedule(preview.input, preview.result.fingerprint, { signal: request.signal });
      if (!request.signal.aborted) { setSaved(result); setUncertain(false); setMessage("Schedule retained. Generate due occurrences, then open each journal for validation, approval and posting."); }
    } catch (cause) { if (!request.signal.aborted) {
      setError(describeApiError(cause, "Schedule save was not confirmed.").summary);
      const rejected = cause instanceof ApiError && (cause.status === 400 || cause.status === 403 ||
        (cause.status === 409 && /ACCRUAL_(?:PREVIEW|DEFINITION)_STALE/.test(cause.responseBody ?? "")));
      if (rejected) {
        setUncertain(false); setPreview(null); setConfirmed(false);
        if (cause instanceof ApiError && cause.status === 403) setRegistry(null);
        setMessage("The schedule was refused. Refresh its authority, correct the inputs and preview again.");
      } else {
        setUncertain(true); setMessage("The outcome is unconfirmed. Inputs remain locked; retry the same reviewed schedule command to recover its retained result.");
      }
    } }
    finally { if (controller.current === request) { controller.current = null; setBusy(null); } }
  }
  async function run(scheduleId: string, version: number) {
    if (!scope || !canConfigure || controller.current || uncertain) return;
    const request = new AbortController(); controller.current = request; setBusy("run"); setError("");
    try { await runAccrualSchedule(scheduleId, scope, version, { signal: request.signal });
      if (!request.signal.aborted) await onDraftsGenerated?.();
      if (!request.signal.aborted) { setQueueVersion(current => current + 1); setMessage("Due occurrence processing finished. Review retained draft states and blockers below."); }
    } catch (cause) { if (!request.signal.aborted) setError(describeApiError(cause, "Due occurrence processing unavailable. Refresh occurrences before retrying.").summary); }
    finally { if (controller.current === request) { controller.current = null; setBusy(null); } }
  }
  return <Card role="region" aria-label="Accrual schedules" className="min-w-0">
    <CardHeader><CardTitle>Accrual schedules</CardTitle><CardDescription>Configure reviewed fixed accrual amounts on a cadence. Each due occurrence becomes a retained journal draft for human approval. Interest calculations and effective-yield lot amortization use their own preparation workflows.</CardDescription></CardHeader>
    <CardContent className="space-y-4">
      {!scope ? <p role="status">Select a fund, ledger book and entity to configure accrual schedules.</p> : <>
        {!registry ? <p role="status">{busy === "load" ? "Loading schedule permissions and definitions…" : "Schedule authority unavailable. Retry to load permissions and retained definitions."}</p> : !canConfigure ? <p role="status">Read only: ManageLedgerReports permission is required to configure schedules or generate due drafts.</p> : null}
        <Button type="button" variant="outline" disabled={busy !== null} onClick={() => { if (!uncertain) { setPreview(null); setConfirmed(false); setSaved(null); } void load(); }}>Refresh retained schedules</Button>
        {registry?.schedules.map(row => <article key={row.scheduleId} className="rounded border border-border p-3 text-sm"><p className="break-all font-semibold">{row.scheduleId} · v{row.version}</p><p>{row.description} · {row.cadence} · <LocalDateText value={row.anchorDate} /> to <LocalDateText value={row.endsOn} fallback="No end date" /> · {row.currency}</p><Button type="button" variant="outline" disabled={!canConfigure || busy !== null || uncertain} onClick={() => void run(row.scheduleId, row.version)}>Generate due drafts for {row.scheduleId}</Button></article>)}
        {registry?.schedules.length === 0 ? <p role="status">No retained accrual schedules for this scope.</p> : null}
        <details><summary className="cursor-pointer font-semibold">Configure a new accrual schedule</summary>
          <form className="mt-3 space-y-3" onSubmit={event => { event.preventDefault(); void prepare(); }}>
            <fieldset disabled={locked || !canConfigure} className="space-y-3"><legend className="sr-only">Accrual schedule inputs</legend>
              <FormGrid columns={2}>{([ ["scheduleId", "Schedule identity", "text"], ["description", "Accrual description", "text"], ["currency", "Book currency", "text"], ["amount", "Amount per occurrence", "text"], ["anchorDate", "First accrual date", "date"], ["endsOn", "Last accrual date", "date"], ["debitAccount", "Debit account path", "text"], ["creditAccount", "Credit account path", "text"] ] as const).map(([field, label, type]) => <FormRow key={field} label={label} labelFor={`${id}-${field}`}><Input required id={`${id}-${field}`} type={type} value={form[field]} onChange={event => edit(field, event.target.value)} /></FormRow>)}
                <FormRow label="Cadence" labelFor={`${id}-cadence`}><Select id={`${id}-cadence`} value={form.cadence} onChange={event => edit("cadence", event.target.value as AccrualCadence)}>{cadences.map(value => <option key={value}>{value}</option>)}</Select></FormRow>
                {([ ["debitAccountType", "Debit account type"], ["creditAccountType", "Credit account type"] ] as const).map(([field, label]) => <FormRow key={field} label={label} labelFor={`${id}-${field}`}><Select id={`${id}-${field}`} value={form[field]} onChange={event => edit(field, event.target.value as AccrualAccountType)}>{accountTypes.map(value => <option key={value}>{value}</option>)}</Select></FormRow>)}
              </FormGrid>
              <RetainedEvidenceFields value={evidence} onChange={value => { edit("scheduleId", form.scheduleId); setEvidence(value); }} />
              <Button type="submit" disabled={busy !== null}>Preview accrual schedule</Button>
            </fieldset>
          </form>
        </details>
        {preview ? <section aria-label="Accrual occurrence preview" className="space-y-3">
          <h3 className="font-semibold">Occurrence preview · {preview.result.currency}</h3><p className="break-all text-xs">Input fingerprint: {preview.result.fingerprint}</p>
          <DenseDataTable columns={occurrenceColumns} rows={preview.result.occurrences}
            getRowId={row => row.effectiveDate} tableId="accrual-occurrence-preview"
            ariaLabel="Accrual occurrence table" caption="Scheduled accruals and period availability"
            emptyText="No accrual occurrences fall within this schedule." tableClassName="min-w-[42rem]"
            maxVisibleRows={null} />
          <Checkbox checked={confirmed} disabled={locked || !!saved} onCheckedChange={setConfirmed}
            label="I reviewed the exact amount, accounts, source evidence and every occurrence." />
          <Button type="button" disabled={!confirmed || !canConfigure || busy !== null || !!saved || preview.result.occurrences.some(row => !row.isOpen)} onClick={() => void save()}>{uncertain ? "Retry same accrual schedule" : "Retain accrual schedule"}</Button>
          {saved ? <Button type="button" disabled={busy !== null} onClick={() => void run(saved.scheduleId, saved.version)}>Generate due accrual drafts</Button> : null}
        </section> : null}
        <RecurringJournalQueue key={queueVersion} scope={scope} availableDraftIds={availableDraftIds} onSelectDraft={onSelectDraft} />
      </>}
      {message ? <p role="status" className="text-sm text-muted-foreground">{message}</p> : null}
      {error ? <p role="alert" className="text-sm text-danger">{error}</p> : null}
    </CardContent>
  </Card>;
}
