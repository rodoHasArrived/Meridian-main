import { Link } from "react-router-dom";
import { formatMoney } from "@/components/accounting/money";
import { Badge } from "@/components/ui/badge";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type { ConsolidationSource, ConsolidationView } from "@/types/consolidation";

const cell = "border-b border-border/60 px-3 py-2 text-left align-top";
const amountCell = `${cell} whitespace-nowrap text-right font-mono tabular-nums`;
const money = (value: number) => formatMoney(value, { parens: true });

function SourceDetails({ sources, currency, label }: { sources: ConsolidationSource[]; currency: string; label: string }) {
  return (
    <TechnicalDetails label={`${label} (${sources.length})`}>
      {sources.length === 0 ? <p className="text-xs text-muted-foreground">No retained source lines.</p> : (
        <ul className="space-y-3 text-xs">
          {sources.map((source) => (
            <li key={`${source.ledgerBookId}:${source.journalEntryId}:${source.lineId}`} className="space-y-1 break-all">
              <p className="font-medium">{source.accountPath} · {source.effectiveDate}</p>
              <p>Posting entity: {source.entityId} · Counterparty: {source.counterpartyId || "Not supplied"}</p>
              <p className="font-mono">Debit {money(source.debit)} {currency} · Credit {money(source.credit)} {currency}</p>
              <dl className="grid gap-1 text-muted-foreground">
                <div><dt className="inline font-medium">Journal: </dt><dd className="inline font-mono">{source.journalEntryId}</dd></div>
                <div><dt className="inline font-medium">Book: </dt><dd className="inline font-mono">{source.ledgerBookId}</dd></div>
                <div><dt className="inline font-medium">Line: </dt><dd className="inline font-mono">{source.lineId}</dd></div>
              </dl>
            </li>
          ))}
        </ul>
      )}
    </TechnicalDetails>
  );
}

