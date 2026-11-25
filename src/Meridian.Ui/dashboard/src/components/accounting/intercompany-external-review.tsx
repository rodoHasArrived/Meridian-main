import { useId } from "react";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import type { AgreementExternalReview, AgreementExternalReviewAttestation } from "@/types/intercompany-agreements";

export type ExternalReviewInput = Omit<AgreementExternalReview, "submittedHash">;
export const newExternalReviewInput = (): ExternalReviewInput => ({ externalReviewer: "", reviewerReference: "", reviewedOn: new Date().toISOString().slice(0, 10), evidence: { reference: "", artifactId: "" }, rationale: "" });
export function ExternalLegalReviewFields({ value, onChange, subjectHash, busy = false }: { value: ExternalReviewInput; onChange: (value: ExternalReviewInput) => void; subjectHash: string; busy?: boolean }) {
  const id = useId();
  return <fieldset disabled={busy} className="space-y-3 rounded-md border border-border p-3"><legend className="px-1 font-semibold">External legal review attestation</legend>
    <p className="text-sm text-muted-foreground">The authenticated operator attests the retained external review. The preparer may attest; the external reviewer is not represented as a Meridian user.</p>
    <FormGrid columns={3}>
      <FormRow label="External reviewer" labelFor={`${id}-name`}><Input id={`${id}-name`} required value={value.externalReviewer} onChange={(e) => onChange({ ...value, externalReviewer: e.target.value })} /></FormRow>
      <FormRow label="External reviewer reference" labelFor={`${id}-ref`}><Input id={`${id}-ref`} required value={value.reviewerReference} onChange={(e) => onChange({ ...value, reviewerReference: e.target.value })} /></FormRow>
      <FormRow label="External review date" labelFor={`${id}-date`}><Input id={`${id}-date`} required type="date" value={value.reviewedOn} onChange={(e) => onChange({ ...value, reviewedOn: e.target.value })} /></FormRow>
      <FormRow label="External review evidence reference" labelFor={`${id}-evidence`} hint="Canonical evidence://evidence-vault/ reference from retained evidence."><Input id={`${id}-evidence`} required value={value.evidence.reference} onChange={(e) => onChange({ ...value, evidence: { ...value.evidence, reference: e.target.value } })} /></FormRow>
      <FormRow label="External review artifact ID" labelFor={`${id}-artifact`}><Input id={`${id}-artifact`} required value={value.evidence.artifactId ?? ""} onChange={(e) => onChange({ ...value, evidence: { ...value.evidence, artifactId: e.target.value } })} /></FormRow>
      <FormRow label="External-review attestation rationale" labelFor={`${id}-rationale`}><Input id={`${id}-rationale`} required value={value.rationale} onChange={(e) => onChange({ ...value, rationale: e.target.value })} /></FormRow>
    </FormGrid>
    <p className="break-all text-sm text-muted-foreground">Exact reviewed subject: {subjectHash}</p>
  </fieldset>;
}
export function isCompleteExternalReview(value: ExternalReviewInput) {
  return Boolean(value.externalReviewer.trim() && value.reviewerReference.trim() && value.reviewedOn && value.evidence.reference.trim() && value.evidence.artifactId?.trim() && value.rationale.trim());
}
export function ExternalLegalReviewReceipt({ review }: { review: AgreementExternalReviewAttestation }) {
  const vaultId = /^evidence:\/\/evidence-vault\/([A-Za-z0-9_-]+)$/.exec(review.evidence.reference)?.[1];
  return <div className="space-y-1 rounded border border-border p-3 text-sm"><p className="font-semibold">Externally reviewed; attested by {review.operator}</p><p>{review.externalReviewer} · {review.reviewerReference} · reviewed {review.reviewedOn}</p><p>{review.rationale} · recorded {review.recordedAtUtc}</p><p className="break-all">{vaultId ? <a className="underline underline-offset-4" href={`/workstation/evidence/vault/${encodeURIComponent(vaultId)}`} target="_blank" rel="noreferrer">{review.evidence.reference}</a> : review.evidence.reference} · artifact {review.evidence.artifactId} · SHA-256 {review.evidence.contentHash ?? "Retained by evidence authority"}</p><p className="break-all text-muted-foreground">Submitted subject {review.submittedHash}</p></div>;
}
