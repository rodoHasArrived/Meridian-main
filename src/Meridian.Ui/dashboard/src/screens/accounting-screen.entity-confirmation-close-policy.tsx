import { useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { entityOperationEndpoints as endpoints } from "@/lib/api/entity-operations.api";
import { formatTimestamp } from "@/lib/format";
import type { EntityOperationScope } from "@/types/entity-operations";
import type { IntercompanyConfirmationClosePolicy, IntercompanyConfirmationClosePolicyAction } from "@/types/intercompany-confirmations";
import { MutationFeedback, OperationError, OperationField, useEntityData, useEntityMutation } from "./accounting-screen.entity-operations.shared";

/** Confirmation findings a close policy may block, in the server's order. */
export const blockableConfirmationCodes: { code: string; label: string }[] = [
  { code: "not-proposed", label: "No confirmation proposed" },
  { code: "awaiting-confirmation", label: "Awaiting the other side" },
  { code: "disputed", label: "Disputed" },
  { code: "balances-changed", label: "Balances changed since confirmation" },
  { code: "resolved-awaiting-proposal", label: "Resolved, awaiting a new proposal" }
];

/**
 * Opt-in confirmation close policy for the selected book (ADR-032). Without an active policy, confirmation stays
 * advisory. One person proposes which findings block close; a different person approves it.
 */
export function EntityConfirmationClosePolicy({ scope }: { scope: EntityOperationScope }) {
  const book = scope.book.ledgerBookId;
  const canManage = scope.capabilities.canManageLedger;
  const policies = useEntityData<IntercompanyConfirmationClosePolicy[]>(endpoints.confirmationClosePolicyList(book));
  const [codes, setCodes] = useState<string[]>(["disputed"]);
  const [reason, setReason] = useState("");
  const mutation = useEntityMutation();
  const open = policies.data?.find((item) => item.status === "Proposed" || item.status === "Active") ?? null;

  async function propose() {
    const command = { book, codes, reason: reason.trim() };
    const result = await mutation.run<IntercompanyConfirmationClosePolicy>(endpoints.confirmationClosePolicies,
      { requestId: mutation.identity(command), ledgerBookId: book, blockingCodes: codes, reason: reason.trim() },
      "Close policy proposed. It blocks nothing until a different reviewer approves it.");
    if (result) { setReason(""); policies.refresh(); }
  }
  async function decide(policy: IntercompanyConfirmationClosePolicy, action: IntercompanyConfirmationClosePolicyAction) {
    const command = { policyId: policy.policyId, action, version: policy.version, reason: reason.trim() };
    const result = await mutation.run<IntercompanyConfirmationClosePolicy>(endpoints.confirmationClosePolicyDecisions(policy.policyId, book),
      { requestId: mutation.identity(command), action, expectedVersion: policy.version, reason: action === "Approve" ? null : reason.trim() },
      action === "Approve" ? "Close policy approved. The listed confirmation findings now block close for this book."
        : action === "Disable" ? "Close policy disabled. Confirmation is advisory again for this book." : "Close policy rejected.");
    if (result) { setReason(""); policies.refresh(); }
  }

  return <section aria-label="Confirmation close policy" className="space-y-3 rounded border border-border p-4">
    <h4 className="font-semibold">Confirmation close policy</h4>
    <p className="text-sm text-muted-foreground">{open?.status === "Active"
      ? `Active since ${open.activeFrom ?? "approval"}: these confirmation findings block close for ${scope.book.displayName} — ${open.blockingCodes.join(", ")}. Status that cannot be read also stops close.`
      : "No active policy: confirmation is advisory and close eligibility is unchanged."}</p>
    <OperationError error={policies.error} />
    {(policies.data ?? []).map((policy) => <div key={policy.policyId} className="space-y-1 text-sm">
      <p><Badge variant={policy.status === "Active" ? "warning" : "outline"}>{policy.status}</Badge> version {policy.version} · blocks {policy.blockingCodes.join(", ")}</p>
      <TechnicalDetails label={`${policy.events.length} retained step(s) for policy ${policy.policyId}`}><ul className="space-y-1 break-all text-sm">{policy.events.map((step) =>
        <li key={step.eventId}>{formatTimestamp(step.recordedAt)} · {step.action} · {step.actor}{step.reason ? ` · ${step.reason}` : ""}</li>)}</ul></TechnicalDetails>
    </div>)}
    {canManage ? <div className="space-y-3">
      <OperationField label="Close policy reason" value={reason} onChange={setReason} hint="Required to propose, reject or disable; recorded with the step." />
      {!open ? <fieldset className="space-y-2"><legend className="text-sm font-medium">Findings that block close</legend>
        {blockableConfirmationCodes.map((item) => <Checkbox key={item.code} label={item.label} checked={codes.includes(item.code)}
          onCheckedChange={(checked) => setCodes((current) => checked ? [...current, item.code] : current.filter((code) => code !== item.code))} />)}
        <Button type="button" variant="outline" disabled={mutation.busy || codes.length === 0 || !reason.trim()}
          disabledReason="Choose at least one finding and enter a reason." onClick={() => void propose()}>Propose close policy</Button>
      </fieldset> : <div className="flex flex-wrap gap-2">
        {open.status === "Proposed" ? <>
          <Button type="button" variant="outline" disabled={mutation.busy} onClick={() => void decide(open, "Approve")}>Approve close policy</Button>
          <Button type="button" variant="outline" disabled={mutation.busy || !reason.trim()} disabledReason="Enter a reason to reject." onClick={() => void decide(open, "Reject")}>Reject close policy</Button>
        </> : <Button type="button" variant="outline" disabled={mutation.busy || !reason.trim()} disabledReason="Enter a reason to disable." onClick={() => void decide(open, "Disable")}>Disable close policy</Button>}
      </div>}
      <p className="text-xs text-muted-foreground">The person who proposed a policy cannot approve it; the server enforces this.</p>
    </div> : <p className="text-xs text-muted-foreground">Ledger management permission is required to propose or decide a close policy.</p>}
    <MutationFeedback state={mutation} />
  </section>;
}
