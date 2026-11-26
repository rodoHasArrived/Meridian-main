import { useEffect, useId, useState } from "react";
import { useLocation } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { Combobox } from "@/components/ui/combobox";
import { narrowInlineAccountOptions } from "@/lib/operating-scope/fund-scope-narrowing";
import { useFundAccountScopeSelection } from "@/lib/operating-scope/scope-picker-context";
import { UNAVAILABLE_SCOPE_STRUCTURE } from "@/lib/operating-scope/structure-options";
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
  const accountOptions = useScopedFundAccountOptions(accountId);

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
      {accountRequired && accountOptions.options.length > 0 ? <div className="space-y-1 text-xs">
        <Combobox id={`${uid}-account`} label="Financial account" aria-describedby={`${uid}-account-help`}
          options={accountOptions.options} value={accountId} placeholder="Choose a fund account"
          emptyText="No fund account in this scope matches." onChange={value => setAccountId(value.trim())} />
        <span id={`${uid}-account-help`} className="text-muted-foreground">{accountOptions.help ?? "Accounts in the current operating scope. Ownership is verified by the server."}</span>
      </div> : null}
      {accountRequired && accountOptions.options.length === 0 ? <label htmlFor={`${uid}-account`} className="space-y-1 text-xs">Financial account ID
        <Input id={`${uid}-account`} aria-label="Financial account ID" value={accountId} autoComplete="off" spellCheck={false}
          onChange={event => setAccountId(event.target.value.trim())} aria-describedby={`${uid}-account-help`} />
        <span id={`${uid}-account-help`} className="text-muted-foreground">The fund account list is not available here, so enter the exact account UUID. Ownership is verified by the server.</span>
      </label> : null}
    </div>
    {error ? <p role="alert" className="text-sm text-danger">{error}</p> : null}
    {loadVersion > 0 && !busy && !error && books.length === 0 ? <p className="text-sm text-muted-foreground">No retained ledger books are available in this scope.</p> : null}
  </div>;
}

/**
 * Fund accounts the operator can choose for the three-way comparison: the shell's known accounts,
 * narrowed by the chosen entity and fund, keeping only UUIDs (the endpoint takes a fund-account GUID).
 * Empty where no shell is mounted or no account is known; the picker then falls back to typed entry.
 */
function useScopedFundAccountOptions(currentAccountId: string): { options: { value: string; label: string }[]; help: string | null } {
  const selection = useFundAccountScopeSelection();
  if (!selection) return { options: [], help: null };
  const narrowing = narrowInlineAccountOptions(
    selection.options,
    selection.structure ?? UNAVAILABLE_SCOPE_STRUCTURE,
    { entityId: selection.scope?.entityId, fundId: selection.scope?.fundId }
  );
  const options = narrowing.options
    .filter(option => isGuid(option.id))
    .map(option => ({ value: option.id, label: option.label && option.label !== option.id ? `${option.label} · ${option.id}` : option.id }));
  if (options.length > 0 && isGuid(currentAccountId) && !options.some(option => option.value === currentAccountId)) {
    options.unshift({ value: currentAccountId, label: `${currentAccountId} (from the link)` });
  }
  return { options, help: narrowing.help ?? null };
}
