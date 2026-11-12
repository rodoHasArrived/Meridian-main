import { useEffect, useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { describeApiError } from "@/lib/api-errors";
import * as api from "@/lib/api/private-capital.api";
import { CapitalBankObservationImport } from "@/screens/capital-call-bank-observation-import";
import { CapitalCallLines, capitalAmount, capitalIsPositive, effectiveCapitalCommitments, type CapitalMutation } from "@/screens/private-capital-presenters";
import type { PreviewPrivateCapitalCall, PrivateCapitalCall, PrivateCapitalCallPreview, PrivateCapitalCommitment } from "@/types/private-capital";
import { TextArea } from "@/components/ui/text-area";
import { Select } from "@/components/ui/select";
import { Checkbox } from "@/components/ui/checkbox";

type Field = readonly [key: string, label: string, type?: string];
const commitmentFields: Field[] = [
  ["commitmentId", "Commitment ID"], ["investorId", "Investor ID"], ["capitalAccountId", "Capital account ID"],
  ["totalCommitment", "Total commitment"], ["commitmentDate", "Commitment date", "date"], ["effectiveFrom", "Effective from", "date"],
  ["investmentPeriodEndDate", "Investment period ends (optional)", "date"], ["expectedRevision", "Expected current revision", "number"]
];
const callFields: Field[] = [
  ["callId", "Capital call ID"], ["amountToCall", "Amount to call"], ["periodId", "Accounting period ID"],
  ["noticeDate", "Notice date", "date"], ["dueDate", "Due date", "date"], ["purpose", "Purpose (optional)"]
];
function fieldsForm(fields: Field[], defaults: Record<string, string> = {}) {
  return Object.fromEntries(fields.map(([key]) => [key, defaults[key] ?? ""]));
}
function Fields({ fields, values, update }: { fields: Field[]; values: Record<string, string>; update: (key: string, value: string) => void }) {
  const id = useId();
  return <FormGrid columns={3}>{fields.map(([key, label, type]) => <FormRow key={key} label={label} labelFor={`${id}-${key}`}>
    <Input id={`${id}-${key}`} type={type ?? "text"} inputMode={/amount|total/i.test(key) ? "decimal" : undefined}
      value={values[key]} onChange={event => update(key, event.target.value)} />
  </FormRow>)}</FormGrid>;
}
function positiveAmount(value: string) {
  if (!/^\d+(?:\.\d+)?$/.test(value.trim()) || !capitalIsPositive(value.trim())) throw new Error("Enter a positive decimal amount without separators or exponents.");
  return value.trim();
}
function date(value: string) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) throw new Error("Complete the required dates.");
  return value;
}
function required(value: string, label: string) {
  if (!value.trim()) throw new Error(`${label} is required.`);
  return value.trim();
}
const evidenceLinks = (text: string) => text.split(/\r?\n/).map(value => value.trim()).filter(Boolean);

