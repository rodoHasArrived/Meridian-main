import { useEffect, useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { describeApiError } from "@/lib/api-errors";
import { previewUnitRegister, type UnitRegisterPreview } from "@/lib/api/private-capital-unit-register.api";
import { getLedgerBooks, getLedgerPeriods } from "@/lib/ledger-reports-api";
import { formatTaxDecimal as formatDecimalText } from "@/lib/ledger-tax-results-format";
import type { LedgerBook, LedgerPeriod } from "@/types";

export function UnitRegisterPanel() {
  const [open, setOpen] = useState(false);
  const sectionId = useId();
  return <section className="panel-surface space-y-4 p-4" aria-label="Unit register reconciliation">
    <div className="flex flex-wrap items-center justify-between gap-3">
      <h3 className="workspace-section-title">Unit register reconciliation</h3>
      <Button variant="outline" aria-expanded={open} aria-controls={sectionId} onClick={() => setOpen(value => !value)}>
        {open ? "Hide reconciliation" : "Review unit register"}
      </Button>
    </div>
    <div id={sectionId} hidden={!open}>{open ? <UnitRegisterForm /> : null}</div>
  </section>;
}

function UnitRegisterForm() {
  const fields = useId();
  const [books, setBooks] = useState<LedgerBook[] | null>(null);
  const [periods, setPeriods] = useState<LedgerPeriod[] | null>(null);
  const [bookId, setBookId] = useState("");
  const [periodId, setPeriodId] = useState("");
  const [evidenceLink, setEvidenceLink] = useState("");
  const [documentId, setDocumentId] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [preview, setPreview] = useState<UnitRegisterPreview | null>(null);
  const [busy, setBusy] = useState(false);
  const active = useRef<AbortController | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    void getLedgerBooks({ signal: controller.signal, allowDevelopmentFallback: false }).then(rows => {
      if (controller.signal.aborted) return;
      setBooks(rows);
      if (rows.length === 1) setBookId(rows[0].ledgerBookId);
    }).catch(failure => {
      if (!controller.signal.aborted) setError(describeApiError(failure, "Ledger books are unavailable.").summary);
    });
    return () => { controller.abort(); active.current?.abort(); };
  }, []);

  useEffect(() => {
    if (!bookId) return;
    const controller = new AbortController();
    void getLedgerPeriods({ ledgerBookId: bookId, status: "HardClosed" }, { signal: controller.signal, allowDevelopmentFallback: false })
      .then(rows => {
        if (!controller.signal.aborted) setPeriods(rows.filter(row => row.ledgerBookId === bookId && row.status === "HardClosed"));
      }).catch(failure => {
        if (!controller.signal.aborted) setError(describeApiError(failure, "Closed periods are unavailable.").summary);
      });
    return () => controller.abort();
  }, [bookId]);

  const canSubmit = !!bookId && !!periodId && !!evidenceLink.trim() && !!documentId.trim();
  const submit = async () => {
    if (!canSubmit || active.current) return;
    const controller = new AbortController();
    active.current = controller;
    setBusy(true);
    setError(null);
    setPreview(null);
    try {
      const result = await previewUnitRegister(bookId, { periodId, evidenceLink: evidenceLink.trim(), documentId: documentId.trim() }, controller.signal);
      if (!controller.signal.aborted) setPreview(result);
    } catch (failure) {
      if (!controller.signal.aborted) setError(describeApiError(failure, "The unit register could not be reconciled.").summary);
    } finally {
      active.current = null;
      if (!controller.signal.aborted) setBusy(false);
    }
  };

  return <div className="space-y-4">
    {error ? <StatusBanner tone="danger" role="alert" title="Unit register review requires attention" detail={error} /> : null}
    <form className="space-y-3" onSubmit={event => { event.preventDefault(); void submit(); }}>
      <fieldset className="space-y-3" disabled={busy}>
        <FormGrid columns={2}>
          <FormRow label="Ledger book" labelFor={fields + "-book"}>
            <Select id={fields + "-book"} placeholder={books ? "Select a ledger book" : "Loading ledger books…"} value={bookId}
              onChange={event => { setBookId(event.currentTarget.value); setPeriodId(""); setPeriods(null); setPreview(null); setError(null); }}>
              {books?.map(book => <option key={book.ledgerBookId} value={book.ledgerBookId}>{book.displayName} · {book.baseCurrency}</option>)}
            </Select>
          </FormRow>
          <FormRow label="Hard-closed period" labelFor={fields + "-period"}>
            <Select id={fields + "-period"} placeholder={periods?.length === 0 ? "No hard-closed periods" : "Select a hard-closed period"} value={periodId}
              disabled={!periods?.length} onChange={event => { setPeriodId(event.currentTarget.value); setPreview(null); }}>
              {periods?.map(period => <option key={period.periodId} value={period.periodId}>{period.label}</option>)}
            </Select>
          </FormRow>
          <FormRow label="Accepted Evidence Vault reference" labelFor={fields + "-evidence"}>
            <Input id={fields + "-evidence"} value={evidenceLink} onChange={event => { setEvidenceLink(event.currentTarget.value); setPreview(null); }} />
          </FormRow>
          <FormRow label="Document ID" labelFor={fields + "-document"}>
            <Input id={fields + "-document"} value={documentId} onChange={event => { setDocumentId(event.currentTarget.value); setPreview(null); }} />
          </FormRow>
        </FormGrid>
        <Button type="submit" disabled={!canSubmit || busy}>{busy ? "Reconciling unit register…" : "Preview unit register"}</Button>
      </fieldset>
    </form>
    {preview ? <UnitRegisterResult preview={preview} /> : null}
  </div>;
}

