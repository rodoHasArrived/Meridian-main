import { useEffect, useRef, useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { FormGrid } from "@/components/ui/form";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { describeApiError } from "@/lib/api-errors";
import { entityOperationEndpoints as endpoints, postEntityOperation } from "@/lib/api/entity-operations.api";
import { formatMoney } from "@/lib/format";
import type { ManualJournalEntryDraft } from "@/types";
import type { EntityOperationScope, IntercompanyAccountingArrangement, IntercompanyAccrualProposal, IntercompanyAccrualRequest } from "@/types/entity-operations";
import { EntityOperationTable, MutationFeedback, OperationError, OperationField, useEntityMutation } from "./accounting-screen.entity-operations.shared";

/**
 * Requests the governed loan-interest accrual for one approved LoanInterest profile. The operator selects
 * the principal profile and the window only; the rate, day count, principal and amount are server-resolved.
 * Drafting retains exactly the previewed computation in the source book's journal review and never posts.
 */
export function IntercompanyInterestAccrual({ scope, arrangement, arrangements }: {
  scope: EntityOperationScope; arrangement: IntercompanyAccountingArrangement; arrangements: IntercompanyAccountingArrangement[];
}) {
  const principalProfiles = arrangements.filter((item) => item.templateKind === "Loan" && item.canonicalAgreementId === arrangement.canonicalAgreementId);
  const [principalSelection, setPrincipalSelection] = useState("");
  const principalId = principalProfiles.some((item) => item.arrangementId === principalSelection) ? principalSelection : principalProfiles[0]?.arrangementId ?? "";
  const [accrualFrom, setAccrualFrom] = useState("");
  const [accrualTo, setAccrualTo] = useState("");
  const [preview, setPreview] = useState<{ request: IntercompanyAccrualRequest; proposal: IntercompanyAccrualProposal } | null>(null);
  const [previewing, setPreviewing] = useState(false);
  const [previewError, setPreviewError] = useState<string | null>(null);
  const [drafted, setDrafted] = useState<ManualJournalEntryDraft | null>(null);
  const active = useRef<AbortController | null>(null);
  const mutation = useEntityMutation();
  useEffect(() => () => active.current?.abort(), []);

  const request: IntercompanyAccrualRequest = { sourceBookId: scope.book.ledgerBookId, interestArrangementId: arrangement.arrangementId,
    principalArrangementId: principalId, accrualFrom, accrualTo };
  const requestKey = JSON.stringify(request);
  // A preview describes one exact selection; any change withdraws it rather than drafting a stale amount.
  const current = preview && JSON.stringify(preview.request) === requestKey ? preview.proposal : null;
  const computation = current?.computation;
  const ready = Boolean(current && computation?.status === "Ready" && current.evidenceDigest && current.draft);
  const windowEntered = Boolean(accrualFrom && accrualTo);
  const mapsBook = arrangement.partyMappings.some((mapping) => mapping.ledgerBookId === scope.book.ledgerBookId);

  async function runPreview(event: FormEvent) {
    event.preventDefault();
    if (previewing || mutation.busy || !principalId || !windowEntered) return;
    active.current?.abort();
    const controller = new AbortController();
    active.current = controller;
    setPreviewing(true); setPreviewError(null); setPreview(null); setDrafted(null);
    try {
      const proposal = await postEntityOperation<IntercompanyAccrualProposal>(endpoints.accrualPreview, request, { signal: controller.signal });
      if (!controller.signal.aborted) setPreview({ request, proposal });
    } catch (failure) {
      if (!controller.signal.aborted) {
        const display = describeApiError(failure, "The accrual preview could not be computed. No amount is shown.");
        setPreviewError([...new Set([display.summary, ...display.details])].join("\n"));
      }
    } finally {
      if (!controller.signal.aborted) setPreviewing(false);
    }
  }

  async function createDraft() {
    if (!current || !ready || mutation.busy) return;
    const result = await mutation.run<ManualJournalEntryDraft>(endpoints.accrualDrafts,
      { accrual: preview!.request, expectedEvidenceDigest: current.evidenceDigest },
      "Accrual draft retained in this book's journal review. Nothing was approved or posted; the counterpart is proposed to the other entity only after this journal posts.");
    if (result) setDrafted(result);
  }

  if (!scope.capabilities.canManageLedger) return <section className="space-y-2 rounded border border-border p-3" aria-label="Loan interest accrual">
    <h5 className="text-sm font-semibold">Accrue interest</h5>
    <p className="text-xs text-muted-foreground">Ledger management permission is required to preview or draft an interest accrual.</p></section>;

  return <section className="space-y-3 rounded border border-border p-3" aria-label="Loan interest accrual">
    <h5 className="text-sm font-semibold">Accrue interest</h5>
    <p className="text-xs text-muted-foreground">Fixed-rate simple interest is calculated by the server from the approved legal terms, this profile's approved revisions and the posted loan principal in {scope.book.displayName}. No rate, principal or amount is entered here. Fees, floating rates and compounding are not calculated.</p>
    {!mapsBook ? <p role="note" className="text-xs text-warning">This profile does not map {scope.book.displayName}. Open the lender's or borrower's own book to accrue its side.</p> : null}
    {principalProfiles.length === 0 ? <p role="note" className="text-xs text-warning">No loan principal profile of the same legal agreement is retained in this perimeter. Principal is read only from posted journals of a reviewed Loan profile.</p> : null}
    <form className="space-y-3" onSubmit={(event) => void runPreview(event)}><fieldset className="space-y-3" disabled={previewing || mutation.busy}>
      <FormGrid columns={3}>
        <OperationField label="Loan principal profile" value={principalId} onChange={setPrincipalSelection} required>
          {principalProfiles.length === 0 ? <option value="">No loan principal profile</option> : null}
          {principalProfiles.map((item) => <option key={item.arrangementId} value={item.arrangementId}>{item.arrangementReference} · {item.status}</option>)}
        </OperationField>
        <OperationField label="First accrued day" value={accrualFrom} onChange={setAccrualFrom} type="date" required />
        <OperationField label="Accrue up to (not including)" value={accrualTo} onChange={setAccrualTo} type="date" required hint="The journal is dated the last accrued day, the day before this date." />
      </FormGrid>
      <Button type="submit" variant="outline" disabled={!principalId || !windowEntered} disabledReason={!principalId ? "Select the loan principal profile." : "Enter the accrual window."}>Preview interest accrual</Button>
    </fieldset></form>
    <OperationError error={previewError} />
    {previewing ? <p role="status">Resolving approved terms and posted principal…</p> : null}
    {current && computation && !ready ? <div role="alert" className="space-y-1 rounded border border-warning/30 p-3 text-sm text-warning">
      <p className="font-semibold">Accrual unavailable. No amount is estimated.</p>
      <ul className="list-disc pl-5">{[...new Set(current.diagnostics.length > 0 ? current.diagnostics : computation.diagnostics)].map((reason) => <li key={reason}>{reason}</li>)}</ul>
    </div> : null}
    {current && computation && ready ? <div className="space-y-3">
      <p className="text-sm"><span className="font-semibold">{formatMoney(computation.amount ?? "", { context: "accounting", currency: computation.currency ?? "" })}</span> interest for {computation.accrualFrom} up to {computation.accrualTo} · journal date {current.evidence?.postingDate ?? current.draft!.accountingDate}</p>
      {current.evidence && current.evidence.functionalCurrency !== computation.currency ? <p className="text-sm">Booked as {formatMoney(current.evidence.functionalAmount, { context: "accounting", currency: current.evidence.functionalCurrency })} through the governed FX authority.</p> : null}
      <EntityOperationTable label="Interest accrual slices" rows={computation.slices} getRowId={(slice) => String(slice.sequence)} emptyText="No accrual slices."
        columns={[
          { id: "window", label: "Days", rowHeader: true, render: (slice) => `${slice.from} up to ${slice.to}` },
          { id: "principal", label: "Posted principal", numeric: true, render: (slice) => slice.principal },
          { id: "rate", label: "Annual rate", numeric: true, render: (slice) => slice.annualRate },
          { id: "basis", label: "Day count", render: (slice) => `${slice.dayCount} · ${slice.yearFraction}` },
          { id: "amount", label: "Unrounded interest", numeric: true, render: (slice) => slice.unroundedAmount },
          { id: "authority", label: "Legal version", render: (slice) => `Version ${slice.agreementVersion}` }
        ]} />
      <EntityOperationTable label="Proposed accrual journal lines" rows={current.draft!.lines} getRowId={(line) => line.lineId} emptyText="No proposed lines."
        columns={[
          { id: "account", label: "Account", rowHeader: true, render: (line) => line.accountPath },
          { id: "side", label: "Side", render: (line) => line.side },
          { id: "amount", label: "Functional amount", numeric: true, render: (line) => formatMoney(line.amount, { context: "accounting", currency: line.currency }) }
        ]} />
      <TechnicalDetails label="Accrual calculation evidence"><dl className="space-y-2 break-all text-xs">
        <div><dt>Calculation rule and kernel</dt><dd>{computation.ruleVersion} · {computation.kernelVersion}</dd></div>
        <div><dt>Evidence digest</dt><dd className="font-mono">{current.evidenceDigest}</dd></div>
        {current.evidence ? <><div><dt>Intercompany transaction</dt><dd>{current.evidence.transactionId}</dd></div>
          <div><dt>Opening posted principal</dt><dd>{current.evidence.principal.openingPrincipal} from {current.evidence.principal.openingJournalCount} posted journal(s); {current.evidence.principal.movements.length} movement(s) inside the window</dd></div></> : null}
        {computation.calendars.map((calendar) => <div key={calendar.calendarId}><dt>Calendar {calendar.calendarId}</dt><dd className="font-mono">{calendar.contentHash}</dd></div>)}
        {computation.slices.map((slice) => <div key={slice.sequence}><dt>Slice {slice.sequence} authority</dt><dd>Mapping revision {slice.accountingArrangementRevisionId} · legal payload <span className="font-mono">{slice.agreementPayloadHash}</span> · {slice.principalEvidenceReference}</dd></div>)}
      </dl></TechnicalDetails>
      <Button type="button" disabled={mutation.busy || Boolean(drafted)} disabledReason={drafted ? "This computation is already retained as a draft." : "Wait for the current accrual change to finish."} onClick={() => void createDraft()}>Create accrual draft for review</Button>
      <p className="text-xs text-muted-foreground">The draft enters ordinary journal review in this book. It is refused if the approved terms, profile, principal or FX inputs changed after this preview.</p>
    </div> : null}
    <MutationFeedback state={mutation} />
    {drafted ? <Button asChild variant="ghost" size="sm" className="underline underline-offset-4"><Link to={`/accounting/journal-entries?${new URLSearchParams({ ledgerBookId: drafted.ledgerBookId ?? scope.book.ledgerBookId, journalEntryId: drafted.journalEntryId })}`}>Open accrual draft {drafted.status} · version {drafted.version}</Link></Button> : null}
  </section>;
}
