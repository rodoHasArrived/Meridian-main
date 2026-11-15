import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { prepareThreeWayAdjustment } from "@/lib/api/accounting-integration.api";
import type { AccountingIntegrationScope, PreparedThreeWayAdjustmentDraft, ThreeWayAdjustmentProposal, ThreeWayReconciliation } from "@/types/accounting-integration";
import { integrationError, isGuid } from "./accounting-integration.scope";

/** A server-supported fee explanation may create a draft; this surface has no approval or posting command. */
export function ThreeWayAdjustmentProposalPanel({ scope, comparison, proposal, disabled, onBusyChange }: {
  scope: AccountingIntegrationScope; comparison: ThreeWayReconciliation; proposal: ThreeWayAdjustmentProposal;
  disabled: boolean; onBusyChange: (busy: boolean) => void;
}) {
  const [draft, setDraft] = useState<PreparedThreeWayAdjustmentDraft | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const periodVersion = comparison.accountingPeriodVersion ?? scope.period.version;
  const [request] = useState(() => ({ ledgerBookId: scope.book.ledgerBookId, accountingPeriodId: scope.period.periodId,
    operationId: comparison.operationId, checkId: proposal.checkId, expectedPeriodVersion: periodVersion!, evidenceHash: proposal.evidenceHash }));
  const alive = useRef(true);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  const ready = proposal.status === "Ready" && proposal.evidenceIds.length > 0 && Boolean(proposal.evidenceHash)
    && typeof periodVersion === "number" && Number.isSafeInteger(periodVersion) && periodVersion >= 0;

  async function prepare() {
    setBusy(true); setError(null); onBusyChange(true);
    try {
      const retained = await prepareThreeWayAdjustment(scope.accountId, request);
      if (!alive.current) return;
      if (!isGuid(retained.journalEntryId) || retained.ledgerBookId !== scope.book.ledgerBookId
        || retained.periodId !== scope.period.periodId || retained.fundProfileId !== scope.book.fundProfileId
        || !["Draft", "NeedsFix"].includes(retained.status))
        throw new Error("The returned review draft does not match the selected scope or draft state. Inspect retained casework before retrying.");
      setDraft(retained);
    } catch (reason) { if (alive.current) setError(integrationError(reason)); }
    finally { if (alive.current) { setBusy(false); onBusyChange(false); } }
  }

  return <div className="space-y-2 rounded border border-border p-3 text-xs" aria-label={`Fee adjustment proposal ${proposal.checkId}`}>
    <div className="flex flex-wrap items-center gap-2"><span className="font-semibold">{proposal.description}</span>
      <Badge variant="outline">{proposal.status === "Ready" ? "Draft preparation available" : "Insufficient evidence"}</Badge></div>
    <p>Proposed fee: <span className="font-mono">{proposal.amount ?? "Unavailable"} {proposal.currency}</span></p>
    <p className="text-muted-foreground">Draft only. Validation, review and posting remain in the existing journal workflow.</p>
    <details><summary className="cursor-pointer">Retained fee evidence</summary>
      {proposal.evidenceIds.length ? <ul className="mt-2 list-disc space-y-1 pl-4">{proposal.evidenceIds.map(id => <li key={id} className="break-all font-mono">{id}</li>)}</ul> : <p className="mt-2">No sufficient independent fee evidence is retained.</p>}
      {proposal.evidenceHash ? <p className="mt-2 break-all font-mono">Evidence hash: {proposal.evidenceHash}</p> : null}
    </details>
    {!ready && proposal.status === "Ready" ? <p className="text-warning">Reload ledger scope and comparison to obtain the retained period version and complete evidence before preparing a draft.</p> : null}
    {ready && !draft ? <Button size="sm" variant="outline" disabled={busy || disabled} onClick={() => void prepare()}>
      {busy ? "Preparing fee adjustment draft…" : error ? "Retry same fee draft preparation" : "Prepare fee adjustment draft"}
    </Button> : null}
    {draft ? <p role="status">Review draft retained · {draft.status}. <Link className="text-primary underline"
      to={`/accounting/journal-entries?${new URLSearchParams({ fundProfileId: draft.fundProfileId,
        ledgerBookId: draft.ledgerBookId, periodId: draft.periodId, journalEntryId: draft.journalEntryId }).toString()}`}>Open fee adjustment draft</Link></p> : null}
    {error ? <p role="alert" className="text-danger">{error} The same preparation request is retained for retry.</p> : null}
  </div>;
}
