import { useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { FormGrid } from "@/components/ui/form";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { entityOperationEndpoints as endpoints } from "@/lib/api/entity-operations.api";
import type { BookControlPreflight, BookControlProfile, EntityOperationScope } from "@/types/entity-operations";
import { EntityOperationTable, MutationFeedback, OperationError, OperationField, useEntityData, useEntityMutation } from "./accounting-screen.entity-operations.shared";

export function EntityBookControls({ scope }: { scope: EntityOperationScope }) {
  const path = endpoints.controls(scope.book.ledgerBookId);
  const profile = useEntityData<BookControlProfile>(scope.capabilities.canManageLedger ? path : null);
  const [preflight, setPreflight] = useState<BookControlPreflight | null>(null);
  const [reason, setReason] = useState("");
  const [evidence, setEvidence] = useState("");
  const mutation = useEntityMutation();
  if (!scope.capabilities.canManageLedger) return <p className="text-sm text-muted-foreground">Ledger management permission is required to inspect and activate book controls.</p>;
  async function prepare() {
    setPreflight(null);
    const result = await mutation.run<BookControlPreflight>(`${path}/preflight`, {}, "Preflight retained. Inspect every decision before activating controls.");
    if (result) setPreflight(result);
  }
  async function activate(event: FormEvent) {
    event.preventDefault();
    if (!preflight || !preflight.canActivate) return;
    const result = await mutation.run<BookControlProfile>(`${path}/activate`, { expectedVersion: preflight.profileVersion,
      preflightDigest: preflight.digest, reason: reason.trim(), evidenceReference: evidence.trim() }, "Controls activated with an immutable migration receipt. Future postings enforce the new requirements.");
    if (result) { profile.refresh(); setPreflight(null); }
  }
  return <section className="space-y-4">
    <h3 className="font-semibold">Book control readiness</h3>
    <OperationError error={profile.error} />
    {profile.loading ? <p role="status">Loading book control profile…</p> : null}
    {profile.data ? <div className="space-y-2"><p><Badge variant={profile.data.mode === "Enforced" ? "outline" : "warning"}>{profile.data.mode === "Enforced" ? "Enforced" : "Legacy warning mode"}</Badge> <span className="text-sm">Policy {profile.data.policyVersion} · profile {profile.data.version}</span></p>
      <p className="text-sm text-muted-foreground">New books enforce controls immediately. Existing books show warnings until a successful, reviewed migration activates enforcement. Balanced journals, review, source freshness and required FX evidence remain mandatory.</p>
      {profile.data.migrationReceiptId ? <p className="break-all text-xs">Migration receipt {profile.data.migrationReceiptId} · {profile.data.activatedBy} · {profile.data.activatedAtUtc}</p> : null}</div> : null}
    <Button type="button" variant="outline" disabled={mutation.busy || profile.loading} disabledReason={mutation.busy ? "Wait for the current book control change to finish." : "Wait for the book control profile to load."} onClick={() => void prepare()}>Run control preflight</Button>
    {preflight ? <div className="space-y-3">
      <EntityOperationTable label="Book control diagnostics" rows={preflight.decisions} getRowId={(decision) => `${decision.control}:${decision.code}`} emptyText="No preflight decisions were returned."
        columns={[
          { id: "control", label: "Control", rowHeader: true, render: (decision) => decision.control },
          { id: "decision", label: "Decision", render: (decision) => `${decision.allowed ? "Ready" : "Blocked"} · ${decision.code}` },
          { id: "evidence", label: "Evidence and resolution", render: (decision) => <>{decision.message}{decision.evidenceRevision ? <p className="mt-1 break-all text-xs">Evidence revision {decision.evidenceRevision}</p> : null}</> }
        ]} />
      <TechnicalDetails label="Preflight provenance"><p className="break-all text-xs">{preflight.preparedAtUtc} · {preflight.policyVersion} · digest {preflight.digest}</p></TechnicalDetails>
      <form onSubmit={(event) => void activate(event)} className="space-y-3"><fieldset disabled={mutation.busy || !preflight.canActivate || profile.data?.mode === "Enforced"}>
        <FormGrid><OperationField label="Control activation reason" value={reason} onChange={setReason} required /><OperationField label="Migration evidence reference" value={evidence} onChange={setEvidence} required /></FormGrid>
        <Button className="mt-3" type="submit">Activate enforced controls</Button>
      </fieldset></form>
      {!preflight.canActivate ? <p className="text-sm text-warning">Resolve each blocked decision and run a fresh preflight before activation.</p> : null}
    </div> : null}
    <MutationFeedback state={mutation} />
    <ControlledCloseAuthorization scope={scope} />
  </section>;
}

type ControlledCloseReceipt = { authorizationId: string; journalEntryId: string; ledgerBookId: string; fundProfileId: string; draftVersion: number; postingFingerprint: string; authorizedBy: string };

function ControlledCloseAuthorization({ scope }: { scope: EntityOperationScope }) {
  const [journalId, setJournalId] = useState("");
  const [version, setVersion] = useState("1");
  const [operation, setOperation] = useState("Settlement");
  const [evidence, setEvidence] = useState("");
  const [reason, setReason] = useState("");
  const [receipt, setReceipt] = useState<ControlledCloseReceipt | null>(null);
  const mutation = useEntityMutation();
  async function authorize(event: FormEvent) {
    event.preventDefault();
    const result = await mutation.run<ControlledCloseReceipt>("/api/ledger/controlled-close/authorizations", {
      journalEntryId: journalId, fundProfileId: scope.book.fundProfileId, draftVersion: Number(version), operation,
      sourceObligationEvidence: evidence.trim(), reason: reason.trim()
    }, "Controlled-close authorization retained for this exact draft version. Journal review and posting remain separate.");
    if (result) setReceipt(result);
  }
  const receiptJournalPath = receipt?.journalEntryId && receipt.ledgerBookId && receipt.fundProfileId && Number.isSafeInteger(receipt.draftVersion) && receipt.draftVersion > 0
    ? `/accounting/journal-entries?${new URLSearchParams({ fundProfileId: receipt.fundProfileId, ledgerBookId: receipt.ledgerBookId, journalEntryId: receipt.journalEntryId })}` : null;
  return <TechnicalDetails label="Authorize a controlled wind-down journal">
    <p className="mb-3 text-sm text-muted-foreground">Winding-down entities may settle obligations and complete a controlled close. Retain source evidence for the exact draft before its normal independent journal approval.</p>
    <form className="space-y-3" onSubmit={(event) => void authorize(event)}><fieldset disabled={mutation.busy} className="space-y-3"><FormGrid columns={3}>
      <OperationField label="Controlled-close draft journal ID" value={journalId} onChange={setJournalId} required />
      <OperationField label="Controlled-close draft version" value={version} onChange={setVersion} type="number" required />
      <OperationField label="Controlled-close operation" value={operation} onChange={setOperation}>{["Settlement", "ClosingExpense", "Tax", "Revaluation", "Correction"].map((value) => <option key={value}>{value}</option>)}</OperationField>
      <OperationField label="Source obligation evidence" value={evidence} onChange={setEvidence} required />
      <OperationField label="Controlled-close reason" value={reason} onChange={setReason} required />
    </FormGrid><Button type="submit" variant="outline">Retain controlled-close authorization</Button></fieldset></form>
    <MutationFeedback state={mutation} />
    {receipt ? <div className="space-y-2 break-all text-xs"><p>Authorization {receipt.authorizationId} · {receipt.authorizedBy}</p><p>Draft fingerprint {receipt.postingFingerprint}</p><p>Authorized journal {receipt.journalEntryId || "Unavailable"} · version {receipt.draftVersion ?? "Unavailable"}</p>{receiptJournalPath ? <Button asChild variant="ghost" size="sm" className="underline underline-offset-4"><Link to={receiptJournalPath}>Review authorized journal</Link></Button> : <p role="alert">Authorized journal identity is unavailable. Inspect the retained authorization before continuing.</p>}</div> : null}
  </TechnicalDetails>;
}
