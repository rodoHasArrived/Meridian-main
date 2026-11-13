import { useEffect, useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { FormGrid, FormRow } from "@/components/ui/form";
import { StatusBanner } from "@/components/ui/status-banner";
import { screenBonds } from "@/lib/api/bond-workflow.api";
import { describeApiError } from "@/lib/api-errors";
import type { BondScreeningPage, BondScreeningQuery } from "@/types/bond-workflow.types";

export function BondScreeningPanel({ asOf, knownAtUtc, currentSecurityId, onCandidate, screen = screenBonds }: {
  asOf: string; knownAtUtc?: string; currentSecurityId: string; onCandidate: (securityId: string) => void; screen?: typeof screenBonds;
}) {
  const id = useId(); const [issuer, setIssuer] = useState(""); const [currency, setCurrency] = useState(""); const [coupon, setCoupon] = useState("");
  const [from, setFrom] = useState(asOf); const [through, setThrough] = useState(() => { const date = new Date(`${asOf}T00:00:00Z`); if (!Number.isFinite(date.getTime())) return ""; date.setUTCFullYear(date.getUTCFullYear() + 30); return date.toISOString().slice(0, 10); });
  const [callProtection, setCallProtection] = useState(""); const [agency, setAgency] = useState(""); const [ratings, setRatings] = useState("");
  const [result, setResult] = useState<BondScreeningPage | null>(null); const [query, setQuery] = useState<BondScreeningQuery | null>(null);
  const [pending, setPending] = useState(false); const [error, setError] = useState<string | null>(null);
  const active = useRef<AbortController | null>(null); useEffect(() => () => active.current?.abort(), []);
  function edit(change: () => void) { active.current?.abort(); setPending(false); setResult(null); setQuery(null); setError(null); change(); }
  async function run(next: boolean) {
    active.current?.abort(); const controller = new AbortController(); active.current = controller; setError(null);
    if (!from || !through || from > through || (ratings.trim() && !agency.trim())) { setError("Enter ordered maturity dates and select an agency before filtering exact rating labels."); return; }
    const request: BondScreeningQuery = next && query && result?.nextCursor ? { ...query, cursor: result.nextCursor } : {
      asOf, knownAtUtc: knownAtUtc ?? new Date().toISOString(), maturityFrom: from, maturityTo: through,
      issuer: issuer.trim() || null, currency: currency.trim() || null, couponKind: coupon || null,
      callProtectedThrough: callProtection || null, ratingAgency: agency.trim() || null,
      acceptedRatings: ratings.split(",").map((rating) => rating.trim()).filter(Boolean), pageSize: 25
    };
    if (!next) setResult(null); setPending(true);
    try {
      const page = await screen(request, { signal: controller.signal, allowDevelopmentFallback: false });
      if (controller.signal.aborted) return;
      setQuery(request); setResult(next && result ? { ...page, rows: [...result.rows, ...page.rows], gaps: [...result.gaps, ...page.gaps] } : page);
    } catch (failure) { if (!controller.signal.aborted) setError(describeApiError(failure, "Bond screening unavailable.").summary); }
    finally { if (!controller.signal.aborted) setPending(false); }
  }
  return <details className="rounded border border-border p-3"><summary className="cursor-pointer text-sm font-medium">Find and screen comparison bonds</summary><form aria-label="Bond screening filters" onSubmit={(event) => { event.preventDefault(); void run(false); }} className="mt-3 space-y-3">
    <p className="text-xs text-muted-foreground">Server-side combined filters use bounded reference pages. Ratings retain their selected agency; missing terms or competing sources stay visible. Economic date {asOf} uses the calculation date above.</p>
    <FormGrid columns={3}>
      <FormRow label="Screen issuer name" labelFor={`${id}-issuer`}><Input id={`${id}-issuer`} value={issuer} onChange={(e) => edit(() => setIssuer(e.target.value))} /></FormRow>
      <FormRow label="Screen currency" labelFor={`${id}-currency`}><Input id={`${id}-currency`} value={currency} maxLength={3} onChange={(e) => edit(() => setCurrency(e.target.value))} /></FormRow>
      <FormRow label="Coupon structure" labelFor={`${id}-coupon`}><Select id={`${id}-coupon`} value={coupon} onChange={(e) => edit(() => setCoupon(e.target.value))}><option value="">Any coupon</option><option value="Fixed">Fixed</option><option value="ZeroCoupon">Zero coupon</option><option value="Floating">Floating / overnight</option><option value="InflationLinked">Inflation linked</option></Select></FormRow>
      <FormRow label="Maturity from" labelFor={`${id}-from`}><Input id={`${id}-from`} type="date" value={from} onChange={(e) => edit(() => setFrom(e.target.value))} /></FormRow>
      <FormRow label="Maturity through" labelFor={`${id}-through`}><Input id={`${id}-through`} type="date" value={through} onChange={(e) => edit(() => setThrough(e.target.value))} /></FormRow>
      <FormRow label="Call-protected through (optional)" labelFor={`${id}-call`}><Input id={`${id}-call`} type="date" value={callProtection} onChange={(e) => edit(() => setCallProtection(e.target.value))} /></FormRow>
      <FormRow label="Rating agency (source label)" labelFor={`${id}-agency`}><Input id={`${id}-agency`} value={agency} onChange={(e) => edit(() => setAgency(e.target.value))} /></FormRow>
      <FormRow label="Accepted rating labels (comma separated)" labelFor={`${id}-ratings`}><Input id={`${id}-ratings`} value={ratings} onChange={(e) => edit(() => setRatings(e.target.value))} /></FormRow>
    </FormGrid><Button type="submit" variant="outline" disabled={pending}>{pending ? "Screening retained bonds…" : "Screen comparison bonds"}</Button>
    {error ? <StatusBanner role="alert" tone="danger" title="Bond screening unavailable" detail={error} /> : null}
    {result ? <section aria-label="Bond screening results" className="space-y-3">
      {result.limitations.length ? <StatusBanner tone="warning" title="Screening evidence scope" detail={<ul>{result.limitations.map((limitation, index) => <li key={index}>{limitation}</li>)}</ul>} /> : null}
      <div className="overflow-x-auto"><table className="w-full text-left text-xs"><caption className="sr-only">Server-screened comparison candidates</caption><thead><tr>{["Security / issuer", "Currency", "Maturity", "Coupon", "Agency ratings", "Evidence / comparison"].map((label) => <th key={label} className="p-2">{label}</th>)}</tr></thead><tbody>{result.rows.map((row, index) => <tr key={`${row.reference.securityId}-${index}`} className="border-t border-border"><th className="max-w-64 break-all p-2 font-normal">{row.reference.issuerName ?? "Issuer unavailable"}<span className="block font-mono">{row.reference.securityId}</span></th><td className="p-2">{row.reference.currency}</td><td className="p-2">{row.reference.lifecycle?.maturityDate ?? "Unavailable"}</td><td className="p-2">{row.reference.couponStructure?.kind ?? row.reference.accrualConvention?.couponKind ?? "Unavailable"}</td><td className="p-2">{row.ratings.length ? row.ratings.map((rating) => <p key={rating.observationId}>{rating.input.ratingAgency} · {rating.input.rating} · {rating.input.sourceName}</p>) : "No retained rating"}</td><td className="p-2">{row.gaps.map((gap, gapIndex) => <p key={gapIndex}>{gap}</p>)}<Button type="button" size="sm" variant="outline" disabled={row.reference.securityId === currentSecurityId} onClick={() => onCandidate(row.reference.securityId)}>{row.reference.securityId === currentSecurityId ? "Current bond" : "Add comparison candidate"}</Button></td></tr>)}</tbody></table>{!result.rows.length ? <p>No matching bonds in this bounded result.</p> : null}</div>
      {result.gaps.length ? <details><summary className="cursor-pointer text-xs">Source coverage gaps ({result.gaps.length})</summary><ul className="space-y-1 pt-2 text-xs">{result.gaps.map((gap, index) => <li className="break-all" key={index}>{gap.securityId}: {gap.reason}</li>)}</ul></details> : null}
      {result.nextCursor ? <Button type="button" variant="outline" disabled={pending} onClick={() => void run(true)}>Load next screened page</Button> : <p className="text-xs text-muted-foreground">End of the returned screening population.</p>}
    </section> : null}
  </form></details>;
}
