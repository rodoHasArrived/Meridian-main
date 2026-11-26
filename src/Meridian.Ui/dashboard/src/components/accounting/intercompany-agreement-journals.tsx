import { useEffect, useId, useState } from "react";
import { Button } from "@/components/ui/button";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { DenseDataTable } from "@/components/meridian/ui-kit-primitives";
import { getIntercompanyAgreementJournalLines } from "@/lib/api/intercompany-agreements.api";
import type { AgreementJournalLine, AgreementJournalLines } from "@/types/intercompany-agreements";

export function IntercompanyAgreementJournals({ agreementId, asOf, initialLedgerBookId = "", load = getIntercompanyAgreementJournalLines }: {
  agreementId: string; asOf: string; initialLedgerBookId?: string; load?: typeof getIntercompanyAgreementJournalLines;
}) {
  const id = useId();
  const [book, setBook] = useState(initialLedgerBookId);
  const [query, setQuery] = useState<{ ledgerBookId: string; cursor?: string }>({ ledgerBookId: initialLedgerBookId });
  const [result, setResult] = useState<AgreementJournalLines | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    if (!query.ledgerBookId) return;
    const controller = new AbortController(); setBusy(true); setError(null); setResult(null);
    load(agreementId, { ...query, asOf }, { signal: controller.signal }).then(next => { if (!controller.signal.aborted) setResult(next); })
      .catch(reason => { if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : "Journal coverage is unavailable."); })
      .finally(() => { if (!controller.signal.aborted) setBusy(false); });
    return () => controller.abort();
  }, [agreementId, asOf, load, query]);
  return <TechnicalDetails label="Source journals and advisory agreement coverage">
    <form onSubmit={event => { event.preventDefault(); setQuery({ ledgerBookId: book.trim() }); }} className="mb-3 flex flex-wrap items-end gap-2">
      <FormRow label="Entity ledger book" labelFor={`${id}-book`} className="min-w-[16rem] flex-1"><Input id={`${id}-book`} required value={book} onChange={event => setBook(event.target.value)} /></FormRow>
      <Button type="submit" variant="outline" disabled={busy}>Load source journals</Button>
    </form>
    <p className="mb-3 text-sm text-muted-foreground">Coverage describes retained references as of {asOf}. It does not prove matching, settlement or elimination. Amounts remain in their declared currencies.</p>
    {busy ? <p role="status" className="text-sm">Loading authorized source lines…</p> : null}
    {error ? <StatusBanner role="alert" tone="warning" title="Agreement coverage unknown" detail={error} /> : null}
    {result ? <>
      <p role="status" className="mb-2 text-sm">Coverage: {result.coverageState}{result.lines.length === 0 ? " · No retained source lines returned." : ""}</p>
      {result.lines.length ? <DenseDataTable<AgreementJournalLine> rows={result.lines} getRowId={line => `${line.journalEntryId}:${line.lineId}`}
        ariaLabel="Agreement source journal lines" caption={`Agreement source journal lines in book ${result.ledgerBookId}`} emptyText="No retained source lines returned." columns={[
          { id: "account", label: "Accounting date / account", rowHeader: true, render: line => <>{line.accountingDate}<br />{line.accountName}<br />{line.description}</> },
          { id: "transaction", label: "Transaction debit / credit", render: line => <span className="font-mono tabular-nums">{line.transactionDebit} / {line.transactionCredit} {line.transactionCurrency}</span> },
          { id: "functional", label: "Functional debit / credit", render: line => <span className="font-mono tabular-nums">{line.debit} / {line.credit} {line.functionalCurrency}</span> },
          { id: "coverage", label: "Agreement coverage", render: line => <>{line.coverageState}<br />Version {line.intercompanyAgreementVersion ?? "unreferenced"}{line.warning ? <p className="mt-1 text-warning">{line.warning}</p> : null}</> },
          { id: "source", label: "Source", render: line => <div className="max-w-[22rem]"><TechnicalDetails label="Inspect source journal facts"><dl className="space-y-1 break-all">
            {[["Journal", line.journalEntryId], ["Line", line.lineId], ["Period", line.periodId], ["Sequence", line.sequence], ["Posting entity", line.postingEntityId], ["Counterparty", line.counterpartyId ?? "Missing"], ["Agreement", line.intercompanyAgreementId ?? "Missing"], ["Source reference", line.sourceReference]].map(([label, value]) => <div key={label}><dt className="text-muted-foreground">{label}</dt><dd>{value}</dd></div>)}
          </dl></TechnicalDetails></div> }
        ]} /> : null}
      {result.nextCursor ? <Button variant="outline" disabled={busy} onClick={() => setQuery(current => ({ ...current, cursor: result.nextCursor! }))}>Next source-line page</Button> : null}
    </> : null}
  </TechnicalDetails>;
}