function UnitRegisterResult({ preview }: { preview: UnitRegisterPreview }) {
  return <section className="space-y-3" aria-label="Unit register result">
    <StatusBanner tone={preview.isTied ? "success" : "warning"}
      title={preview.isTied ? "Unit register ties to retained journals" : "Unit register discrepancies require review"}
      detail={`${preview.shareClassId} · ${preview.asOf} · ${preview.currency}`} />
    {preview.issues.length ? <ul className="list-disc space-y-1 pl-5 text-sm">{preview.issues.map((issue, index) =>
      <li key={`${issue.code}-${index}`}>{issue.message}</li>)}</ul> : null}
    <dl className="grid gap-2 sm:grid-cols-3">{[
      ["Units outstanding", preview.unitsOutstanding],
      ["Class NAV", preview.classNav],
      ["NAV per unit", preview.navPerUnit]
    ].map(([label, amount]) => <div key={label} className="rounded border border-border p-2">
      <dt className="text-sm text-muted-foreground">{label}</dt>
      <dd className="font-mono text-sm tabular-nums">{formatDecimalText(amount)}</dd>
    </div>)}</dl>
    <TechnicalDetails label={`Holdings (${preview.holdings.length})`}>
      <ul className="divide-y divide-border text-sm">{preview.holdings.map(holding => <li key={holding.investorId} className="py-2">
        {holding.investorId} · {formatDecimalText(holding.units)} units
      </li>)}</ul>
    </TechnicalDetails>
    <TechnicalDetails label={`Dealing journals (${preview.movements.length})`}>
      <ul className="divide-y divide-border text-sm">{preview.movements.map(movement => <li key={movement.transactionId} className="py-2">
        {movement.dealingDate} · {movement.type} · {movement.investorId} · {formatDecimalText(movement.amount)} {preview.currency}
        <div className="break-all font-mono text-xs">{movement.transactionId}</div>
      </li>)}</ul>
    </TechnicalDetails>
    <TechnicalDetails label="Retained evidence">
      <dl className="break-all text-xs">
        <dt>Register source hash</dt><dd className="font-mono">{preview.sourceHashSha256}</dd>
        <dt>Close snapshot hash</dt><dd className="font-mono">{preview.closeSnapshotHashSha256}</dd>
      </dl>
    </TechnicalDetails>
  </section>;
}
