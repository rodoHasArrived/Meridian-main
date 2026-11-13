import { useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { formatMoney } from "@/lib/format";
import type { ManualJournalEntryDraft } from "@/types";
import type { IntercompanyProposal } from "@/types/entity-operations";
import { EntityOperationTable, MutationFeedback, OperationError, OperationField, useEntityData, useEntityMutation } from "./accounting-screen.entity-operations.shared";

interface CounterpartRefreshPreview { currentDraft: ManualJournalEntryDraft | null; draft: ManualJournalEntryDraft; fingerprint: string }

export function CounterpartRepricing({ proposal, canManage, onApplied }: {
  proposal: IntercompanyProposal; canManage: boolean; onApplied: () => void;
}) {
  const [enabled, setEnabled] = useState(false);
  const [reason, setReason] = useState("");
  const preview = useEntityData<CounterpartRefreshPreview>(enabled ? `/api/ledger/intercompany/proposals/${proposal.source.proposalId}/refresh-preview` : null);
  const mutation = useEntityMutation();
  const current = preview.data?.currentDraft;
  const proposed = preview.data?.draft;
  const ids = [...new Set([...(current?.lines.map((line) => line.lineId) ?? []), ...(proposed?.lines.map((line) => line.lineId) ?? [])])];
  const rows = ids.map((id) => ({ id, before: current?.lines.find((line) => line.lineId === id), after: proposed?.lines.find((line) => line.lineId === id) }));
  async function apply(event: FormEvent) {
    event.preventDefault();
    if (!preview.data?.currentDraft || !reason.trim()) return;
    const command = { expectedDraftVersion: preview.data.currentDraft.version, previewFingerprint: preview.data.fingerprint, reason: reason.trim() };
    const result = await mutation.run<ManualJournalEntryDraft>(`/api/ledger/intercompany/proposals/${proposal.source.proposalId}/refresh`,
      { ...command, requestId: mutation.identity({ proposalId: proposal.source.proposalId, ...command }) },
      "Counterpart repricing retained against the reviewed source and FX evidence. Its previous review was cleared; obtain fresh independent journal approval.");
    if (result) { setEnabled(false); onApplied(); }
  }
  return <section className="space-y-3 rounded border border-border p-3" aria-label="Counterpart FX repricing">
    <h5 className="text-sm font-semibold">Explicit foreign-currency draft repricing</h5>
    <p className="text-xs text-muted-foreground">Preview the existing counterpart against a newly approved FX policy. The server preserves its accounts and sides and reprices its original retained source amount. Applying requires access to both books and clears prior draft review.</p>
    <Button type="button" variant="outline" disabled={!canManage || preview.loading || mutation.busy} onClick={() => { setEnabled(true); preview.refresh(); }}>Preview counterpart FX repricing</Button>
    <OperationError error={preview.error} />
    {preview.loading ? <p role="status">Preparing the retained before-and-after counterpart amounts…</p> : null}
    {preview.data && current && proposed ? <div className="space-y-3"><p className="text-sm">Target draft {current.version} · {current.status} · original journal date {current.accountingDate}</p>
      <EntityOperationTable label="Counterpart repricing before and after" rows={rows} getRowId={(row) => row.id} emptyText="No retained counterpart lines."
        columns={[
          { id: "account", label: "Account", rowHeader: true, render: (row) => row.after?.accountPath ?? row.before?.accountPath ?? "Unavailable" },
          { id: "side", label: "Side", render: (row) => row.before?.side === row.after?.side ? row.before?.side : `${row.before?.side ?? "Missing"} → ${row.after?.side ?? "Missing"}` },
          { id: "before", label: "Current functional amount", numeric: true, render: (row) => row.before ? formatMoney(row.before.amount, { context: "accounting", currency: row.before.currency }) : "Not retained" },
          { id: "after", label: "Proposed functional amount", numeric: true, render: (row) => row.after ? formatMoney(row.after.amount, { context: "accounting", currency: row.after.currency }) : "Not retained" },
          { id: "original", label: "Original transaction evidence", render: (row) => row.after?.transactionCurrency ? `${String(row.after.transactionCurrency.amount)} ${row.after.transactionCurrency.currency} · rate ${String(row.after.transactionCurrency.fxRateToFunctional)}` : "Not retained" }
        ]} />
      <TechnicalDetails label="Counterpart repricing source and FX authority"><dl className="space-y-2 break-all text-xs"><div><dt>Retained source digest</dt><dd className="font-mono">{proposal.sourceDigest}</dd></div><div><dt>Preview fingerprint</dt><dd className="font-mono">{preview.data.fingerprint}</dd></div>
        {proposed.intercompanyFx ? <><div><dt>Approved rate-set revision</dt><dd>{proposed.intercompanyFx.rateSetId} · {proposed.intercompanyFx.rateSetRevision}</dd></div><div><dt>Policy revision</dt><dd>{proposed.intercompanyFx.policyId} · {proposed.intercompanyFx.policyRevision}</dd></div><div><dt>Configuration hash</dt><dd className="font-mono">{proposed.intercompanyFx.configurationHash}</dd></div></> : null}</dl></TechnicalDetails>
      <form className="space-y-3" onSubmit={(event) => void apply(event)}><fieldset className="space-y-3" disabled={!canManage || mutation.busy}><OperationField label="Counterpart FX repricing reason" value={reason} onChange={setReason} required />
        <Button type="submit" disabled={!reason.trim()}>Apply repricing and renew review</Button></fieldset></form>
    </div> : null}
    {preview.data && !current ? <p role="alert" className="text-sm text-warning">The server did not return the retained current draft. Repricing cannot be applied without its before-state and version.</p> : null}
    <MutationFeedback state={mutation} />
  </section>;
}
