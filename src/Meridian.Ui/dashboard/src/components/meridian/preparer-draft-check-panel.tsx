import { useEffect, useId, useRef, useState } from "react";
import { PreparerAttestationProofDownload } from "@/components/meridian/preparer-attestation-evidence";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { isApiError } from "@/lib/api-errors";
import {
  changePrincipalDeclaration, PREPARER_ATTESTATION_DISCLOSURE,
  type AttestationRequest, type DeclarationRequest, type RevocationRequest
} from "@/lib/single-principal-attestation-api";
import {
  PREPARER_DRAFT_CHECK, PREPARER_DRAFT_POLICY, getDraftAttestationWorkspace, getPreparerDraftSnapshot,
  isSupportedDraftReceipt, preparerDraftSubjectId, recordPreparerDraftCheck, revokePreparerDraftCheck,
  type DraftAttestationWorkspace, type PreparerDraftSnapshot
} from "@/lib/preparer-draft-check-api";

type Pending = { kind: "check"; request: AttestationRequest }
  | { kind: "declaration"; request: DeclarationRequest } | { kind: "revoke"; request: RevocationRequest };

/** A separate preparer check of a retained calculation. It never changes or posts that calculation. */
export function PreparerDraftCheckPanel({ securityId, runId }: { securityId: string; runId: string }) {
  return <DraftCheck key={`${securityId}/${runId}`} securityId={securityId} runId={runId} />;
}

