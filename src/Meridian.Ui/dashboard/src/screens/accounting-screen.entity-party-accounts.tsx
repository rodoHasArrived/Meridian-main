import type { AccountingArrangementPartyMapping, IntercompanyAccountBinding } from "@/types/entity-operations";
import { entityOperationEndpoints as endpoints } from "@/lib/api/entity-operations.api";
import { nullable, OperationError, OperationField, useEntityData } from "./accounting-screen.entity-operations.shared";

export function AgreementPartyAccounts({ party, index, asOf, onChange }: {
  party: AccountingArrangementPartyMapping; index: number; asOf: string; onChange: (patch: Partial<AccountingArrangementPartyMapping>) => void;
}) {
  const bindings = useEntityData<IntercompanyAccountBinding[]>(party.ledgerBookId && asOf ? endpoints.bindings(party.ledgerBookId, asOf) : null);
  return <>
    {([ ["receivableAccountNodeId", "Receivable"], ["payableAccountNodeId", "Payable"], ["settlementAccountNodeId", null], ["revenueAccountNodeId", "Revenue"], ["expenseAccountNodeId", "Expense"] ] as const).map(([field, role]) => {
      const choices = role ? bindings.data?.filter((binding) => !binding.isArchived && binding.role === role) ?? [] : [];
      const required = field === "receivableAccountNodeId" || field === "payableAccountNodeId";
      return <OperationField key={field} label={`Party ${index + 1} ${field.replace("AccountNodeId", " account node")}`} value={party[field] ?? ""}
        onChange={(value) => onChange({ [field]: required ? value : nullable(value) })} required={required}>
        {choices.length > 0 ? <><option value="">Select retained {role?.toLowerCase()} node</option>{party[field] && !choices.some((choice) => choice.chartAccountNodeId === party[field]) ? <option value={party[field]!}>Retained node {party[field]} · review mapping</option> : null}
          {choices.map((choice) => <option key={choice.revisionId} value={choice.chartAccountNodeId}>{choice.accountName} · {choice.chartAccountNodeId}</option>)}</> : undefined}
      </OperationField>;
    })}
    {bindings.error ? <div className="md:col-span-3"><OperationError error={bindings.error} /><p className="text-xs text-muted-foreground">Retained role lookup is unavailable. Named account node fields remain available; the server validates their identities.</p></div> : null}
  </>;
}
