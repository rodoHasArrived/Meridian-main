import { useEffect, useRef, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { PreparerAttestationProofDownload } from "@/components/meridian/preparer-attestation-evidence";
import { isApiError } from "@/lib/api-errors";
import {
  EVIDENCE_ACKNOWLEDGMENT, PREPARER_ATTESTATION_DISCLOSURE, changePrincipalDeclaration, getAttestationSubject, getAttestationWorkspace, isSupportedPreparerReceipt,
  recordPreparerAttestation, revokePreparerAttestation,
  type AttestationRequest, type AttestationSubject, type AttestationWorkspace,
  type DeclarationRequest, type RevocationRequest
} from "@/lib/single-principal-attestation-api";

type PendingCommand = { kind: "declaration"; request: DeclarationRequest }
  | { kind: "attestation"; request: AttestationRequest } | { kind: "revocation"; request: RevocationRequest };

/** A preparer assertion alongside document review; it never changes document acceptance or approval. */
export function SinglePrincipalAttestationPanel({ vaultId, documentId }: { vaultId: string; documentId: string }) {
  return <AttestationPanel key={`${vaultId}/${documentId}`} vaultId={vaultId} documentId={documentId} />;
}

function AttestationPanel({ vaultId, documentId }: { vaultId: string; documentId: string }) {
  const [workspace, setWorkspace] = useState<AttestationWorkspace | null>(null);
  const [subject, setSubject] = useState<AttestationSubject | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [reason, setReason] = useState("");
  const [declarationReason, setDeclarationReason] = useState("");
  const [revokeReason, setRevokeReason] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const [pending, setPending] = useState<PendingCommand | null>(null);
  const [reload, setReload] = useState(0);
  const alive = useRef(true);
  const inFlight = useRef(false);

  useEffect(() => {
    alive.current = true;
    return () => { alive.current = false; };
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setWorkspace(null);
    setSubject(null);
    setConfirmed(false);
    void Promise.allSettled([
      getAttestationWorkspace({ signal: controller.signal }),
      getAttestationSubject(vaultId, documentId, { signal: controller.signal })
    ]).then(([stateResult, subjectResult]) => {
      if (controller.signal.aborted) return;
      if (stateResult.status === "fulfilled") setWorkspace(stateResult.value);
      if (subjectResult.status === "fulfilled") setSubject(subjectResult.value);
      const failures = [stateResult, subjectResult].filter((result) => result.status === "rejected");
      setError(failures.length ? failures.map((result) => result.status === "rejected" ? errorMessage(result.reason) : "").join(" ") : null);
      setLoading(false);
    });
    return () => controller.abort();
  }, [vaultId, documentId, reload]);

  async function submit(command: PendingCommand) {
    if (inFlight.current) return;
    inFlight.current = true;
    setBusy(true);
    setPending(command);
    setError(null);
    setNotice(null);
    try {
      if (command.kind === "declaration") await changePrincipalDeclaration(command.request);
      else if (command.kind === "attestation") await recordPreparerAttestation(command.request);
      else await revokePreparerAttestation(command.request);
      if (!alive.current) return;
      setPending(null);
      setReason("");
      setDeclarationReason("");
      setRevokeReason("");
      setConfirmed(false);
      setNotice("Recorded. Reloading retained attestation history.");
      setReload((value) => value + 1);
    } catch (failure) {
      if (alive.current) {
        if (isApiError(failure) && failure.status >= 400 && failure.status < 500) {
          setPending(null);
          setConfirmed(false);
          setWorkspace(null);
          setSubject(null);
          setError(`${errorMessage(failure)} Refresh the current policy and evidence before trying again.`);
        } else setError(`${errorMessage(failure)} The outcome is unconfirmed. Retry the same command; do not create another assertion.`);
      }
    } finally {
      inFlight.current = false;
      if (alive.current) setBusy(false);
    }
  }

  const state = workspace?.state;
  const declaration = state?.declaration;
  const ownsDeclaration = declaration?.enabled && sameActor(declaration.principalId, workspace?.actor);
  const policyReconfirmationRequired = ownsDeclaration && declaration?.policyVersion !== state?.policyVersion;
  const locked = loading || busy || pending !== null;
  const canAttest = !locked && state?.policyEnabled === true && workspace?.canAttest === true
    && ownsDeclaration && subject?.eligible === true && state.allowedActions.includes(EVIDENCE_ACKNOWLEDGMENT);
  const receipts = state?.receipts.filter((receipt) => receipt.subjectKind === "evidence-document" && receipt.subjectId === `${vaultId}/${documentId}`) ?? [];

  return (
    <section className="mt-4 space-y-3 rounded-md border border-border/80 bg-secondary/10 p-3" aria-label="Single-principal preparer attestation" aria-busy={loading || busy}>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <h4 className="text-sm font-semibold">Preparer attestation</h4>
          <p className="mt-1 text-sm text-muted-foreground">Acknowledge inspecting retained evidence. This does not accept the document, approve a journal, or independently review an amount.</p>
        </div>
        <Button type="button" variant="outline" size="sm" disabled={loading || busy} onClick={() => setReload((value) => value + 1)}>Refresh attestation</Button>
      </div>
      {loading ? <p role="status" className="text-sm">Loading attestation policy and retained document snapshot…</p> : null}
      {error ? <p role="alert" className="break-words text-sm text-danger">{error}</p> : null}
      {notice ? <p role="status" className="text-sm">{notice}</p> : null}
      {pending && !busy ? <Button type="button" variant="outline" size="sm" onClick={() => void submit(pending)}>Retry same command</Button> : null}
      {state && workspace ? <>
        <div className="flex flex-wrap gap-2">
          <Badge variant={state.policyEnabled ? "warning" : "outline"}>{state.policyEnabled ? "Policy enabled" : "Policy disabled (default)"}</Badge>
          <Badge variant="outline">{declaration?.enabled ? "Declaration active" : "No active declaration"}</Badge>
        </div>
        <p className="text-sm font-semibold">{PREPARER_ATTESTATION_DISCLOSURE}</p>
        <p className="break-all text-xs text-muted-foreground">Scope: {state.scope.tenantId} / {state.scope.companyId}. Principal: {workspace.actor || "unavailable"}.</p>
        {!state.policyEnabled ? <p role="status" className="text-sm">Host policy is disabled. New declarations and attestations are unavailable. Existing assertions can still be revoked and declarations withdrawn.</p> : null}
        {!workspace.canAttest ? <p className="text-sm text-muted-foreground">Missing preparer-attestation grant.</p> : null}
        {!workspace.canAdministerDeclaration ? <p className="text-sm text-muted-foreground">Missing declaration-administration grant.</p> : null}
        {!workspace.canRevoke ? <p className="text-sm text-muted-foreground">Missing attestation-revocation grant.</p> : null}
        {declaration?.enabled && !ownsDeclaration ? <p className="text-sm text-muted-foreground">The active declaration belongs to another principal.</p> : null}
        {subject ? <details className="rounded-md border border-border/60 p-2">
          <summary className="cursor-pointer text-sm font-semibold">Exact retained evidence and checks</summary>
          <dl className="mt-2 grid gap-2 text-xs">
            {Object.entries({ Subject: subject.subjectId, Revision: subject.revision, "Source SHA-256": subject.sourceHash, "Source provenance": subject.sourceProvenance, "Source actor": subject.sourceActor ?? "Unrecorded" }).map(([label, value]) => <div key={label}><dt className="font-semibold">{label}</dt><dd className="break-all font-mono">{value}</dd></div>)}
          </dl>
          <ul className="mt-2 list-disc space-y-1 pl-4 text-sm">{subject.checks.map((check) => <li key={check}>{check}</li>)}</ul>
        </details> : null}
        {subject && !subject.eligible ? <p role="status" className="text-sm">This retained document is not eligible. Resolve the evidence checks before attesting.</p> : null}
        <form className="space-y-2" onSubmit={(event) => {
          event.preventDefault();
          if (!canAttest || !confirmed || !reason.trim() || !subject || !declaration) return;
          void submit({ kind: "attestation", request: {
            commandId: crypto.randomUUID(), expectedDeclarationRevision: declaration.revision,
            actionId: EVIDENCE_ACKNOWLEDGMENT, subjectKind: subject.subjectKind, subjectId: subject.subjectId,
            subjectRevision: subject.revision, subjectHash: subject.hash, reason: reason.trim(), confirmation: state.confirmation
          } });
        }}>
          <label className="grid gap-1 text-sm">Attestation reason<Input value={reason} disabled={!canAttest} onChange={(event) => setReason(event.target.value)} required maxLength={2000} /></label>
          <label className="flex items-start gap-2 text-sm"><input className="mt-1 accent-primary" type="checkbox" checked={confirmed} disabled={!canAttest} onChange={(event) => setConfirmed(event.target.checked)} /><span>{state.confirmation}</span></label>
          <Button type="submit" size="sm" disabled={!canAttest || !reason.trim() || !confirmed}>Attest evidence inspection</Button>
        </form>
        <details className="rounded-md border border-border/60 p-2">
          <summary className="cursor-pointer text-sm font-semibold">Single-principal declaration and history</summary>
          <p className="mt-2 text-sm">Enabling names the current principal only. Withdrawal stops new attestations; retained receipts remain visible.</p>
          {policyReconfirmationRequired ? <p className="mt-2 text-sm">The host offers a newer policy. Reconfirmation records a new declaration revision; the original policy and receipts remain historical.</p> : null}
          <form className="mt-2 space-y-2" onSubmit={(event) => {
            event.preventDefault();
            if (locked || (!state.policyEnabled && !declaration?.enabled) || !workspace.canAdministerDeclaration || !declarationReason.trim()) return;
            void submit({ kind: "declaration", request: { commandId: crypto.randomUUID(), expectedRevision: declaration?.revision ?? 0,
              enabled: !declaration?.enabled, principalId: workspace.actor, policyVersion: state.policyVersion, reason: declarationReason.trim() } });
          }}>
            <label className="grid gap-1 text-sm">Declaration reason<Input disabled={locked || !workspace.canAdministerDeclaration || (!state.policyEnabled && !declaration?.enabled)} value={declarationReason} onChange={(event) => setDeclarationReason(event.target.value)} required maxLength={2000} /></label>
            <Button type="submit" variant="outline" size="sm" disabled={locked || (!state.policyEnabled && !declaration?.enabled) || !workspace.canAdministerDeclaration || !declarationReason.trim()}>{declaration?.enabled ? "Withdraw declaration" : "Enable declaration for myself"}</Button>
            {policyReconfirmationRequired ? <Button type="button" variant="outline" size="sm"
              disabled={locked || !state.policyEnabled || !workspace.canAdministerDeclaration || !declarationReason.trim()}
              onClick={() => void submit({ kind: "declaration", request: {
                commandId: crypto.randomUUID(), expectedRevision: declaration.revision, enabled: true,
                principalId: workspace.actor, policyVersion: state.policyVersion, reason: declarationReason.trim()
              } })}>Reconfirm current policy</Button> : null}
          </form>
          <ul className="mt-2 space-y-2 text-sm" aria-label="Declaration history">{state.declarationHistory.map((entry) => <li key={entry.revision} className="break-words">Revision {entry.revision}: {entry.enabled ? "Enabled" : "Withdrawn"} by {entry.changedBy} at {entry.recordedAt}. {entry.reason}</li>)}</ul>
        </details>
        <div className="space-y-2">
          <h5 className="text-sm font-semibold">Retained preparer assertions ({receipts.length})</h5>
          {!receipts.length ? <p className="text-sm text-muted-foreground">No retained assertions for this document.</p> : null}
          {receipts.map((receipt) => {
            const revocation = state.revocations.find((entry) => entry.receiptId === receipt.receiptId);
            const supported = isSupportedPreparerReceipt(receipt);
            return <div key={receipt.receiptId} className="space-y-2 rounded-md border border-border/70 p-2 text-sm">
              <p className="font-semibold">{supported ? PREPARER_ATTESTATION_DISCLOSURE : "Review classification unavailable — unsupported receipt"}</p>
              <p className="break-words">{receipt.attestedBy} at {receipt.recordedAt}. {receipt.reason}</p>
              <p className="break-all text-xs">Receipt {receipt.receiptId}. Evidence revision {receipt.subjectRevision}.</p>
              <p className="break-words text-xs">Source provenance: {receipt.sourceProvenance}. Source actor: {receipt.sourceActor ?? "Unrecorded"}.</p>
              <p className="text-sm">{revocation ? `Revoked by ${revocation.actor} at ${revocation.recordedAt}: ${revocation.reason}` : "Historical assertion; current support is not assessed. No independent approval."}</p>
              {supported ? <PreparerAttestationProofDownload receiptId={receipt.receiptId} disabled={loading || busy} /> : null}
              {supported && !revocation && sameActor(receipt.attestedBy, workspace.actor) && workspace.canRevoke ? <form className="space-y-2" onSubmit={(event) => {
                event.preventDefault();
                if (locked || !revokeReason.trim()) return;
                void submit({ kind: "revocation", request: { commandId: crypto.randomUUID(), receiptId: receipt.receiptId, expectedReceiptRevision: 1, reason: revokeReason.trim() } });
              }}>
                <label className="grid gap-1 text-sm">Revocation reason<Input disabled={locked} value={revokeReason} onChange={(event) => setRevokeReason(event.target.value)} required maxLength={2000} /></label>
                <Button type="submit" variant="outline" size="sm" disabled={locked || !revokeReason.trim()}>Revoke my assertion</Button>
              </form> : null}
            </div>;
          })}
        </div>
      </> : null}
    </section>
  );
}

function errorMessage(error: unknown) { return error instanceof Error ? error.message : "Attestation evidence is unavailable."; }

function sameActor(left?: string | null, right?: string | null) {
  return Boolean(left?.trim() && right?.trim()) && left!.trim().toLowerCase() === right!.trim().toLowerCase();
}
