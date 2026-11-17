import { useState } from "react";
import { Button } from "@/components/ui/button";
import { FormGrid } from "@/components/ui/form";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { entityOperationEndpoints as endpoints } from "@/lib/api/entity-operations.api";
import type { EntityOperationScope, IntercompanyAccountingArrangement } from "@/types/entity-operations";
import type {
  IntercompanyBalanceConfirmation, IntercompanyConfirmationAction, IntercompanyConfirmationParty, IntercompanyConfirmationSummary
} from "@/types/intercompany-confirmations";
import { EntityOperationTable, MutationFeedback, OperationError, OperationField, today, useEntityData, useEntityMutation } from "./accounting-screen.entity-operations.shared";

const statusText: Record<IntercompanyBalanceConfirmation["status"], string> = {
  Proposed: "Awaiting both sides", ConfirmedByPartyOne: "Party one confirmed", ConfirmedByPartyTwo: "Party two confirmed",
  Confirmed: "Confirmed by both sides", Disputed: "Disputed", Resolved: "Dispute resolved · propose again"
};

/**
 * Reciprocal balance confirmation for the selected book. Both sides' balances are pinned from posted journals by the
 * server; this book's operator confirms or disputes only this book's side. Advisory: close readiness is unchanged.
 */
export function EntityBalanceConfirmations({ scope }: { scope: EntityOperationScope }) {
  const [asOf, setAsOf] = useState(today());
  const [request, setRequest] = useState<string | null>(null);
  const [profileId, setProfileId] = useState("");
  const [reason, setReason] = useState("");
  const summary = useEntityData<IntercompanyConfirmationSummary>(request);
  const profiles = useEntityData<IntercompanyAccountingArrangement[]>(request && scope.capabilities.canManageLedger
    ? `${endpoints.accountingArrangements}?${new URLSearchParams({ bookId: scope.book.ledgerBookId, asOf })}` : null);
  const mutation = useEntityMutation();
  const book = scope.book.ledgerBookId;
  const side = (item: IntercompanyBalanceConfirmation): IntercompanyConfirmationParty => item.partyOne.ledgerBookId === book ? "PartyOne" : "PartyTwo";
  const ownSideConfirmed = (item: IntercompanyBalanceConfirmation) => item.status === "Confirmed" || item.status === (side(item) === "PartyOne" ? "ConfirmedByPartyOne" : "ConfirmedByPartyTwo");
  const selectedProfile = profiles.data?.some((item) => item.arrangementId === profileId) ? profileId : profiles.data?.[0]?.arrangementId ?? "";

  function load() {
    const path = endpoints.confirmationSummary(book, asOf);
    if (path === request) summary.refresh(); else setRequest(path);
  }

  async function propose() {
    const body = { accountingArrangementId: selectedProfile, asOf };
    const result = await mutation.run<IntercompanyBalanceConfirmation>(endpoints.confirmations, { requestId: mutation.identity(body), ...body },
      "Both sides' posted balances are pinned for confirmation. Nothing was posted.");
    if (result) summary.refresh();
  }

  async function act(item: IntercompanyBalanceConfirmation, action: IntercompanyConfirmationAction) {
    const command = { confirmationId: item.confirmationId, action, party: action === "Resolve" ? null : side(item),
      expectedVersion: item.version, expectedBalanceDigest: item.balanceDigest, reason: action === "Confirm" ? null : reason.trim() };
    const result = await mutation.run<IntercompanyBalanceConfirmation>(endpoints.confirmationActions(item.confirmationId),
      { requestId: mutation.identity(command), ...command },
      action === "Confirm" ? "This book's side confirmed the pinned balances." : action === "Dispute" ? "Dispute retained with its reason." : "Dispute resolved. Propose the corrected balances for confirmation.");
    if (result) { setReason(""); summary.refresh(); }
  }

  return <section className="space-y-4" aria-label="Reciprocal balance confirmation"><h3 className="font-semibold">Reciprocal balance confirmation</h3>
    <p className="text-sm text-muted-foreground">Each side confirms the due-to/due-from balance the other side holds, from posted journals only. Confirmation status is advisory: it appears in close readiness and never blocks close or posting.</p>
    <form onSubmit={(event) => { event.preventDefault(); load(); }} className="space-y-3"><FormGrid columns={3}>
      <OperationField label="Confirmation as of" value={asOf} onChange={(value) => { setAsOf(value); setRequest(null); }} type="date" required />
      <p className="self-center text-sm">{scope.book.displayName} · {scope.book.baseCurrency}</p></FormGrid>
      <Button type="submit" variant="outline" disabled={!scope.capabilities.canViewLedger || summary.loading || !asOf}>Load confirmations</Button></form>
    {!scope.capabilities.canViewLedger ? <p className="text-sm text-muted-foreground">Ledger report access is required to inspect balance confirmations.</p> : null}
    <OperationError error={summary.error} />{summary.loading ? <p role="status">Reading posted balances and retained confirmations…</p> : null}
    {summary.data ? <>
      <StatusBanner title={summary.data.findings.length ? `${summary.data.findings.length} advisory finding(s)` : "All agreement balances confirmed"}
        tone={summary.data.findings.length ? "warning" : "info"} detail={`As of ${summary.data.asOf} · advisory only; close eligibility is unchanged.`} />
      {summary.data.findings.length ? <ul className="space-y-2" aria-label="Balance confirmation findings">{summary.data.findings.map((finding) =>
        <li key={`${finding.code}:${finding.accountingArrangementId}`} className="rounded border border-border p-3 text-sm"><p className="font-semibold">{finding.message}</p><p className="break-all text-muted-foreground">{finding.code} · accounting profile {finding.accountingArrangementId}</p></li>)}</ul> : null}
      <EntityOperationTable label="Reciprocal balance confirmations" rows={summary.data.confirmations} getRowId={(row) => row.confirmationId}
        emptyText="No confirmation has been proposed for this date." columns={[
          { id: "profile", label: "Accounting profile", rowHeader: true, render: (row) => <span className="break-all">{row.accountingArrangementId}</span> },
          { id: "status", label: "Status", render: (row) => `${statusText[row.status]} · version ${row.version}` },
          { id: "one", label: "Party one balance", render: (row) => `${row.partyOne.transactionSignedBalance} ${row.partyOne.transactionCurrency}${row.partyOne.ledgerBookId === book ? " (this book)" : ""}` },
          { id: "two", label: "Party two balance", render: (row) => `${row.partyTwo.transactionSignedBalance} ${row.partyTwo.transactionCurrency}${row.partyTwo.ledgerBookId === book ? " (this book)" : ""}` },
          { id: "net", label: "Net", render: (row) => `${row.netTransactionBalance} ${row.partyOne.transactionCurrency}` },
          { id: "actions", label: "This book's side", render: (row) => !scope.capabilities.canManageLedger ? "Read only" : <div className="flex flex-wrap gap-2">
            <Button type="button" size="sm" variant="outline" disabled={mutation.busy || row.status === "Disputed" || row.status === "Resolved" || ownSideConfirmed(row)}
              disabledReason="This side has already confirmed, or the record is disputed or resolved." onClick={() => void act(row, "Confirm")}>Confirm this book's balance</Button>
            <Button type="button" size="sm" variant="outline" disabled={mutation.busy || !reason.trim() || row.status === "Confirmed" || row.status === "Disputed" || row.status === "Resolved"}
              disabledReason="Enter a reason; only an open proposal can be disputed." onClick={() => void act(row, "Dispute")}>Dispute</Button>
            <Button type="button" size="sm" variant="outline" disabled={mutation.busy || !reason.trim() || row.status !== "Disputed"}
              disabledReason="Enter a reason; only a disputed record can be resolved." onClick={() => void act(row, "Resolve")}>Resolve dispute</Button>
          </div> },
          { id: "history", label: "History", render: (row) => <TechnicalDetails label={`${row.events.length} retained step(s)`}><ul className="space-y-1 break-all text-sm">{row.events.map((step) =>
            <li key={step.eventId}>{step.recordedAt} · {step.action}{step.party ? ` · ${step.party}` : ""} · {step.actor}{step.reason ? ` · ${step.reason}` : ""}</li>)}</ul><p className="break-all text-sm">Balance digest {row.balanceDigest}</p></TechnicalDetails> }
        ]} />
      {scope.capabilities.canManageLedger ? <div className="space-y-3">
        <OperationField label="Dispute or resolution reason" value={reason} onChange={setReason} hint="Required to dispute or resolve; recorded with the step." />
        <FormGrid columns={2}><OperationField label="Accounting profile to confirm" value={selectedProfile} onChange={setProfileId}>
          {profiles.data?.length ? profiles.data.map((item) => <option key={item.arrangementId} value={item.arrangementId}>{item.arrangementReference} · {item.templateKind}</option>)
            : <option value="">No approved profile maps this book on this date</option>}
        </OperationField></FormGrid>
        <Button type="button" variant="outline" disabled={mutation.busy || !selectedProfile} disabledReason="Choose an approved accounting profile." onClick={() => void propose()}>Pin current balances for confirmation</Button>
        <OperationError error={profiles.error} />
      </div> : null}
      <MutationFeedback state={mutation} />
    </> : !request ? <p className="text-sm text-muted-foreground">Choose a date and load the confirmations for this book.</p> : null}
  </section>;
}
