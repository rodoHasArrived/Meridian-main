import { useState } from "react";
import { Button } from "@/components/ui/button";
import { FormGrid } from "@/components/ui/form";
import { TechnicalDetails } from "@/components/ui/technical-details";
import type { LedgerDimensionSet, ManualJournalEntryLine } from "@/types";
import type { ManualJournalEntryWorkbenchViewModel } from "./accounting-screen.view-model";
import type { ConsolidationFxConfiguration, IntercompanyAccountBinding, IntercompanyAgreement } from "@/types/entity-operations";
import { entityOperationEndpoints as endpoints } from "@/lib/api/entity-operations.api";
import { OperationError, OperationField, useEntityData } from "./accounting-screen.entity-operations.shared";

export function ManualJournalIntercompanyDimensions({ view, line }: { view: ManualJournalEntryWorkbenchViewModel; line: ManualJournalEntryLine }) {
  const [opened, setOpened] = useState(false);
  const bookId = view.draft.ledgerBookId;
  const agreements = useEntityData<IntercompanyAgreement[]>(opened && bookId ? `/api/ledger/intercompany/agreements?${new URLSearchParams({ bookId, asOf: view.draft.accountingDate })}` : null);
  const bindings = useEntityData<IntercompanyAccountBinding[]>(opened && bookId ? endpoints.bindings(bookId, view.draft.accountingDate) : null);
  const fx = useEntityData<ConsolidationFxConfiguration>(opened && bookId ? `${endpoints.fxConfiguration(bookId)}?${new URLSearchParams({ asOf: view.draft.accountingDate })}` : null, true);
  const [transactionCurrency, setTransactionCurrency] = useState(line.transactionCurrency?.currency ?? "");
  const [transactionAmount, setTransactionAmount] = useState(String(line.transactionCurrency?.amount ?? ""));
  const [transactionRate, setTransactionRate] = useState(String(line.transactionCurrency?.fxRateToFunctional ?? ""));
  const editable = ["Draft", "NeedsFix", "Rejected"].includes(view.draft.status) && !view.draft.assetAccountingDraft;
  const dimensions = line.dimensions ?? {};
  function update(patch: Partial<LedgerDimensionSet>) {
    view.updateLine(line.lineId, { dimensions: { ...dimensions, ...patch } });
  }
  function selectAgreement(id: string) {
    const agreement = agreements.data?.find((item) => item.agreementId === id);
    const party = agreement?.partyMappings.find((item) => item.ledgerBookId === bookId);
    const counterparty = agreement && party ? (agreement.partyOneEntityId === party.entityId ? agreement.partyTwoEntityId : agreement.partyOneEntityId) : null;
    update({ agreementId: agreement?.agreementId ?? null, agreementRevisionId: agreement?.revisionId ?? null,
      entityId: party?.entityId ?? dimensions.entityId, counterpartyId: counterparty,
      intercompanyTransactionId: dimensions.intercompanyTransactionId || (agreement ? `journal:${view.draft.journalEntryId}` : null) });
  }
  function selectBinding(id: string) {
    const binding = bindings.data?.find((item) => item.revisionId === id);
    view.updateLine(line.lineId, { accountPath: binding?.accountPath ?? line.accountPath,
      dimensions: { ...dimensions, chartAccountNodeId: binding?.chartAccountNodeId ?? null, accountBindingRevisionId: binding?.revisionId ?? null } });
  }
  const exactPositive = (value: string) => /^(?:[0-9]+)(?:\.[0-9]+)?$/.test(value) && !/^0+(?:\.0+)?$/.test(value);
  const canApplyCurrency = /^[A-Z]{3}$/.test(transactionCurrency) && exactPositive(transactionAmount) && exactPositive(transactionRate)
    && (transactionCurrency === view.draft.currency || fx.data !== null);
  return <TechnicalDetails label="Intercompany agreement and original currency" onToggle={(event) => setOpened(event.currentTarget.open)}>
    <p className="mb-3 text-xs text-muted-foreground">Enforced books require an approved agreement revision, a retained account binding and a shared transaction reference on intercompany-role lines. Selecting these changes the draft and requires fresh validation.</p>
    <OperationError error={agreements.error} /><OperationError error={bindings.error} />
    <fieldset disabled={!editable} className="space-y-3"><FormGrid>
      <OperationField label="Line approved intercompany agreement" value={dimensions.agreementId ?? ""} onChange={selectAgreement}>
        <option value="">No intercompany agreement</option>{dimensions.agreementId && !agreements.data?.some((agreement) => agreement.agreementId === dimensions.agreementId && agreement.status === "Approved") ? <option value={dimensions.agreementId}>Retained agreement revision · review effective-date eligibility</option> : null}
        {agreements.data?.filter((agreement) => agreement.status === "Approved").map((agreement) => <option key={agreement.agreementId} value={agreement.agreementId}>{agreement.facilityReference} · revision {agreement.revisionNumber} · {agreement.currency}</option>)}
      </OperationField>
      <OperationField label="Line intercompany account binding" value={dimensions.accountBindingRevisionId ?? ""} onChange={selectBinding}>
        <option value="">Select retained account role</option>{dimensions.accountBindingRevisionId && !bindings.data?.some((binding) => binding.revisionId === dimensions.accountBindingRevisionId && !binding.isArchived) ? <option value={dimensions.accountBindingRevisionId}>Retained account revision · review effective-date eligibility</option> : null}
        {bindings.data?.filter((binding) => !binding.isArchived).map((binding) => <option key={binding.revisionId} value={binding.revisionId}>{binding.role} · {binding.accountName} · revision {binding.revisionNumber}</option>)}
      </OperationField>
      <OperationField label="Line counterparty entity ID" value={dimensions.counterpartyId ?? ""} onChange={(value) => update({ counterpartyId: value.trim() || null })} />
      <OperationField label="Shared intercompany transaction reference" value={dimensions.intercompanyTransactionId ?? ""} onChange={(value) => update({ intercompanyTransactionId: value.trim() || null })} />
    </FormGrid>
      <TechnicalDetails label="Retained intercompany dimension identities"><FormGrid>
        {([ ["agreementId", "Line agreement ID"], ["agreementRevisionId", "Line agreement revision ID"], ["chartAccountNodeId", "Line stable chart account node"], ["accountBindingRevisionId", "Line account binding revision ID"] ] as const).map(([field, label]) => <OperationField key={field} label={label} value={dimensions[field] ?? ""} onChange={(value) => update({ [field]: value.trim() || null })} />)}
      </FormGrid></TechnicalDetails>
      <div className="space-y-3 rounded border border-border p-3"><h5 className="text-sm font-semibold">Original transaction currency evidence</h5>
        <p className="text-xs text-muted-foreground">Functional line amounts remain as entered in the journal. The server validates original amounts against the rate and approved book policy; the browser does not calculate conversion.</p>
        <OperationError error={fx.error} />{fx.data ? <p className="break-all text-xs">Approved FX policy {fx.data.policy.policyId} · {fx.data.policy.revision} · rate set {fx.data.rateSetId}, revision {fx.data.rateSetRevision}</p> : <p className="text-xs text-warning">An effective approved FX book policy is required for foreign transaction amounts.</p>}
        <FormGrid columns={3}><OperationField label="Original transaction currency" value={transactionCurrency} onChange={(value) => setTransactionCurrency(value.toUpperCase())} pattern="[A-Z]{3}" />
          <OperationField label="Original transaction amount" value={transactionAmount} onChange={setTransactionAmount} pattern="[0-9]+(\.[0-9]+)?" />
          <OperationField label="Original rate to functional currency" value={transactionRate} onChange={setTransactionRate} pattern="[0-9]+(\.[0-9]+)?" /></FormGrid>
        <div className="flex flex-wrap gap-2"><Button type="button" size="sm" variant="outline" disabled={!canApplyCurrency} onClick={() => view.updateLine(line.lineId, { transactionCurrency: { currency: transactionCurrency, amount: transactionAmount, fxRateToFunctional: transactionRate } })}>Apply original currency evidence</Button>
          <Button type="button" size="sm" variant="outline" disabled={!line.transactionCurrency} onClick={() => view.updateLine(line.lineId, { transactionCurrency: null })}>Clear original currency evidence</Button></div>
        {line.transactionCurrency ? <p role="status" className="text-xs">Retained in draft: {String(line.transactionCurrency.amount)} {line.transactionCurrency.currency} · rate {String(line.transactionCurrency.fxRateToFunctional)}</p> : null}
      </div>
    </fieldset>
    {!editable ? <p className="mt-2 text-xs text-muted-foreground">This journal is read-only at its current review state.</p> : null}
  </TechnicalDetails>;
}