/** Renders server amounts verbatim; pending drafts never enter posted consolidated balances. */
export function ConsolidationResults({ view }: { view: ConsolidationView }) {
  const reviewQuery = new URLSearchParams({ fundProfileId: view.fundProfileId, ledgerBookId: view.request.eliminationBookId });
  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center gap-2">
        <Badge variant="outline">{view.currency || "Currency unresolved"}</Badge>
        <Badge variant="outline">Primary basis</Badge>
        <span className="text-xs text-muted-foreground">Ownership effective {view.request.asOf} · {view.entityIds.length} entities</span>
      </div>
      <p className="text-sm text-muted-foreground">{view.scopeLimitation}</p>
      {view.blockers.length > 0 ? (
        <div role="alert" className="rounded border border-warning/40 bg-warning/10 p-3 text-sm">
          <p className="font-semibold">Elimination drafts are blocked</p>
          <ul className="mt-1 list-disc pl-5">{view.blockers.map((blocker) => <li key={blocker}>{blocker}</li>)}</ul>
        </div>
      ) : null}
      <div>
        <h3 className="text-sm font-semibold">Group balances</h3>
        <p className="my-2 text-xs leading-5 text-muted-foreground">
          Consolidated balances include posted eliminations only. Proposed eliminations and the resulting preview remain unposted until journal approval and posting.
        </p>
        <div className="overflow-x-auto" role="region" aria-label="Group balance table" tabIndex={0}>
          <table className="w-full text-xs">
            <caption className="sr-only">Group balances in {view.currency}; proposed and posted eliminations are separate.</caption>
            <thead><tr>{["Account", "Gross", "Proposed eliminations", "Posted eliminations", "Consolidated", "Preview if posted", "Source evidence"].map((label) => <th scope="col" className={cell} key={label}>{label}</th>)}</tr></thead>
            <tbody>{view.balances.map((balance) => (
              <tr key={`${balance.accountPath}:${balance.accountType}`}>
                <th scope="row" className={cell}><span className="block">{balance.accountPath}</span><span className="font-normal text-muted-foreground">{balance.accountType}</span></th>
                {[balance.grossBalance, balance.proposedEliminations, balance.postedEliminations, balance.consolidatedBalance, balance.previewBalance].map((value, index) => <td key={index} className={amountCell}>{money(value)}</td>)}
                <td className={`${cell} min-w-64`}><SourceDetails sources={balance.sources} currency={view.currency} label={`Sources for ${balance.accountPath}`} /></td>
              </tr>
            ))}</tbody>
          </table>
        </div>
        {view.balances.length === 0 ? <p className="py-3 text-sm text-muted-foreground">No source balances in this perimeter and period.</p> : null}
      </div>
      <div>
        <h3 className="text-sm font-semibold">Reciprocal matches and unmatched differences</h3>
        <p className="my-2 text-xs leading-5 text-muted-foreground">Receivables are matched to the counterparty’s reciprocal payable. Unmatched amounts remain in the group balances.</p>
        <div className="overflow-x-auto" role="region" aria-label="Reciprocal match table" tabIndex={0}>
          <table className="w-full text-xs">
            <caption className="sr-only">Reciprocal balances and retained unmatched differences in {view.currency}</caption>
            <thead><tr>{["Posting entity → counterparty", "Receivable", "Reciprocal payable", "Matched", "Unmatched receivable", "Unmatched payable", "Source evidence"].map((label) => <th scope="col" className={cell} key={label}>{label}</th>)}</tr></thead>
            <tbody>{view.matches.map((match) => (
              <tr key={`${match.postingEntityId}:${match.counterpartyId}`}>
                <th scope="row" className={`${cell} break-all`}>{match.postingEntityId} → {match.counterpartyId}</th>
                {[match.receivable, match.payable, match.matchedAmount, match.unmatchedReceivable, match.unmatchedPayable].map((value, index) => <td key={index} className={amountCell}>{money(value)}</td>)}
                <td className={`${cell} min-w-64`}><SourceDetails sources={match.sources} currency={view.currency} label={`Sources for ${match.postingEntityId} to ${match.counterpartyId}`} /></td>
              </tr>
            ))}</tbody>
          </table>
        </div>
        {view.matches.length === 0 ? <p className="py-3 text-sm text-muted-foreground">No reciprocal intercompany balances found.</p> : null}
      </div>
      <div>
        <h3 className="text-sm font-semibold">Elimination journal review</h3>
        <p className="my-2 text-xs leading-5 text-muted-foreground">Refresh the preview after source changes or journal posting. Changed sources require renewed review; reruns retain journal history and correction links.</p>
        <Link className="text-sm font-medium text-primary underline underline-offset-4" to={`${WORKSTATION_ROUTE_CATALOG.accountingJournalEntries}?${reviewQuery}`}>Open journal approval workflow</Link>
        <ul className="mt-3 space-y-3">
          {view.drafts.map((draft) => (
            <li key={draft.journalEntryId} className="rounded border border-border p-3 text-xs">
              <div className="flex flex-wrap items-center gap-2"><span className="break-all font-mono">{draft.journalEntryId}</span><Badge variant="outline">{draft.status}</Badge>{draft.requiresRenewedReview ? <Badge variant="warning">Renewed review required</Badge> : null}</div>
              {draft.adjustsJournalEntryId ? <p className="mt-2 break-all">Adjusts journal <span className="font-mono">{draft.adjustsJournalEntryId}</span></p> : null}
              <TechnicalDetails className="mt-2" label={`Journal lines for ${draft.journalEntryId}`}>
                <ul className="space-y-2">{draft.lines.map((line) => <li key={line.lineId} className="break-all">{line.side} {money(line.amount)} {line.currency} · {line.accountPath} · Entity {line.entityId || "Not supplied"}</li>)}</ul>
              </TechnicalDetails>
            </li>
          ))}
        </ul>
        {view.drafts.length === 0 ? <p className="mt-3 text-sm text-muted-foreground">No elimination journals retained for this scope. Preview amounts have not been posted.</p> : null}
      </div>
      <TechnicalDetails label="Perimeter and source audit details">
        <dl className="space-y-2 break-all text-xs">
          <div><dt className="font-semibold">Entities</dt><dd>{view.entityIds.join(", ") || "Unresolved"}</dd></div>
          <div><dt className="font-semibold">Effective ownership links</dt><dd>{view.ownershipLinkIds.join(", ") || "Unresolved"}</dd></div>
          <div><dt className="font-semibold">Source fingerprint</dt><dd className="font-mono">{view.sourceFingerprint}</dd></div>
        </dl>
      </TechnicalDetails>
    </div>
  );
}
