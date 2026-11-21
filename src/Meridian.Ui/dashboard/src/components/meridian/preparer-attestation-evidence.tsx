import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import {
  getPreparerAttestationProof,
  isSupportedPreparerReceipt,
  PREPARER_ATTESTATION_DISCLOSURE,
  type AttestationScope
} from "@/lib/single-principal-attestation-api";
import type {
  PreparerAttestationEvidenceProjection,
  PreparerEvidenceAcknowledgment
} from "@/types/preparer-attestation-evidence";

const boundary = "These are evidence-task acknowledgments. They do not approve an amount, establish independent review, or contribute to Verified Coverage.";

/** A separate assertion classification; never a readiness or financial-assurance badge. */
export function PreparerAttestationEvidence({ projection, expectedScope, inline = false }: {
  projection?: PreparerAttestationEvidenceProjection | null;
  expectedScope?: AttestationScope;
  inline?: boolean;
}) {
  if (!projection) return null;
  const known = projection.schemaVersion === 1 && projection.disclosure === PREPARER_ATTESTATION_DISCLOSURE
    && (!expectedScope || sameScope(projection.scope, expectedScope, true));
  const available = known && projection.availability === "Available" && validCounts(projection);
  const unavailable = !available;
  const summary = unavailable
    ? "Preparer evidence classification unavailable. No financial approval is established."
    : `${projection.recordedReceiptCount} recorded evidence-task receipt(s); ${projection.unrevokedReceiptCount} unrevoked; ${projection.revokedReceiptCount} revoked.`;

  // Inline Number Passports must remain phrasing content inside a numerical readout.
  if (inline) return <span role="group" aria-label="Preparer evidence classification" className="mt-3 block space-y-1 border-t border-border/70 pt-2 text-sm">
    <span className="block font-semibold">Preparer evidence acknowledgments</span>
    <span className="block">{summary}</span>
    {available && projection.acknowledgments.length ? <span className="block font-semibold">{PREPARER_ATTESTATION_DISCLOSURE}</span> : null}
    <span className="block text-muted-foreground">{boundary}</span>
  </span>;

  return <section aria-label="Preparer evidence classification" className="space-y-3 rounded-md border border-border/70 bg-secondary/10 p-3 text-sm">
    <h4 className="font-semibold">Preparer evidence acknowledgments</h4>
    <p role={unavailable ? "status" : undefined}>{summary}</p>
    <p className="text-muted-foreground">{boundary}</p>
    {available ? <>
      <p className="text-xs text-muted-foreground">Assessed at {projection.assessedAt}. Source provenance: {projection.syntheticReceiptCount} synthetic; {projection.unclassifiedReceiptCount} unclassified. Unrevoked does not mean currently supported.</p>
      {projection.acknowledgments.map((entry) => <Acknowledgment key={entry.receipt.receiptId} entry={entry} scope={projection.scope} />)}
    </> : null}
    {projection.warnings?.length ? <ul className="list-disc space-y-1 pl-4" aria-label="Preparer evidence warnings">{projection.warnings.map((warning, index) => <li key={index}>{warning}</li>)}</ul> : null}
  </section>;
}

