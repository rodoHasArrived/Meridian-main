import { useEffect, useId, useState } from "react";
import { useLocation } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { listIntegrationBooks, listIntegrationPeriods } from "@/lib/api/accounting-integration.api";
import type { LedgerBook } from "@/types";
import type { AccountingIntegrationScope, AccountingPeriodOption } from "@/types/accounting-integration";

export const integrationError = (error: unknown) => error instanceof Error ? error.message : "The request failed. Refresh retained state before retrying.";
export const isGuid = (value: string) => /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value) && value !== "00000000-0000-0000-0000-000000000000";
export const scopeIdentity = (scope: AccountingIntegrationScope) => `${scope.book.ledgerBookId}:${scope.period.periodId}:${scope.accountId}`;

/** Explicit retained book/period selection shared by the bounded accounting additions. */
export function IntegrationScopePicker({ onSelect, accountRequired = true }: {
  onSelect: (scope: AccountingIntegrationScope | null) => void; accountRequired?: boolean;
}) {
  const uid = useId();
  const { search } = useLocation();
  const query = new URLSearchParams(search);
  const requestedBook = query.get("ledgerBookId") ?? "";
  const requestedPeriod = query.get("periodId") ?? "";
  const requestedAccount = query.get("fundAccountId") ?? query.get("accountId") ?? "";
  const [books, setBooks] = useState<LedgerBook[]>([]);
  const [periods, setPeriods] = useState<AccountingPeriodOption[]>([]);
  const [bookId, setBookId] = useState(requestedBook);
  const [periodId, setPeriodId] = useState(requestedPeriod);
  const [accountId, setAccountId] = useState(requestedAccount);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loadVersion, setLoadVersion] = useState(0);
  const selectedBook = books.find(book => book.ledgerBookId === bookId);
  const selectedPeriod = periods.find(period => period.periodId === periodId && period.ledgerBookId === bookId);

  useEffect(() => {
    setBookId(requestedBook); setPeriodId(requestedPeriod); setAccountId(requestedAccount);
  }, [requestedBook, requestedPeriod, requestedAccount]);

  useEffect(() => {
    if (!loadVersion) return;
    const controller = new AbortController();
    setBusy(true);
    setError(null);
    listIntegrationBooks({ signal: controller.signal }).then(rows => {
      if (!controller.signal.aborted) setBooks(rows);
    }).catch(reason => { if (!controller.signal.aborted) setError(integrationError(reason)); })
      .finally(() => { if (!controller.signal.aborted) setBusy(false); });
    return () => controller.abort();
  }, [loadVersion]);

  useEffect(() => {
    if (!selectedBook) return;
    const controller = new AbortController();
    setPeriods([]);
    listIntegrationPeriods(selectedBook.ledgerBookId, { signal: controller.signal }).then(rows => {
      if (!controller.signal.aborted) setPeriods(rows);
    }).catch(reason => { if (!controller.signal.aborted) setError(integrationError(reason)); });
    return () => controller.abort();
  }, [selectedBook]);

  useEffect(() => {
    onSelect(selectedBook && selectedPeriod && (!accountRequired || isGuid(accountId))
      ? { book: selectedBook, period: selectedPeriod, accountId } : null);
  }, [selectedBook, selectedPeriod, accountId, accountRequired, onSelect]);

  return <div className="space-y-3">
    <Button type="button" variant="outline" size="sm" disabled={busy} onClick={() => setLoadVersion(value => value + 1)}>
      {busy ? "Loading ledger scope…" : "Load ledger scope"}
    </Button>
    <div className="grid gap-3 lg:grid-cols-3">
      <label htmlFor={`${uid}-book`} className="space-y-1 text-xs">Ledger book
        <Select id={`${uid}-book`} aria-label="Ledger book" value={bookId} onChange={event => { setBookId(event.target.value); setPeriodId(""); }}>
          <option value="">Select retained book</option>
          {books.map(book => <option key={book.ledgerBookId} value={book.ledgerBookId}>{book.displayName} · {book.baseCurrency}</option>)}
        </Select>
      </label>
      <label htmlFor={`${uid}-period`} className="space-y-1 text-xs">Accounting period
        <Select id={`${uid}-period`} aria-label="Accounting period" value={periodId} disabled={!selectedBook} onChange={event => setPeriodId(event.target.value)}>
          <option value="">Select retained period</option>
          {periods.map(period => <option key={period.periodId} value={period.periodId}>{period.label} · {period.status}</option>)}
        </Select>
      </label>
      {accountRequired ? <label htmlFor={`${uid}-account`} className="space-y-1 text-xs">Financial account ID
        <Input id={`${uid}-account`} aria-label="Financial account ID" value={accountId} autoComplete="off" spellCheck={false}
          onChange={event => setAccountId(event.target.value.trim())} aria-describedby={`${uid}-account-help`} />
        <span id={`${uid}-account-help`} className="text-muted-foreground">Exact account UUID; ownership is verified by the server.</span>
      </label> : null}
    </div>
    {error ? <p role="alert" className="text-sm text-danger">{error}</p> : null}
    {loadVersion > 0 && !busy && !error && books.length === 0 ? <p className="text-sm text-muted-foreground">No retained ledger books are available in this scope.</p> : null}
  </div>;
}
