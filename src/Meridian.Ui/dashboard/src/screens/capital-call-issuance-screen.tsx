import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { ApiError, describeApiError } from "@/lib/api-errors";
import * as api from "@/lib/api/private-capital.api";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { presentCapitalCallRunOutcome } from "@/screens/capital-call-issuance-screen.view-model";
import { CapitalCallPlanForm, CapitalCommitmentForm, CapitalFundingForm } from "@/screens/capital-call-issuance-screen.forms";
import { CapitalCallNoticeAction } from "@/screens/capital-call-notice-action";
import { CapitalCallLines, capitalAmount, capitalCallIsOverdue, capitalIsPositive, capitalJournalRoute, type CapitalMutation } from "@/screens/private-capital-presenters";
import type { CapitalCallIssuanceIntakeRunResult } from "@/lib/api/capital-call-issuance.api";
import type { LedgerBook, SessionInfo } from "@/types";
import type { PrivateCapitalCall, PrivateCapitalCommitment, PrivateCapitalNotice, PrivateCapitalWorkbench } from "@/types/private-capital";

export function CapitalCallIssuanceScreen({ session }: { session?: SessionInfo | null }) {
  return <CapitalScopeSelection key={`${session?.displayName}:${session?.role}:${session?.environment}`} />;
}

