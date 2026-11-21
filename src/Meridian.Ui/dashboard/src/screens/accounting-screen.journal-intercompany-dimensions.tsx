import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { FormGrid } from "@/components/ui/form";
import { TechnicalDetails } from "@/components/ui/technical-details";
import type { ManualJournalEntryLine } from "@/types";
import type { ManualJournalEntryWorkbenchViewModel } from "./accounting-screen.view-model";
import type { AccountingArrangementPartyMapping, ConsolidationFxConfiguration, IntercompanyAccountBinding, IntercompanyAccountingArrangement } from "@/types/entity-operations";
import { entityOperationEndpoints as endpoints } from "@/lib/api/entity-operations.api";
import { OperationError, OperationField, useEntityData } from "./accounting-screen.entity-operations.shared";

export function ManualJournalIntercompanyDimensions({ view, line }: { view: ManualJournalEntryWorkbenchViewModel; line: ManualJournalEntryLine }) {
  const [opened, setOpened] = useState(false);
  const bookId = view.draft.ledgerBookId;
  const arrangements = useEntityData<IntercompanyAccountingArrangement[]>(opened && bookId ? `${endpoints.accountingArrangements}?${new URLSearchParams({ bookId, asOf: view.draft.accountingDate })}` : null);
  const bindings = useEntityData<IntercompanyAccountBinding[]>(opened && bookId ? endpoints.bindings(bookId, view.draft.accountingDate) : null);
  const fx = useEntityData<ConsolidationFxConfiguration>(opened && bookId ? `${endpoints.fxConfiguration(bookId)}?${new URLSearchParams({ asOf: view.draft.accountingDate })}` : null, true);
  const [transactionCurrency, setTransactionCurrency] = useState(line.transactionCurrency?.currency ?? "");
  const [transactionAmount, setTransactionAmount] = useState(String(line.transactionCurrency?.amount ?? ""));
  const [transactionRate, setTransactionRate] = useState(String(line.transactionCurrency?.fxRateToFunctional ?? ""));
  const editable = ["Draft", "NeedsFix", "Rejected"].includes(view.draft.status) && !view.draft.assetAccountingDraft;
  const dimensions = line.dimensions ?? {};
  const [arrangementId, setArrangementId] = useState(dimensions.accountingArrangementId ?? "");
  const [bindingRevisionId, setBindingRevisionId] = useState(dimensions.accountBindingRevisionId ?? "");
  const [transactionReference, setTransactionReference] = useState(dimensions.intercompanyTransactionId ?? `journal:${view.draft.journalEntryId}`);
  useEffect(() => {
    setArrangementId(dimensions.accountingArrangementId ?? "");
    setBindingRevisionId(dimensions.accountBindingRevisionId ?? "");
    setTransactionReference(dimensions.intercompanyTransactionId ?? `journal:${view.draft.journalEntryId}`);
  }, [dimensions.accountingArrangementId, dimensions.accountingArrangementRevisionId, dimensions.accountBindingRevisionId, dimensions.intercompanyTransactionId, view.draft.journalEntryId]);
  const arrangement = arrangements.data?.find((item) => item.arrangementId === arrangementId && item.status === "Approved");
  const party = arrangement?.partyMappings.find((item) => item.ledgerBookId === bookId);
  const availableBindings = bindings.data?.filter((binding) => !binding.isArchived && party && mappedAccountNode(party, binding.role) === binding.chartAccountNodeId) ?? [];
  const binding = availableBindings.find((item) => item.revisionId === bindingRevisionId);
  function selectArrangement(id: string) {
    setArrangementId(id);
    const nextParty = arrangements.data?.find((item) => item.arrangementId === id)?.partyMappings.find((item) => item.ledgerBookId === bookId);
    const matchingBinding = bindings.data?.find((item) => !item.isArchived && nextParty && mappedAccountNode(nextParty, item.role) === item.chartAccountNodeId
      && (item.chartAccountNodeId === dimensions.chartAccountNodeId || item.accountPath === line.accountPath));
    setBindingRevisionId(matchingBinding?.revisionId ?? "");
  }
  function applyArrangement() {
    if (!arrangement || !party || !binding || !transactionReference.trim()) return;
    const counterparty = arrangement.partyOneEntityId === party.entityId ? arrangement.partyTwoEntityId : arrangement.partyOneEntityId;
    view.updateLine(line.lineId, { accountPath: binding.accountPath, entityId: party.entityId,
      dimensions: { ...dimensions, entityId: party.entityId, counterpartyId: counterparty,
        intercompanyAgreementId: arrangement.canonicalAgreementId, intercompanyAgreementVersion: arrangement.canonicalAgreementVersion,
        accountingArrangementId: arrangement.arrangementId, accountingArrangementRevisionId: arrangement.revisionId,
        chartAccountNodeId: binding.chartAccountNodeId, accountBindingRevisionId: binding.revisionId,
        intercompanyTransactionId: transactionReference.trim() } });
  }
  const exactPositive = (value: string) => /^(?:[0-9]+)(?:\.[0-9]+)?$/.test(value) && !/^0+(?:\.0+)?$/.test(value);
  const canApplyCurrency = /^[A-Z]{3}$/.test(transactionCurrency) && exactPositive(transactionAmount) && exactPositive(transactionRate)
    && (transactionCurrency === view.draft.currency || fx.data !== null);
  return <TechnicalDetails label="Intercompany accounting arrangement and original currency" onToggle={(event) => setOpened(event.currentTarget.open)}>
    <p className="mb-3 text-xs text-muted-foreground">An accounting arrangement pins an approved legal agreement version and this book's account roles. Choose the arrangement, account binding and shared transaction reference, then apply them together. Applying changes the draft and requires fresh validation.</p>
    <OperationError error={arrangements.error} /><OperationError error={bindings.error} />
    <fieldset disabled={!editable} className="space-y-3"><FormGrid>
      <OperationField label="Line approved accounting arrangement" value={arrangementId} onChange={selectArrangement}>
        <option value="">Select approved accounting arrangement</option>{arrangementId && !arrangements.data?.some((item) => item.arrangementId === arrangementId && item.status === "Approved") ? <option value={arrangementId}>Retained arrangement revision · review effective-date eligibility</option> : null}
        {arrangements.data?.filter((item) => item.status === "Approved").map((item) => <option key={item.arrangementId} value={item.arrangementId}>{item.arrangementReference} · revision {item.revisionNumber} · {item.currency}</option>)}
      </OperationField>
      <OperationField label="Line intercompany account binding" value={bindingRevisionId} onChange={setBindingRevisionId}>
        <option value="">Select retained account role</option>{bindingRevisionId && !availableBindings.some((item) => item.revisionId === bindingRevisionId) ? <option value={bindingRevisionId}>Retained account revision · review arrangement eligibility</option> : null}
        {availableBindings.map((item) => <option key={item.revisionId} value={item.revisionId}>{item.role} · {item.accountName} · revision {item.revisionNumber}</option>)}
      </OperationField>
      <OperationField label="Shared intercompany transaction reference" value={transactionReference} onChange={setTransactionReference} />
    </FormGrid>
      {arrangement ? <p className="break-all text-xs">Legal agreement {arrangement.canonicalAgreementId} · approved version {arrangement.canonicalAgreementVersion} · {arrangement.currency}. Counterparty {arrangement.partyOneEntityId === party?.entityId ? arrangement.partyTwoEntityId : arrangement.partyOneEntityId}.</p> : null}
      <div className="flex flex-wrap gap-2"><Button type="button" size="sm" variant="outline" disabled={!arrangement || !party || !binding || !transactionReference.trim()} disabledReason={!arrangement ? "Select an approved accounting arrangement." : !party ? "The arrangement must include this journal book." : !binding ? "Select an eligible retained account binding." : "Enter the shared intercompany transaction reference."} onClick={applyArrangement}>Apply accounting arrangement</Button>
        <Button type="button" size="sm" variant="outline" disabled={!dimensions.accountingArrangementId && !dimensions.accountingArrangementRevisionId} disabledReason="This line has no retained accounting arrangement to clear." onClick={() => view.updateLine(line.lineId, { dimensions: { ...dimensions, accountingArrangementId: null, accountingArrangementRevisionId: null } })}>Clear accounting arrangement</Button></div>
      <TechnicalDetails label="Retained intercompany dimension identities"><dl className="space-y-2 break-all text-xs">
        {([ ["intercompanyAgreementId", "Legal agreement ID"], ["intercompanyAgreementVersion", "Approved legal agreement version"], ["accountingArrangementId", "Accounting arrangement ID"], ["accountingArrangementRevisionId", "Accounting arrangement revision ID"], ["chartAccountNodeId", "Stable chart account node"], ["accountBindingRevisionId", "Account binding revision ID"], ["intercompanyTransactionId", "Shared transaction reference"] ] as const).map(([field, label]) => <div key={field}><dt className="font-medium">{label}</dt><dd>{String(dimensions[field] ?? "Not retained")}</dd></div>)}
      </dl></TechnicalDetails>
      <div className="space-y-3 rounded border border-border p-3"><h5 className="text-sm font-semibold">Original transaction currency evidence</h5>
        <p className="text-xs text-muted-foreground">Functional line amounts remain as entered in the journal. The server validates original amounts against the rate and approved book policy; the browser does not calculate conversion.</p>
        <OperationError error={fx.error} />{fx.data ? <p className="break-all text-xs">Approved FX policy {fx.data.policy.policyId} · {fx.data.policy.revision} · rate set {fx.data.rateSetId}, revision {fx.data.rateSetRevision}</p> : <p className="text-xs text-warning">An effective approved FX book policy is required for foreign transaction amounts.</p>}
        <FormGrid columns={3}><OperationField label="Original transaction currency" value={transactionCurrency} onChange={(value) => setTransactionCurrency(value.toUpperCase())} pattern="[A-Z]{3}" />
          <OperationField label="Original transaction amount" value={transactionAmount} onChange={setTransactionAmount} pattern="[0-9]+(\.[0-9]+)?" />
          <OperationField label="Original rate to functional currency" value={transactionRate} onChange={setTransactionRate} pattern="[0-9]+(\.[0-9]+)?" /></FormGrid>
        <div className="flex flex-wrap gap-2"><Button type="button" size="sm" variant="outline" disabled={!canApplyCurrency} disabledReason="Enter a three-letter currency and exact positive amount and rate; foreign amounts also require an approved book FX policy." onClick={() => view.updateLine(line.lineId, { transactionCurrency: { currency: transactionCurrency, amount: transactionAmount, fxRateToFunctional: transactionRate } })}>Apply original currency evidence</Button>
          <Button type="button" size="sm" variant="outline" disabled={!line.transactionCurrency} disabledReason="This line has no original currency evidence to clear." onClick={() => view.updateLine(line.lineId, { transactionCurrency: null })}>Clear original currency evidence</Button></div>
        {line.transactionCurrency ? <p role="status" className="text-xs">Retained in draft: {String(line.transactionCurrency.amount)} {line.transactionCurrency.currency} · rate {String(line.transactionCurrency.fxRateToFunctional)}</p> : null}
      </div>
    </fieldset>
    {!editable ? <p className="mt-2 text-xs text-muted-foreground">This journal is read-only at its current review state.</p> : null}
  </TechnicalDetails>;
}

function mappedAccountNode(party: AccountingArrangementPartyMapping, role: IntercompanyAccountBinding["role"]): string | null {
  switch (role) {
    case "Receivable": return party.receivableAccountNodeId;
    case "Payable": return party.payableAccountNodeId;
    case "Revenue": return party.revenueAccountNodeId;
    case "Expense": return party.expenseAccountNodeId;
    default: return null;
  }
}
