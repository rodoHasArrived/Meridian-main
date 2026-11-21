import { useId } from "react";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
export const emptyRetainedEvidence = { evidenceId: "", uri: "", kind: "Accrual", sourceSystem: "", retainedAtUtc: "", retainedBy: "", contentHash: "", evidenceVersion: "" };
export type RetainedEvidenceForm = typeof emptyRetainedEvidence;
export function RetainedEvidenceFields({ value, onChange }: { value: RetainedEvidenceForm; onChange: (value: RetainedEvidenceForm) => void }) {
  const id = useId();
  return <fieldset className="space-y-2"><legend className="text-sm font-semibold">Retained source evidence</legend>
    <p className="text-xs text-muted-foreground">Use an existing retained record. Its identity, custodian and hash or version remain attached to every occurrence for review.</p>
    <FormGrid columns={2}>{([ ["evidenceId", "Evidence identity"], ["uri", "Retained evidence location"], ["kind", "Evidence kind"], ["sourceSystem", "Evidence source system"], ["retainedAtUtc", "Evidence retained at (UTC)"], ["retainedBy", "Evidence custodian"], ["contentHash", "Evidence SHA-256 (or supply version)"], ["evidenceVersion", "Evidence version (or supply SHA-256)"] ] as const).map(([field, label]) => <FormRow key={field} label={label} labelFor={`${id}-${field}`}>
      <Input id={`${id}-${field}`} required={field !== "contentHash" && field !== "evidenceVersion"} value={value[field]} placeholder={field === "retainedAtUtc" ? "2026-10-01T00:00:00Z" : undefined} onChange={event => onChange({ ...value, [field]: event.target.value })} />
    </FormRow>)}</FormGrid>
  </fieldset>;
}
export function retainedEvidenceReference(value: RetainedEvidenceForm): Record<string, unknown> {
  if (Object.entries(value).some(([key, text]) => key !== "contentHash" && key !== "evidenceVersion" && !text.trim())) throw new Error("Complete the retained evidence identity, location, source, time and custodian.");
  if (!Number.isFinite(Date.parse(value.retainedAtUtc)) || !/(?:Z|\+00:00)$/.test(value.retainedAtUtc)) throw new Error("Enter the evidence retention time in UTC, ending in Z or +00:00.");
  if (!value.contentHash && !/^[1-9]\d*$/.test(value.evidenceVersion)) throw new Error("Supply an existing evidence SHA-256 or positive source version.");
  if (value.contentHash && !/^[a-f0-9]{64}$/i.test(value.contentHash)) throw new Error("The evidence SHA-256 must contain 64 hexadecimal characters.");
  const version = value.evidenceVersion ? Number(value.evidenceVersion) : null;
  if (version !== null && (!Number.isSafeInteger(version) || version < 1)) throw new Error("The evidence version must be a positive safe integer.");
  return { ...value, contentHash: value.contentHash || null, evidenceVersion: version };
}
