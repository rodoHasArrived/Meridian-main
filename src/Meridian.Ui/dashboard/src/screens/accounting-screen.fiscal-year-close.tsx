import { useEffect, useId, useRef, useState } from "react";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Button } from "@/components/ui/button";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { LocalDateText } from "@/components/ui/local-date-text";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { describeApiError } from "@/lib/api-errors";
import {
  finalizeFiscalYearClose, getFiscalYearClosePlan, getFiscalYearCloseReceipt,
  type FinalizeFiscalYearCloseRequest, type FiscalYearCloseBalance, type FiscalYearClosePlan, type FiscalYearCloseReceipt,
  type FiscalYearCloseScope
} from "@/lib/api/fiscal-year-close.api";
import { getLedgerBooks, getLedgerPeriods } from "@/lib/ledger-reports-api";
import { formatTaxDecimal as formatDecimalText } from "@/lib/ledger-tax-results-format";
import { accountingBasisDisplayName } from "@/screens/accounting-screen.basis-bridge.view-model";
import type { LedgerBook, LedgerPeriod } from "@/types";

function errorMessage(error: unknown) {
  return describeApiError(error, "The fiscal-year close service is unavailable.").summary;
}

export function FiscalYearClosePanel({ initialBookId }: { initialBookId?: string }) {
  const panelId = useId();
  const [open, setOpen] = useState(false);
  return <section className="panel-surface space-y-4 p-4" aria-label="Fiscal-year close">
    <div className="flex flex-wrap items-center justify-between gap-3">
      <h3 className="workspace-section-title">Fiscal-year close</h3>
      <Button variant="outline" aria-expanded={open} aria-controls={panelId} onClick={() => setOpen(value => !value)}>
        {open ? "Hide fiscal close" : "Review fiscal close"}
      </Button>
    </div>
    <div id={panelId} hidden={!open}>{open ? <CloseBookSelection key={initialBookId} initialBookId={initialBookId} /> : null}</div>
  </section>;
}

function CloseBookSelection({ initialBookId }: { initialBookId?: string }) {
  const fieldId = useId();
  const [books, setBooks] = useState<LedgerBook[] | null>(null);
  const [bookId, setBookId] = useState(initialBookId ?? "");
  const [error, setError] = useState<string | null>(null);
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    void getLedgerBooks({ signal: controller.signal, allowDevelopmentFallback: false })
      .then(result => {
        if (controller.signal.aborted) return;
        setBooks(result);
        if (result.length === 1) setBookId(result[0].ledgerBookId);
      })
      .catch(reason => { if (!controller.signal.aborted) setError(errorMessage(reason)); });
    return () => controller.abort();
  }, [revision]);
  if (error) return <>
    <StatusBanner tone="danger" role="alert" title="Ledger books unavailable" detail={error} />
    <Button variant="outline" onClick={() => { setError(null); setRevision(revision + 1); }}>Retry</Button>
  </>;
  if (!books) return <p role="status">Loading ledger books…</p>;
  if (books.length === 0) return <p role="status">No ledger books are available for this session.</p>;
  const book = books.find(row => row.ledgerBookId.toLowerCase() === bookId.toLowerCase());
  return <div className="space-y-4">
    <FormRow label="Ledger book" labelFor={fieldId}>
      <Select id={fieldId} placeholder="Select a ledger book" value={book?.ledgerBookId ?? ""} onChange={event => setBookId(event.currentTarget.value)}>
        {books.map(row => <option key={row.ledgerBookId} value={row.ledgerBookId}>
          {row.displayName} · {accountingBasisDisplayName(row.accountingBasis)} · {row.baseCurrency}
        </option>)}
      </Select>
    </FormRow>
    {book ? <FiscalYearSelection key={book.ledgerBookId} book={book} /> : null}
  </div>;
}

function FiscalYearSelection({ book }: { book: LedgerBook }) {
  const fieldId = useId();
  const [periods, setPeriods] = useState<LedgerPeriod[] | null>(null);
  const [fiscalYear, setFiscalYear] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    void getLedgerPeriods({ ledgerBookId: book.ledgerBookId }, { signal: controller.signal, allowDevelopmentFallback: false })
      .then(result => { if (!controller.signal.aborted) setPeriods(result); })
      .catch(reason => { if (!controller.signal.aborted) setError(errorMessage(reason)); });
    return () => controller.abort();
  }, [book.ledgerBookId, revision]);
  if (error) return <>
    <StatusBanner tone="danger" role="alert" title="Fiscal years unavailable" detail={error} />
    <Button variant="outline" onClick={() => { setError(null); setRevision(revision + 1); }}>Retry</Button>
  </>;
  if (!periods) return <p role="status">Loading fiscal years…</p>;
  const years = [...new Set(periods.filter(period => period.ledgerBookId.toLowerCase() === book.ledgerBookId.toLowerCase()).map(period => period.fiscalYear))]
    .sort((left, right) => right - left);
  if (years.length === 0) return <p role="status">This book has no accounting periods.</p>;
  const year = fiscalYear ?? years[0];
  return <div className="space-y-4">
    <FormRow label="Fiscal year" labelFor={fieldId}>
      <Select id={fieldId} value={String(year)} onChange={event => setFiscalYear(Number(event.currentTarget.value))}>
        {years.map(value => <option key={value} value={value}>{value}</option>)}
      </Select>
    </FormRow>
    <FiscalCloseReview key={year} ledgerBookId={book.ledgerBookId} fiscalYear={year} currency={book.baseCurrency} />
  </div>;
}

