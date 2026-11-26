import { useEffect, useRef, useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { FormGrid } from "@/components/ui/form";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { describeApiError } from "@/lib/api-errors";
import { entityOperationEndpoints as endpoints, postEntityOperation } from "@/lib/api/entity-operations.api";
import { formatMoney } from "@/lib/format";
import type { ManualJournalEntryDraft } from "@/types";
import type { EntityOperationScope, IntercompanyAccountingArrangement } from "@/types/entity-operations";
import type { IntercompanyFeeAccrualProposal, IntercompanyFeeAccrualRequest, IntercompanyFeeAccrualSlice } from "@/types/intercompany-fees";
import { EntityOperationTable, MutationFeedback, OperationError, OperationField, useEntityMutation } from "./accounting-screen.entity-operations.shared";

const basisLabel = (slice: IntercompanyFeeAccrualSlice) => slice.basis === "PerAnnum"
  ? `One year (per annum) · ${slice.dayCount ?? "No day count"}` : slice.basis === "PerPeriod" ? "Each schedule period" : slice.basis;

/** The server derives direction from the agreement's declared roles; the operator never chooses it. */
function directionText(sourceRole: string | undefined) {
  if (sourceRole === "Receivable") return "Service provider side: this book records the fee receivable and fee revenue.";
  if (sourceRole === "Payable") return "Recipient side: this book records the fee expense and fee payable.";
  return null;
}

/**
 * Requests the governed fixed management-fee accrual (rule intercompany-fee-v1) for one approved ManagementFee
 * profile. The operator selects the window only; the declared fee, basis, schedule, direction and amount are
 * server-resolved. Drafting retains exactly the previewed computation in the source book's journal review and never posts.
 */
export function IntercompanyManagementFeeAccrual({ scope, arrangement }: {
  scope: EntityOperationScope; arrangement: IntercompanyAccountingArrangement;
}) {
  const [accrualFrom, setAccrualFrom] = useState("");
  const [accrualTo, setAccrualTo] = useState("");
  const [preview, setPreview] = useState<{ request: IntercompanyFeeAccrualRequest; proposal: IntercompanyFeeAccrualProposal } | null>(null);
  const [previewing, setPreviewing] = useState(false);
  const [previewError, setPreviewError] = useState<string | null>(null);
  const [drafted, setDrafted] = useState<ManualJournalEntryDraft | null>(null);
  const active = useRef<AbortController | null>(null);
  const mutation = useEntityMutation();
  useEffect(() => () => active.current?.abort(), []);

  const request: IntercompanyFeeAccrualRequest = { sourceBookId: scope.book.ledgerBookId, feeArrangementId: arrangement.arrangementId, accrualFrom, accrualTo };
  const requestKey = JSON.stringify(request);
  // A preview describes one exact selection; any change withdraws it rather than drafting a stale amount.
  const current = preview && JSON.stringify(preview.request) === requestKey ? preview.proposal : null;
  const computation = current?.computation;
  const ready = Boolean(current && computation?.status === "Ready" && current.evidenceDigest && current.draft);
  const windowEntered = Boolean(accrualFrom && accrualTo);
  const mapsBook = arrangement.partyMappings.some((mapping) => mapping.ledgerBookId === scope.book.ledgerBookId);
  const direction = directionText(current?.evidence?.sourceRole);

  async function runPreview(event: FormEvent) {
    event.preventDefault();
    if (previewing || mutation.busy || !windowEntered) return;
    active.current?.abort();
    const controller = new AbortController();
    active.current = controller;
    setPreviewing(true); setPreviewError(null); setPreview(null); setDrafted(null);
    try {
      const proposal = await postEntityOperation<IntercompanyFeeAccrualProposal>(endpoints.feePreview, request, { signal: controller.signal });
      if (!controller.signal.aborted) setPreview({ request, proposal });
    } catch (failure) {
      if (!controller.signal.aborted) {
        const display = describeApiError(failure, "The fee preview could not be computed. No amount is shown.");
        setPreviewError([...new Set([display.summary, ...display.details])].join("\n"));
      }
    } finally {
      if (!controller.signal.aborted) setPreviewing(false);
    }
  }

  async function createDraft() {
    if (!current || !ready || mutation.busy) return;
    const result = await mutation.run<ManualJournalEntryDraft>(endpoints.feeDrafts,
      { accrual: preview!.request, expectedEvidenceDigest: current.evidenceDigest },
      "Fee draft retained in this book's journal review. Nothing was approved or posted; the counterpart is proposed to the other entity only after this journal posts.");
    if (result) setDrafted(result);
  }

  if (!scope.capabilities.canManageLedger) return <section className="space-y-2 rounded border border-border p-3" aria-label="Management fee accrual">
    <h5 className="text-sm font-semibold">Accrue fee</h5>
    <p className="text-xs text-muted-foreground">Ledger management permission is required to preview or draft a management fee.</p></section>;

  return <section className="space-y-3 rounded border border-border p-3" aria-label="Management fee accrual">
    <h5 className="text-sm font-semibold">Accrue fee</h5>
    <p className="text-xs text-muted-foreground">The fixed management fee is calculated by the server from the approved legal terms, their declared fee basis and schedule, and this profile's approved revisions. No fee, basis or amount is entered here, and the direction comes from the agreement's declared roles. Percentage fees are not calculated. Nothing posts: each side reviews its own books.</p>
    {!mapsBook ? <p role="note" className="text-xs text-warning">This profile does not map {scope.book.displayName}. Open the service provider's or recipient's own book to accrue its side.</p> : null}
    <form className="space-y-3" onSubmit={(event) => void runPreview(event)}><fieldset className="space-y-3" disabled={previewing || mutation.busy}>
      <FormGrid columns={2}>
        <OperationField label="First accrued day" value={accrualFrom} onChange={setAccrualFrom} type="date" required />
        <OperationField label="Accrue up to (not including)" value={accrualTo} onChange={setAccrualTo} type="date" required hint="The journal is dated the last accrued day, the day before this date." />
      </FormGrid>
      <Button type="submit" variant="outline" disabled={!windowEntered} disabledReason="Enter the fee window.">Preview management fee</Button>
    </fieldset></form>
    <OperationError error={previewError} />
    {previewing ? <p role="status">Resolving approved terms and fee schedule…</p> : null}
    {current && computation && !ready ? <div role="alert" className="space-y-1 rounded border border-warning/30 p-3 text-sm text-warning">
      <p className="font-semibold">Fee unavailable. No amount is estimated.</p>
      <ul className="list-disc pl-5">{[...new Set(current.diagnostics.length > 0 ? current.diagnostics : computation.diagnostics)].map((reason) => <li key={reason}>{reason}</li>)}</ul>
    </div> : null}
    {current && computation && ready ? <div className="space-y-3">
      <p className="text-sm"><span className="font-semibold">{formatMoney(computation.amount ?? "", { context: "accounting", currency: computation.currency ?? "" })}</span> management fee for {computation.accrualFrom} up to {computation.accrualTo} · journal date {current.evidence?.postingDate ?? current.draft!.accountingDate} · rule {computation.ruleVersion}</p>
      {direction ? <p className="text-sm">{direction}</p> : null}
      {current.evidence && current.evidence.functionalCurrency !== computation.currency ? <p className="text-sm">Booked as {formatMoney(current.evidence.functionalAmount, { context: "accounting", currency: current.evidence.functionalCurrency })} through the governed FX authority.</p> : null}
      <EntityOperationTable label="Management fee slices" rows={computation.slices} getRowId={(slice) => String(slice.sequence)} emptyText="No fee slices."
        columns={[
          { id: "window", label: "Days", rowHeader: true, render: (slice) => `${slice.from} up to ${slice.to}` },
          { id: "period", label: "Schedule period", render: (slice) => `Period ${slice.schedulePeriodSequence} · ${slice.periodStart} up to ${slice.periodEnd}` },
          { id: "basis", label: "Fee basis", render: basisLabel },
          { id: "declared", label: "Declared fee", numeric: true, render: (slice) => slice.declaredFee },
          { id: "fraction", label: "Fraction earned", numeric: true, render: (slice) => slice.fraction },
          { id: "amount", label: "Unrounded fee", numeric: true, render: (slice) => slice.unroundedAmount },
          { id: "authority", label: "Legal version", render: (slice) => `Version ${slice.agreementVersion}` }
        ]} />
      <EntityOperationTable label="Proposed fee journal lines" rows={current.draft!.lines} getRowId={(line) => line.lineId} emptyText="No proposed lines."
        columns={[
          { id: "account", label: "Account", rowHeader: true, render: (line) => line.accountPath },
          { id: "side", label: "Side", render: (line) => line.side },
          { id: "amount", label: "Functional amount", numeric: true, render: (line) => formatMoney(line.amount, { context: "accounting", currency: line.currency }) }
        ]} />
      <TechnicalDetails label="Fee calculation evidence"><dl className="space-y-2 break-all text-xs">
        <div><dt>Calculation rule and kernel</dt><dd>{computation.ruleVersion} · {computation.kernelVersion}</dd></div>
        {computation.unroundedAmount ? <div><dt>Unrounded total</dt><dd>{computation.unroundedAmount}</dd></div> : null}
        <div><dt>Evidence digest</dt><dd className="font-mono">{current.evidenceDigest}</dd></div>
        {current.evidence ? <div><dt>Intercompany transaction</dt><dd>{current.evidence.transactionId} · source role {current.evidence.sourceRole}</dd></div> : null}
        {computation.calendars.map((calendar) => <div key={calendar.calendarId}><dt>Calendar {calendar.calendarId}</dt><dd className="font-mono">{calendar.contentHash}</dd></div>)}
        {computation.slices.map((slice) => <div key={slice.sequence}><dt>Slice {slice.sequence} authority</dt><dd>Mapping revision {slice.accountingArrangementRevisionId} · legal payload <span className="font-mono">{slice.agreementPayloadHash}</span></dd></div>)}
      </dl></TechnicalDetails>
      <Button type="button" disabled={mutation.busy || Boolean(drafted)} disabledReason={drafted ? "This computation is already retained as a draft." : "Wait for the current fee change to finish."} onClick={() => void createDraft()}>Create fee draft for review</Button>
      <p className="text-xs text-muted-foreground">The draft enters ordinary journal review in this book. It is refused if the approved terms, profile or FX inputs changed after this preview. The counterpart is proposed to the other entity only after this journal posts.</p>
    </div> : null}
    <MutationFeedback state={mutation} />
    {drafted ? <Button asChild variant="ghost" size="sm" className="underline underline-offset-4"><Link to={`/accounting/journal-entries?${new URLSearchParams({ ledgerBookId: drafted.ledgerBookId ?? scope.book.ledgerBookId, journalEntryId: drafted.journalEntryId })}`}>Open fee draft {drafted.status} · version {drafted.version}</Link></Button> : null}
  </section>;
}
