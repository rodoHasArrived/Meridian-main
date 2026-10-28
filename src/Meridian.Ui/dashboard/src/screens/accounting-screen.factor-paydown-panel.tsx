import { useEffect, useId, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { ApiError, describeApiError } from "@/lib/api-errors";
import { createFactorPaydownDraft, prepareFactorPaydown } from "@/lib/api/factor-paydown.api";
import type { AccountingBasisKind } from "@/types";
import type { FactorPaydownDraftResult, FactorPaydownPreview, FactorPaydownRequest } from "@/types/factor-paydown";

interface Props { periodVersion: number | null; bookId: string; periodId: string | null; fundProfileId: string; accountingBasis?: AccountingBasisKind; active?: boolean }
const fields = [
  ["securityId", "Security ID"], ["bookPositionId", "Book position ID"],
  ["effectiveDate", "Paydown effective date", "date"],
  ["priorFactor", "Prior factor"], ["newFactor", "New factor"],
  ["occurredAtUtc", "Factor observed at (UTC)"], ["sourceDomain", "Factor source system"],
  ["sourceEntityId", "Factor source reference"], ["sourceContentHash", "Factor source SHA-256"],
  ["cashAccountPath", "Cash account path"], ["carryingAccountPath", "Carrying account path"],
  ["realizedGainAccountPath", "Realized gain account path"], ["realizedLossAccountPath", "Realized loss account path"]
] as const;
type Field = typeof fields[number][0];
type Form = Record<Field, string>;
const componentLabels: Record<string, string> = {
  "lot.principal": "Principal repaid", "lot.basisRelieved": "Carrying basis relieved",
  "lot.realizedGain": "Realized gain", "lot.realizedLoss": "Realized loss"
};
const emptyForm = Object.fromEntries(fields.map(([field]) => [field, ""])) as Form;

/** A fresh selection requires fresh authority; no preview survives a book/period/tab round trip. */
export function FactorPaydownPanel(props: Props) {
  if (props.active === false) return null;
  return <FactorPaydownScopePanel key={`${props.bookId}:${props.periodId}:${props.fundProfileId}:${props.accountingBasis}:${props.periodVersion}`} {...props} />;
}

function FactorPaydownScopePanel({ bookId, periodId, periodVersion, fundProfileId, accountingBasis = "Primary" }: Props) {
  const id = useId();
  const [form, setForm] = useState<Form>(emptyForm);
  const [evidence, setEvidence] = useState("");
  const [description, setDescription] = useState("");
  const [preview, setPreview] = useState<{ result: FactorPaydownPreview; request: FactorPaydownRequest } | null>(null);
  const [draft, setDraft] = useState<FactorPaydownDraftResult | null>(null);
  const [confirmed, setConfirmed] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState<"prepare" | "draft" | null>(null);
  const [uncertain, setUncertain] = useState(false);
  const controller = useRef<AbortController | null>(null);
  useEffect(() => () => controller.current?.abort(), []);

  function invalidate() {
    controller.current?.abort();
    controller.current = null;
    setBusy(null); setPreview(null); setConfirmed(false); setError(""); setDraft(null);
    setMessage("Inputs changed. Prepare again before creating a review draft.");
  }
  function update(field: Field, value: string) {
    invalidate(); setForm((current) => ({ ...current, [field]: value }));
  }
  function buildRequest(): FactorPaydownRequest {
    if (!bookId || !periodId || periodVersion === null || !fundProfileId) throw new Error("Select an authoritative ledger book and period first.");
    if (!Object.values(form).every((value) => value.trim())) throw new Error("Complete all factor observation and account fields.");
    if (!Number.isSafeInteger(periodVersion) || periodVersion < 0) {
      throw new Error("The selected period must supply a supported authoritative version. Refresh the ledger selection.");
    }
    if (!/^\d+(?:\.\d+)?$/.test(form.priorFactor) || !/^\d+(?:\.\d+)?$/.test(form.newFactor)) {
      throw new Error("Enter exact nonnegative decimal factors, without separators or exponents.");
    }
    const retained: unknown = JSON.parse(evidence);
    if (!Array.isArray(retained) || retained.length === 0 || retained.some((item) => !item || typeof item !== "object" || Array.isArray(item))) {
      throw new Error("Supply a nonempty JSON array of retained factor evidence identities.");
    }
    return { ...form, ledgerBookId: bookId, periodId, fundProfileId, accountingBasis,
      expectedPeriodVersion: periodVersion, factorEvidence: retained };
  }
  async function prepare() {
    if (controller.current || uncertain) return;
    setError(""); setPreview(null); setDraft(null); setConfirmed(false);
    const requestController = new AbortController(); controller.current = requestController; setBusy("prepare");
    try {
      const request = buildRequest();
      const result = await prepareFactorPaydown(request, { signal: requestController.signal });
      if (!requestController.signal.aborted) { setPreview({ result, request }); setMessage("Review the authoritative lot allocation before creating a draft."); }
    } catch (cause) {
      if (!requestController.signal.aborted) setError(describeApiError(cause, "Factor paydown request failed.").summary);
    } finally {
      if (controller.current === requestController) { controller.current = null; setBusy(null); }
    }
  }
  async function createDraft() {
    if (!preview || !confirmed || !description.trim() || controller.current || draft) return;
    const requestController = new AbortController(); controller.current = requestController; setBusy("draft"); setError("");
    try {
      const result = await createFactorPaydownDraft(preview.request, preview.result.preparationFingerprint,
        description.trim(), { signal: requestController.signal });
      if (!requestController.signal.aborted) {
        setDraft(result); setUncertain(false);
        setMessage(result.candidate.journalEntryId ? "Review draft retained. Submit and approve it through the existing journal workflow. No posting was requested."
          : "The server did not retain an eligible draft. Inspect its blockers before preparing again.");
      }
    } catch (cause) {
      if (!requestController.signal.aborted) {
        setError(describeApiError(cause, "Factor paydown request failed.").summary);
        if (cause instanceof ApiError && cause.status === 409 && cause.responseBody?.includes('"FACTOR_PAYDOWN_PREVIEW_STALE"')) {
          setUncertain(false); setPreview(null); setConfirmed(false);
          setMessage("The reviewed preparation changed. Prepare and review the current lots again.");
        } else {
          setUncertain(true);
          setMessage("Draft creation was not confirmed. Keep this reviewed request and retry it; do not replace it with a different paydown.");
        }
      }
    } finally {
      if (controller.current === requestController) { controller.current = null; setBusy(null); }
    }
  }
  const locked = busy === "draft" || uncertain || !!draft?.candidate.journalEntryId;
  return <Card className="panel-surface min-w-0" role="region" aria-label="Factor paydown">
    <CardHeader><CardTitle>Factor paydown</CardTitle><CardDescription>Prepare principal repayment and lot basis relief from a retained factor observation. Same-currency active long positions only; drafts require the existing journal review and approval workflow.</CardDescription></CardHeader>
    <CardContent className="space-y-4">
      {!bookId || !periodId || periodVersion === null || !fundProfileId ? <p role="status">Select a ledger book and period to prepare a factor paydown.</p> : <>
        <p className="break-all text-xs text-muted-foreground">Book {bookId} · Period {periodId} · {accountingBasis}</p>
        <form onSubmit={(event) => { event.preventDefault(); void prepare(); }}>
          <fieldset disabled={locked} className="space-y-3"><legend className="sr-only">Factor observation and posting accounts</legend>
            <FormGrid columns={3}>{fields.map(([field, label, type]) => <FormRow key={field} label={label} labelFor={`${id}-${field}`}>
              <Input id={`${id}-${field}`} value={form[field]} type={type ?? "text"} onChange={(event) => update(field, event.target.value)} />
            </FormRow>)}</FormGrid>
            <FormRow label="Retained factor evidence JSON" labelFor={`${id}-evidence`} hint="Paste existing retained evidence identities, including review and subject bindings. This form does not grant evidence authority.">
              <textarea id={`${id}-evidence`} className="min-h-24 w-full rounded border border-border bg-background p-2 font-mono text-xs" value={evidence}
                onChange={(event) => { invalidate(); setEvidence(event.target.value); }} />
            </FormRow>
            <Button type="submit" disabled={busy !== null}>{busy === "prepare" ? "Preparing paydown…" : "Prepare factor paydown"}</Button>
          </fieldset>
        </form>
        {preview ? <section aria-label="Factor-paydown preview" className="space-y-3">
          <h3 className="text-sm font-semibold">Authoritative paydown preview</h3>
          <dl className="grid grid-cols-2 gap-2 text-sm">{Object.entries(preview.result.components).map(([key, amount]) => <div key={key}><dt className="text-muted-foreground">{componentLabels[key] ?? key}</dt><dd className="font-mono">{String(amount)} {preview.result.instruction.currency}</dd></div>)}</dl>
          <div className="overflow-x-auto"><table className="w-full text-left text-xs"><caption className="sr-only">Lot principal and remaining basis</caption><thead><tr><th scope="col">Lot</th><th scope="col">Principal repaid</th><th scope="col">Transaction basis after</th><th scope="col">Functional basis after</th></tr></thead><tbody>{preview.result.instruction.lots.map((lot) => <tr key={lot.expectedLot.lotId}><th scope="row" className="py-2 font-mono">{lot.expectedLot.lotId}</th><td>{String(lot.principal)}</td><td>{String(lot.transactionBasisAfter)}</td><td>{String(lot.functionalBasisAfter)}</td></tr>)}</tbody></table></div>
          <fieldset disabled={locked} className="space-y-3"><legend className="sr-only">Review draft confirmation</legend>
            <FormRow label="Paydown journal description" labelFor={`${id}-description`}><Input id={`${id}-description`} value={description} onChange={(event) => { setDescription(event.target.value); setConfirmed(false); }} /></FormRow>
            <label className="flex items-start gap-2 text-sm"><input type="checkbox" checked={confirmed} onChange={(event) => setConfirmed(event.target.checked)} />I reviewed the factor evidence, lot allocation and posting accounts.</label>
          </fieldset>
          <Button type="button" disabled={busy !== null || !confirmed || !description.trim() || !!draft} onClick={() => void createDraft()}>{busy === "draft" ? "Creating review draft…" : uncertain ? "Retry same review draft" : "Create paydown review draft"}</Button>
        </section> : null}
      </>}
      {message ? <p role="status" aria-live="polite" className="text-sm text-muted-foreground">{message}</p> : null}
      {error ? <p role="alert" className="text-sm text-danger">{error}</p> : null}
      {draft?.candidate.issues?.length ? <ul aria-label="Paydown draft blockers" className="list-disc pl-5 text-sm">{draft.candidate.issues.map((issue, index) => <li key={index}>{issue.message}</li>)}</ul> : null}
      {draft?.candidate.journalEntryId ? <div className="space-y-2 text-sm"><p>Review draft: <span className="font-mono">{draft.candidate.journalEntryId}</span></p><Link className="text-primary underline" to={`/accounting/journal-entries?${new URLSearchParams({ fundProfileId, ledgerBookId: bookId, periodId: periodId ?? "", journalEntryId: draft.candidate.journalEntryId })}`}>Open paydown journal approval workflow</Link></div> : null}
    </CardContent>
  </Card>;
}
