import { Link } from "react-router-dom";
import { formatMoney } from "@/components/accounting/money";
import { DenseDataTable } from "@/components/meridian/ui-kit-primitives";
import { Badge } from "@/components/ui/badge";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { currencyMinorUnitPrecision } from "@/lib/format";
import type { ConsolidationSource, ConsolidationView } from "@/types/consolidation";

const cell = "border-b border-border/60 px-3 py-2 text-left align-top";
const amountCell = `${cell} whitespace-nowrap text-right font-mono tabular-nums`;
const money = (value: number, currency: string) => formatMoney(value, { parens: true, decimals: currencyMinorUnitPrecision(currency) });

function SourceDetails({ sources, currency, label }: { sources: ConsolidationSource[]; currency: string; label: string }) {
  return (
    <TechnicalDetails label={`${label} (${sources.length})`}>
      {sources.length === 0 ? <p className="text-xs text-muted-foreground">No retained source lines.</p> : (
        <ul className="space-y-3 text-xs">
          {sources.map((source) => (
            <li key={`${source.ledgerBookId}:${source.journalEntryId}:${source.lineId}`} className="space-y-1 break-all">
              <p className="font-medium">{source.accountPath} · {source.effectiveDate}</p>
              <p>Posting entity: {source.entityId} · Counterparty: {source.counterpartyId || "Not supplied"}</p>
              {source.counterpartyDisposition === "FormerPerimeterExternal" ? <p>Former perimeter · external balance, not eliminated</p> : null}
              <p className="font-mono">Debit {money(source.debit, currency)} {currency} · Credit {money(source.credit, currency)} {currency}</p>
              {source.transactionCurrency && source.transactionDebit != null && source.transactionCredit != null ? <p className="font-mono">Original transaction: debit {money(source.transactionDebit, source.transactionCurrency)} · credit {money(source.transactionCredit, source.transactionCurrency)} {source.transactionCurrency}</p> : null}
              <dl className="grid gap-1 text-muted-foreground">
                <div><dt className="inline font-medium">Journal: </dt><dd className="inline font-mono">{source.journalEntryId}</dd></div>
                <div><dt className="inline font-medium">Book: </dt><dd className="inline font-mono">{source.ledgerBookId}</dd></div>
                <div><dt className="inline font-medium">Line: </dt><dd className="inline font-mono">{source.lineId}</dd></div>
                {source.originalAccountPath ? <div><dt className="inline font-medium">Original account: </dt><dd className="inline">{source.originalAccountPath}</dd></div> : null}
                {source.agreementRevisionId ? <div><dt className="inline font-medium">Agreement revision: </dt><dd className="inline font-mono">{source.agreementRevisionId}</dd></div> : null}
                {source.accountBindingRevisionId ? <div><dt className="inline font-medium">Account binding revision: </dt><dd className="inline font-mono">{source.accountBindingRevisionId}</dd></div> : null}
                {source.intercompanyTransactionId ? <div><dt className="inline font-medium">Transaction reference: </dt><dd className="inline">{source.intercompanyTransactionId}</dd></div> : null}
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
  const governedMatches = Boolean(view.ruleVersion && view.ruleVersion !== "w10-v1");
  const incomeMatches = view.matches.some((match) => match.treatment === "consolidation.revenue-expense");
  const entityName = (id: string) => view.entityNames?.[id] ?? id;
  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center gap-2">
        <Badge variant="outline">{view.currency || "Currency unresolved"}</Badge>
        <Badge variant="outline">Primary basis</Badge>
        {view.ruleVersion ? <Badge variant="outline">{view.ruleVersion}</Badge> : null}
        <span className="text-xs text-muted-foreground">Ownership effective {view.request.asOf} · {view.entityIds.length} entities</span>
      </div>
      <p className="text-sm text-muted-foreground">{view.scopeLimitation}</p>
      {view.ruleVersion ? <p className="text-xs text-muted-foreground">v1 retains the two-entity, one-currency rule. v2 uses stable account roles across direct wholly owned entities; v3 adds governed trial-balance translation and reserve; v4 also eliminates matched revenue and expense. Investment/equity elimination is outside these treatment versions.</p> : null}
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
                {[balance.grossBalance, balance.proposedEliminations, balance.postedEliminations, balance.consolidatedBalance, balance.previewBalance].map((value, index) => <td key={index} className={amountCell}>{money(value, view.currency)}</td>)}
                <td className={`${cell} min-w-64`}><SourceDetails sources={balance.sources} currency={view.currency} label={`Sources for ${balance.accountPath}`} /></td>
              </tr>
            ))}</tbody>
          </table>
        </div>
        {view.balances.length === 0 ? <p className="py-3 text-sm text-muted-foreground">No source balances in this perimeter and period.</p> : null}
      </div>
      <div>
        <h3 className="text-sm font-semibold">Reciprocal matches and unmatched differences</h3>
        <p className="my-2 text-xs leading-5 text-muted-foreground">Receivables match reciprocal payables; approved income treatments match revenue and expense by shared transaction reference. Booking differences remain in group balances. Translation differences are disclosed separately.</p>
        <div className="overflow-x-auto" role="region" aria-label="Reciprocal match table" tabIndex={0}>
          <table className="w-full text-xs">
            <caption className="sr-only">Reciprocal balances and retained unmatched differences in {view.currency}</caption>
            <thead><tr>{["Posting entity → counterparty", incomeMatches ? "First-side balance" : "Receivable", incomeMatches ? "Reciprocal balance" : "Reciprocal payable", "Matched", incomeMatches ? "Unmatched first side" : "Unmatched receivable", incomeMatches ? "Unmatched second side" : "Unmatched payable", ...(governedMatches ? ["Agreement and transaction", "Original currency match", "Translation difference"] : []), "Source evidence"].map((label) => <th scope="col" className={cell} key={label}>{label}</th>)}</tr></thead>
            <tbody>{view.matches.map((match) => (
              <tr key={`${match.postingEntityId}:${match.counterpartyId}:${match.agreementId ?? "legacy"}:${match.treatment ?? "balance"}:${match.intercompanyTransactionId ?? "balance"}:${match.matchingCurrency ?? view.currency}`}>
                <th scope="row" className={cell}><span title={match.postingEntityId}>{entityName(match.postingEntityId)}</span> → <span title={match.counterpartyId}>{entityName(match.counterpartyId)}</span></th>
                {[match.receivable, match.payable, match.matchedAmount, match.unmatchedReceivable, match.unmatchedPayable].map((value, index) => <td key={index} className={amountCell}>{money(value, view.currency)}</td>)}
                {governedMatches ? <><td className={cell}><p>{match.treatment === "consolidation.revenue-expense" ? "Revenue / expense" : "Receivable / payable"}</p>{match.intercompanyTransactionId ? <p className="mt-1">{match.intercompanyTransactionId}</p> : null}
                  <TechnicalDetails className="mt-2" label="Agreement identity"><p className="break-all font-mono text-xs">{match.agreementId || "Legacy unassigned"}</p></TechnicalDetails></td>
                  <td className={amountCell}>{match.matchedTransactionAmount == null ? "Not retained" : `${money(match.matchedTransactionAmount, match.matchingCurrency ?? view.currency)} ${match.matchingCurrency ?? ""}`}<p className="mt-1 whitespace-normal text-xs">Unmatched {match.unmatchedFirstTransactionAmount == null ? "—" : money(match.unmatchedFirstTransactionAmount, match.matchingCurrency ?? view.currency)} / {match.unmatchedSecondTransactionAmount == null ? "—" : money(match.unmatchedSecondTransactionAmount, match.matchingCurrency ?? view.currency)} {match.matchingCurrency}</p></td>
                  <td className={amountCell}>{match.translationDifference == null ? "Not applicable" : `${money(match.translationDifference, view.currency)} ${view.currency}`}</td></> : null}
                <td className={`${cell} min-w-64`}><SourceDetails sources={match.sources} currency={view.currency} label={`Sources for ${entityName(match.postingEntityId)} to ${entityName(match.counterpartyId)}`} /></td>
              </tr>
            ))}</tbody>
          </table>
        </div>
        {view.matches.length === 0 ? <p className="py-3 text-sm text-muted-foreground">No reciprocal intercompany balances found.</p> : null}
      </div>
      {view.translationReserve || view.translationDifferences || view.roundingDifferences ? <TranslationDifferences view={view} /> : null}
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
                <ul className="space-y-2">{draft.lines.map((line) => <li key={line.lineId} className="break-all">{line.side} {money(line.amount, line.currency)} {line.currency} · {line.accountPath} · Entity {line.entityId || "Not supplied"}</li>)}</ul>
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
          {view.translationFingerprint ? <div><dt className="font-semibold">Translation authority fingerprint</dt><dd className="font-mono">{view.translationFingerprint}</dd></div> : null}
        </dl>
      </TechnicalDetails>
    </div>
  );
}

function TranslationDifferences({ view }: { view: ConsolidationView }) {
  const entities = [...new Set([...Object.keys(view.translationReserve ?? {}), ...Object.keys(view.translationDifferences ?? {}), ...Object.keys(view.roundingDifferences ?? {})])];
  const amount = (values: Record<string, number> | null | undefined, entity: string) => values?.[entity] == null ? "Not retained" : `${money(values[entity], view.currency)} ${view.currency}`;
  return <section className="space-y-2" aria-labelledby="consolidation-translation-differences"><h3 className="text-sm font-semibold" id="consolidation-translation-differences">Translation reserve and separate differences</h3>
    <p className="text-xs text-muted-foreground">Server-calculated whole-trial-balance translation. Booking breaks remain in the reciprocal matching table; rounding is disclosed separately from translation.</p>
    <div role="region" aria-label="Translation and rounding differences" className="overflow-x-auto" tabIndex={0}>
      <DenseDataTable ariaLabel="Per-entity translation and rounding differences" caption="Per-entity cumulative translation reserve and separate FX and rounding differences"
        rows={entities} getRowId={(entity) => entity} emptyText="No per-entity translation differences retained." maxVisibleRows={50}
        columns={[
          { id: "entity", label: "Entity", rowHeader: true, render: (entity) => <span title={entity}>{view.entityNames?.[entity] ?? entity}</span> },
          { id: "reserve", label: "Translation reserve", numeric: true, render: (entity) => amount(view.translationReserve, entity) },
          { id: "translation", label: "Translation difference", numeric: true, render: (entity) => amount(view.translationDifferences, entity) },
          { id: "rounding", label: "Rounding difference", numeric: true, render: (entity) => amount(view.roundingDifferences, entity) }
        ]} />
    </div>
  </section>;
}