export function CapitalCommitmentForm({ bookId, locked, mutate }: { bookId: string; locked: boolean; mutate: CapitalMutation }) {
  const [values, setValues] = useState(() => fieldsForm(commitmentFields, { expectedRevision: "0" }));
  const [evidence, setEvidence] = useState("");
  const [error, setError] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const id = useId();
  const update = (key: string, value: string) => { setValues(current => ({ ...current, [key]: value })); setConfirmed(false); setError(""); };
  async function submit() {
    setError("");
    try {
      const expectedRevision = Number(values.expectedRevision);
      if (!/^\d+$/.test(values.expectedRevision) || !Number.isSafeInteger(expectedRevision)) throw new Error("Expected revision must be zero for a new commitment, or the retained revision when amending.");
      const retainedEvidence = evidenceLinks(evidence);
      if (!retainedEvidence.length) throw new Error("Retained commitment evidence is required.");
      const request = {
        commitmentId: required(values.commitmentId, "Commitment ID"), investorId: required(values.investorId, "Investor ID"),
        capitalAccountId: required(values.capitalAccountId, "Capital account ID"), totalCommitment: positiveAmount(values.totalCommitment),
        commitmentDate: date(values.commitmentDate), effectiveFrom: date(values.effectiveFrom),
        investmentPeriodEndDate: values.investmentPeriodEndDate || null, evidenceLinks: retainedEvidence, expectedRevision, isActive: true
      };
      if (!confirmed) throw new Error("Confirm the terms and evidence before retaining a draft revision.");
      await mutate("Commitment draft retention", signal => api.createPrivateCapitalCommitment(bookId, request, { signal }));
    } catch (cause) { setError(describeApiError(cause, "Commitment could not be retained.").summary); }
  }
  return <Card><CardHeader><CardTitle>Prepare commitment revision</CardTitle><CardDescription>Retain source terms for review. Revision zero creates a new commitment; an amendment names the current revision. Retaining a draft does not call capital or post a journal.</CardDescription></CardHeader><CardContent>
    <form onSubmit={event => { event.preventDefault(); void submit(); }}><fieldset disabled={locked} className="space-y-3"><legend className="sr-only">Commitment terms</legend>
      <Fields fields={commitmentFields} values={values} update={update} />
      <FormRow label="Retained commitment evidence (one reference per line)" labelFor={`${id}-evidence`}><TextArea rows={2} id={`${id}-evidence`} className="min-h-20 w-full text-sm"
        value={evidence} onChange={event => { setEvidence(event.target.value); setConfirmed(false); }} /></FormRow>
      <Checkbox className="flex gap-2 text-sm" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} label={<>I checked the source terms and retained evidence.</>} />
      <Button type="submit" disabled={locked || !confirmed} disabledReason={locked ? "Wait for the current operation or resolve its unconfirmed outcome." : !confirmed ? "Check the source terms and retained evidence first." : undefined}>Retain commitment draft</Button>
    </fieldset></form>{error ? <p role="alert" className="mt-2 text-sm text-danger">{error}</p> : null}
  </CardContent></Card>;
}

