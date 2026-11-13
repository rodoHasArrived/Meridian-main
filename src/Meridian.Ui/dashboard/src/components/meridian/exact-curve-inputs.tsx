import { useEffect, useId, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { FormGrid, FormRow } from "@/components/ui/form";
import { describeApiError } from "@/lib/api-errors";
import { listSecurityCurves, listSecurityCurveVersions } from "@/lib/api/security-curves.api";
import type { ExactCurvePin, SecurityCurveDefinition, SecurityCurveVersion } from "@/types/security-curves.types";

export interface ExactCurveInputsProps {
  pins: ExactCurvePin[];
  onChange: (pins: ExactCurvePin[]) => void;
  disabled?: boolean;
  roleLabel?: string;
  helpText?: string;
  required?: boolean;
}

/** Explicit retained version selection; never silently chooses the latest version. */
export function ExactCurveInputs({ pins, onChange, disabled = false, roleLabel = "Calculation role", required = false, helpText = "Choose immutable retained inputs for reproducibility. With no pins, the server resolves eligible market inputs or returns explicit blockers." }: ExactCurveInputsProps) {
  const [open, setOpen] = useState(false);
  return <fieldset className="min-w-0 space-y-3" disabled={disabled}>
    <legend className="text-sm font-semibold">Exact curve versions ({required ? "required" : "optional"})</legend>
    <p className="text-xs text-muted-foreground">{helpText}</p>
    <Button type="button" variant="outline" onClick={() => { setOpen(true); onChange([...pins, { role: "", curveId: "", curveVersionId: "" }]); }}>Add curve pin</Button>
    {(open || pins.length > 0) && pins.map((pin, index) => <CurvePinRow key={index} pin={pin} index={index} roleLabel={roleLabel}
      onChange={(next) => onChange(pins.map((row, i) => i === index ? next : row))}
      onRemove={() => onChange(pins.filter((_, i) => i !== index))} />)}
  </fieldset>;
}

function CurvePinRow({ pin, index, roleLabel, onChange, onRemove }: {
  pin: ExactCurvePin; index: number; roleLabel: string;
  onChange: (pin: ExactCurvePin) => void; onRemove: () => void;
}) {
  const id = useId();
  const [definitions, setDefinitions] = useState<SecurityCurveDefinition[]>([]);
  const [versions, setVersions] = useState<SecurityCurveVersion[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [reload, setReload] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    setError(null);
    listSecurityCurves({ signal: controller.signal }).then((rows) => {
      if (!controller.signal.aborted) setDefinitions(rows);
    }).catch((failure) => { if (!controller.signal.aborted) setError(describeApiError(failure, "Curve definitions unavailable.").summary); });
    return () => controller.abort();
  }, [reload]);
  useEffect(() => {
    const controller = new AbortController();
    setVersions([]);
    setLoading(false);
    if (!pin.curveId) return () => controller.abort();
    setLoading(true);
    setError(null);
    listSecurityCurveVersions(pin.curveId, { signal: controller.signal }).then((rows) => {
      if (controller.signal.aborted) return;
      if (rows.some((row) => row.curveId !== pin.curveId)) throw new Error("Curve versions returned a different curve.");
      setVersions(rows);
    }).catch((failure) => { if (!controller.signal.aborted) setError(describeApiError(failure, "Curve versions unavailable.").summary); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [pin.curveId, reload]);
  return <div className="rounded border border-border p-3 space-y-2">
    <FormGrid columns={3}>
      <FormRow label={`${roleLabel} ${index + 1}`} labelFor={`${id}-role`}><Input id={`${id}-role`} required value={pin.role} onChange={(e) => onChange({ ...pin, role: e.target.value })} /></FormRow>
      <FormRow label={`Curve ${index + 1}`} labelFor={`${id}-curve`}><Select id={`${id}-curve`} required value={pin.curveId} onChange={(e) => onChange({ ...pin, curveId: e.target.value, curveVersionId: "" })}>
        <option value="">Select curve</option>{pin.curveId && !definitions.some((row) => row.curveId === pin.curveId) ? <option value={pin.curveId}>{pin.curveId} (retained selection)</option> : null}
        {definitions.map((row) => <option key={row.curveId} value={row.curveId}>{row.curveKey} · {row.kind} · {row.currency}</option>)}
      </Select></FormRow>
      <FormRow label={`Curve version ${index + 1}`} labelFor={`${id}-version`}><Select id={`${id}-version`} required disabled={!pin.curveId || loading} value={pin.curveVersionId} onChange={(e) => onChange({ ...pin, curveVersionId: e.target.value })}>
        <option value="">{loading ? "Loading versions…" : "Select exact version"}</option>
        {pin.curveVersionId && !versions.some((row) => row.curveVersionId === pin.curveVersionId) ? <option value={pin.curveVersionId}>{pin.curveVersionId} (retained selection)</option> : null}
        {versions.map((row) => <option key={row.curveVersionId} value={row.curveVersionId}>v{row.version} · {row.asOf} · recorded {row.recordedAt}</option>)}
      </Select></FormRow>
    </FormGrid>
    {error ? <p role="alert" className="text-xs text-danger">{error} <Button type="button" variant="outline" onClick={() => setReload((value) => value + 1)}>Retry curves</Button></p> : null}
    {!error && pin.curveId && !loading && versions.length === 0 ? <p role="status" className="text-xs">No retained versions are available for this curve.</p> : null}
    {versions.find((row) => row.curveVersionId === pin.curveVersionId) ? <p className="break-all font-mono text-xs">{pin.curveVersionId} · SHA-256 {versions.find((row) => row.curveVersionId === pin.curveVersionId)?.contentSha256}</p> : null}
    <Button type="button" variant="outline" onClick={onRemove}>Remove curve pin {index + 1}</Button>
  </div>;
}
