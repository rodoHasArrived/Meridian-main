import { useEffect, useId, useMemo, useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { TableWorkbench, type TableWorkbenchColumn } from "@/components/meridian/table-workbench";
import { StatusBadge } from "@/design-system/status";
import { getLedgerBooks } from "@/lib/ledger-reports-api";
import { getBookReceiptRun, getBookReceiptRuns, runBookReceiptQueue } from "@/lib/api/income-receipt-queue.api";
import type { LedgerBook } from "@/types";
import type { BookReceiptQueueItem, BookReceiptRun } from "@/types/income-receipt-queue.types";
import { IncomeReceiptDetails, receiptAmount } from "./income-receipt-queue.details";

const statuses: Record<string, { label: string; severity: string }> = {
  Due: { label: "Due", severity: "review" }, Overdue: { label: "Overdue", severity: "blocked" },
  PartiallyReceived: { label: "Partially received", severity: "action" }, Disputed: { label: "Disputed", severity: "blocked" },
  ReceivedButUnposted: { label: "Received but unposted", severity: "review" }, Reconciled: { label: "Reconciled", severity: "ready" },
  IncompleteEvidence: { label: "Incomplete evidence", severity: "action" }, Scheduled: { label: "Scheduled", severity: "info" }
};
function statusLabel(status: string) { return statuses[status]?.label ?? status; }
function errorMessage(error: unknown) { return error instanceof Error ? error.message : "Receipt evidence could not be loaded."; }
function localToday() { const date = new Date(); return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`; }
function orderRuns(runs: BookReceiptRun[]) { return [...runs].sort((a, b) => b.asOfDate.localeCompare(a.asOfDate) || b.sequence - a.sequence || b.runId.localeCompare(a.runId)); }

export function IncomeReceiptQueuePanel({ operatorIdentity }: { operatorIdentity?: string | null }) {
  return <IncomeReceiptBookPicker key={operatorIdentity ?? "session"} />;
}

function IncomeReceiptBookPicker() {
  const [params, setParams] = useSearchParams();
  const [books, setBooks] = useState<LedgerBook[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reload, setReload] = useState(0);
  const bookId = params.get("ledgerBookId") || books?.[0]?.ledgerBookId || "";
  useEffect(() => {
    const controller = new AbortController();
    setBooks(null); setError(null);
    getLedgerBooks({ signal: controller.signal, allowDevelopmentFallback: false }).then(result => {
      if (!controller.signal.aborted) setBooks(result);
    }).catch(cause => { if (!controller.signal.aborted) setError(errorMessage(cause)); });
    return () => controller.abort();
  }, [reload]);
  const book = books?.find(candidate => candidate.ledgerBookId === bookId);
  return <Card id="income-receipt-queue" aria-label="Book receipt queue">
    <CardHeader><CardTitle>Book receipt queue</CardTitle><CardDescription>Coupon, interest and principal obligations from authoritative holdings and accepted projections.</CardDescription></CardHeader>
    <CardContent className="space-y-4">
      <div className="flex flex-wrap items-end gap-3">
        <label className="min-w-64 flex-1 space-y-1 text-xs font-medium">Ledger book<Select aria-label="Receipt ledger book" value={bookId} disabled={!books?.length} onChange={event => setParams(current => { const next = new URLSearchParams(current); next.set("ledgerBookId", event.target.value); next.delete("receiptRunId"); return next; })}>
          {!books?.length ? <option value="">{books ? "No authorized ledger books" : "Loading ledger books…"}</option> : null}
          {books?.length && !book ? <option value={bookId}>Selected book unavailable</option> : null}
          {books?.map(candidate => <option key={candidate.ledgerBookId} value={candidate.ledgerBookId}>{candidate.displayName} · {candidate.baseCurrency}</option>)}
        </Select></label>
      </div>
      {error ? <div role="alert" className="space-y-2"><p>{error}</p><Button variant="outline" size="sm" onClick={() => setReload(value => value + 1)}>Retry ledger books</Button></div> : null}
      {books?.length === 0 ? <p className="text-sm text-muted-foreground">No ledger books are available in the current scope.</p> : null}
      {books?.length && !book ? <p role="alert" className="text-sm">The selected ledger book is not available in the current scope. Choose an authorized book.</p> : null}
      {book ? <BookReceiptRuns key={book.ledgerBookId} book={book} /> : null}
    </CardContent>
  </Card>;
}

function BookReceiptRuns({ book }: { book: LedgerBook }) {
  const [params, setParams] = useSearchParams();
  const [runs, setRuns] = useState<BookReceiptRun[] | null>(null);
  const [run, setRun] = useState<BookReceiptRun | null>(null);
  const [asOfDate, setAsOfDate] = useState(localToday);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [running, setRunning] = useState(false);
  const [reload, setReload] = useState(0);
  const activeMutation = useRef<AbortController | null>(null);
  const request = useRef(0);
  const runId = params.get("receiptRunId") || runs?.[0]?.runId || "";
  const selectRun = (id: string) => { setRun(null); setParams(current => { const next = new URLSearchParams(current); next.set("ledgerBookId", book.ledgerBookId); next.set("receiptRunId", id); return next; }); };
  useEffect(() => () => { request.current++; activeMutation.current?.abort(); }, []);
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true); setError(null);
    getBookReceiptRuns(book.ledgerBookId, { signal: controller.signal }).then(result => {
      if (!controller.signal.aborted) { setRuns(orderRuns(result)); setLoading(false); }
    }).catch(cause => { if (!controller.signal.aborted) { setError(errorMessage(cause)); setLoading(false); } });
    return () => controller.abort();
  }, [book.ledgerBookId, reload]);
  useEffect(() => {
    const controller = new AbortController();
    const current = ++request.current;
    setRun(null);
    if (!runId) return () => controller.abort();
    setLoading(true); setError(null);
    getBookReceiptRun(book.ledgerBookId, runId, { signal: controller.signal }).then(result => {
      if (!controller.signal.aborted && current === request.current) { setRun(result); setLoading(false); }
    }).catch(cause => { if (!controller.signal.aborted && current === request.current) { setError(errorMessage(cause)); setLoading(false); } });
    return () => controller.abort();
  }, [book.ledgerBookId, runId, reload]);
  async function runDailyCheck() {
    if (activeMutation.current || !/^\d{4}-\d{2}-\d{2}$/.test(asOfDate)) return;
    const controller = new AbortController();
    activeMutation.current = controller;
    setRunning(true); setError(null);
    try {
      const result = await runBookReceiptQueue(book.ledgerBookId, asOfDate, { signal: controller.signal });
      if (controller.signal.aborted) return;
      setRuns(previous => orderRuns([result, ...(previous ?? []).filter(candidate => candidate.runId !== result.runId)]));
      selectRun(result.runId);
      setRun(result);
    } catch (cause) { if (!controller.signal.aborted) setError(errorMessage(cause)); }
    finally { if (!controller.signal.aborted) { activeMutation.current = null; setRunning(false); } }
  }
  return <div className="space-y-4">
    <div className="flex flex-wrap items-end gap-3">
      <label className="space-y-1 text-xs font-medium">As of date<Input type="date" aria-label="Receipt as of date" value={asOfDate} disabled={running} onChange={event => setAsOfDate(event.target.value)} /></label>
      <Button disabled={running || !/^\d{4}-\d{2}-\d{2}$/.test(asOfDate)} onClick={() => void runDailyCheck()}>{running ? "Running daily check…" : "Run daily check"}</Button>
      <label className="min-w-64 flex-1 space-y-1 text-xs font-medium">Retained daily run<Select aria-label="Retained receipt run" value={runId} disabled={!runs?.length || running} onChange={event => selectRun(event.target.value)}>
        {!runs?.length ? <option value="">{runs ? "No retained runs" : "Loading retained runs…"}</option> : null}
        {runId && runs?.length && !runs.some(candidate => candidate.runId === runId) ? <option value={runId}>Selected retained run</option> : null}
        {runs?.map(candidate => <option key={candidate.runId} value={candidate.runId}>{candidate.asOfDate} · observation {candidate.sequence} · {candidate.runId}</option>)}
      </Select></label>
    </div>
    <p className="text-xs text-muted-foreground">A daily check retains the source evidence and updates linked reconciliation exceptions. Opening a retained run replays its original evidence.</p>
    {error ? <div role="alert" className="space-y-2"><p className="text-sm">{error}</p><Button variant="outline" size="sm" disabled={running} onClick={() => setReload(value => value + 1)}>Reload receipt evidence</Button></div> : null}
    {loading && !run ? <p role="status" className="text-sm">Loading retained receipt evidence…</p> : null}
    {!loading && runs?.length === 0 && !runId ? <p className="text-sm text-muted-foreground">No retained receipt run for this book. Run a daily check to establish its obligations and evidence coverage.</p> : null}
    {run && run.runId === runId ? <ReceiptRunView key={run.runId} run={run} /> : null}
  </div>;
}

function ReceiptRunView({ run }: { run: BookReceiptRun }) {
  const [status, setStatus] = useState("");
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const detailId = useId();
  const rows = run.items.filter(item => (!status || (status === "evidence" ? !item.evidenceComplete : item.status === status)) && `${item.displayName} ${item.flowType} ${item.owner ?? "Unassigned"} ${item.currency} ${item.obligationKey}`.toLowerCase().includes(search.toLowerCase()));
  const selected = rows.find(item => item.obligationKey === selectedKey);
  const incomplete = run.coverage.filter(coverage => !coverage.isComplete);
  const columns = useMemo<TableWorkbenchColumn<BookReceiptQueueItem>[]>(() => [
    { id: "instrument", label: "Instrument / owner", rowHeader: true, wrap: true, render: item => <div><span className="font-medium">{item.displayName}</span><div className="text-xs text-muted-foreground">{item.flowType} · {item.currency} · <span>{item.owner || "Unassigned"}</span></div></div>, exportValue: item => `${item.displayName} / ${item.flowType} / ${item.currency} / ${item.owner || "Unassigned"}` },
    { id: "due", label: "Due / age", render: item => <div>{item.dueDate}<div className="text-xs text-muted-foreground">{item.agingDays} days after due</div></div>, exportValue: item => item.dueDate },
    { id: "status", label: "Status", wrap: true, render: item => <div className="space-y-1"><StatusBadge status={statuses[item.status]?.severity ?? "action"} label={statusLabel(item.status)} />{!item.evidenceComplete && item.status !== "IncompleteEvidence" ? <div className="text-xs">Incomplete evidence</div> : null}</div>, exportValue: item => statusLabel(item.status) },
    ...(["expectedAmount", "receivedAmount", "postedAmount", "remainingAmount"] as const).map((field, index) => ({ id: field, label: ["Expected", "Received", "Posted", "Remaining"][index]!, numeric: true, render: (item: BookReceiptQueueItem) => receiptAmount(item[field], item.currency), exportValue: (item: BookReceiptQueueItem) => item[field], exportType: "decimal" as const }))
  ], []);
  return <div className="space-y-3">
    <div className="flex flex-wrap items-center justify-between gap-2"><p className="text-sm font-medium">As of {run.asOfDate} · {run.items.length} obligations</p><StatusBadge status={incomplete.length || run.items.some(item => !item.evidenceComplete) ? "action" : "ready"} label={incomplete.length || run.items.some(item => !item.evidenceComplete) ? "Incomplete evidence" : "Evidence complete"} /></div>
    {incomplete.length ? <div role="status" className="rounded-[var(--radius-card)] border border-border p-3 text-sm"><p className="font-medium">Provider coverage is incomplete</p><ul className="list-disc pl-5">{incomplete.map((coverage, index) => <li key={`${coverage.area}:${index}`}>{coverage.area}: {coverage.reason || "Required evidence is unavailable."}</li>)}</ul></div> : null}
    <div className="flex flex-wrap gap-3">
      <label className="min-w-64 space-y-1 text-xs font-medium">Obligation status<Select value={status} aria-label="Receipt status" onChange={event => setStatus(event.target.value)}><option value="">All statuses</option>{Object.entries(statuses).map(([value, entry]) => <option key={value} value={value}>{entry.label}</option>)}<option value="evidence">Any incomplete evidence</option></Select></label>
      <label className="min-w-64 flex-1 space-y-1 text-xs font-medium">Find instrument, owner or currency<Input aria-label="Find receipt obligations" value={search} onChange={event => setSearch(event.target.value)} placeholder="Search this retained run" /></label>
    </div>
    <TableWorkbench workbenchId="book-income-receipts" sourceKey={`${run.scope.tenantId}:${run.scope.companyId}:${run.scope.ledgerBookId}:${run.runId}`} columns={columns} rows={rows} getRowId={item => item.obligationKey} getRowSelectAriaLabel={item => `Inspect ${item.displayName} ${item.flowType} receipt`} getRowAriaControls={() => detailId} getRowAriaExpanded={item => item.obligationKey === selected?.obligationKey} onRowSelect={item => setSelectedKey(item.obligationKey)} selectedRowId={selected?.obligationKey} emptyText={run.items.length ? "No obligations match these filters." : "No expected obligations were established. Review source coverage before concluding nothing is due."} ariaLabel="Book receipt obligations" sourceDescription={`Retained receipt run ${run.runId}, as of ${run.asOfDate}`} completePopulation exportContext={{ ledgerBookId: run.scope.ledgerBookId, runId: run.runId, asOfDate: run.asOfDate, currency: "Per obligation; no currency conversion", status: status || "All", search }} compactToolbar />
    {selected ? <IncomeReceiptDetails id={detailId} item={selected} bookId={run.scope.ledgerBookId} /> : <p className="text-xs text-muted-foreground">Select an obligation to inspect expectation history, receipt allocations, posted journals and source evidence.</p>}
    <TechnicalDetails label="Daily run provenance"><dl className="grid gap-2 text-xs sm:grid-cols-2">{Object.entries({ "Run": run.runId, "Engine": run.engineVersion, "Observation": run.sequence, "Input hash": run.inputHash, "Ledger book": run.scope.ledgerBookId, "Tenant": run.scope.tenantId, "Company": run.scope.companyId }).map(([label, value]) => <div key={label}><dt className="text-muted-foreground">{label}</dt><dd className="break-all">{value}</dd></div>)}</dl><ul className="mt-3 space-y-1 text-xs">{run.coverage.map((coverage, index) => <li key={`${coverage.area}:${index}`}>{coverage.area}: {coverage.isComplete ? "Complete" : "Incomplete"}{coverage.reason ? ` · ${coverage.reason}` : ""}</li>)}</ul></TechnicalDetails>
  </div>;
}
