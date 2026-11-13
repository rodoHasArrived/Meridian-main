import { type FormEvent, useEffect, useId, useRef, useState } from "react";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { bondDecimalText, DECIMAL_LITERAL, utcInstantFromLocalInput } from "@/lib/bond-decimal-format";
import { describeApiError, isApiError } from "@/lib/api-errors";
import { defineSecurityCurve, listSecurityCurves, listSecurityCurveVersions, publishSecurityCurve } from "@/lib/api/security-curves.api";
import type { DefineSecurityCurveRequest, SecurityCurveDefinition, SecurityCurvePoint, SecurityCurveVersion } from "@/types/security-curves.types";

const emptyDefinition: DefineSecurityCurveRequest = {
  curveKey: "", kind: "Discount", currency: "", indexName: null, tenor: null,
  dayCount: "Actual365", interpolation: "Linear", source: "", description: null
};
const emptyPoint = (): SecurityCurvePoint => ({ date: "", value: "", tenor: "" });
interface ComparedPillar { date: string; inspect: string; compare: string; tenor: string }
const pillarColumns: DenseDataTableColumn<ComparedPillar>[] = [
  { id: "date", label: "Date", render: (row) => row.date },
  { id: "inspect", label: "Inspect value", align: "right", render: (row) => <span className="font-mono">{row.inspect}</span> },
  { id: "compare", label: "Compare value", align: "right", render: (row) => <span className="font-mono">{row.compare}</span> },
  { id: "tenor", label: "Tenor", render: (row) => row.tenor }
];

