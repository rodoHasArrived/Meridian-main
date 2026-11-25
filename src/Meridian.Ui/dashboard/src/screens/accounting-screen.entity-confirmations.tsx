import { useState } from "react";
import { Button } from "@/components/ui/button";
import { FormGrid } from "@/components/ui/form";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { entityOperationEndpoints as endpoints } from "@/lib/api/entity-operations.api";
import { formatTimestamp } from "@/lib/format";
import type { EntityOperationScope, IntercompanyAccountingArrangement } from "@/types/entity-operations";
import type {
  IntercompanyBalanceConfirmation, IntercompanyConfirmationAction, IntercompanyConfirmationAdjustmentDraft,
  IntercompanyConfirmationAdjustmentDraftRequest, IntercompanyConfirmationParty, IntercompanyConfirmationSummary
} from "@/types/intercompany-confirmations";
import { EntityOperationTable, MutationFeedback, OperationError, OperationField, today, useEntityData, useEntityMutation } from "./accounting-screen.entity-operations.shared";
import { EntityConfirmationClosePolicy } from "./accounting-screen.entity-confirmation-close-policy";

const statusText: Record<IntercompanyBalanceConfirmation["status"], string> = {
  Proposed: "Awaiting both sides", ConfirmedByPartyOne: "Party one confirmed", ConfirmedByPartyTwo: "Party two confirmed",
  Confirmed: "Confirmed by both sides", Disputed: "Disputed", Resolved: "Dispute resolved · draft any correction, then propose again"
};

/** True when the two books belong to different companies of the tenant (DEC-IC-CONFIRM-003). */
function isCrossCompany(item: IntercompanyBalanceConfirmation) {
  return Boolean(item.partyOneCompanyId || item.partyTwoCompanyId) && item.partyOneCompanyId !== item.partyTwoCompanyId;
}

/** Sides that recorded their own resolution of the latest cross-company dispute. */
function sidesThatResolved(item: IntercompanyBalanceConfirmation): IntercompanyConfirmationParty[] {
  const dispute = item.events.map((step) => step.action).lastIndexOf("Dispute");
  return item.events.slice(dispute + 1).filter((step) => step.action === "Resolve" && step.party).map((step) => step.party!);
}

const decimalPattern = "-?[0-9]+(\\.[0-9]+)?";

/**
 * Reciprocal balance confirmation for the selected book. Both sides' balances are pinned from posted journals by the
 * server; this book's operator confirms or disputes only this book's side. The other book may belong to another company
 * of the tenant; then each company also records its own resolution of a dispute. After a resolution this book may
 * pre-fill a correcting journal draft, which goes through ordinary review and is never posted here. Advisory unless
 * the book adopts a reviewed confirmation close policy (ADR-032), shown below the confirmations.
 */
