import { useEffect, useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { describeApiError } from "@/lib/api-errors";
import { bondDecimalText, DECIMAL_LITERAL } from "@/lib/bond-decimal-format";
import {
  getRateObservations, getRateCurveDefinitions, ingestRates,
  type RateConstructionMethod, type RateCurveDefinition, type RateCurveVersion, type SecurityRateObservation
} from "@/lib/api/security-rates.api";

export interface SecurityRatesIngestionPanelProps {
  securityId: string;
  loadObservations?: typeof getRateObservations;
  loadCurves?: typeof getRateCurveDefinitions;
  publish?: typeof ingestRates;
}

/** Rates are reviewed separately from bond price quotes; publication creates a new immutable curve version. */
export function SecurityRatesIngestionPanel({ securityId, loadObservations = getRateObservations, loadCurves = getRateCurveDefinitions, publish = ingestRates }: SecurityRatesIngestionPanelProps) {
  const id = useId();
  const today = new Date().toISOString().slice(0, 10);
  const [series, setSeries] = useState("SOFR");
  const [from, setFrom] = useState(today);
  const [to, setTo] = useState(today);
  const [observations, setObservations] = useState<SecurityRateObservation[]>([]);
  const [curves, setCurves] = useState<RateCurveDefinition[]>([]);
  const [selected, setSelected] = useState("");
  const [curveId, setCurveId] = useState("");
  const [pointDate, setPointDate] = useState(today);
  const [method, setMethod] = useState<RateConstructionMethod>("ObservedFixingFlatForecast");
  const [description, setDescription] = useState("");
  const [factor, setFactor] = useState("");
  const [reviewed, setReviewed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [published, setPublished] = useState<RateCurveVersion | null>(null);
  const request = useRef<AbortController | null>(null);
  const observation = selected === "" ? undefined : observations[Number(selected)];
  const curve = curves.find(candidate => candidate.curveId === curveId);
  const compatible = method === "ReviewedDiscountFactors"
    ? curve?.kind === "Discount" && DECIMAL_LITERAL.test(factor)
    : curve?.kind === "Forward" && observation?.kind === "ObservedFixing";

  useEffect(() => {
    request.current?.abort();
    setObservations([]); setSelected(""); setPublished(null); setReviewed(false); setError(null); setBusy(false);
    return () => request.current?.abort();
  }, [securityId]);

  function edit(change: () => void) {
    request.current?.abort(); setBusy(false); setPublished(null); setReviewed(false); setError(null); change();
  }

  async function load() {
    request.current?.abort();
    const current = new AbortController(); request.current = current;
    setBusy(true); setError(null); setPublished(null); setSelected(""); setReviewed(false);
    try {
      const [rows, definitions] = await Promise.all([
        loadObservations(series, from, to, { signal: current.signal }), loadCurves({ signal: current.signal })
      ]);
      if (current.signal.aborted) return;
      setObservations(rows); setCurves(definitions); setCurveId("");
    } catch (failure) {
      if (!current.signal.aborted) setError(describeApiError(failure, "Rates could not be loaded. Review the provider connection.").summary);
    } finally { if (!current.signal.aborted) setBusy(false); }
  }

  async function publishVersion() {
    if (!observation || !reviewed || !compatible) return;
    const current = new AbortController(); request.current = current;
    setBusy(true); setError(null);
    try {
      const version = await publish({ curveId, asOf: to, constructionMethod: method, methodDescription: description,
        points: [{ pointDate, observation, ...(method === "ReviewedDiscountFactors" ? { reviewedDiscountFactor: factor } : {}) }]
      }, { signal: current.signal });
      if (!current.signal.aborted) { setPublished(version); setReviewed(false); }
    } catch (failure) {
      if (!current.signal.aborted) setError(describeApiError(failure, "The curve was not published. Review the conventions and permissions.").summary);
    } finally { if (!current.signal.aborted) setBusy(false); }
  }

  return <Card id="bond-rates-fixings" tabIndex={-1} aria-label="Rates, fixings and curve ingestion">
    <CardHeader><CardTitle>Rates, fixings and curves</CardTitle><CardDescription>
      Keep observed fixings separate from forecasts and assumptions. Review a construction before publishing to the immutable curve store.
    </CardDescription></CardHeader>
    <CardContent className="space-y-4">
      <FormGrid columns={3}>
        <FormRow label="FRED series" labelFor={`${id}-series`}><Input id={`${id}-series`} value={series} onChange={event => edit(() => { setSeries(event.target.value); setObservations([]); setSelected(""); })} /></FormRow>
        <FormRow label="Observation start" labelFor={`${id}-from`}><Input type="date" id={`${id}-from`} value={from} onChange={event => edit(() => { setFrom(event.target.value); setObservations([]); setSelected(""); })} /></FormRow>
        <FormRow label="Observation end / curve as-of" labelFor={`${id}-to`}><Input type="date" id={`${id}-to`} value={to} onChange={event => edit(() => { setTo(event.target.value); setObservations([]); setSelected(""); })} /></FormRow>
      </FormGrid>
      <Button type="button" variant="secondary" disabled={busy || !series.trim() || !from || !to || from > to} onClick={() => void load()}>Load typed observations</Button>
      {error ? <StatusBanner tone="danger" title="Rates need attention" detail={error} /> : null}
      <FormRow label="Select fixing or rate observation" labelFor={`${id}-observation`}>
        <Select id={`${id}-observation`} value={selected} onChange={event => edit(() => setSelected(event.target.value))}>
          <option value="">{observations.length ? "Select an observation" : "Load observations from a configured connection"}</option>
          {observations.map((row, index) => <option key={`${row.observationDate}-${row.vintageDate}-${index}`} value={index}>{row.seriesId} · {row.observationDate} · {row.originalValue} {row.sourceUnits} · {row.kind}</option>)}
        </Select>
      </FormRow>
      {selected !== "" && observation ? <>
        <dl className="grid gap-2 text-xs md:grid-cols-2" aria-label="Selected rate source evidence">
          <div><dt className="text-muted-foreground">Source / role / currency</dt><dd>{observation.provider} / {observation.kind} / {observation.currency ?? "Unspecified"}</dd></div>
          <div><dt className="text-muted-foreground">Original value / units</dt><dd>{observation.originalValue} / {observation.sourceUnits} ({observation.unit})</dd></div>
          <div><dt className="text-muted-foreground">Observed / published</dt><dd>{observation.observationDate} / {observation.publishedAt ?? "Publication time unavailable"}</dd></div>
          <div><dt className="text-muted-foreground">Vintage / retrieved</dt><dd>{observation.vintageDate ?? "Vintage unavailable"} – {observation.vintageEndDate ?? "unknown"} / {observation.retrievedAt}</dd></div>
          <div><dt className="text-muted-foreground">Transformation</dt><dd>{observation.transformation}</dd></div>
        </dl>
        {observation.kind === "TreasuryConstantMaturityYield" ? <StatusBanner tone="warning" title="Treasury par yield needs construction" detail="A constant-maturity yield is not a zero rate or discount factor. Supply factors from a reviewed construction, such as a par-curve bootstrap, and describe its conventions." /> : null}
        <FormGrid columns={2}>
          <FormRow label="Existing curve" labelFor={`${id}-curve`}><Select id={`${id}-curve`} value={curveId} onChange={event => edit(() => setCurveId(event.target.value))}><option value="">Select a defined curve</option>{curves.map(row => <option key={row.curveId} value={row.curveId}>{row.curveKey} · {row.kind} · {row.currency} · {row.indexName ?? "discount"}</option>)}</Select></FormRow>
          <FormRow label="Construction method" labelFor={`${id}-method`}><Select id={`${id}-method`} value={method} onChange={event => edit(() => setMethod(event.target.value as RateConstructionMethod))}><option value="ObservedFixingFlatForecast">Assumption: hold observed fixing flat</option><option value="ReviewedDiscountFactors">Externally constructed, reviewed discount factors</option></Select></FormRow>
          <FormRow label="Pillar date" labelFor={`${id}-pillar`}><Input type="date" id={`${id}-pillar`} value={pointDate} onChange={event => edit(() => setPointDate(event.target.value))} /></FormRow>
          {method === "ReviewedDiscountFactors" ? <FormRow label="Reviewed discount factor" labelFor={`${id}-factor`}><Input id={`${id}-factor`} inputMode="decimal" value={factor} onChange={event => edit(() => setFactor(event.target.value))} /></FormRow> : null}
          <FormRow label="Construction conventions and assumptions" labelFor={`${id}-description`} span={2}><Input id={`${id}-description`} value={description} onChange={event => edit(() => setDescription(event.target.value))} /></FormRow>
        </FormGrid>
        {method === "ObservedFixingFlatForecast" ? <p className="text-xs text-muted-foreground">Output role: forecast assumption. The retained input remains an observed fixing. A historical fixing does not establish a future reset rate.</p> : null}
        <p className="text-xs text-muted-foreground">This publishes a complete one-pillar version. Earlier versions remain retained; consumers selecting the latest curve may use this new version. The server verifies the observation against the provider again before publication.</p>
        <label className="flex items-start gap-2 text-xs"><input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} />I reviewed the source, vintage, curve, units and construction assumptions.</label>
        <Button type="button" disabled={busy || !reviewed || !compatible || !description.trim() || !pointDate || pointDate < to} onClick={() => void publishVersion()}>Publish reviewed curve version</Button>
      </> : null}
      {published ? <StatusBanner tone="success" title={`Retained ${published.curveKey} version ${published.version}`} detail={<span>Curve version {published.curveVersionId}; hash {published.contentSha256}. Normalized pillar {published.points.map(point => `${point.date}: ${bondDecimalText(point.value)}`).join(", ")}. Source and construction evidence are retained with the pillar.</span>} /> : null}
    </CardContent>
  </Card>;
}