function DraftCheck({ securityId, runId }: { securityId: string; runId: string }) {
  const id = useId();
  const [workspace, setWorkspace] = useState<DraftAttestationWorkspace | null>(null);
  const [snapshot, setSnapshot] = useState<PreparerDraftSnapshot | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [reason, setReason] = useState("");
  const [declarationReason, setDeclarationReason] = useState("");
  const [revocationReasons, setRevocationReasons] = useState<Record<string, string>>({});
  const [confirmed, setConfirmed] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [pending, setPending] = useState<Pending | null>(null);
  const [refresh, setRefresh] = useState(0);
  const alive = useRef(true);
  const inFlight = useRef(false);

  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true); setWorkspace(null); setSnapshot(null); setConfirmed(false); setReason("");
    void Promise.allSettled([
      getDraftAttestationWorkspace({ signal: controller.signal }),
      getPreparerDraftSnapshot(securityId, runId, { signal: controller.signal })
    ]).then(([state, target]) => {
      if (controller.signal.aborted) return;
      if (state.status === "fulfilled") setWorkspace(state.value);
      if (target.status === "fulfilled") setSnapshot(target.value);
      const errors: string[] = [];
      if (state.status === "rejected") errors.push(message(state.reason));
      if (target.status === "rejected") errors.push(isApiError(target.reason) && target.reason.status === 404
        ? "This retained run has no currently available draft-check support. Legacy unbound runs remain ineligible."
        : message(target.reason));
      setError(errors.length ? errors.join(" ") : null);
      setLoading(false);
    });
    return () => controller.abort();
  }, [securityId, runId, refresh]);

  async function submit(command: Pending) {
    if (inFlight.current) return;
    inFlight.current = true; setBusy(true); setPending(command); setError(null); setNotice(null);
    try {
      if (command.kind === "check") await recordPreparerDraftCheck(command.request);
      else if (command.kind === "declaration") await changePrincipalDeclaration(command.request);
      else await revokePreparerDraftCheck(command.request);
      if (!alive.current) return;
      setPending(null); setReason(""); setDeclarationReason(""); setRevocationReasons({}); setConfirmed(false);
      setNotice("Recorded. Reloading the retained preparer history.");
      setRefresh(value => value + 1);
    } catch (failure) {
      if (!alive.current) return;
      if (isApiError(failure) && failure.status >= 400 && failure.status < 500) {
        setPending(null); setConfirmed(false); setWorkspace(null); setSnapshot(null);
        setError(`${message(failure)} Refresh the declaration and retained run before confirming again.`);
      } else setError(`${message(failure)} The outcome is unconfirmed. Retry the same command; do not create another check.`);
    } finally {
      inFlight.current = false;
      if (alive.current) setBusy(false);
    }
  }

  const state = workspace?.state;
  const declaration = state?.declaration;
  const locked = loading || busy || pending !== null;
  const ownsDeclaration = declaration?.enabled === true && sameActor(declaration.principalId, workspace?.actor);
  const currentPolicy = state?.policyVersion === PREPARER_DRAFT_POLICY;
  const readyDeclaration = ownsDeclaration && currentPolicy && declaration?.policyVersion === PREPARER_DRAFT_POLICY;
  const canCheck = !locked && workspace?.canAttestDrafts === true && state?.policyEnabled === true
    && state.allowedActions.includes(PREPARER_DRAFT_CHECK) && readyDeclaration && snapshot?.eligible === true
    && sameActor(snapshot.preparedBy, workspace.actor);
  let targetId: string | null = null;
  try { targetId = preparerDraftSubjectId(securityId, runId); } catch { /* Invalid identities never enable commands. */ }
  const receipts = state?.receipts.filter(receipt => receipt.subjectKind === "contractual-cashflow-draft" && receipt.subjectId === targetId) ?? [];
  const canEnable = !locked && workspace?.canAdministerDeclaration === true && state?.policyEnabled === true
    && currentPolicy && state.allowedActions.includes(PREPARER_DRAFT_CHECK) && !readyDeclaration;

  return <section aria-label="Retained calculation preparer check" aria-busy={loading || busy}
    className="grid gap-3 rounded-md border border-border/70 bg-secondary/10 p-3 text-sm">
    <div className="flex flex-wrap items-start justify-between gap-2">
      <div><h3 className="font-semibold">Preparer check for this retained calculation</h3>
        <p className="break-all text-muted-foreground">Run {runId}. This task does not approve, post or release anything.</p></div>
      <Button type="button" size="sm" variant="outline" disabled={loading || busy} onClick={() => setRefresh(value => value + 1)}>Refresh draft check</Button>
    </div>
    {loading ? <p role="status">Loading scoped draft-check support…</p> : null}
    {error ? <p role="alert" className="text-danger">{error}</p> : null}
    {notice ? <p role="status">{notice}</p> : null}
    {pending ? <div className="grid gap-2"><p>The prior command is awaiting a confirmed outcome. Its identity is retained for retry.</p>
      <Button type="button" size="sm" variant="outline" disabled={busy || loading} onClick={() => void submit(pending)}>Retry same draft command</Button></div> : null}
    {workspace ? <>
      <p className="text-muted-foreground">Tenant/company scope: {state?.scope.tenantId} / {state?.scope.companyId}. Bound principal: {declaration?.principalId ?? "none recorded"}.</p>
      <p>{state?.policyEnabled && state.allowedActions.includes(PREPARER_DRAFT_CHECK) ? "Draft-check provider enabled." : "Draft-check provider disabled (default)."}
        {declaration ? ` Declaration revision ${declaration.revision}: ${declaration.enabled ? "enabled" : "withdrawn"}.` : " No declaration recorded."}</p>
      {workspace.canAttestDrafts !== true ? <p>Recording a draft check requires the separate preparer grant and Security Master modification permission.</p> : null}
      {!readyDeclaration && currentPolicy ? <div className="grid gap-2 rounded-sm border border-border/60 p-2">
        <p>The bound principal must explicitly enable or reaffirm the version-2 declaration before draft checks are available.
          This administrative action binds {workspace.actor} for the whole tenant/company scope, including other eligible preparer tasks.</p>
        {workspace.canAdministerDeclaration ? <form className="flex flex-wrap items-end gap-2" onSubmit={event => {
          event.preventDefault();
          if (!canEnable || !declarationReason.trim()) return;
          void submit({ kind: "declaration", request: { commandId: crypto.randomUUID(), expectedRevision: declaration?.revision ?? 0,
            enabled: true, principalId: workspace.actor, policyVersion: state!.policyVersion, reason: declarationReason.trim() } });
        }}>
          <label htmlFor={`${id}-declaration`} className="grid min-w-0 flex-1 gap-1">Declaration reason
            <Input id={`${id}-declaration`} disabled={!canEnable} value={declarationReason} maxLength={2000} required onChange={event => setDeclarationReason(event.target.value)} /></label>
          <Button type="submit" size="sm" variant="outline" disabled={!canEnable || !declarationReason.trim()}>Enable or reaffirm draft declaration</Button>
        </form> : <p>A declaration administrator must confirm the current policy.</p>}
      </div> : null}
      {snapshot ? <div className="grid gap-1">
        <p className="break-all font-mono text-xs">Exact revision {snapshot.revision} · source {snapshot.sourceHash}</p>
        <p>Source provenance: {snapshot.sourceProvenance}. Original preparer: {snapshot.preparedBy}.</p>
        <ul className="list-inside list-disc">{snapshot.checks.map(check => <li key={check}>{check}</li>)}</ul>
      </div> : null}
      <form className="grid gap-2" onSubmit={event => {
        event.preventDefault();
        if (!canCheck || !confirmed || !reason.trim() || !snapshot || !declaration) return;
        void submit({ kind: "check", request: { commandId: crypto.randomUUID(), expectedDeclarationRevision: declaration.revision,
          actionId: PREPARER_DRAFT_CHECK, subjectKind: snapshot.subjectKind, subjectId: snapshot.subjectId,
          subjectRevision: snapshot.revision, subjectHash: snapshot.hash, reason: reason.trim(), confirmation: snapshot.confirmation } });
      }}>
        <label htmlFor={`${id}-reason`} className="grid gap-1">Draft-check reason
          <Input id={`${id}-reason`} disabled={!canCheck} value={reason} maxLength={2000} required onChange={event => setReason(event.target.value)} /></label>
        <Checkbox disabled={!canCheck} checked={confirmed} onCheckedChange={setConfirmed}
          label={snapshot?.confirmation ?? "An exact server-confirmed calculation revision is required."} />
        <Button type="submit" size="sm" variant="outline" disabled={!canCheck || !confirmed || !reason.trim()}>Attest my non-postable draft check</Button>
      </form>
      <div className="grid gap-2" aria-label="Retained draft-check history"><h4 className="font-semibold">Retained draft checks ({receipts.length})</h4>
        {receipts.map(receipt => {
          const supported = isSupportedDraftReceipt(receipt);
          const revoked = state!.revocations.find(item => item.receiptId === receipt.receiptId);
          const revokeReason = revocationReasons[receipt.receiptId] ?? "";
          return <article key={receipt.receiptId} className="grid gap-2 rounded-sm border border-border/60 p-2">
            <p className="font-semibold">{supported ? PREPARER_ATTESTATION_DISCLOSURE : "Review classification unavailable; this record grants no approval."}</p>
            <p className="break-words">{receipt.attestedBy} at {receipt.recordedAt}: {receipt.reason}</p>
            <p>Source provenance: {receipt.sourceProvenance}. Historical assertion; current support has not been reassessed.</p>
            {revoked ? <p className="break-words">Revoked by {revoked.actor}: {revoked.reason}. Original assertion retained.</p> : null}
            {supported ? <PreparerAttestationProofDownload receiptId={receipt.receiptId} disabled={loading || busy} /> : null}
            {supported && !revoked && workspace.canRevokeDrafts === true && sameActor(receipt.attestedBy, workspace.actor) ? <form className="flex flex-wrap items-end gap-2" onSubmit={event => {
              event.preventDefault();
              if (locked || !revokeReason.trim()) return;
              void submit({ kind: "revoke", request: { commandId: crypto.randomUUID(), receiptId: receipt.receiptId, expectedReceiptRevision: 1, reason: revokeReason.trim() } });
            }}>
              <label className="grid min-w-0 flex-1 gap-1">Draft revocation reason<Input disabled={locked} value={revokeReason} maxLength={2000} required
                onChange={event => setRevocationReasons(previous => ({ ...previous, [receipt.receiptId]: event.target.value }))} /></label>
              <Button type="submit" size="sm" variant="outline" disabled={locked || !revokeReason.trim()}>Revoke my draft check</Button>
            </form> : null}
          </article>;
        })}
      </div>
    </> : null}
  </section>;
}

function sameActor(left?: string | null, right?: string | null) {
  return Boolean(left?.trim() && right?.trim()) && left!.trim().toLowerCase() === right!.trim().toLowerCase();
}
function message(error: unknown) { return error instanceof Error ? error.message : "Draft-check support is unavailable."; }
