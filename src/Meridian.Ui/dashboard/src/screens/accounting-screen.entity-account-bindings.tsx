import { useRef, useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { TextArea } from "@/components/ui/text-area";
import { FormGrid, FormRow } from "@/components/ui/form";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { AccountingConfigurationWorkspace } from "@/types";
import type { EntityOperationScope, IntercompanyAccountBinding } from "@/types/entity-operations";
import { EntityOperationTable, MutationFeedback, newIdentity, OperationError, OperationField, today, useEntityData, useEntityMutation } from "./accounting-screen.entity-operations.shared";

const roles: IntercompanyAccountBinding["role"][] = ["Receivable", "Payable", "Revenue", "Expense", "TranslationReserve"];

export function EntityAccountBindings({ scope }: { scope: EntityOperationScope }) {
  const query = new URLSearchParams({ fundProfileId: scope.book.fundProfileId, ledgerBookId: scope.book.ledgerBookId });
  const configuration = useEntityData<AccountingConfigurationWorkspace>(scope.capabilities.canManageLedger ? `${UI_API_ROUTES.LedgerAccountingConfiguration}?${query}` : null);
  const history = useEntityData<IntercompanyAccountBinding[]>(scope.capabilities.canManageLedger ? `/api/ledger/intercompany/account-bindings/history?${new URLSearchParams({ ledgerBookId: scope.book.ledgerBookId })}` : null);
  const [nodeId, setNodeId] = useState("");
  const [role, setRole] = useState<IntercompanyAccountBinding["role"]>("Receivable");
  const [effectiveFrom, setEffectiveFrom] = useState(today());
  const [effectiveTo, setEffectiveTo] = useState("");
  const [aliases, setAliases] = useState("");
  const [reason, setReason] = useState("");
  const newBindings = useRef(new Map<string, string>());
  const mutation = useEntityMutation();
  const node = configuration.data?.chartOfAccounts.find((account) => account.nodeId === nodeId);
  const previous = history.data?.filter((binding) => binding.chartAccountNodeId === nodeId).sort((left, right) => right.revisionNumber - left.revisionNumber)[0];
  if (!scope.capabilities.canManageLedger) return <p className="text-sm text-muted-foreground">Ledger management permission is required to configure chart roles and retain account bindings.</p>;
  function selectNode(id: string) {
    setNodeId(id);
    const account = configuration.data?.chartOfAccounts.find((candidate) => candidate.nodeId === id);
    const prior = history.data?.filter((binding) => binding.chartAccountNodeId === id).sort((left, right) => right.revisionNumber - left.revisionNumber)[0];
    setRole(prior?.role ?? account?.intercompanyRole ?? "Receivable");
    setAliases(prior ? [...new Set([...prior.historicalNames, prior.accountName])].join("\n") : "");
  }
  async function assignRole() {
    if (!node) return;
    const result = await mutation.run<AccountingConfigurationWorkspace>(UI_API_ROUTES.LedgerAccountingConfigurationChart,
      { fundProfileId: scope.book.fundProfileId, ledgerBookId: scope.book.ledgerBookId, actor: "", node: { ...node, intercompanyRole: role } },
      "Chart role saved. Retain its effective account binding to establish consolidation and posting authority.");
    if (result) configuration.refresh();
  }
  async function retain(event: FormEvent) {
    event.preventDefault();
    if (!node) return;
    if (!newBindings.current.has(nodeId)) newBindings.current.set(nodeId, newIdentity());
    const command = { bindingId: previous?.bindingId ?? newBindings.current.get(nodeId), ledgerBookId: scope.book.ledgerBookId,
      chartAccountNodeId: nodeId, effectiveFrom, effectiveTo: effectiveTo || null, historicalNames: aliases.split("\n").map((name) => name.trim()).filter(Boolean),
      reason: reason.trim(), expectedPreviousRevisionId: previous?.revisionId ?? null, isArchived: node.isArchived };
    const result = await mutation.run<IntercompanyAccountBinding>("/api/ledger/intercompany/account-bindings",
      { ...command, revisionId: mutation.identity(command) }, "Effective account binding retained with its historical names and audit reason.");
    if (result) history.refresh();
  }
  return <section className="space-y-4"><h3 className="font-semibold">Stable intercompany account roles</h3>
    <p className="text-sm text-muted-foreground">Chart nodes retain their identity through a rename. Reviewed binding revisions preserve historical names so posted source lines and old eliminations remain traceable. A historical name cannot be reused for another chart node.</p>
    <OperationError error={configuration.error} /><OperationError error={history.error} />
    <EntityOperationTable label="Retained intercompany account mappings" rows={history.data ?? []} getRowId={(binding) => binding.revisionId} emptyText="No retained intercompany account bindings for this book."
      columns={[
        { id: "node", label: "Chart node and role", rowHeader: true, render: (binding) => `${binding.chartAccountNodeId} · ${binding.role}` },
        { id: "account", label: "Account", render: (binding) => <>{binding.accountName}<p className="mt-1 text-xs">{binding.accountPath} · {binding.accountType}{binding.isArchived ? " · Archived" : ""}</p><p className="mt-1 text-xs">Historical names: {binding.historicalNames.join(", ") || "None"}</p></> },
        { id: "effective", label: "Effective window", render: (binding) => `${binding.effectiveFrom} → ${binding.effectiveTo ?? "Open"} (exclusive)` },
        { id: "revision", label: "Retained revision", className: "break-all", render: (binding) => <>{binding.revisionNumber} · {binding.recordedBy}<p className="mt-1 font-mono text-xs">{binding.revisionId}</p><p className="mt-1 text-xs">{binding.reason}</p></> }
      ]} />
    {history.data?.length === 0 ? <p className="text-sm text-muted-foreground">No retained intercompany account bindings for this book.</p> : null}
    <form className="space-y-3 rounded border border-border p-4" onSubmit={(event) => void retain(event)}><fieldset disabled={mutation.busy || configuration.loading || history.loading} className="space-y-3"><FormGrid columns={3}>
      <OperationField label="Stable chart node" value={nodeId} onChange={selectNode} required><option value="">Select an existing chart account</option>{configuration.data?.chartOfAccounts.filter((account) => !account.symbol && !account.financialAccountId).map((account) => <option key={account.nodeId} value={account.nodeId}>{account.accountName} · {account.accountType} · {account.nodeId}</option>)}</OperationField>
      <OperationField label="Intercompany account role" value={role} onChange={(value) => setRole(value as IntercompanyAccountBinding["role"])} disabled={Boolean(previous)} hint={previous ? "A retained node's accounting role is immutable. A new role requires a distinct chart node." : undefined}>{roles.map((value) => <option key={value}>{value}</option>)}</OperationField>
      <OperationField label="Account binding effective from" value={effectiveFrom} onChange={setEffectiveFrom} type="date" required />
      <OperationField label="Account binding effective to (exclusive)" value={effectiveTo} onChange={setEffectiveTo} type="date" />
      <OperationField label="Account binding reason" value={reason} onChange={setReason} required />
      <FormRow label="Historical account names (one per line)" labelFor="intercompany-account-aliases"><TextArea id="intercompany-account-aliases" className="min-h-24" value={aliases} onChange={(event) => setAliases(event.target.value)} /></FormRow>
    </FormGrid><div className="flex flex-wrap gap-2"><Button type="button" variant="outline" disabled={!node} disabledReason="Select a chart account before saving its role." onClick={() => void assignRole()}>Save chart role</Button>
      <Button type="submit" disabled={!node || node.intercompanyRole !== role} disabledReason={!node ? "Select a chart account before retaining its binding." : "Save the selected chart role before retaining its binding."}>Retain account binding revision</Button>
      <Button type="button" variant="outline" onClick={() => { configuration.refresh(); history.refresh(); }}>Refresh account authority</Button></div>
      {node && node.intercompanyRole !== role ? <p className="text-sm text-warning">Save the selected role to the chart before retaining its account binding.</p> : null}
    </fieldset></form><MutationFeedback state={mutation} />
  </section>;
}