export function CapitalCallPlanForm({ bookId, commitments, locked, mutate, onRetained }: {
  bookId: string; commitments: PrivateCapitalCommitment[]; locked: boolean; mutate: CapitalMutation; onRetained: (id: string) => void
}) {
  const [values, setValues] = useState(() => fieldsForm(callFields));
  const [selected, setSelected] = useState<string[]>([]);
  const [preview, setPreview] = useState<{ result: PrivateCapitalCallPreview; request: PreviewPrivateCapitalCall } | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const [confirmed, setConfirmed] = useState(false);
  const controller = useRef<AbortController | null>(null);
  useEffect(() => () => controller.current?.abort(), []);
  const eligible = effectiveCapitalCommitments(commitments, values.noticeDate);
  function invalidate() { controller.current?.abort(); controller.current = null; setLoading(false); setPreview(null); setConfirmed(false); setError(""); }
  async function prepare() {
    invalidate();
    const requestController = new AbortController(); controller.current = requestController; setLoading(true);
    try {
      if (!selected.length) throw new Error("Select at least one approved commitment.");
      if (selected.some(id => !eligible.some(row => row.commitmentId === id))) throw new Error("Commitment eligibility changed. Review the approved commitments for this notice date again.");
      const request: PreviewPrivateCapitalCall = { callId: required(values.callId, "Capital call ID"), amountToCall: positiveAmount(values.amountToCall),
        periodId: required(values.periodId, "Accounting period ID"), noticeDate: date(values.noticeDate), dueDate: date(values.dueDate), commitmentIds: selected, purpose: values.purpose.trim() || null };
      const result = await api.previewPrivateCapitalCall(bookId, request, { signal: requestController.signal });
      if (!requestController.signal.aborted) setPreview({ result, request });
    } catch (cause) { if (!requestController.signal.aborted) setError(describeApiError(cause, "Call allocation could not be prepared.").summary);
    } finally { if (controller.current === requestController) { controller.current = null; setLoading(false); } }
  }
  return <Card><CardHeader><CardTitle>Prepare capital call</CardTitle><CardDescription>The server allocates pro rata by uncalled commitment using retained approved terms and posted activity. The notice date selects the effective revision. Review the allocation before retaining the call.</CardDescription></CardHeader><CardContent className="space-y-3">
    <form onSubmit={event => { event.preventDefault(); void prepare(); }}><fieldset disabled={locked} className="space-y-3"><legend className="sr-only">Call terms and approved commitments</legend>
      <Fields fields={callFields} values={values} update={(key, value) => { invalidate(); if (key === "noticeDate") setSelected([]); setValues(current => ({ ...current, [key]: value })); }} />
      <fieldset className="space-y-2"><legend className="text-sm font-semibold">Approved commitments</legend>
        {!values.noticeDate ? <p className="text-sm">Enter a notice date to select the effective approved commitment revisions.</p> : !eligible.length ? <p className="text-sm">No active approved commitment revision is effective for this notice date.</p> : eligible.map(row => <Checkbox key={row.commitmentId} className="flex flex-wrap items-center gap-2 text-sm" checked={selected.includes(row.commitmentId)} onChange={event => { invalidate(); setSelected(current => event.target.checked ? [...new Set([...current, row.commitmentId])] : current.filter(value => value !== row.commitmentId)); }} label={<>

          {row.commitmentId} · {row.investorId} · v{row.revision} · Uncalled {capitalAmount(row.uncalledAmount, row.scope.currency)}
        </>} />)}
      </fieldset><Button type="submit" variant="outline" disabled={loading || !eligible.length}>{loading ? "Preparing allocation…" : "Preview call allocation"}</Button>
    </fieldset></form>
    {error ? <p role="alert" className="text-sm text-danger">{error}</p> : null}
    {preview ? <section className="space-y-3" aria-label="Authoritative call preview">
      {preview.result.blockers.length ? <ul role="alert" className="list-disc pl-5 text-sm text-danger">{preview.result.blockers.map(reason => <li key={reason}>{reason}</li>)}</ul> : null}
      {preview.result.call ? <><CapitalCallLines call={preview.result.call} /><p className="break-all text-xs text-muted-foreground">Plan {preview.result.call.planHash}</p></> : null}
      {preview.result.isReady && preview.result.call ? <fieldset disabled={locked} className="space-y-2"><legend className="sr-only">Call allocation review</legend>
        <Checkbox className="flex gap-2 text-sm" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} label={<>I reviewed each allocation and the uncalled balances.</>} />
        <Button disabled={locked || !confirmed} disabledReason={locked ? "Wait for the current operation or resolve its unconfirmed outcome." : !confirmed ? "Review and confirm each allocation and uncalled balance first." : undefined} onClick={() => { const reviewed = preview; void mutate("Call plan retention", async signal => {
          const call = await api.retainPrivateCapitalCall(bookId, reviewed.request, reviewed.result.call!.planHash, { signal }); onRetained(call.callId); return call;
        }); }}>Retain reviewed call</Button>
      </fieldset> : null}
    </section> : null}
  </CardContent></Card>;
}

