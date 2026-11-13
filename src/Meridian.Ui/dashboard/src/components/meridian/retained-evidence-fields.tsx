import { useId } from "react";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";

export interface RetainedEvidenceFieldsValue {
  evidenceId: string; evidenceUri: string; contentHashSha256: string; sourceSystem: string; sourceReference: string;
  reviewedBy: string; reviewedAtUtc: string; effectiveDate: string; evidenceVersion: string;
  retainedAtUtc: string; retainedBy: string; subjectId: string;
}
export const emptyRetainedEvidence = (): RetainedEvidenceFieldsValue => ({ evidenceId: "", evidenceUri: "", contentHashSha256: "", sourceSystem: "", sourceReference: "", reviewedBy: "", reviewedAtUtc: "", effectiveDate: "", evidenceVersion: "", retainedAtUtc: "", retainedBy: "", subjectId: "" });
const fields = [["evidenceId", "Evidence ID"], ["evidenceUri", "Evidence URI"], ["subjectId", "Subject ID"], ["evidenceVersion", "Evidence version"], ["effectiveDate", "Effective date", "date"], ["contentHashSha256", "Content SHA-256"], ["sourceSystem", "Source system"], ["sourceReference", "Source reference"], ["reviewedBy", "Reviewed by"], ["reviewedAtUtc", "Reviewed at (UTC)"], ["retainedBy", "Retained by"], ["retainedAtUtc", "Retained at (UTC)"]] as const;
/** Metadata identifies an existing Accepted retained artifact; entry never grants evidence authority. */
export function RetainedEvidenceFields({ label, subjectType, value, onChange }: { label: string; subjectType: string; value: RetainedEvidenceFieldsValue; onChange: (value: RetainedEvidenceFieldsValue) => void }) {
  const id = useId();
  return <details className="rounded border border-border p-3"><summary className="cursor-pointer text-sm font-semibold">{label}</summary>
    <p className="my-2 text-xs text-muted-foreground">Identify existing Accepted evidence of type {subjectType}. Copy metadata from its retained record; entering it does not create or approve evidence.</p>
    <FormGrid columns={3}>{fields.map(([key, title, type]) => <FormRow key={key} label={title} labelFor={`${id}-${key}`}><Input id={`${id}-${key}`} aria-label={`${label}: ${title}`} type={type ?? "text"} value={value[key]} onChange={event => onChange({ ...value, [key]: event.target.value })} /></FormRow>)}</FormGrid>
  </details>;
}
export function retainedEvidencePayload(value: RetainedEvidenceFieldsValue, subjectType: string): Record<string, unknown> {
  if (Object.values(value).some(item => !item.trim())) throw new Error("Complete the retained evidence identity fields before preparing.");
  if (!/^\d+$/.test(value.evidenceVersion) || !Number.isSafeInteger(Number(value.evidenceVersion)) || Number(value.evidenceVersion) < 1)
    throw new Error("Evidence version must be a positive whole number.");
  if (!/^[a-fA-F0-9]{64}$/.test(value.contentHashSha256)) throw new Error("Evidence SHA-256 must contain 64 hexadecimal characters.");
  for (const instant of [value.reviewedAtUtc, value.retainedAtUtc]) {
    if (!/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d+)?Z$/.test(instant) || !Number.isFinite(Date.parse(instant)))
      throw new Error("Evidence review and retention times require explicit UTC timestamps, for example 2026-11-01T12:00:00Z.");
  }
  return { ...value, evidenceVersion: Number(value.evidenceVersion), subjectType, reviewStatus: "Accepted" };
}