function CapitalScopeSelection() {
  const [params, setParams] = useSearchParams();
  const bookId = params.get("ledgerBookId") ?? "";
  const [books, setBooks] = useState<LedgerBook[]>([]);
  const [draftBook, setDraftBook] = useState(bookId);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [locked, setLocked] = useState(false);
  const [revision, setRevision] = useState(0);
  useEffect(() => { setDraftBook(bookId); setLocked(false); }, [bookId]);
  useEffect(() => {
    const controller = new AbortController(); setLoading(true); setError("");
    api.getPrivateCapitalBooks({ signal: controller.signal }).then(value => {
      if (!controller.signal.aborted) setBooks(value);
    }).catch(cause => {
      if (!controller.signal.aborted) { setBooks([]); setError(describeApiError(cause, "Ledger books could not load.").summary); }
    }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [revision]);
  const unapplied = draftBook !== bookId;
  const selectedBook = books.find(book => book.ledgerBookId === bookId);
  return <div className="space-y-4 p-4" data-testid="capital-call-issuance-screen">
    <div className="flex flex-wrap items-start justify-between gap-3"><div>
      <h2 className="text-lg font-semibold">Commitments and capital calls</h2>
      <p className="max-w-4xl text-sm text-muted-foreground">Review commitments, retain call allocations and match cash receipts to posted calls. Issuance, funding, journal approval and notice delivery each retain their own state.</p>
    </div><Link className="text-sm text-primary underline" to={WORKSTATION_ROUTE_CATALOG.accountingCapitalAccounts}>Capital accounts</Link></div>
    <h2 className="sr-only">Capital operations</h2>
    <Card><CardHeader><CardTitle>Operating book</CardTitle><CardDescription>Entity, fund, currency and authenticated ownership come from the retained book. Select a book before preparing capital activity.</CardDescription></CardHeader>
      <CardContent className="space-y-3"><form className="flex flex-wrap items-end gap-3" onSubmit={event => {
        event.preventDefault(); const next = new URLSearchParams(params); next.set("ledgerBookId", draftBook); setParams(next);
      }}><label className="grid gap-1 text-sm">Ledger book
          <select className="rounded border border-border bg-background p-2" value={draftBook} disabled={loading || locked}
            onChange={event => setDraftBook(event.target.value)}><option value="">Select a retained book</option>
            {books.map(book => <option key={book.ledgerBookId} value={book.ledgerBookId}>{book.displayName} · {book.baseCurrency} · {book.accountingBasis}</option>)}
          </select></label><Button type="submit" variant="outline" disabled={!draftBook || locked || loading || (!unapplied && !!bookId)}>Use book</Button>
          <Button type="button" variant="ghost" disabled={loading || locked} onClick={() => setRevision(value => value + 1)}>Reload books</Button>
        </form>
        {loading ? <p role="status">Loading retained ledger books…</p> : null}
        {error ? <StatusBanner role="alert" tone="danger" title="Book authority unavailable" detail={error} /> : null}
        {!loading && !error && books.length === 0 ? <p role="status">No retained ledger books are available. Configure a legal-entity book in Accounting before continuing.</p> : null}
        {unapplied ? <p role="status" className="text-sm text-warning">Apply the new book selection before reviewing or changing capital records.</p> : null}
      </CardContent></Card>
    {bookId && selectedBook && !error ? <fieldset disabled={unapplied || loading} className="min-w-0"><legend className="sr-only">Capital activity for selected book</legend>
      <CapitalBookWorkbench key={bookId} bookId={bookId} onLockedChange={setLocked} />
    </fieldset> : !loading && bookId && !error ? <StatusBanner tone="warning" title="Selected book is unavailable" detail="Select a book returned by the authenticated ledger catalog." /> : null}
  </div>;
}

function CapitalBookWorkbench({ bookId, onLockedChange }: { bookId: string; onLockedChange: (locked: boolean) => void }) {
  const [workbench, setWorkbench] = useState<PrivateCapitalWorkbench | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState("");
  const [uncertain, setUncertain] = useState(false);
  const [result, setResult] = useState<CapitalCallIssuanceIntakeRunResult | null>(null);
  const [selectedCall, setSelectedCall] = useState("");
  const reads = useRef<AbortController | null>(null);
  const writes = useRef<AbortController | null>(null);
  const pending = useRef<{ label: string; action: (signal: AbortSignal) => Promise<unknown>; refresh: boolean } | null>(null);
  const refresh = useCallback(async () => {
    reads.current?.abort(); const controller = new AbortController(); reads.current = controller;
    setLoading(true); setWorkbench(null); setError("");
    try { const value = await api.getPrivateCapitalWorkbench(bookId, { signal: controller.signal });
      if (!controller.signal.aborted) setWorkbench(value);
    } catch (cause) { if (!controller.signal.aborted) setError(describeApiError(cause, "Capital authority could not load.").summary);
    } finally { if (!controller.signal.aborted) setLoading(false); }
  }, [bookId]);
  useEffect(() => { void refresh(); return () => { reads.current?.abort(); writes.current?.abort(); }; }, [refresh]);
  useEffect(() => { onLockedChange(!!busy || uncertain); return () => onLockedChange(false); }, [busy, uncertain, onLockedChange]);
  const mutate: CapitalMutation = async (label, action, options) => {
    if (writes.current) return false;
    const command = pending.current ?? { label, action, refresh: options?.refresh !== false }; pending.current = command;
    const controller = new AbortController(); writes.current = controller;
    setBusy(command.label); setError(""); setMessage(""); setResult(null);
    try {
      const response = await command.action(controller.signal);
      if (controller.signal.aborted) return false;
      if (response && typeof response === "object" && "readinessBlockers" in response) setResult(response as CapitalCallIssuanceIntakeRunResult);
      pending.current = null; setUncertain(false); setMessage(`${command.label} completed. Review the retained record and any server-reported blockers.`);
      if (command.refresh) await refresh(); return true;
    } catch (cause) {
      if (!controller.signal.aborted) {
        setError(describeApiError(cause, `${command.label} failed.`).summary);
        const unknown = !(cause instanceof ApiError) || cause.status >= 500;
        setUncertain(unknown); if (!unknown) pending.current = null;
      }
      return false;
    } finally { if (writes.current === controller) { writes.current = null; setBusy(""); } }
  };
  const locked = loading || !!busy || uncertain;
  const call = workbench?.calls.find(row => row.callId === selectedCall) ?? null;
  return <div className="space-y-4">
    <div className="flex flex-wrap items-center justify-between gap-3"><p className="text-sm text-muted-foreground">{workbench ? `Retained capital activity · ${workbench.scope.currency}` : "Loading book authority"}</p>
      {workbench ? <TechnicalDetails label="Operating authority"><p className="break-all text-xs">Fund {workbench.scope.fundProfileId} · Entity {workbench.scope.entityId} · Book {workbench.scope.ledgerBookId}</p></TechnicalDetails> : null}
      <Button variant="outline" disabled={loading || !!busy} onClick={() => void refresh()}>Refresh retained records</Button></div>
    {loading ? <p role="status">Loading commitment and posting authority…</p> : null}
    {busy ? <p role="status" className="text-sm">{busy} is in progress…</p> : null}
    {error ? <StatusBanner role="alert" tone="danger" title="Capital activity unavailable" detail={error} /> : null}
    {message ? <p role="status" className="text-sm">{message}</p> : null}
    {uncertain ? <StatusBanner role="alert" tone="warning" title="Write outcome is unconfirmed" detail={<>
      Inspect retained records or retry the same request. Keep the original commitment, call or receipt identity.
      <Button className="ml-3" size="sm" disabled={!!busy} onClick={() => { if (pending.current) void mutate(pending.current.label, pending.current.action); }}>Retry same request</Button>
    </>} /> : null}
    {result ? <CapitalIntakeResult result={result} bookId={bookId} /> : null}
    {workbench ? <>
      <CapitalCommitmentForm bookId={bookId} locked={locked} mutate={mutate} />
      <CommitmentRegister rows={workbench.commitments} currency={workbench.scope.currency} bookId={bookId} locked={locked} mutate={mutate} />
      <CapitalCallPlanForm key={workbench.generatedAtUtc} bookId={bookId} commitments={workbench.commitments} locked={locked} mutate={mutate} onRetained={setSelectedCall} />
      <Card><CardHeader><CardTitle>Retained calls</CardTitle><CardDescription>Plans reserve no posted cash. Posted issuance creates the receivable; posted funding reduces it.</CardDescription></CardHeader><CardContent className="space-y-3">
        {workbench.calls.length === 0 ? <p className="text-sm" role="status">No capital calls retained for this book.</p> : <div className="flex flex-wrap gap-2">{workbench.calls.map(row =>
          <Button key={row.callId} variant={selectedCall === row.callId ? "default" : "outline"} disabled={locked} onClick={() => setSelectedCall(row.callId)}>{row.callId} · {capitalAmount(row.amountToCall, row.scope.currency)}{capitalCallIsOverdue(row, workbench.generatedAtUtc) ? <Badge variant="warning">Overdue receivable</Badge> : null}</Button>)}</div>}
        {call ? <CallDetail key={call.callId} call={call} generatedAtUtc={workbench.generatedAtUtc} notices={workbench.notices} locked={locked} mutate={mutate} /> : workbench.calls.length ? <p className="text-sm">Select a retained call to inspect allocation, issuance and cash.</p> : null}
      </CardContent></Card>
      <PostedCapitalReceipts workbench={workbench} />
    </> : null}
  </div>;
}

function CommitmentRegister({ rows, currency, bookId, locked, mutate }: { rows: PrivateCapitalCommitment[]; currency: string; bookId: string; locked: boolean; mutate: CapitalMutation }) {
  const [reviewed, setReviewed] = useState<string | null>(null);
  const [confirmed, setConfirmed] = useState(false);
  const row = rows.find(value => `${value.commitmentId}:${value.revision}` === reviewed);
  const columns: DenseDataTableColumn<PrivateCapitalCommitment>[] = [
    { id: "commitment", label: "Commitment / investor", render: value => <span className="font-medium">{value.commitmentId}<span className="block text-xs text-muted-foreground">{value.investorId} · {value.capitalAccountId}</span></span> },
    { id: "revision", label: "Revision / status", render: value => <span>v{value.revision} <Badge variant={value.approvalStatus === 1 ? "success" : "outline"}>{value.approvalStatus === 1 ? "Approved" : "Draft"}</Badge>{!value.isActive ? " · Inactive" : ""}</span> },
    ...([["totalCommitment", "Total"], ["calledAmount", "Called"], ["fundedAmount", "Funded"], ["uncalledAmount", "Uncalled"]] as const)
      .map(([field, label]) => ({ id: field, label, numeric: true, className: "whitespace-nowrap", render: (value: PrivateCapitalCommitment) => <span className="font-mono">{capitalAmount(value[field], currency)}</span> })),
    { id: "review", label: "Review", render: value => <Button size="sm" variant="outline" disabled={locked} onClick={() => { setReviewed(`${value.commitmentId}:${value.revision}`); setConfirmed(false); }}>Review {value.commitmentId}</Button> }
  ];
  return <Card><CardHeader><CardTitle>Retained commitment register</CardTitle><CardDescription>Draft revisions become eligible for call allocation only after an explicit review and server approval.</CardDescription></CardHeader><CardContent className="space-y-3">
    <DenseDataTable rows={rows} columns={columns} getRowId={value => `${value.commitmentId}:${value.revision}`}
      getRowAriaLabel={value => `Commitment ${value.commitmentId}, revision ${value.revision}, investor ${value.investorId}`}
      ariaLabel="Commitment register and posted capital balances" caption="Commitment register and posted capital balances"
      emptyText="No commitments retained for this book." />
    {row ? <section className="space-y-2 rounded border border-border p-3" aria-label="Commitment review">
      <h3 className="text-sm font-semibold">Review {row.commitmentId} · revision {row.revision}</h3>
      <p className="text-sm">Effective {row.effectiveFrom}; investment period ends {row.investmentPeriodEndDate ?? "not specified"}. Prepared by {row.createdBy}; approval {row.approvedBy ? `by ${row.approvedBy}` : "pending"}.</p>
      <ul className="list-disc break-all pl-5 text-xs">{row.evidenceLinks.map(link => <li key={link}>{link}</li>)}</ul>
      {row.approvalStatus === 0 ? <><label className="flex gap-2 text-sm"><input type="checkbox" checked={confirmed} disabled={locked} onChange={event => setConfirmed(event.target.checked)} />I reviewed the retained terms, investor, scope and evidence for this revision.</label>
        <Button disabled={locked || !confirmed} disabledReason={locked ? "Wait for the current operation or resolve its unconfirmed outcome." : !confirmed ? "Review the retained terms, investor, scope and evidence first." : undefined} onClick={() => void mutate("Commitment approval", signal => api.approvePrivateCapitalCommitment(bookId, row.commitmentId, row.revision, { signal }))}>Approve reviewed revision</Button></> : <p className="text-sm">This revision is already approved.</p>}
    </section> : null}
  </CardContent></Card>;
}

function CallDetail({ call, generatedAtUtc, notices, locked, mutate }: { call: PrivateCapitalCall; generatedAtUtc: string; notices: PrivateCapitalNotice[]; locked: boolean; mutate: CapitalMutation }) {
  const [confirmIssuance, setConfirmIssuance] = useState(false);
  const callNotices = (notices ?? []).filter(notice => notice.callId === call.callId);
  return <section className="space-y-3" aria-label={`Call ${call.callId}`}>
    <p className="text-sm">Notice {call.noticeDate} · Due {call.dueDate} · {call.purpose ?? "No purpose specified"}</p>
    {capitalCallIsOverdue(call, generatedAtUtc) ? <StatusBanner role="status" tone="warning" title="Overdue receivable" detail={`A posted receivable remains outstanding after ${call.dueDate}. Evaluated at server observation ${generatedAtUtc}.`} /> : null}
    <TechnicalDetails label="Retained plan and preparation"><p className="break-all text-xs">Period {call.periodId} · Plan {call.planHash} · {call.createdBy} · {call.createdAtUtc}</p></TechnicalDetails>
    <CapitalCallLines call={call} />
    <div className="rounded border border-border p-3 space-y-2"><p className="text-sm">Queue issuance drafts for the retained allocation. Review, approval and posting take place in Journal entries.</p>
      <label className="flex gap-2 text-sm"><input type="checkbox" checked={confirmIssuance} disabled={locked} onChange={event => setConfirmIssuance(event.target.checked)} />I reviewed this retained call allocation.</label>
      <Button disabled={locked || !confirmIssuance} disabledReason={locked ? "Wait for the current operation or resolve its unconfirmed outcome." : !confirmIssuance ? "Review and confirm the retained call allocation first." : undefined} onClick={() => void mutate("Issuance draft intake", signal => api.intakePrivateCapitalCall(call.scope.ledgerBookId, call.callId, { signal }))}>Queue reviewed issuance drafts</Button>
      <Link className="ml-3 text-sm text-primary underline" to={capitalJournalRoute(call.scope.ledgerBookId, call.periodId)}>Open journal approval queue</Link>
    </div>
    <CapitalFundingForm call={call} locked={locked} mutate={mutate} />
    <section className="space-y-2" aria-label="Investor notices"><h3 className="text-sm font-semibold">Investor notices</h3>
      <p className="text-sm text-muted-foreground">Generate a draft notice from posted issuance. Regenerate creates a new notice run; retrying an unconfirmed request recovers its original run. Release and delivery use the governed Reporting workflow and remain separate from cash.</p>
      <div className="flex flex-wrap gap-2">{[...new Set(call.lines.filter(line => capitalIsPositive(line.issuedAmount)).map(line => line.investorId))].map(investorId => {
        const regenerate = callNotices.some(notice => notice.investorId === investorId);
        return <CapitalCallNoticeAction key={investorId} scope={call.scope} callId={call.callId} investorId={investorId} hasNotice={regenerate} locked={locked} mutate={mutate} />;
      })}</div>
      {callNotices.map(notice => <div key={notice.runId} className="rounded border border-border p-2 text-sm">
        <p>{notice.investorId} · Governance: {notice.governanceState} · Delivery: {notice.deliveryState}</p>
        <p className="text-xs text-muted-foreground">Package {notice.packageId ?? "not released"}</p>
        {/^\/reporting(?:\/|\?|$)/.test(notice.route) ? <Link className="text-primary underline" to={notice.route}>Inspect notice run {notice.runId}</Link> : <span>Reporting route unavailable</span>}
      </div>)}
      {!callNotices.length ? <p className="text-sm">No retained notice run for this call. No delivery is inferred.</p> : null}
    </section>
  </section>;
}

function CapitalIntakeResult({ result, bookId }: { result: CapitalCallIssuanceIntakeRunResult; bookId: string }) {
  const outcome = presentCapitalCallRunOutcome(result);
  return <Card><CardContent className="space-y-3 pt-4"><StatusBanner role="status" tone={outcome.tone} title={outcome.title} detail={outcome.detail} />
    {outcome.blockers.length ? <ul role="alert" className="list-disc pl-5 text-sm text-danger">{outcome.blockers.map(reason => <li key={reason}>{reason}</li>)}</ul> : null}
    {Object.entries(result.evidenceAssessments).map(([key, assessment]) => <details key={key} className="text-sm">
      <summary>Evidence {assessment.quality}{assessment.requiresInvestigation ? " · Investigation required" : ""}</summary>
      <p>{assessment.summary}</p>
      {assessment.reasons.length ? <ul className="list-disc pl-5">{assessment.reasons.map(reason => <li key={reason}>{reason}</li>)}</ul> : null}
      <ul className="break-all text-xs">{assessment.evidenceLinks.map(link => <li key={link}>{link}</li>)}</ul>
    </details>)}
    {result.intake.created.map(draft => <p className="text-sm" key={draft.journalEntryId}><Badge variant="outline">{draft.status}</Badge>{" "}<Link className="text-primary underline" to={capitalJournalRoute(bookId, "", draft.journalEntryId)}>{draft.memo} · {draft.journalEntryId}</Link></p>)}
    {result.producerSkips.map((skip, index) => <p key={index} className="text-sm">{skip.subject}: {skip.reason}</p>)}
    {result.intake.skipped.map((skip, index) => <p key={index} className="text-sm">{skip.journalEntryId}: {skip.reason}</p>)}
  </CardContent></Card>;
}

function PostedCapitalReceipts({ workbench }: { workbench: PrivateCapitalWorkbench }) {
  return <Card><CardHeader><CardTitle>Posted capital activity</CardTitle><CardDescription>Only durable posting receipts establish called or funded balances. Funding receipt identity and bank line remain inspectable.</CardDescription></CardHeader><CardContent>
    {workbench.receipts.length === 0 ? <p role="status" className="text-sm">No posted capital activity for this book.</p> : <ul className="space-y-3">{workbench.receipts.map(receipt => <li key={receipt.postingId} className="rounded border border-border p-3 text-sm">
      <div className="flex flex-wrap items-center gap-2"><Badge variant="success">Posted</Badge><span>{receipt.kind} · {receipt.installmentId} · {capitalAmount(receipt.amount, receipt.scope.currency)}</span></div>
      <p className="mt-1 text-xs">Effective {receipt.effectiveDate}</p>
      <TechnicalDetails label="Posting references"><p className="break-all text-xs">Receipt {receipt.fundingReceiptId ?? "Issuance"} · Bank line {receipt.bankLineId ?? "Not applicable"} · Journal {receipt.journalEntryId}</p></TechnicalDetails>
      <Link className="text-xs text-primary underline" to={capitalJournalRoute(receipt.scope.ledgerBookId, "", receipt.journalEntryId)}>Inspect posted journal</Link>
    </li>)}</ul>}
  </CardContent></Card>;
}