const fundingFields: Field[] = [["fundingReceiptId", "Funding receipt ID"], ["bankLineId", "Bank transaction line ID"], ["fundedAmount", "Cash received"], ["receivedDate", "Cash received date", "date"]];
export function CapitalFundingForm({ call, locked, mutate }: { call: PrivateCapitalCall; locked: boolean; mutate: CapitalMutation }) {
  const [values, setValues] = useState(() => fieldsForm(fundingFields));
  const [selected, setSelected] = useState("");
  const [evidence, setEvidence] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const [error, setError] = useState("");
  const id = useId();
  const eligible = call.lines.filter(line => capitalIsPositive(line.outstandingAmount) && capitalIsPositive(line.issuedAmount));
  async function submit() {
    setError("");
    try {
      const line = eligible.find(row => row.installmentId === selected);
      if (!line) throw new Error("Select a posted installment with an outstanding receivable.");
      const evidenceReferences = evidenceLinks(evidence);
      if (!evidenceReferences.length) throw new Error("Retained bank or remittance evidence is required.");
      if (!confirmed) throw new Error("Review the receipt identity, amount and evidence before preparing a funding draft.");
      const request = { fundProfileId: call.scope.fundProfileId, currency: call.scope.currency, callId: call.callId,
        ledgerBookId: call.scope.ledgerBookId, periodId: call.periodId, entityId: call.scope.entityId, receivedDate: date(values.receivedDate),
        fundings: [{ commitmentId: line.commitmentId, capitalAccountId: line.capitalAccountId, investorId: line.investorId,
          fundedAmount: positiveAmount(values.fundedAmount), fundingReceiptId: required(values.fundingReceiptId, "Funding receipt ID"),
          bankLineId: required(values.bankLineId, "Bank transaction line ID"), evidenceLinks: evidenceReferences }] };
      await mutate("Funding draft intake", signal => api.intakePrivateCapitalFunding(request, { signal }));
    } catch (cause) { setError(describeApiError(cause, "Funding could not be prepared.").summary); }
  }
  return <section className="space-y-3 rounded border border-border p-3" aria-label="Funding receipt preparation">
    <h3 className="text-sm font-semibold">Record cash receipt</h3><p className="text-sm text-muted-foreground">Use one identity per actual receipt and bank line. Partial receipts remain separate; only posted funding reduces the receivable. Funding uses the call's accounting period; a closed period blocks the workflow. Receipts for another accounting period are unavailable here.</p>
    <CapitalBankObservationImport scope={call.scope} locked={locked} mutate={mutate} onImported={result => {
      setValues(current => ({ ...current, bankLineId: result.bankTransaction.bankTransactionId,
        fundedAmount: String(result.bankTransaction.amount), receivedDate: result.bankTransaction.effectiveDate }));
      setEvidence(result.evidenceLink); setConfirmed(false); setError("");
    }} />
    {!eligible.length ? <p role="status" className="text-sm">No posted outstanding installment is eligible for funding. Inspect issuance approval and posting first.</p> : <form onSubmit={event => { event.preventDefault(); void submit(); }}>
      <fieldset disabled={locked} className="space-y-3"><legend className="sr-only">Cash receipt and evidence</legend>
        <label className="grid gap-1 text-sm">Posted installment<Select value={selected} onChange={event => { setSelected(event.target.value); setConfirmed(false); }}>
          <option value="">Select a posted installment</option>{eligible.map(line => <option key={line.installmentId} value={line.installmentId}>{line.installmentId} · Outstanding {capitalAmount(line.outstandingAmount, call.scope.currency)}</option>)}
        </Select></label><Fields fields={fundingFields} values={values} update={(key, value) => { setValues(current => ({ ...current, [key]: value })); setConfirmed(false); }} />
        <FormRow label="Retained bank or remittance evidence (one reference per line)" labelFor={`${id}-evidence`}><TextArea rows={2} id={`${id}-evidence`} className="min-h-20 w-full text-sm"
          value={evidence} onChange={event => { setEvidence(event.target.value); setConfirmed(false); }} /></FormRow>
        <Checkbox className="flex gap-2 text-sm" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} label={<>I matched this cash receipt to the installment and retained bank evidence.</>} />
        <Button type="submit" disabled={locked || !confirmed} disabledReason={locked ? "Wait for the current operation or resolve its unconfirmed outcome." : !confirmed ? "Match the cash receipt to the installment and retained bank evidence first." : undefined}>Queue funding draft for review</Button>
      </fieldset></form>}
    {error ? <p role="alert" className="text-sm text-danger">{error}</p> : null}
  </section>;
}