function FiscalCloseReview({ ledgerBookId, fiscalYear, currency }: FiscalYearCloseScope & { currency: string }) {
  const reasonId = useId();
  const [review, setReview] = useState<{ plan: FiscalYearClosePlan; receipt: FiscalYearCloseReceipt | null } | null>(null);
  const [retainedView, setRetainedView] = useState<FiscalYearCloseReceipt | null>(null);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [revision, setRevision] = useState(0);
  const activeOperation = useRef<AbortController | null>(null);
  const pending = useRef<FinalizeFiscalYearCloseRequest | null>(null);
  const scope = { ledgerBookId, fiscalYear };

  useEffect(() => {
    const controller = new AbortController();
    const requestScope = { ledgerBookId, fiscalYear };
    void Promise.all([getFiscalYearClosePlan(requestScope, controller.signal), getFiscalYearCloseReceipt(requestScope, controller.signal)])
      .then(([plan, receipt]) => { if (!controller.signal.aborted) setReview({ plan, receipt }); })
      .catch(failure => { if (!controller.signal.aborted) setError(errorMessage(failure)); });
    return () => { controller.abort(); activeOperation.current?.abort(); };
  }, [ledgerBookId, fiscalYear, revision]);

  const plan = review?.plan;
  const latest = review?.receipt;
  const isRetained = !!latest && latest.plan.planHashSha256 === plan?.planHashSha256;
  const canFinalize = !!plan?.canFinalizeFiscalYearClose && plan.isReady && !isRetained;

  const finalize = async () => {
    if (activeOperation.current || !canFinalize || !plan?.planHashSha256 || !reason.trim()) return;
    const previous = pending.current;
    const supersedesReceiptId = latest?.receiptId ?? null;
    const request = previous?.expectedPlanHashSha256 === plan.planHashSha256 &&
      previous.reason === reason.trim() && previous.supersedesReceiptId === supersedesReceiptId
      ? previous
      : { commandId: crypto.randomUUID(), expectedPlanHashSha256: plan.planHashSha256, reason: reason.trim(), supersedesReceiptId };
    pending.current = request;
    const controller = new AbortController();
    activeOperation.current = controller;
    setBusy(true);
    setError(null);
    try {
      const receipt = await finalizeFiscalYearClose(scope, request, controller.signal);
      if (controller.signal.aborted) return;
      setReview({ plan, receipt });
      setRetainedView(null);
      setReason("");
      pending.current = null;
    } catch (failure) {
      if (!controller.signal.aborted) setError(errorMessage(failure));
    } finally {
      if (activeOperation.current === controller) activeOperation.current = null;
      if (!controller.signal.aborted) setBusy(false);
    }
  };

  const showPredecessor = async (receiptId: string) => {
    if (activeOperation.current) return;
    const controller = new AbortController();
    activeOperation.current = controller;
    setBusy(true);
    setError(null);
    try {
      const receipt = await getFiscalYearCloseReceipt(scope, controller.signal, receiptId);
      if (!controller.signal.aborted) setRetainedView(receipt);
    } catch (failure) {
      if (!controller.signal.aborted) setError(errorMessage(failure));
    } finally {
      if (activeOperation.current === controller) activeOperation.current = null;
      if (!controller.signal.aborted) setBusy(false);
    }
  };

  const receipt = retainedView ?? latest;
  return <div className="space-y-4" aria-busy={busy}>
    {error ? <StatusBanner tone="danger" role="alert" title="Fiscal-year close requires attention" detail={error} /> : null}
    <Button variant="outline" size="sm" disabled={busy} onClick={() => {
      setReview(null); setRetainedView(null); setError(null); setRevision(revision + 1);
    }}>Refresh close evidence</Button>
    {!plan && !error ? <p role="status">Loading close evidence…</p> : null}
    {receipt ? <section className="space-y-2 rounded border border-border p-3" aria-label="Retained close receipt">
      <h4 className="text-sm font-semibold">{retainedView ? "Prior close receipt" : "Retained close receipt"}</h4>
      <p className="text-sm">Recorded by {receipt.authorizedBy} · {accountingBasisDisplayName(receipt.accountingBasis)} · {receipt.functionalCurrency}</p>
      <p className="text-xs text-muted-foreground">Policy {receipt.accountingPolicyId} · version {receipt.accountingPolicyVersion}</p>
      <p className="text-sm">{receipt.reason}</p>
      <p className="break-all font-mono text-xs">{receipt.receiptId}</p>
      <div className="flex flex-wrap gap-2">
        {!retainedView && !isRetained ? <Button size="sm" variant="outline" onClick={() => setRetainedView(receipt)}>View retained evidence</Button> : null}
        {receipt.supersedesReceiptId ? <Button size="sm" variant="outline" disabled={busy} onClick={() => void showPredecessor(receipt.supersedesReceiptId!)}>View preceding receipt</Button> : null}
        {retainedView ? <Button size="sm" variant="outline" onClick={() => setRetainedView(null)}>Return to current close</Button> : null}
      </div>
    </section> : null}
    {plan ? <CloseEvidence plan={retainedView?.plan ?? plan} currency={retainedView?.functionalCurrency ?? plan.book?.functionalCurrency ?? currency} /> : null}
    {plan?.isReady && !isRetained && !canFinalize && !retainedView
      ? <p className="text-sm text-muted-foreground">{plan.proposedClosingTransfers.length
        ? "Complete the proposed transfers through governed period close before retaining this plan."
        : "Retaining the close requires controller authorization."}</p> : null}
    {canFinalize && !retainedView ? <form className="space-y-3" onSubmit={event => { event.preventDefault(); void finalize(); }}>
      <FormRow label="Close reason" labelFor={reasonId}>
        <Input id={reasonId} required disabled={busy} value={reason} onChange={event => setReason(event.currentTarget.value)} />
      </FormRow>
      <Button type="submit" disabled={busy || !reason.trim()}>{latest ? "Retain successor close" : "Retain fiscal close"}</Button>
    </form> : null}
  </div>;
}

