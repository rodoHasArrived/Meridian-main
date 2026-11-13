import { useEffect, useId, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { RetainedEvidenceFields, emptyRetainedEvidence, retainedEvidencePayload, type RetainedEvidenceFieldsValue } from "@/components/meridian/retained-evidence-fields";
import { Select } from "@/components/ui/select";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { ApiError, describeApiError } from "@/lib/api-errors";
import { createCanonicalAmortizationDraft, prepareCanonicalAmortization, type CanonicalAmortizationDraft, type CanonicalAmortizationPreview, type CanonicalAmortizationRequest } from "@/lib/api/canonical-amortization.api";
import type { AccountingBasisKind } from "@/types";

interface Props { bookId: string; periodId: string | null; periodVersion: number | null; fundProfileId: string; accountingBasis?: AccountingBasisKind; active?: boolean }
interface Form { taxLotRecordId: string; asOfDate: string; assetAccountPath: string; incomeAccountPath: string; securityEvidence: RetainedEvidenceFieldsValue; eventEvidence: RetainedEvidenceFieldsValue; scheduleEndDate: string; scheduleFrequency: "Monthly" | "Quarterly" | "Annual"; remaining: boolean; worst: boolean }
const fields = [["taxLotRecordId", "Canonical tax lot ID"], ["asOfDate", "Amortization occurrence date", "date"], ["assetAccountPath", "Amortization asset account path"], ["incomeAccountPath", "Amortization income account path"]] as const;
const emptyForm: Form = { taxLotRecordId: "", asOfDate: "", assetAccountPath: "", incomeAccountPath: "", securityEvidence: emptyRetainedEvidence(), eventEvidence: emptyRetainedEvidence(), scheduleEndDate: "", scheduleFrequency: "Monthly", remaining: false, worst: false };

type JournalLine = CanonicalAmortizationPreview["lines"][number];
type ScheduleOccurrence = NonNullable<CanonicalAmortizationPreview["occurrences"]>[number];
const journalColumns: DenseDataTableColumn<JournalLine>[] = [
  { id: "account", label: "Account", rowHeader: true, render: row => row.accountId },
  { id: "debit", label: "Debit", numeric: true, render: row => row.debit },
  { id: "credit", label: "Credit", numeric: true, render: row => row.credit }
];
const scheduleColumns: DenseDataTableColumn<ScheduleOccurrence>[] = [
  { id: "date", label: "Occurrence", rowHeader: true, render: row => row.date },
  { id: "transaction", label: "Transaction basis", numeric: true, render: row => row.transactionCostBasis },
  { id: "functional", label: "Functional basis", numeric: true, render: row => row.functionalCostBasis }
];

/** Occurrences use the retained acquisition schedule; the browser never overrides its accounting method or yield. */
export function CanonicalAmortizationPanel(props: Props) {
  if (props.active === false) return null;
  return <ScopedAmortizationPanel key={`${props.bookId}:${props.periodId}:${props.periodVersion}:${props.fundProfileId}:${props.accountingBasis}`} {...props} />;
}
function ScopedAmortizationPanel({ bookId, periodId, periodVersion, fundProfileId, accountingBasis = "Primary" }: Props) {
  const id = useId();
  const [form, setForm] = useState<Form>(emptyForm);
  const [preview, setPreview] = useState<{ request: CanonicalAmortizationRequest; result: CanonicalAmortizationPreview } | null>(null);
  const [draft, setDraft] = useState<CanonicalAmortizationDraft | null>(null);
  const [description, setDescription] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const [busy, setBusy] = useState<"prepare" | "draft" | null>(null);
  const [uncertain, setUncertain] = useState(false);
  const [error, setError] = useState("");
  const controller = useRef<AbortController | null>(null);
  useEffect(() => () => controller.current?.abort(), []);
  function edit<K extends keyof Form>(key: K, value: Form[K]) {
    controller.current?.abort(); controller.current = null;
    setBusy(null); setPreview(null); setDraft(null); setError(""); setConfirmed(false);
    setForm(current => ({ ...current, [key]: value }));
  }
  async function prepare() {
    if (controller.current || uncertain) return;
    setError(""); setPreview(null); setConfirmed(false); setDraft(null);
    const requestController = new AbortController(); controller.current = requestController; setBusy("prepare");
    try {
      if (!bookId || !periodId || periodVersion === null || !fundProfileId || !Number.isSafeInteger(periodVersion))
        throw new Error("Select an authoritative book and open period first.");
      if (!form.taxLotRecordId.trim() || !form.asOfDate || !form.assetAccountPath.trim() || !form.incomeAccountPath.trim())
        throw new Error("Complete the lot, occurrence date and both posting accounts.");
      const request: CanonicalAmortizationRequest = {
        ledgerBookId: bookId, taxLotRecordId: form.taxLotRecordId.trim(), periodId, expectedPeriodVersion: periodVersion,
        accountingBasis, fundProfileId, asOfDate: form.asOfDate, assetAccountPath: form.assetAccountPath.trim(), incomeAccountPath: form.incomeAccountPath.trim(),
        securityEvidence: retainedEvidencePayload(form.securityEvidence, "SecurityMasterProjection"), eventEvidence: retainedEvidencePayload(form.eventEvidence, "AssetAccountingEvent"),
        scheduleEndDate: form.scheduleEndDate || null, scheduleFrequency: form.scheduleFrequency,
        amortizeRemainingFace: form.remaining, automaticYieldToWorst: form.worst
      };
      const result = await prepareCanonicalAmortization(request, { signal: requestController.signal });
      if (!requestController.signal.aborted) setPreview({ request, result });
    } catch (cause) { if (!requestController.signal.aborted) setError(describeApiError(cause, "Canonical amortization preparation is unavailable.").summary); }
    finally { if (controller.current === requestController) { controller.current = null; setBusy(null); } }
  }
  async function createDraft() {
    if (!preview || !confirmed || !description.trim() || controller.current || draft) return;
    const requestController = new AbortController(); controller.current = requestController; setBusy("draft"); setError("");
    try {
      const result = await createCanonicalAmortizationDraft(preview.request, preview.result.preparationFingerprint, description.trim(), { signal: requestController.signal });
      if (!requestController.signal.aborted) { setDraft(result); setUncertain(false); }
    } catch (cause) {
      if (!requestController.signal.aborted) {
        setError(describeApiError(cause, "Amortization draft could not be confirmed.").summary);
        if (cause instanceof ApiError && cause.status === 409 && cause.responseBody?.includes("AMORTIZATION_PREVIEW_STALE")) { setPreview(null); setConfirmed(false); setUncertain(false); }
        else if (!(cause instanceof ApiError && [400, 401, 403, 404, 409, 501].includes(cause.status))) setUncertain(true);
      }
    } finally { if (controller.current === requestController) { controller.current = null; setBusy(null); } }
  }
  const locked = busy === "draft" || uncertain || !!draft;
  const available = !!bookId && !!periodId && periodVersion !== null && !!fundProfileId;
  return <Card className="panel-surface min-w-0" role="region" aria-label="Canonical lot amortization">
    <CardHeader><CardTitle>Accretion and amortization occurrences</CardTitle><CardDescription>Configure an occurrence from the lot's retained acquisition method and yield. Preview exact carrying-value changes, review the journal, then hand off for independent approval and governed posting. Requires ledger management permission and reviewed evidence.</CardDescription></CardHeader>
    <CardContent className="space-y-4">
      {!available ? <p role="status">Select an authoritative ledger book and period to configure amortization.</p> : <>
        <form onSubmit={event => { event.preventDefault(); void prepare(); }}>
          <fieldset disabled={locked} className="space-y-3"><legend className="sr-only">Amortization schedule and occurrence configuration</legend>
            <p className="text-sm text-muted-foreground">Method and effective yield come from the canonical acquisition. Changing them requires a separate governed acquisition correction.</p>
            <FormGrid columns={2}>{fields.map(([key, label, type]) => <FormRow key={key} label={label} labelFor={`${id}-${key}`}><Input id={`${id}-${key}`} type={type ?? "text"} value={form[key]} onChange={event => edit(key, event.target.value)} /></FormRow>)}</FormGrid>
            <label className="flex gap-2 text-sm"><input type="checkbox" checked={form.remaining} onChange={event => edit("remaining", event.target.checked)} />Amortize remaining face after retained paydowns</label>
            <label className="flex gap-2 text-sm"><input type="checkbox" checked={form.worst} onChange={event => edit("worst", event.target.checked)} />Select automatic yield to worst for independent review</label>
            <FormGrid columns={2}>
              <FormRow label="Schedule preview through" labelFor={`${id}-schedule-end`} hint="Optional. Preview up to 120 occurrences without retaining future journals."><Input id={`${id}-schedule-end`} type="date" value={form.scheduleEndDate} onChange={event => edit("scheduleEndDate", event.target.value)} /></FormRow>
              <FormRow label="Schedule occurrence frequency" labelFor={`${id}-schedule-frequency`}><Select id={`${id}-schedule-frequency`} value={form.scheduleFrequency} onChange={event => edit("scheduleFrequency", event.target.value as Form["scheduleFrequency"])}><option value="Monthly">Monthly</option><option value="Quarterly">Quarterly</option><option value="Annual">Annual</option></Select></FormRow>
            </FormGrid>
            <RetainedEvidenceFields label="Reviewed Security Master evidence" subjectType="SecurityMasterProjection" value={form.securityEvidence} onChange={value => edit("securityEvidence", value)} />
            <RetainedEvidenceFields label="Reviewed amortization occurrence evidence" subjectType="AssetAccountingEvent" value={form.eventEvidence} onChange={value => edit("eventEvidence", value)} />
            <Button type="submit" disabled={busy !== null}>{busy === "prepare" ? "Preparing amortization…" : "Preview amortization occurrence"}</Button>
          </fieldset>
        </form>
        {preview ? <section aria-label="Amortization occurrence review" className="space-y-3">
          <h3 className="text-sm font-semibold">Review calculated occurrence</h3>
          <p className="text-sm">Lot {preview.result.instruction.expectedLot.lotId} · version {preview.result.instruction.expectedLot.version} · {preview.result.instruction.calculationVersion}</p>
          <p className="text-sm">Method: {preview.result.instruction.expectedLot.acquisition.faceValueTerms?.amortizationMethod ?? "Unavailable"} · Annual yield: {preview.result.instruction.expectedLot.acquisition.faceValueTerms?.effectiveYield ?? "Not required by retained method"}</p>
          <dl className="grid grid-cols-2 gap-2 text-sm">{Object.entries(preview.result.projection).map(([key, amount]) => <div key={key}><dt>{({ transactionCostBasis: "Transaction basis after", functionalCostBasis: "Functional basis after", transactionMovement: "Transaction movement", functionalMovement: "Functional movement" } as Record<string, string>)[key]}</dt><dd className="font-mono">{amount}</dd></div>)}</dl>
          <DenseDataTable
            ariaLabel="Proposed amortization journal"
            caption={`Proposed amortization journal · ${preview.result.instruction.expectedLot.acquisition.functionalCurrency}`}
            columns={journalColumns} rows={preview.result.lines} getRowId={row => row.accountId}
            emptyText="No journal lines were prepared." maxVisibleRows={null}
          />
          {preview.result.occurrences?.length ? <DenseDataTable
            ariaLabel="Amortization schedule preview"
            caption="Schedule preview · cumulative basis from the retained acquisition · only the selected occurrence is handed off"
            columns={scheduleColumns} rows={preview.result.occurrences} getRowId={row => row.date}
            emptyText="No schedule occurrences were prepared." maxVisibleRows={null}
          /> : null}
          {preview.result.instruction.yieldToWorst ? <details><summary>Automatic yield-to-worst comparison</summary><pre className="overflow-auto text-xs">{JSON.stringify(preview.result.instruction.yieldToWorst, null, 2)}</pre></details> : null}
          <fieldset disabled={locked} className="space-y-3"><legend className="sr-only">Amortization journal handoff</legend>
            <FormRow label="Amortization journal description" labelFor={`${id}-description`}><Input id={`${id}-description`} value={description} onChange={event => { setDescription(event.target.value); setConfirmed(false); }} /></FormRow>
            <label className="flex gap-2 text-sm"><input type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} />I reviewed the retained schedule, evidence, occurrence and proposed journal.</label>
          </fieldset>
          <Button disabled={busy !== null || !confirmed || !description.trim() || !!draft} onClick={() => void createDraft()}>{busy === "draft" ? "Creating amortization review draft…" : uncertain ? "Retry same amortization draft" : "Create amortization review draft"}</Button>
        </section> : null}
      </>}
      {uncertain ? <p role="status">The draft outcome is uncertain. Keep these reviewed inputs and retry the same occurrence.</p> : null}
      {error ? <p role="alert" className="text-sm text-danger">{error}</p> : null}
      {draft?.candidate.issues.length ? <ul aria-label="Amortization draft blockers">{draft.candidate.issues.map((issue, index) => <li key={index}>{issue.message}</li>)}</ul> : null}
      {draft?.candidate.journalEntryId ? <p role="status"><Link className="text-primary underline" to={`/accounting/journal-entries?${new URLSearchParams({ fundProfileId, ledgerBookId: bookId, periodId: periodId ?? "", journalEntryId: draft.candidate.journalEntryId })}`}>Open amortization journal review</Link>. Validation, independent approval and canonical posting remain in the governed journal workflow.</p> : null}
    </CardContent>
  </Card>;
}