export function EntityBalanceConfirmations({ scope }: { scope: EntityOperationScope }) {
  const [asOf, setAsOf] = useState(today());
  const [request, setRequest] = useState<string | null>(null);
  const [profileId, setProfileId] = useState("");
  const [reason, setReason] = useState("");
  const [adjustmentRole, setAdjustmentRole] = useState<IntercompanyConfirmationAdjustmentDraftRequest["role"]>("Receivable");
  const [adjustmentAmount, setAdjustmentAmount] = useState("");
  const [offsetAccount, setOffsetAccount] = useState("");
  const [adjustmentDate, setAdjustmentDate] = useState("");
  const summary = useEntityData<IntercompanyConfirmationSummary>(request);
  const profiles = useEntityData<IntercompanyAccountingArrangement[]>(request && scope.capabilities.canManageLedger
    ? `${endpoints.accountingArrangements}?${new URLSearchParams({ bookId: scope.book.ledgerBookId, asOf })}` : null);
  const mutation = useEntityMutation();
  const book = scope.book.ledgerBookId;
  const side = (item: IntercompanyBalanceConfirmation): IntercompanyConfirmationParty => item.partyOne.ledgerBookId === book ? "PartyOne" : "PartyTwo";
  const ownSideConfirmed = (item: IntercompanyBalanceConfirmation) => item.status === "Confirmed" || item.status === (side(item) === "PartyOne" ? "ConfirmedByPartyOne" : "ConfirmedByPartyTwo");
  const ownSideResolved = (item: IntercompanyBalanceConfirmation) => isCrossCompany(item) && sidesThatResolved(item).includes(side(item));
  const company = (item: IntercompanyBalanceConfirmation, party: IntercompanyConfirmationParty) =>
    isCrossCompany(item) ? ` · ${(party === "PartyOne" ? item.partyOneCompanyId : item.partyTwoCompanyId) ?? "no company"}` : "";
  const adjustmentReady = Boolean(adjustmentAmount.trim() && new RegExp(`^${decimalPattern}$`).test(adjustmentAmount.trim())
    && Number(adjustmentAmount) !== 0 && offsetAccount.trim());
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
    // Across companies each side records its own resolution; within one company a resolution settles both sides.
    const party = action === "Resolve" && !isCrossCompany(item) ? null : side(item);
    const command = { confirmationId: item.confirmationId, action, party,
      expectedVersion: item.version, expectedBalanceDigest: item.balanceDigest, reason: action === "Confirm" ? null : reason.trim() };
    const result = await mutation.run<IntercompanyBalanceConfirmation>(endpoints.confirmationActions(item.confirmationId),
      { requestId: mutation.identity(command), ...command },
      action === "Confirm" ? "This book's side confirmed the pinned balances." : action === "Dispute" ? "Dispute retained with its reason."
        : party ? "This book's resolution is retained. The dispute is resolved once the other company records its own."
          : "Dispute resolved. Draft any correction, then propose the corrected balances for confirmation.");
    if (result) { setReason(""); summary.refresh(); }
  }

  async function draftAdjustment(item: IntercompanyBalanceConfirmation) {
    const command: Omit<IntercompanyConfirmationAdjustmentDraftRequest, "requestId"> = {
      party: side(item), expectedVersion: item.version, expectedBalanceDigest: item.balanceDigest, role: adjustmentRole,
      adjustmentAmount: adjustmentAmount.trim(), offsetAccountNodeId: offsetAccount.trim(), accountingDate: adjustmentDate || item.asOf, memo: null
    };
    const result = await mutation.run<IntercompanyConfirmationAdjustmentDraft>(endpoints.confirmationAdjustmentDrafts(item.confirmationId),
      { requestId: mutation.identity({ confirmationId: item.confirmationId, ...command }), ...command },
      "Adjustment draft retained in this book's journal entry review queue. Nothing was submitted or posted; after it posts, pin the balances again.");
    if (result) { setAdjustmentAmount(""); summary.refresh(); }
  }

  return <section className="space-y-4" aria-label="Reciprocal balance confirmation"><h3 className="font-semibold">Reciprocal balance confirmation</h3>
    <p className="text-sm text-muted-foreground">Each side confirms the due-to/due-from balance the other side holds, from posted journals only. The other side may belong to another company of this tenant; each company acts only on its own side. Confirmation status appears in close readiness. It never blocks posting, and blocks close only for the findings named by this book's approved confirmation close policy.</p>
    <form onSubmit={(event) => { event.preventDefault(); load(); }} className="space-y-3"><FormGrid columns={3}>
      <OperationField label="Confirmation as of" value={asOf} onChange={(value) => { setAsOf(value); setRequest(null); }} type="date" required />
      <p className="self-center text-sm">{scope.book.displayName} · {scope.book.baseCurrency}</p></FormGrid>
      <Button type="submit" variant="outline" disabled={!scope.capabilities.canViewLedger || summary.loading || !asOf}>Load confirmations</Button></form>
    {!scope.capabilities.canViewLedger ? <p className="text-sm text-muted-foreground">Ledger report access is required to inspect balance confirmations.</p> : null}
    <OperationError error={summary.error} />{summary.loading ? <p role="status">Reading posted balances and retained confirmations…</p> : null}
    {summary.data ? <>
      <StatusBanner title={summary.data.findings.length ? `${summary.data.findings.length} advisory finding(s)` : "All agreement balances confirmed"}
        tone={summary.data.findings.length ? "warning" : "info"} detail={`As of ${summary.data.asOf} · close eligibility changes only through an active confirmation close policy.`} />
      {summary.data.findings.length ? <ul className="space-y-2" aria-label="Balance confirmation findings">{summary.data.findings.map((finding) =>
        <li key={`${finding.code}:${finding.accountingArrangementId}`} className="rounded border border-border p-3 text-sm"><p className="font-semibold">{finding.message}</p><p className="break-all text-muted-foreground">{finding.code} · accounting profile {finding.accountingArrangementId}</p></li>)}</ul> : null}
      <EntityOperationTable label="Reciprocal balance confirmations" rows={summary.data.confirmations} getRowId={(row) => row.confirmationId}
        emptyText="No confirmation has been proposed for this date." columns={[
          { id: "profile", label: "Accounting profile", rowHeader: true, render: (row) => <span className="break-all">{row.accountingArrangementId}</span> },
          { id: "status", label: "Status", render: (row) => `${statusText[row.status]} · version ${row.version}` },
          { id: "one", label: "Party one balance", render: (row) => `${row.partyOne.transactionSignedBalance} ${row.partyOne.transactionCurrency}${row.partyOne.ledgerBookId === book ? " (this book)" : ""}${company(row, "PartyOne")}` },
          { id: "two", label: "Party two balance", render: (row) => `${row.partyTwo.transactionSignedBalance} ${row.partyTwo.transactionCurrency}${row.partyTwo.ledgerBookId === book ? " (this book)" : ""}${company(row, "PartyTwo")}` },
          { id: "net", label: "Net", render: (row) => `${row.netTransactionBalance} ${row.partyOne.transactionCurrency}` },
          { id: "actions", label: "This book's side", render: (row) => !scope.capabilities.canManageLedger ? "Read only" : <div className="flex flex-wrap gap-2">
            <Button type="button" size="sm" variant="outline" disabled={mutation.busy || row.status === "Disputed" || row.status === "Resolved" || ownSideConfirmed(row)}
              disabledReason="This side has already confirmed, or the record is disputed or resolved." onClick={() => void act(row, "Confirm")}>Confirm this book's balance</Button>
            <Button type="button" size="sm" variant="outline" disabled={mutation.busy || !reason.trim() || row.status === "Confirmed" || row.status === "Disputed" || row.status === "Resolved"}
              disabledReason="Enter a reason; only an open proposal can be disputed." onClick={() => void act(row, "Dispute")}>Dispute</Button>
            <Button type="button" size="sm" variant="outline" disabled={mutation.busy || !reason.trim() || row.status !== "Disputed" || ownSideResolved(row)}
              disabledReason="Enter a reason; only a disputed record can be resolved, once per side across companies." onClick={() => void act(row, "Resolve")}>
              {isCrossCompany(row) ? "Record this book's resolution" : "Resolve dispute"}</Button>
            <Button type="button" size="sm" variant="outline" disabled={mutation.busy || row.status !== "Resolved" || !adjustmentReady}
              disabledReason="Only a resolved dispute can be corrected; enter a nonzero amount and an offset account." onClick={() => void draftAdjustment(row)}>Draft adjustment</Button>
          </div> },
          { id: "history", label: "History", render: (row) => <TechnicalDetails label={`${row.events.length} retained step(s)`}><ul className="space-y-1 break-all text-sm">{row.events.map((step) =>
            <li key={step.eventId}>{formatTimestamp(step.recordedAt)} · {step.action}{step.party ? ` · ${step.party}` : ""} · {step.actor}{step.reason ? ` · ${step.reason}` : ""}</li>)}</ul><p className="break-all text-sm">Balance digest {row.balanceDigest}</p></TechnicalDetails> }
        ]} />
      {scope.capabilities.canManageLedger ? <div className="space-y-3">
        <OperationField label="Dispute or resolution reason" value={reason} onChange={setReason} hint="Required to dispute or resolve; recorded with the step." />
        {summary.data.confirmations.some((item) => item.status === "Resolved") ? <fieldset className="space-y-2 rounded border border-border p-3">
          <legend className="px-1 text-sm font-semibold">Correction after a resolved dispute</legend>
          <p className="text-sm text-muted-foreground">Pre-fills a balanced draft on this book's mapped intercompany account. A reviewer approves and posts it in the journal workbench; nothing is posted here.</p>
          <FormGrid columns={2}>
            <OperationField label="Intercompany account to correct" value={adjustmentRole} onChange={(value) => setAdjustmentRole(value as IntercompanyConfirmationAdjustmentDraftRequest["role"])}>
              <option value="Receivable">Due from (receivable)</option><option value="Payable">Due to (payable)</option></OperationField>
            <OperationField label="Adjustment amount" value={adjustmentAmount} onChange={setAdjustmentAmount} pattern={decimalPattern}
              hint="Agreement currency. Positive debits the intercompany account, negative credits it." />
            <OperationField label="Offset account node" value={offsetAccount} onChange={setOffsetAccount} hint="A non-intercompany account in this book's chart." />
            <OperationField label="Adjustment accounting date" value={adjustmentDate || asOf} onChange={setAdjustmentDate} type="date" hint="Must fall in an open period of this book." />
          </FormGrid>
        </fieldset> : null}
        <FormGrid columns={2}><OperationField label="Accounting profile to confirm" value={selectedProfile} onChange={setProfileId}>
          {profiles.data?.length ? profiles.data.map((item) => <option key={item.arrangementId} value={item.arrangementId}>{item.arrangementReference} · {item.templateKind}</option>)
            : <option value="">No approved profile maps this book on this date</option>}
        </OperationField></FormGrid>
        <Button type="button" variant="outline" disabled={mutation.busy || !selectedProfile} disabledReason="Choose an approved accounting profile." onClick={() => void propose()}>Pin current balances for confirmation</Button>
        <OperationError error={profiles.error} />
      </div> : null}
      <MutationFeedback state={mutation} />
    </> : !request ? <p className="text-sm text-muted-foreground">Choose a date and load the confirmations for this book.</p> : null}
    <EntityConfirmationClosePolicy scope={scope} />
  </section>;
}
