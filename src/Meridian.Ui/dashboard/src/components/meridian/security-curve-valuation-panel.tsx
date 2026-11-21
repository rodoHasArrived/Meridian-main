import { type FormEvent, useEffect, useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { ExactCurveInputs } from "@/components/meridian/exact-curve-inputs";
import { apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import { describeApiError } from "@/lib/api-errors";
import { bondDecimalText, utcInstantFromLocalInput } from "@/lib/bond-decimal-format";
import type { ExactCurvePin } from "@/types/security-curves.types";

interface CurveValuationRequest { securityId: string; asOf: string; knownAt?: string; model: "DiscountedCashFlow"; curves: ExactCurvePin[] }
interface CurveValuationResult {
  securityId: string; modelKey: string; modelVersion: string; currency: string; asOf: string; knowledgeAsOf: string;
  presentValue: string | number | null; price: string | number | null; priceUnit: string; observability: string;
  inputs: Array<{ kind: string; reference: string; id?: string | null; version?: number | null; contentSha256?: string | null; asOf?: string | null; recordedAt?: string | null }>;
  blockers: Array<{ code: string; reason: string }>; diagnostics: string[]; isBlocked: boolean;
}
export function calculateSecurityCurveValuation(request: CurveValuationRequest, options: ApiRequestOptions = {}): Promise<CurveValuationResult> {
  const path = UI_API_ROUTES.SecurityMasterValuation.replace("{securityId:guid}", encodeURIComponent(request.securityId));
  return apiPostJson(path, request, { ...options, allowDevelopmentFallback: false });
}

/** DCF consumes retained curve versions; yield analytics intentionally has no curve selection. */
export function SecurityCurveValuationPanel({ securityId, calculate = calculateSecurityCurveValuation }: { securityId: string; calculate?: typeof calculateSecurityCurveValuation }) {
  const id = useId();
  const [asOf, setAsOf] = useState(() => new Date().toISOString().slice(0, 10));
  const [knownAt, setKnownAt] = useState("");
  const [pins, setPins] = useState<ExactCurvePin[]>([]);
  const [result, setResult] = useState<CurveValuationResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const request = useRef<AbortController | null>(null);
  useEffect(() => { setResult(null); setError(null); setPending(false); setPins([]); return () => request.current?.abort(); }, [securityId]);
  function edit(change: () => void) { request.current?.abort(); setPending(false); setResult(null); setError(null); change(); }
  async function submit(event: FormEvent) {
    event.preventDefault(); request.current?.abort(); setResult(null); setError(null);
    if (!asOf || pins.length === 0 || pins.some((pin) => !pin.curveId || !pin.curveVersionId || !(pin.role.trim() === "Discount" || /^Forward:.+/.test(pin.role.trim()))) || pins.filter((pin) => pin.role.trim() === "Discount").length !== 1 || new Set(pins.map((pin) => pin.role.trim().toLowerCase())).size !== pins.length) {
      setError("Select one exact Discount curve version. Optional forward pins use Forward:index, for example Forward:SOFR. Every role must be unique and complete."); return;
    }
    const controller = new AbortController(); request.current = controller; setPending(true);
    try {
      const value = await calculate({ securityId, asOf: `${asOf}T00:00:00Z`, ...(knownAt ? { knownAt: utcInstantFromLocalInput(knownAt) } : {}), model: "DiscountedCashFlow", curves: pins.map((pin) => ({ ...pin, role: pin.role.trim() })) }, { signal: controller.signal });
      if (controller.signal.aborted) return;
      if (value.securityId.toLowerCase() !== securityId.toLowerCase()) throw new Error("The valuation returned a different security. Refresh the retained record.");
      setResult(value);
    } catch (failure) { if (!controller.signal.aborted) setError(describeApiError(failure, "Curve valuation unavailable. This host needs the retained curve valuation service.").summary); }
    finally { if (!controller.signal.aborted) setPending(false); }
  }
  return <Card className="panel-surface"><CardHeader><CardTitle>Curve valuation</CardTitle><CardDescription>Discount projected contractual cash flows using exact retained market inputs. ViewSecurityMaster permission is required. This calculated value grants no posting authority.</CardDescription></CardHeader>
    <CardContent className="space-y-4"><form aria-label="Curve valuation inputs" onSubmit={submit}><fieldset disabled={pending} className="space-y-3"><FormGrid columns={2}>
      <FormRow label="Curve valuation date (UTC)" labelFor={`${id}-asof`}><Input id={`${id}-asof`} type="date" required value={asOf} onChange={(e) => edit(() => setAsOf(e.target.value))} /></FormRow>
      <FormRow label="Valuation recorded cutoff (UTC, optional)" labelFor={`${id}-known`}><Input id={`${id}-known`} type="datetime-local" value={knownAt} onChange={(e) => edit(() => setKnownAt(e.target.value))} /></FormRow>
    </FormGrid><ExactCurveInputs required pins={pins} onChange={(value) => edit(() => setPins(value))} disabled={pending} helpText="Select one Discount role and optional Forward:index roles (for example Forward:SOFR). All pins name immutable retained versions. The server checks curve kind, currency, cutoffs and support." />
    <Button type="submit" disabled={pending}>{pending ? "Valuing…" : "Calculate curve valuation"}</Button></fieldset></form>
    {error ? <StatusBanner tone="danger" role="alert" title="Curve valuation unavailable" detail={error} /> : null}
    {pending ? <p role="status">Calculating from retained curve versions…</p> : null}
    {result ? <><p role="status">{result.isBlocked ? "Curve valuation blocked." : "Curve valuation ready."}</p>
      <p className="text-xs">Model {result.modelKey} {result.modelVersion} · economic cutoff {result.asOf} · recorded cutoff {result.knowledgeAsOf} · {result.observability}</p>
      {result.isBlocked ? <StatusBanner tone="warning" title="Valuation blocked" detail={<ul>{result.blockers.map((blocker) => <li key={blocker.code}>{blocker.code}: {blocker.reason}</li>)}</ul>} /> : <dl className="grid grid-cols-2 gap-3"><div><dt>Present value ({result.currency})</dt><dd className="font-mono">{bondDecimalText(result.presentValue)}</dd></div><div><dt>Price ({result.priceUnit})</dt><dd className="font-mono">{bondDecimalText(result.price)}</dd></div></dl>}
      {result.diagnostics.map((diagnostic, index) => <p key={index} className="text-xs">{diagnostic}</p>)}
      <details><summary className="cursor-pointer font-semibold text-sm">Exact valuation input provenance</summary><ul className="mt-2 space-y-2 text-xs">{result.inputs.map((input, index) => <li key={index} className="break-all">{input.kind} · {input.reference} · {input.id ?? "No retained ID"} · version {input.version ?? "Unspecified"}{input.contentSha256 ? <span className="block font-mono">SHA-256 {input.contentSha256}</span> : null}{input.asOf ? <span className="block">As of {input.asOf}</span> : null}{input.recordedAt ? <span className="block">Recorded {input.recordedAt}</span> : null}</li>)}</ul></details>
    </> : null}
    </CardContent></Card>;
}