/** Write-once definitions and append-only versions; all financial values remain exact input text. */
export function SecurityCurveWorkbench() {
  const id = useId();
  const [definitions, setDefinitions] = useState<SecurityCurveDefinition[]>([]);
  const [selected, setSelected] = useState("");
  const [versions, setVersions] = useState<SecurityCurveVersion[]>([]);
  const [left, setLeft] = useState("");
  const [right, setRight] = useState("");
  const [definition, setDefinition] = useState<DefineSecurityCurveRequest>(emptyDefinition);
  const [points, setPoints] = useState<SecurityCurvePoint[]>([emptyPoint(), emptyPoint()]);
  const [asOf, setAsOf] = useState("");
  const [source, setSource] = useState("");
  const [sourcedAt, setSourcedAt] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [readDenied, setReadDenied] = useState(false);
  const [writeDenied, setWriteDenied] = useState(false);
  const [reload, setReload] = useState(0);
  const [loading, setLoading] = useState(false);
  const request = useRef<AbortController | null>(null);
  const mutation = useRef<AbortController | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true); setError(null); setReadDenied(false);
    listSecurityCurves({ signal: controller.signal }).then((rows) => {
      if (!controller.signal.aborted) setDefinitions(rows);
    }).catch((failure) => {
      if (controller.signal.aborted) return;
      setDefinitions([]); setVersions([]);
      setReadDenied(isApiError(failure) && [401, 403].includes(failure.status));
      setError(describeApiError(failure, "Curves unavailable. This host needs the Security Master curve store.").summary);
    }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [reload]);
  useEffect(() => {
    request.current?.abort();
    const controller = new AbortController(); request.current = controller;
    setVersions([]); setLeft(""); setRight(""); setConfirmed(false); setMessage(null);
    if (!selected) return () => controller.abort();
    setLoading(true); setError(null);
    listSecurityCurveVersions(selected, { signal: controller.signal }).then((rows) => {
      if (controller.signal.aborted) return;
      if (rows.some((row) => row.curveId !== selected)) throw new Error("The response contains versions of a different curve. Refresh the curve list.");
      setVersions(rows);
    }).catch((failure) => { if (!controller.signal.aborted) setError(describeApiError(failure, "Curve history unavailable.").summary); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [selected, reload]);
  useEffect(() => () => mutation.current?.abort(), []);

  function editPoints(change: () => void) { setConfirmed(false); setMessage(null); change(); }
  function showFailure(failure: unknown) {
    if (isApiError(failure) && [401, 403].includes(failure.status)) {
      setWriteDenied(true);
      setError("Creating definitions and publishing versions require ModifySecurityMaster permission. Your current session cannot perform this action.");
    } else setError(describeApiError(failure, "Curve write unavailable. Inspect retained history before retrying an uncertain write.").summary);
  }
  async function create(event: FormEvent) {
    event.preventDefault();
    if (busy || writeDenied || readDenied) return;
    if (!definition.curveKey.trim() || !/^[A-Za-z]{3}$/.test(definition.currency.trim()) || !definition.source.trim() || (definition.kind !== "Discount" && !definition.indexName?.trim())) {
      setError("Enter a curve key, three-letter currency, source, and an index for Forward or Inflation curves."); return;
    }
    const controller = new AbortController(); mutation.current = controller;
    setBusy(true); setError(null); setMessage(null);
    try {
      const row = await defineSecurityCurve({ ...definition, currency: definition.currency.trim().toUpperCase(), indexName: definition.kind === "Discount" ? null : definition.indexName?.trim() }, { signal: controller.signal });
      if (controller.signal.aborted) return;
      setDefinitions((rows) => [...rows.filter((candidate) => candidate.curveId !== row.curveId), row]);
      setSelected(row.curveId); setDefinition(emptyDefinition);
      setMessage(`Definition retained: ${row.curveKey}. Publish dated pillars below.`);
    } catch (failure) { if (!controller.signal.aborted) showFailure(failure); }
    finally { if (!controller.signal.aborted) setBusy(false); }
  }
  async function publish(event: FormEvent) {
    event.preventDefault();
    if (busy || writeDenied || readDenied || !selected) return;
    if (!confirmed || !asOf || !source.trim() || points.length === 0 || points.some((point) => !point.date || !DECIMAL_LITERAL.test(String(point.value).trim()))) {
      setError("Enter the as-of date, source and dated decimal pillars, then confirm the immutable publication."); return;
    }
    if (new Set(points.map((point) => point.date)).size !== points.length || points.some((point) => point.date < asOf)) {
      setError("Each pillar needs a unique date on or after the as-of date."); return;
    }
    const controller = new AbortController(); mutation.current = controller;
    setBusy(true); setError(null); setMessage(null);
    try {
      const row = await publishSecurityCurve({ curveId: selected, asOf, source: source.trim(), sourcedAt: sourcedAt ? utcInstantFromLocalInput(sourcedAt) : null, points: points.map((point) => ({ ...point, value: String(point.value).trim(), tenor: point.tenor?.trim() || null })) }, { signal: controller.signal });
      if (controller.signal.aborted) return;
      if (row.curveId !== selected) throw new Error("Published response names a different curve. Inspect retained history.");
      setVersions((rows) => [...rows.filter((candidate) => candidate.curveVersionId !== row.curveVersionId), row]);
      setLeft(row.curveVersionId); setConfirmed(false); setMessage(`Version ${row.version} retained. Exact version ID: ${row.curveVersionId}`);
    } catch (failure) { if (!controller.signal.aborted) showFailure(failure); }
    finally { if (!controller.signal.aborted) setBusy(false); }
  }
  const chosen = definitions.find((row) => row.curveId === selected);
  const leftVersion = versions.find((row) => row.curveVersionId === left);
  const rightVersion = versions.find((row) => row.curveVersionId === right);
  const dates = [...new Set([...(leftVersion?.points ?? []), ...(rightVersion?.points ?? [])].map((point) => point.date))].sort();
  const comparedPillars: ComparedPillar[] = dates.map((date) => {
    const a = leftVersion?.points.find((point) => point.date === date);
    const b = rightVersion?.points.find((point) => point.date === date);
    return { date, inspect: a ? bondDecimalText(a.value) : "Absent", compare: b ? bondDecimalText(b.value) : "Absent", tenor: a?.tenor ?? b?.tenor ?? "—" };
  });
  return <Card className="panel-surface">
    <CardHeader><CardTitle>Curves</CardTitle><CardDescription>Define retained market curves, publish dated pillars and compare immutable history. Reading requires ViewSecurityMaster; writes require ModifySecurityMaster.</CardDescription></CardHeader>
    <CardContent className="space-y-4">
      <div className="flex flex-wrap items-end gap-3">
        <FormRow label="Stored curve" labelFor={`${id}-select`}><Select id={`${id}-select`} value={selected} disabled={busy || readDenied} onChange={(e) => { setSelected(e.target.value); setPoints([emptyPoint(), emptyPoint()]); setSource(""); setAsOf(""); setSourcedAt(""); }}>
          <option value="">Select a curve</option>{definitions.map((row) => <option key={row.curveId} value={row.curveId}>{row.curveKey} · {row.kind} · {row.currency}</option>)}
        </Select></FormRow>
        <Button type="button" variant="outline" disabled={busy} onClick={() => { setWriteDenied(false); setReload((value) => value + 1); }}>Refresh curves</Button>
      </div>
      {loading ? <p role="status">Loading retained curves…</p> : null}
      {!loading && !error && definitions.length === 0 ? <p>No curve definitions are retained. Create a definition to publish market inputs.</p> : null}
      {error ? <StatusBanner tone="danger" title="Curves unavailable" detail={error} role="alert" /> : null}
      {message ? <p role="status" className="break-all text-sm">{message}</p> : null}
      <details><summary className="cursor-pointer text-sm font-semibold">Create curve definition</summary>
        <p className="my-2 text-xs text-muted-foreground">Definitions are write-once. Choose the key, conventions and source before creating.</p>
        <form aria-label="Curve definition" onSubmit={create}><fieldset disabled={busy || readDenied || writeDenied} className="space-y-3">
          <FormGrid columns={3}>
            <FormRow label="Curve key" labelFor={`${id}-key`}><Input id={`${id}-key`} required value={definition.curveKey} onChange={(e) => setDefinition({ ...definition, curveKey: e.target.value })} /></FormRow>
            <FormRow label="Curve kind" labelFor={`${id}-kind`}><Select id={`${id}-kind`} value={definition.kind} onChange={(e) => setDefinition({ ...definition, kind: e.target.value as DefineSecurityCurveRequest["kind"] })}><option>Discount</option><option>Forward</option><option>Inflation</option></Select></FormRow>
            <FormRow label="Curve currency" labelFor={`${id}-currency`}><Input id={`${id}-currency`} required maxLength={3} value={definition.currency} onChange={(e) => setDefinition({ ...definition, currency: e.target.value })} /></FormRow>
            <FormRow label="Index name" labelFor={`${id}-index`}><Input id={`${id}-index`} disabled={definition.kind === "Discount"} required={definition.kind !== "Discount"} value={definition.indexName ?? ""} onChange={(e) => setDefinition({ ...definition, indexName: e.target.value })} /></FormRow>
            <FormRow label="Curve tenor (optional)" labelFor={`${id}-tenor`}><Input id={`${id}-tenor`} value={definition.tenor ?? ""} onChange={(e) => setDefinition({ ...definition, tenor: e.target.value })} /></FormRow>
            <FormRow label="Day count" labelFor={`${id}-daycount`}><Select id={`${id}-daycount`} value={definition.dayCount} onChange={(e) => setDefinition({ ...definition, dayCount: e.target.value })}><option>Actual365</option><option>Actual360</option><option>ActualActualIsda</option><option>ActualActualIcma</option><option>ThirtyE360</option><option>ThirtyE360Isda</option><option>Business252</option><option>Nl365</option><option>Actual36525</option><option>Thirty360</option></Select></FormRow>
            <FormRow label="Definition source" labelFor={`${id}-source`}><Input id={`${id}-source`} required value={definition.source} onChange={(e) => setDefinition({ ...definition, source: e.target.value })} /></FormRow>
            <FormRow label="Description (optional)" labelFor={`${id}-description`}><Input id={`${id}-description`} value={definition.description ?? ""} onChange={(e) => setDefinition({ ...definition, description: e.target.value })} /></FormRow>
          </FormGrid><p className="text-xs">Interpolation: Linear. Forward rates use annual fractions (0.04 = 4%); Inflation uses index levels; Discount uses factors.</p>
          <Button type="submit">Create curve definition</Button>
        </fieldset></form>
      </details>
      {chosen ? <>
        <p className="break-all text-xs">{chosen.curveKey} · {chosen.kind} · {chosen.currency} · {chosen.indexName ?? "No index"} · {chosen.dayCount} · {chosen.interpolation} · source {chosen.source} · ID {chosen.curveId}</p>
        <FormGrid columns={2}>{(["Inspect version", "Compare version"] as const).map((label, index) => <FormRow key={label} label={label} labelFor={`${id}-history-${index}`}><Select id={`${id}-history-${index}`} value={index === 0 ? left : right} onChange={(e) => index === 0 ? setLeft(e.target.value) : setRight(e.target.value)}><option value="">Select retained version</option>{versions.map((row) => <option key={row.curveVersionId} value={row.curveVersionId}>v{row.version} · as of {row.asOf} · recorded {row.recordedAt}</option>)}</Select></FormRow>)}</FormGrid>
        {!loading && versions.length === 0 ? <p>No retained versions. Publish pillars to make this curve selectable for calculations.</p> : null}
        {leftVersion || rightVersion ? <DenseDataTable columns={pillarColumns} rows={comparedPillars} getRowId={(row) => row.date}
          ariaLabel="Retained curve pillars" caption="Retained curve pillars" emptyText="No retained pillars are available for these versions." /> : null}
        {[leftVersion, rightVersion].filter((row): row is SecurityCurveVersion => row !== undefined).map((row, index) => <p key={`${row.curveVersionId}-${index}`} className="break-all text-xs">v{row.version} · {row.curveVersionId} · as of {row.asOf} · recorded {row.recordedAt} · sourced {row.sourcedAt ?? "Unspecified"} · {row.source} · publisher {row.publishedBy}<br /><span className="font-mono">SHA-256 {row.contentSha256}</span></p>)}
        <details><summary className="cursor-pointer text-sm font-semibold">Publish curve version</summary>
          <form aria-label="Curve version publication" onSubmit={publish}><fieldset className="mt-3 space-y-3" disabled={busy || readDenied || writeDenied}>
            <p className="text-xs">Append-only publication. {chosen.kind === "Forward" ? "Enter annual fractions (0.04 = 4%)." : chosen.kind === "Discount" ? "Enter positive discount factors no greater than 1, decreasing with maturity." : "Enter positive inflation index levels."}</p>
            <FormGrid columns={3}>
              <FormRow label="Curve as-of date" labelFor={`${id}-asof`}><Input id={`${id}-asof`} type="date" required value={asOf} onChange={(e) => editPoints(() => setAsOf(e.target.value))} /></FormRow>
              <FormRow label="Publication source" labelFor={`${id}-publication-source`}><Input id={`${id}-publication-source`} required value={source} onChange={(e) => editPoints(() => setSource(e.target.value))} /></FormRow>
              <FormRow label="Sourced at (UTC, optional)" labelFor={`${id}-sourced`}><Input id={`${id}-sourced`} type="datetime-local" value={sourcedAt} onChange={(e) => editPoints(() => setSourcedAt(e.target.value))} /></FormRow>
            </FormGrid>
            {points.map((point, index) => <div key={index} className="flex flex-wrap items-end gap-2"><FormGrid columns={3}>
              <FormRow label={`Pillar date ${index + 1}`} labelFor={`${id}-date-${index}`}><Input id={`${id}-date-${index}`} type="date" required value={point.date} onChange={(e) => editPoints(() => setPoints(points.map((row, i) => i === index ? { ...row, date: e.target.value } : row)))} /></FormRow>
              <FormRow label={`Pillar value ${index + 1}`} labelFor={`${id}-value-${index}`}><Input id={`${id}-value-${index}`} inputMode="decimal" required value={point.value} onChange={(e) => editPoints(() => setPoints(points.map((row, i) => i === index ? { ...row, value: e.target.value } : row)))} /></FormRow>
              <FormRow label={`Pillar tenor ${index + 1}`} labelFor={`${id}-tenor-${index}`}><Input id={`${id}-tenor-${index}`} value={point.tenor ?? ""} onChange={(e) => editPoints(() => setPoints(points.map((row, i) => i === index ? { ...row, tenor: e.target.value } : row)))} /></FormRow>
            </FormGrid><Button type="button" variant="outline" disabled={points.length <= 1} onClick={() => editPoints(() => setPoints(points.filter((_, i) => i !== index)))}>Remove pillar {index + 1}</Button></div>)}
            <Button type="button" variant="outline" onClick={() => editPoints(() => setPoints([...points, emptyPoint()]))}>Add pillar</Button>
            <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)} />I reviewed these pillars and understand this publishes an immutable version.</label>
            <Button type="submit" disabled={!confirmed}>Publish curve version</Button>
          </fieldset></form>
        </details>
      </> : null}
    </CardContent>
  </Card>;
}