function Acknowledgment({ entry, scope }: { entry: PreparerEvidenceAcknowledgment; scope: AttestationScope }) {
  const { receipt, originalDeclaration, revocation } = entry;
  const known = isSupportedPreparerReceipt(receipt) && sameScope(receipt.scope, scope)
    && sameScope(originalDeclaration.scope, scope) && originalDeclaration.declarationId === receipt.declarationId
    && originalDeclaration.revision === receipt.declarationRevision && originalDeclaration.enabled
    && originalDeclaration.policyVersion === receipt.policyVersion && originalDeclaration.policyHash === receipt.policyHash
    && receipt.origin === "HumanOperator" && sameText(receipt.preparedBy, receipt.attestedBy)
    && sameText(originalDeclaration.principalId, receipt.attestedBy);
  const revocationMatches = revocation?.receiptId === receipt.receiptId;
  const applicability = !known ? "Review classification unavailable — unsupported or mismatched receipt"
    : revocationMatches ? "Revoked; original assertion retained."
      : entry.applicability === "ExactSource" ? "The exact source snapshot matches this assessment. Financial approval remains separate."
        : entry.applicability === "StaleSource" ? "Source changed; this historical assertion does not establish current support."
          : "Current source applicability is unavailable; historical assertion only.";
  return <article className="space-y-2 rounded-md border border-border/60 bg-background/50 p-3">
    <p className="font-semibold">{known ? PREPARER_ATTESTATION_DISCLOSURE : "Review classification unavailable — unsupported receipt"}</p>
    <p>{applicability}</p>
    {known ? <>
      <p className="break-words">{receipt.attestedBy} at {receipt.recordedAt}. {receipt.reason}</p>
      <p className="break-words text-xs">Source provenance: {receipt.sourceProvenance}. Source actor: {receipt.sourceActor ?? "Unrecorded"}.</p>
      <details className="rounded-md border border-border/60 p-2">
        <summary className="cursor-pointer font-semibold">Exact assertion and declaration</summary>
        <dl className="mt-2 grid gap-2 text-xs">
          {Object.entries({ Reference: `preparer-attestation:v1:${receipt.receiptId}`, Subject: receipt.subjectId,
            "Subject revision": receipt.subjectRevision, "Source SHA-256": receipt.sourceHash,
            Declaration: `${receipt.declarationId} / revision ${receipt.declarationRevision}`,
            Policy: `${receipt.policyVersion} / ${receipt.policyHash}` }).map(([label, value]) =>
            <div key={label}><dt className="font-semibold">{label}</dt><dd className="break-all font-mono">{value}</dd></div>)}
        </dl>
      </details>
      {revocationMatches ? <p className="break-words">Revoked by {revocation.actor} at {revocation.recordedAt}: {revocation.reason}</p> : null}
      <PreparerAttestationProofDownload receiptId={receipt.receiptId} />
    </> : null}
  </article>;
}

export function PreparerAttestationProofDownload({ receiptId, disabled = false }: { receiptId: string; disabled?: boolean }) {
  return <ProofDownload key={receiptId} receiptId={receiptId} disabled={disabled} />;
}

function ProofDownload({ receiptId, disabled }: { receiptId: string; disabled: boolean }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const request = useRef<AbortController | null>(null);
  useEffect(() => () => request.current?.abort(), [receiptId]);

  async function download() {
    if (request.current || disabled) return;
    const controller = new AbortController();
    request.current = controller;
    setBusy(true);
    setError(null);
    try {
      const blob = await getPreparerAttestationProof(receiptId, { signal: controller.signal });
      if (controller.signal.aborted) return;
      const url = URL.createObjectURL(blob);
      try {
        const anchor = document.createElement("a");
        anchor.href = url;
        anchor.download = `preparer-assertion-${receiptId}.json`;
        anchor.click();
      } finally { URL.revokeObjectURL(url); }
    } catch (failure) {
      if (!controller.signal.aborted)
        setError(failure instanceof Error ? failure.message : "Original assertion proof is unavailable.");
    } finally {
      request.current = null;
      if (!controller.signal.aborted) setBusy(false);
    }
  }
  return <div className="space-y-1">
    <Button type="button" variant="outline" size="sm" disabled={disabled || busy} onClick={() => void download()}>
      {busy ? "Downloading original proof…" : "Download original proof"}
    </Button>
    {error ? <p role="alert" className="text-sm text-danger">{error}</p> : null}
  </div>;
}

function sameText(left?: string | null, right?: string | null) {
  return Boolean(left?.trim() && right?.trim()) && left!.trim().toLowerCase() === right!.trim().toLowerCase();
}
function sameScope(actual: AttestationScope, expected: AttestationScope, optionalBook = false) {
  return sameText(actual?.tenantId, expected?.tenantId) && sameText(actual?.companyId, expected?.companyId)
    && (optionalBook && !actual.ledgerBookId || (actual.ledgerBookId ?? "").toLowerCase() === (expected.ledgerBookId ?? "").toLowerCase());
}
function validCounts(projection: PreparerAttestationEvidenceProjection) {
  const counts = [projection.recordedReceiptCount, projection.unrevokedReceiptCount, projection.revokedReceiptCount,
    projection.syntheticReceiptCount, projection.unclassifiedReceiptCount];
  return counts.every(count => count !== null && Number.isInteger(count) && count >= 0)
    && Array.isArray(projection.acknowledgments)
    && projection.acknowledgments.every(entry => Boolean(entry?.receipt?.receiptId && entry?.originalDeclaration?.declarationId))
    && projection.recordedReceiptCount === projection.acknowledgments?.length
    && projection.unrevokedReceiptCount! + projection.revokedReceiptCount! === projection.recordedReceiptCount
    && projection.syntheticReceiptCount! + projection.unclassifiedReceiptCount! === projection.recordedReceiptCount
    && new Set(projection.acknowledgments.map(entry => entry.receipt.receiptId)).size === projection.acknowledgments.length;
}
