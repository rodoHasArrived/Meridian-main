import { Link } from "react-router-dom";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { formatTaxDecimal } from "@/lib/ledger-tax-results-format";
import type { BookReceiptQueueItem } from "@/types/income-receipt-queue.types";

export function receiptAmount(value: string, currency: string): string {
  return `${formatTaxDecimal(value)} ${currency}`;
}

/** Only navigable web evidence becomes a link. Retained opaque identities remain copyable text. */
export function ReceiptEvidenceLink({ source }: { source: string }) {
  const internal = source.startsWith("/") && !source.startsWith("//") && !source.includes("\\");
  const external = /^https?:\/\//i.test(source);
  // API evidence is a retained server document, not a client-side application route.
  if (internal && !source.startsWith("/api/")) return <Link className="text-primary underline break-all" to={source}>{source}</Link>;
  if (internal || external) return <a className="text-primary underline break-all" href={source} target="_blank" rel="noopener noreferrer">{source}</a>;
  return <span className="break-all">{source}</span>;
}

function Sources({ links }: { links: string[] }) {
  return links.length ? <ul className="space-y-1">{[...new Set(links)].map(source => <li key={source}><ReceiptEvidenceLink source={source} /></li>)}</ul> : <p className="text-muted-foreground">No source link retained.</p>;
}

export function IncomeReceiptDetails({ item, bookId, id }: { item: BookReceiptQueueItem; bookId: string; id: string }) {
  const exceptionQuery = new URLSearchParams({ ledgerBookId: bookId, reconciliationBreak: item.exceptionId ?? "" });
  return <section id={id} aria-label={`Receipt evidence for ${item.displayName}`} className="space-y-3 rounded-[var(--radius-card)] border border-border p-4 text-sm">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div><h4 className="font-semibold">{item.displayName} · {item.flowType}</h4><p className="text-xs text-muted-foreground">Due {item.dueDate} · {item.agingDays} days after due · Owner: {item.owner || "Unassigned"}</p></div>
      {item.exceptionId ? <Link className="text-primary underline" to={`/accounting/reconciliation?${exceptionQuery}`}>Open linked exception</Link> : null}
    </div>
    <dl className="grid grid-cols-2 gap-3 xl:grid-cols-4">
      {([["Expected", item.expectedAmount], ["Received", item.receivedAmount], ["Posted", item.postedAmount], ["Remaining", item.remainingAmount]] as const).map(([label, amount]) => <div key={label}><dt className="text-xs text-muted-foreground">{label}</dt><dd className="font-mono text-sm tabular-nums break-all">{receiptAmount(amount, item.currency)}</dd></div>)}
    </dl>
    {!item.evidenceComplete ? <div role="status"><p className="font-medium">Incomplete evidence</p><ul className="list-disc pl-5">{item.evidenceIssues.length ? item.evidenceIssues.map(issue => <li key={issue}>{issue}</li>) : <li>Required source coverage is incomplete.</li>}</ul></div> : null}
    <TechnicalDetails label={`Expectation history (${item.expectationHistory.length})`} description="Each version retains the holding quantity, accepted projection and terms used when it was first observed.">
      <ol className="space-y-4">{item.expectationHistory.map(version => <li key={version.versionId} className="space-y-2">
        <p className="font-medium">Observed {version.firstObservedDate} · {receiptAmount(version.expectation.expectedAmount, version.expectation.currency)}</p>
        <dl className="grid gap-2 text-xs sm:grid-cols-2">
          {Object.entries({ "Version": version.versionId, "Holding quantity": formatTaxDecimal(version.expectation.quantity), "Position": version.expectation.positionId, "Security": version.expectation.securityId, "Due date": version.expectation.dueDate, "Terms version": version.expectation.termsVersionId, "Terms hash": version.expectation.termsHash, "Accepted projection": version.expectation.projectionRunId, "Projected cash flow": version.expectation.projectedCashFlowId }).map(([label, value]) => <div key={label}><dt className="text-muted-foreground">{label}</dt><dd className="break-all">{value}</dd></div>)}
        </dl>
        <Sources links={version.expectation.evidenceLinks} />
        {version.expectation.retainedEvidence.map(evidence => <TechnicalDetails key={evidence.evidenceId} label={`Retained evidence · ${evidence.sourceSystem}`}>
          <dl className="grid gap-2 text-xs sm:grid-cols-2">{Object.entries(evidence).map(([label, value]) => <div key={label}><dt className="text-muted-foreground">{label}</dt><dd className="break-all">{String(value)}</dd></div>)}</dl>
          <ReceiptEvidenceLink source={evidence.evidenceUri} />
        </TechnicalDetails>)}
      </li>)}</ol>
    </TechnicalDetails>
    <TechnicalDetails label={`Cash receipts (${item.receipts.length})`} description="Cash evidence and its split allocations are separate from posted journals. Reversal records reference the original receipt.">
      {item.receipts.length ? <ol className="space-y-4">{item.receipts.map(receipt => <li key={receipt.receiptId} className="space-y-1">
        <p className="font-medium">{receiptAmount(receipt.amount, receipt.currency)} · {receipt.effectiveDate}</p>
        <p className="break-all">Receipt {receipt.receiptId} · {receipt.sourceSystem} · {receipt.sourceReference}</p>
        {receipt.reversesReceiptId ? <p>Reverses receipt {receipt.reversesReceiptId}</p> : null}
        <ul className="list-disc pl-5">{receipt.allocations.map((allocation, index) => <li key={`${allocation.obligationKey}:${index}`} className="break-all">{allocation.obligationKey}: {receiptAmount(allocation.amount, receipt.currency)}</li>)}</ul>
        <Sources links={receipt.evidenceLinks} />
      </li>)}</ol> : <p>No retained cash receipts.</p>}
    </TechnicalDetails>
    <TechnicalDetails label={`Posted journals (${item.postings.length})`}>
      {item.postings.length ? <ol className="space-y-4">{item.postings.map((posting, index) => <li key={`${posting.postingId}:${index}`} className="space-y-1">
        <p className="font-medium">{receiptAmount(posting.amount, posting.currency)} · {posting.effectiveDate}{posting.isReversed ? " · Reversed" : ""}</p>
        <p className="break-all">Journal {posting.journalEntryId} · Receipt {posting.receiptId} · Posting {posting.postingId}</p>
        {posting.journalCashAmount != null ? <p>Total journal cash: {receiptAmount(posting.journalCashAmount, posting.currency)}</p> : null}
        <Sources links={posting.evidenceLinks} />
      </li>)}</ol> : <p>No retained posted journals.</p>}
    </TechnicalDetails>
    <TechnicalDetails label="Source links and obligation identity"><p className="mb-2 break-all">{item.obligationKey}</p><Sources links={item.evidenceLinks} /></TechnicalDetails>
  </section>;
}
