import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { Select } from "@/components/ui/select";
import { formatMoney, formatTimestamp } from "@/lib/format";
import { getExecutionJournalEntries, getLedgerBooks } from "@/lib/ledger-reports-api";
import { workstationRouteWithQuery } from "@/lib/workspace";
import type { JournalOrder } from "@/lib/api/execution-workflows.api";
import type { LedgerBook, LedgerPostedJournalEntry } from "@/types";

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** The book whose fund-structure node is exactly this account, when one exists; never a guess. */
export function bookForAccount(books: readonly LedgerBook[], accountId: string): LedgerBook | null {
  const matches = books.filter((book) => book.fundStructureNodeKind === "Account"
    && book.fundStructureNodeId.toLowerCase() === accountId.toLowerCase());
  return matches.length === 1 ? matches[0] : null;
}

/** The order id and each fill id that can be a ledger fill identifier (GUIDs only). */
export function executionLookups(order: Pick<JournalOrder, "orderId" | "events">): ({ orderId: string } | { fillId: string })[] {
  const fillIds = [...new Set(order.events.flatMap((event) => event.activityFill?.fillId && GUID.test(event.activityFill.fillId) ? [event.activityFill.fillId.toLowerCase()] : []))];
  return [{ orderId: order.orderId }, ...fillIds.map((fillId) => ({ fillId }))];
}

/**
 * The journal entries a ledger book posted from this order or its fills, found by the identifiers the
 * postings recorded. Automated fill accounting records the fill but not the order, so fills are looked
 * up too; the results are merged by journal entry.
 */
export function OrderLedgerPostings({ order }: { order: JournalOrder }) {
  const [books, setBooks] = useState<LedgerBook[] | null>(null);
  const [chosenBookId, setChosenBookId] = useState("");
  const [state, setState] = useState<{ key: string; entries?: LedgerPostedJournalEntry[]; error?: string } | null>(null);
  useEffect(() => {
    let active = true;
    void getLedgerBooks().then((result) => { if (active) setBooks(result); }, () => { if (active) setBooks([]); });
    return () => { active = false; };
  }, []);
  const matched = useMemo(() => books ? bookForAccount(books, order.accountId) : null, [books, order.accountId]);
  const bookId = matched?.ledgerBookId ?? chosenBookId;
  const lookups = useMemo(() => executionLookups(order), [order]);
  const key = JSON.stringify([bookId, lookups]);
  useEffect(() => {
    if (!bookId) return;
    const controller = new AbortController();
    setState({ key });
    void Promise.all(lookups.map((lookup) => getExecutionJournalEntries(bookId, lookup, { signal: controller.signal })))
      .then((results) => {
        if (controller.signal.aborted) return;
        const byId = new Map(results.flat().map((entry) => [entry.journalEntryId, entry]));
        setState({ key, entries: [...byId.values()].sort((left, right) => left.timestamp.localeCompare(right.timestamp)) });
      })
      .catch((failure: unknown) => { if (!controller.signal.aborted) setState({ key, error: failure instanceof Error ? failure.message : "Ledger postings could not be loaded." }); });
    return () => controller.abort();
  }, [bookId, key, lookups]);
  const current = state?.key === key ? state : null;
  const book = books?.find((candidate) => candidate.ledgerBookId === bookId) ?? null;
  const fillCount = lookups.length - 1;

  return (
    <section aria-labelledby={`order-ledger-${order.orderId}`} className="mt-3 space-y-2 border-t border-border pt-3 text-xs">
      <h4 id={`order-ledger-${order.orderId}`} className="text-sm font-semibold">Ledger postings</h4>
      {books === null ? <p role="status">Loading ledger books…</p>
        : !matched ? (
          <label className="block">Ledger book for account {order.accountId}
            <Select className="mt-1" value={chosenBookId} onChange={(event) => setChosenBookId(event.target.value)}>
              <option value="">Choose the book this account posts to</option>
              {books.map((candidate) => <option key={candidate.ledgerBookId} value={candidate.ledgerBookId}>{candidate.displayName} · {candidate.baseCurrency}</option>)}
            </Select>
          </label>
        ) : null}
      {!bookId ? null
        : current?.error ? <p role="alert">{current.error}</p>
        : !current?.entries ? <p role="status">Looking up postings in {book?.displayName ?? "the selected book"}…</p>
        : current.entries.length === 0 ? <p role="status">No postings in {book?.displayName ?? "this book"} recorded this order{fillCount > 0 ? ` or its ${fillCount === 1 ? "fill" : `${fillCount} fills`}` : ""}.</p>
        : (
          <ul className="space-y-1" aria-label={`Ledger postings for order ${order.orderId}`}>
            {current.entries.map((entry) => (
              <li key={entry.journalEntryId} className="flex flex-wrap items-baseline gap-x-2">
                <Link className="font-mono underline underline-offset-4" to={workstationRouteWithQuery("accountingJournalEntryDetail", { journalEntryId: entry.journalEntryId, periodId: entry.periodId })}>{entry.journalEntryId}</Link>
                <span>{formatTimestamp(entry.timestamp)}</span>
                <span className="text-muted-foreground">{entry.description}</span>
                <span className="font-mono tabular-nums">{book ? formatMoney(entry.totalDebits, { context: "accounting", currency: book.baseCurrency }) : entry.totalDebits}</span>
              </li>
            ))}
          </ul>
        )}
      <p className="text-muted-foreground">Matched on the order id{fillCount > 0 ? " and fill ids" : ""} each posting recorded; nothing is matched by symbol, amount or time.</p>
    </section>
  );
}
