import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { BondMarketInputsPanel } from "@/components/meridian/bond-market-inputs-panel";
import { BondSavedComparison } from "@/components/meridian/bond-saved-comparison";
import { BondScreeningPanel } from "@/components/meridian/bond-screening-panel";
import { calculateBondWorkflow } from "@/lib/api/bond-workflow.api";
import { describeApiError } from "@/lib/api-errors";
import { bondDecimalText, DECIMAL_LITERAL, utcInstantFromLocalInput } from "@/lib/bond-decimal-format";
import type { BondWorkflowRequest, BondWorkflowResult } from "@/types/bond-workflow.types";

const today = () => new Date().toISOString().slice(0, 10);
type ComparisonRow = { securityId: string; result?: BondWorkflowResult; error?: string };

export function BondWorkflowPanel({ securityId, calculate = calculateBondWorkflow }: {
  securityId: string; calculate?: typeof calculateBondWorkflow;
}) {
  return <WorkflowInputs key={securityId} securityId={securityId} calculate={calculate} />;
}

function WorkflowInputs({ securityId, calculate }: { securityId: string; calculate: typeof calculateBondWorkflow }) {
  const id = useId();
  const [date, setDate] = useState(today);
  const [settlement, setSettlement] = useState(today);
  const [quoteDate, setQuoteDate] = useState(today);
  const [face, setFace] = useState("10000");
  const [price, setPrice] = useState("");
  const [fees, setFees] = useState("0");
  const [rateShift, setRateShift] = useState("0");
  const [spreadShift, setSpreadShift] = useState("0");
  const [priceBasis, setPriceBasis] = useState<BondWorkflowRequest["priceBasis"]>("NominalClean");
  const [comparisons, setComparisons] = useState("");
  const [comparisonOpen, setComparisonOpen] = useState(false);
  const [comparisonFocusVersion, setComparisonFocusVersion] = useState(0);
  const comparisonInput = useRef<HTMLTextAreaElement | null>(null);
  const [knownAt, setKnownAt] = useState("");
  const [quoteObservationId, setQuoteObservationId] = useState<string | null>(null);
  const [selectionReceiptId, setSelectionReceiptId] = useState("");
  const [pricingAccountId, setPricingAccountId] = useState("");
  const [pricingBookId, setPricingBookId] = useState("");
  const [confirmationCash, setConfirmationCash] = useState("");
  const [confirmationEvidence, setConfirmationEvidence] = useState("");
  const [submittedRequests, setSubmittedRequests] = useState<BondWorkflowRequest[]>([]);
  const [result, setResult] = useState<BondWorkflowResult | null>(null);
  const [rows, setRows] = useState<ComparisonRow[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const active = useRef<AbortController | null>(null);
  useEffect(() => () => active.current?.abort(), []);
  useEffect(() => { if (comparisonFocusVersion > 0) comparisonInput.current?.focus(); }, [comparisonFocusVersion]);

  function edit(change: () => void) {
    active.current?.abort(); setPending(false); setResult(null); setRows([]); setSubmittedRequests([]); setError(null); change();
  }
  async function submit(event: FormEvent) {
    event.preventDefault(); active.current?.abort();
    const controller = new AbortController(); active.current = controller;
    setError(null); setResult(null); setRows([]);
    if (![face, fees, rateShift, spreadShift].every((value) => DECIMAL_LITERAL.test(value.trim()))
      || !date || !settlement || !quoteDate || Number(face) <= 0 || Number(fees) < 0
      || (price.trim() !== "" && (!DECIMAL_LITERAL.test(price.trim()) || Number(price) < 0))) {
      setError("Enter valid dates, positive original face, and decimal price, fees and shifts."); return;
    }
    if (confirmationCash.trim() && (!DECIMAL_LITERAL.test(confirmationCash.trim()) || !confirmationEvidence.trim())) {
      setError("A confirmation cash amount requires a decimal amount and its retained evidence reference."); return;
    }
    if (Boolean(pricingAccountId.trim()) !== Boolean(pricingBookId.trim())) {
      setError("Account-specific pricing requires both its account ID and authorized ledger book ID."); return;
    }
    const peers = comparisons.split(/\r?\n/).filter((line) => line.trim()).map((line) => line.split(",").map((part) => part.trim()));
    if (peers.length > 9 || peers.some((parts) => parts.length !== 2 || !parts[0] || !DECIMAL_LITERAL.test(parts[1]) || Number(parts[1]) < 0)) {
      setError("Enter up to nine comparison rows as security ID, clean price. Each row needs its own price."); return;
    }
    const request: BondWorkflowRequest = {
      securityId, context: { asOf: `${date}T00:00:00Z`, settlementDate: settlement, ...(knownAt ? { knownAt: utcInstantFromLocalInput(knownAt) } : {}) },
      originalFace: face.trim(), cleanPricePer100: price.trim() || null, quoteSettlementDate: quoteDate,
      fees: fees.trim(), priceBasis, rateShiftBps: rateShift.trim(), spreadShiftBps: spreadShift.trim(),
      quoteObservationId, priceSelectionReceiptId: selectionReceiptId.trim() || null, accountId: pricingAccountId.trim() || null,
      observedDirtyCash: confirmationCash.trim() || null, confirmationEvidenceReference: confirmationEvidence.trim() || null
    };
    setPending(true);
    const candidates = [{ securityId, price: request.cleanPricePer100 }, ...peers.map(([peer, peerPrice]) => ({ securityId: peer, price: peerPrice }))];
    const responses = await Promise.all(candidates.map(async (candidate): Promise<ComparisonRow> => {
      try {
        const calculated = await calculate({ ...request, securityId: candidate.securityId, cleanPricePer100: candidate.price,
          ...(candidate.securityId !== securityId ? { quoteObservationId: null, priceSelectionReceiptId: null, observedDirtyCash: null, confirmationEvidenceReference: null } : {}) },
          { signal: controller.signal, allowDevelopmentFallback: false, ledgerBookId: pricingBookId.trim() || undefined });
        if (calculated.securityId.toLowerCase() !== candidate.securityId.toLowerCase()) throw new Error("Response security does not match the requested bond.");
        if (calculated.context.settlementDate !== settlement || Date.parse(calculated.context.asOf) !== Date.parse(request.context.asOf))
          throw new Error("Response dates do not match the reviewed calculation.");
        return { securityId: candidate.securityId, result: calculated };
      } catch (failure) {
        return { securityId: candidate.securityId, error: describeApiError(failure, "Bond calculation unavailable.").summary };
      }
    }));
    if (controller.signal.aborted) return;
    setSubmittedRequests(candidates.map((candidate, index) => responses[index].result?.request ?? ({ ...request, securityId: candidate.securityId, cleanPricePer100: candidate.price,
      ...(candidate.securityId !== securityId ? { quoteObservationId: null, priceSelectionReceiptId: null, observedDirtyCash: null, confirmationEvidenceReference: null } : {}) })));
    setRows(responses); setResult(responses[0].result ?? null); setError(responses[0].error ?? null); setPending(false);
  }

  return <Card className="panel-surface">
    <CardHeader><div className="flex flex-wrap items-center justify-between gap-2"><CardTitle>Bond purchase and comparison</CardTitle><Badge variant="outline">All-model analysis</Badge></div>
      <CardDescription>Original face, signed cash flows and retained model inputs. Manual prices are operator assumptions. Forecast yields and modeled settlement adjustments remain distinct from provider quotes.</CardDescription></CardHeader>
    <CardContent className="space-y-4">
      <form onSubmit={submit} aria-label="Bond workflow inputs" className="space-y-3">
        <FormGrid columns={3}>
          <FormRow label="Economic date (UTC)" labelFor={`${id}-date`}><Input id={`${id}-date`} type="date" value={date} onChange={(e) => edit(() => setDate(e.target.value))} required /></FormRow>
          <FormRow label="Comparison settlement" labelFor={`${id}-settle`}><Input id={`${id}-settle`} type="date" value={settlement} onChange={(e) => edit(() => setSettlement(e.target.value))} required /></FormRow>
          <FormRow label="Original quote settlement" labelFor={`${id}-quote`}><Input id={`${id}-quote`} type="date" value={quoteDate} onChange={(e) => edit(() => setQuoteDate(e.target.value))} required /></FormRow>
          <FormRow label="Original face amount" labelFor={`${id}-face`}><Input id={`${id}-face`} inputMode="decimal" value={face} onChange={(e) => edit(() => setFace(e.target.value))} /></FormRow>
          <FormRow label="Clean quote / 100 current face" labelFor={`${id}-price`} hint="Optional for curve valuation. Manual editing clears a retained quote selection."><Input id={`${id}-price`} inputMode="decimal" value={price} onChange={(e) => edit(() => { setPrice(e.target.value); setQuoteObservationId(null); setSelectionReceiptId(""); })} /></FormRow>
          <FormRow label="Quote basis" labelFor={`${id}-basis`}><Select id={`${id}-basis`} value={priceBasis} onChange={(e) => edit(() => setPriceBasis(e.target.value as BondWorkflowRequest["priceBasis"]))}><option value="NominalClean">Nominal clean</option><option value="RealClean">Real clean · indexed principal</option></Select></FormRow>
          <FormRow label="Fees (transaction currency)" labelFor={`${id}-fees`}><Input id={`${id}-fees`} inputMode="decimal" value={fees} onChange={(e) => edit(() => setFees(e.target.value))} /></FormRow>
          <FormRow label="Parallel rate shift (bp)" labelFor={`${id}-rates`}><Input id={`${id}-rates`} inputMode="decimal" value={rateShift} onChange={(e) => edit(() => setRateShift(e.target.value))} /></FormRow>
          <FormRow label="Credit spread shift (bp)" labelFor={`${id}-spread`}><Input id={`${id}-spread`} inputMode="decimal" value={spreadShift} onChange={(e) => edit(() => setSpreadShift(e.target.value))} /></FormRow>
        </FormGrid>
        <FormRow label="Knowledge cutoff (UTC, optional)" labelFor={`${id}-known`}><Input id={`${id}-known`} type="datetime-local" value={knownAt} onChange={(e) => edit(() => setKnownAt(e.target.value))} /></FormRow>
        <FormRow label="Price selection receipt ID (optional)" labelFor={`${id}-receipt`} hint="Use an existing retained pricing decision. The server verifies its exact inputs."><Input id={`${id}-receipt`} value={selectionReceiptId} onChange={(e) => edit(() => setSelectionReceiptId(e.target.value))} /></FormRow>
        <FormRow label="Pricing account ID (optional)" labelFor={`${id}-pricing-account`} hint="Scopes connection entitlement checks and retained pricing selection."><Input id={`${id}-pricing-account`} value={pricingAccountId} onChange={(e) => edit(() => setPricingAccountId(e.target.value))} /></FormRow>
        <FormRow label="Pricing ledger book ID (required with account)" labelFor={`${id}-pricing-book`}><Input id={`${id}-pricing-book`} value={pricingBookId} onChange={(e) => edit(() => setPricingBookId(e.target.value))} /></FormRow>
        <details><summary className="cursor-pointer text-sm font-medium">Compare a broker confirmation</summary><FormGrid columns={2}><FormRow label="Observed confirmation cash" labelFor={`${id}-confirmation`}><Input id={`${id}-confirmation`} inputMode="decimal" value={confirmationCash} onChange={(e) => edit(() => setConfirmationCash(e.target.value))} /></FormRow><FormRow label="Confirmation evidence reference" labelFor={`${id}-confirmation-evidence`}><Input id={`${id}-confirmation-evidence`} value={confirmationEvidence} onChange={(e) => edit(() => setConfirmationEvidence(e.target.value))} /></FormRow></FormGrid></details>
        {quoteObservationId ? <p className="break-all text-xs text-muted-foreground">Selected retained quote {quoteObservationId}. <Button type="button" variant="outline" size="sm" onClick={() => edit(() => setQuoteObservationId(null))}>Clear retained quote</Button></p> : null}
        <details open={comparisonOpen} onToggle={(event) => setComparisonOpen(event.currentTarget.open)}><summary className="cursor-pointer text-sm font-medium">Compare other bonds at the same face and settlement</summary>
          <label className="mt-2 block text-sm" htmlFor={`${id}-compare`}>One security ID and clean price per line, separated by a comma</label>
          <textarea ref={comparisonInput} id={`${id}-compare`} className="mt-1 min-h-24 w-full rounded border border-border bg-background p-2 text-sm focus-visible:outline focus-visible:outline-2" value={comparisons} onChange={(e) => edit(() => setComparisons(e.target.value))} />
          <p className="text-xs text-muted-foreground">Up to nine alternatives. Compare monetary values within the same currency; no FX conversion is implied.</p>
        </details>
        <Button type="submit" disabled={pending}>{pending ? "Calculating bonds…" : "Calculate purchase and comparison"}</Button>
      </form>
      <BondMarketInputsPanel key={`${securityId}-${pricingAccountId}`} securityId={securityId} accountId={pricingAccountId} onQuoteSelected={(observation) => edit(() => { setQuoteObservationId(observation.observationId); setPrice(""); setSelectionReceiptId(""); if (observation.input.quote) setQuoteDate(observation.input.quote.settlementDate); })} />
      <BondScreeningPanel key={`screen-${date}-${knownAt}`} asOf={date} knownAtUtc={knownAt ? utcInstantFromLocalInput(knownAt) : undefined} currentSecurityId={securityId} onCandidate={(candidate) => edit(() => { setComparisons((current) => current.split(/\r?\n/).some((line) => line.split(",")[0].trim() === candidate) ? current : `${current}${current.trim() ? "\n" : ""}${candidate}, `); setComparisonOpen(true); setComparisonFocusVersion((version) => version + 1); })} />
      {error ? <StatusBanner tone="danger" role="alert" title="Calculation unavailable" detail={error} /> : null}
      {rows.length > 1 ? <div className="overflow-x-auto"><table className="w-full text-left text-sm">
        <caption className="sr-only">Bond comparison at common original face and settlement; amounts remain in each currency</caption>
        <thead><tr>{["Security", "Currency", "Total purchase cash", "Present value", "Projected-flow yield", "Yield to worst", "Duration (years)", "DV01 / bp", "Coverage / evidence"].map((label) => <th key={label} className="p-2">{label}</th>)}</tr></thead>
        <tbody>{rows.map((row, index) => <tr key={`${row.securityId}-${index}`} className="border-t border-border">
          <th className="max-w-64 break-all p-2 font-normal">{row.securityId}</th><td className="p-2">{row.result?.currency ?? "Unavailable"}</td>
          <td className="p-2">{bondDecimalText(row.result?.totalCash)}</td><td className="p-2">{bondDecimalText(row.result?.presentValue)}</td>
          <td className="p-2">{bondDecimalText(row.result?.yield)}</td><td className="p-2">{bondDecimalText(row.result?.yieldToWorst)}</td>
          <td className="p-2">{bondDecimalText(row.result?.duration)}</td><td className="p-2">{bondDecimalText(row.result?.dv01)}</td>
          <td className="p-2"><div className="flex flex-wrap items-center gap-2"><span>{row.error ?? (row.result?.isBlocked ? "Blocked" : row.result?.issues.length || row.result?.metrics.some((metric) => !metric.available) ? "Partial coverage" : "Calculated")}</span>
            {row.result ? <Button type="button" size="sm" variant="outline" aria-label={`Inspect calculation for ${row.securityId}`} onClick={() => { setResult(row.result!); setError(null); }}>Inspect</Button> : null}
          </div></td>
        </tr>)}</tbody></table></div> : null}
      {result ? <WorkflowResult result={result} /> : null}
      <BondSavedComparison requests={submittedRequests} />
    </CardContent>
  </Card>;
}

function WorkflowResult({ result }: { result: BondWorkflowResult }) {
  const metrics = [["Clean consideration", result.cleanConsideration], ["Purchased accrued interest", result.accruedInterest], ["Fees", result.fees], ["Total purchase cash", result.totalCash], ["Curve present value", result.presentValue], ["Current face", result.currentFace], ["Meridian projected-flow yield (annual fraction)", result.yield], ["Meridian yield to worst (annual fraction)", result.yieldToWorst], ["Provider yield (provider convention)", result.providerYield], ["Calibrated spread (annual fraction)", result.calibratedSpread], ["DV01 (signed cash / bp)", result.dv01], ["CS01 (signed cash / bp)", result.cs01], ["Duration (years)", result.duration]] as const;
  return <section aria-label="Bond workflow result" className="space-y-3">
    <div className="flex flex-wrap gap-2"><Badge variant="outline">{result.currency}</Badge><Badge variant="outline">{result.modelVersion}</Badge><Badge variant="outline">{result.isBlocked ? "Calculation blocked" : result.issues.length ? "Partial coverage" : "Calculated"}</Badge></div>
    <p className="break-all text-xs text-muted-foreground">Security {result.securityId}</p>
    <dl className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">{metrics.map(([label, value]) => <div key={label}><dt className="text-xs text-muted-foreground">{label}</dt><dd className="font-mono text-sm">{bondDecimalText(value)}</dd></div>)}</dl>
    <p className="text-xs text-muted-foreground">Amounts are absolute {result.currency} for original face {bondDecimalText(result.originalFace)}. Coupon outflows remain negative. This preview grants no trading, payment or posting authority.</p>
    <p className="text-xs text-muted-foreground">Yield basis: {result.yieldBasis === "NominalForecastCashFlowAnnualEffective" ? "Nominal forecast cash flows, annual effective. Future rates or index levels remain assumptions." : "Annual-effective cash-flow yield."}</p>
    {result.quoteSource ? <p className="text-xs text-muted-foreground">Quote source: {result.quoteSource} · {result.quoteCategory ?? "Category unavailable"} · observed {result.quoteObservedAt ?? "Time unavailable"}{result.quoteIsExpired ? " · Expired quote" : ""}</p> : null}
    {result.quoteSettlementDate && result.quoteSettlementDate !== result.context.settlementDate ? <StatusBanner tone="info" title="Modeled settlement adjustment" detail={<>Original settlement {result.quoteSettlementDate}: clean {bondDecimalText(result.originalCleanConsideration)}, dirty {bondDecimalText(result.originalDirtyConsideration)}. Comparison settlement {result.context.settlementDate}. The original quote remains source evidence; adjusted amounts use the retained cash-flow model and calibrated spread.</>} /> : null}
    {result.settlementDifference != null ? <StatusBanner tone={/^-?0(?:\.0+)?$/.test(String(result.settlementDifference)) ? "success" : "warning"} title="Broker confirmation cash difference" detail={`${bondDecimalText(result.settlementDifference)} ${result.currency} · observed cash less calculated clean consideration, accrued interest and fees.`} /> : null}
    {result.providerYieldConvention ? <p className="text-xs text-muted-foreground">Provider yield convention: {result.providerYieldConvention}. It remains separate from Meridian-calculated yields.</p> : null}
    {result.quoteObservationId || result.priceSelectionReceiptId ? <p className="break-all text-xs text-muted-foreground">Quote observation: {result.quoteObservationId ?? "None"} · price selection receipt: {result.priceSelectionReceiptId ?? "None"}</p> : null}
    {result.exerciseScenarios?.length ? <details><summary className="cursor-pointer text-sm">Discrete exercise yields and notice dates</summary><ul className="space-y-1 pt-2 text-xs">{result.exerciseScenarios.map((scenario, index) => <li key={index}>{scenario.kind} · {scenario.exerciseDate} · redemption {bondDecimalText(scenario.redemptionPrice)} · yield {bondDecimalText(scenario.yield)} · latest notice {scenario.latestNoticeDate ?? "Unavailable"}{scenario.issue ? ` · ${scenario.issue}` : ""}</li>)}</ul></details> : null}
    {result.issues.length ? <StatusBanner tone="warning" title="Input and metric coverage" detail={<ul className="space-y-1">{result.issues.map((issue, index) => <li key={`${issue.code}-${index}`}>{issue.operation}: {issue.reason} <span className="break-all">({issue.code})</span></li>)}</ul>} /> : null}
    {result.metrics.some((metric) => !metric.available) ? <ul className="text-xs text-muted-foreground">{result.metrics.filter((metric) => !metric.available).map((metric) => <li key={metric.name}>{metric.name}: {metric.reason ?? "Unavailable"}</li>)}</ul> : null}
    <div className="overflow-x-auto"><table className="w-full text-left text-sm"><caption className="mb-2 text-left font-medium">Expected coupon and principal cash</caption><thead><tr>{["Payment date", "Coupon", "Principal", "Outstanding principal", "Index ratio", "Rate evidence"].map((label) => <th key={label} className="p-2">{label}</th>)}</tr></thead><tbody>{result.cashFlows.map((flow, index) => <tr key={`${flow.paymentDate}-${index}`} className="border-t border-border"><td className="p-2">{flow.paymentDate}</td><td className="p-2 font-mono">{bondDecimalText(flow.interest)}</td><td className="p-2 font-mono">{bondDecimalText(flow.principal)}</td><td className="p-2 font-mono">{bondDecimalText(flow.outstandingPrincipal)}</td><td className="p-2 font-mono">{bondDecimalText(flow.indexRatio)}</td><td className="p-2">{flow.isForecast ? "Includes forecasts" : "Contractual / observed"}{flow.observations.length ? <details><summary className="cursor-pointer">{flow.observations.length} observations</summary><ul className="max-h-48 overflow-auto text-xs">{flow.observations.map((observation, observationIndex) => <li key={observationIndex}>{observation.observationDate} · {observation.days} days · {bondDecimalText(observation.rate)} · {observation.isObserved ? "Observed" : "Forecast"} · {observation.source}</li>)}</ul></details> : null}</td></tr>)}</tbody></table>{!result.cashFlows.length ? <p className="text-sm text-muted-foreground">No projected payments available.</p> : null}</div>
    <details><summary className="cursor-pointer text-sm font-medium">Retained calculation inputs ({result.inputs.length})</summary><ul className="space-y-2 pt-2 text-xs">{result.inputs.map((input, index) => <li key={index} className="break-all">{input.kind} · {input.key}{input.version != null ? ` · version ${input.version}` : ""}{input.contentSha256 ? ` · SHA-256 ${input.contentSha256}` : ""}</li>)}</ul></details>
  </section>;
}