const balanceColumns: DenseDataTableColumn<FiscalYearCloseBalance>[] = [
  { id: "account", label: "Account", rowHeader: true, render: row => row.accountName },
  { id: "scope", label: "Scope", render: row => [row.symbol, row.financialAccountId, ...Object.values(row.dimensions ?? {})].filter(Boolean).join(" · ") },
  { id: "debit", label: "Debit", numeric: true, render: row => formatDecimalText(row.debitTotal) },
  { id: "credit", label: "Credit", numeric: true, render: row => formatDecimalText(row.creditTotal) },
  { id: "balance", label: "Balance", numeric: true, render: row => formatDecimalText(row.balance) }
];

function CloseEvidence({ plan, currency }: { plan: FiscalYearClosePlan; currency: string }) {
  if (!plan.isReady) return <section className="space-y-2" aria-label="Fiscal close blockers">
    <StatusBanner tone="warning" title="Fiscal close is blocked" />
    <ul className="list-disc space-y-1 pl-5 text-sm">{plan.blockers.map((blocker, index) =>
      <li key={`${blocker.code}-${index}`}>{blocker.message}</li>)}</ul>
  </section>;
  const sections = [
    { label: "Pre-close trial balance", rows: plan.preClosingTrialBalance },
    { label: "Posted closing transfers", rows: plan.postedClosingTransfers },
    { label: "Proposed closing transfers", rows: plan.proposedClosingTransfers },
    { label: "Post-close trial balance", rows: plan.postClosingTrialBalance },
    { label: "Next-year opening balances", rows: plan.nextYearOpeningBalances }
  ];
  const totals = [
    { label: "Reported net income", amount: plan.reportedNetIncome },
    { label: "Opening equity", amount: plan.openingEquity },
    { label: "Post-close equity", amount: plan.postClosingEquity }
  ];
  return <div className="space-y-3">
    <p className="text-sm"><LocalDateText value={plan.fiscalYearStart} /> to <LocalDateText value={plan.fiscalYearEnd} /></p>
    <dl className="grid gap-2 sm:grid-cols-3">{totals.map(total => <div key={total.label} className="rounded border border-border p-3">
      <dt className="text-sm text-muted-foreground">{total.label}</dt>
      <dd className="font-mono text-sm tabular-nums">{formatDecimalText(total.amount)} {currency}</dd>
    </div>)}</dl>
    {sections.map(section => <details key={section.label} className="rounded border border-border px-3 py-2">
      <summary className="cursor-pointer text-sm font-medium">{section.label} ({section.rows.length})</summary>
      <DenseDataTable columns={balanceColumns} rows={section.rows} ariaLabel={section.label}
        getRowId={row => JSON.stringify([row.accountName, row.accountType, row.symbol, row.financialAccountId, row.dimensions])}
        emptyText="No entries in this section." />
    </details>)}
    <details className="rounded border border-border px-3 py-2">
      <summary className="cursor-pointer text-sm font-medium">Retained period evidence</summary>
      <ul className="mt-2 space-y-2 text-sm">{plan.periods.map(period => <li key={period.periodId}>
        {period.label} · {period.status} · snapshot v{period.snapshotVersion}
        <div className="break-all font-mono text-xs">{period.snapshotHashSha256}</div>
      </li>)}</ul>
      <p className="mt-2 break-all text-xs">Plan hash <code>{plan.planHashSha256}</code></p>
    </details>
  </div>;
}
