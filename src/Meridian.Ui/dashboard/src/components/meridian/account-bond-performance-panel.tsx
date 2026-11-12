import { useEffect, useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { FormGrid, FormRow } from "@/components/ui/form";
import { StatusBanner } from "@/components/ui/status-banner";
import { explainBondPerformance, importBondPerformanceTransactions } from "@/lib/api/bond-workflow.api";
import { describeApiError } from "@/lib/api-errors";
import { bondDecimalText } from "@/lib/bond-decimal-format";
import type { BondPerformanceResult } from "@/types/bond-workflow.types";

export function AccountBondPerformancePanel({ bookId, accountId, start, end, explain = explainBondPerformance, importSource = importBondPerformanceTransactions }: {
  bookId: string; accountId: string; start: string; end: string; explain?: typeof explainBondPerformance; importSource?: typeof importBondPerformanceTransactions;
}) {
  const id = useId(); const [lots, setLots] = useState(""); const [result, setResult] = useState<BondPerformanceResult | null>(null);
  const [pending, setPending] = useState(false); const [error, setError] = useState<string | null>(null);
  const [evidenceUri, setEvidenceUri] = useState(""); const [documentId, setDocumentId] = useState(""); const [sourceMessage, setSourceMessage] = useState<string | null>(null);
  const active = useRef<AbortController | null>(null); useEffect(() => () => active.current?.abort(), []);
  async function run() {
    active.current?.abort(); const controller = new AbortController(); active.current = controller; setResult(null); setError(null);
    if (!bookId.trim() || !accountId.trim() || !start || !end || start > end) { setError("Enter the account, ledger book and an ordered planning period first."); return; }
    setPending(true);
    try {
      const response = await explain(bookId.trim(), accountId.trim(), { scope: { tenantId: "", companyId: "", accountId: accountId.trim(), bookId: bookId.trim(), baseCurrency: "" },
        periodStart: start, periodEnd: end, knownAt: new Date().toISOString(), taxLotRecordIds: lots.split(/[\s,]+/).filter(Boolean) }, { signal: controller.signal, allowDevelopmentFallback: false });
      if (controller.signal.aborted) return;
      if (response.scope.accountId.toLowerCase() !== accountId.trim().toLowerCase() || response.scope.bookId.toLowerCase() !== bookId.trim().toLowerCase()
        || response.periodStart !== start || response.periodEnd !== end) throw new Error("Performance explanation returned a different account, book or period.");
      setResult(response);
    } catch (failure) { if (!controller.signal.aborted) setError(describeApiError(failure, "Bond performance explanation unavailable.").summary); }
    finally { if (!controller.signal.aborted) setPending(false); }
  }
  async function retainReviewedSource() {
    active.current?.abort(); const controller = new AbortController(); active.current = controller; setResult(null); setError(null); setSourceMessage(null);
    if (![bookId, accountId, evidenceUri, documentId].every((value) => value.trim())) { setError("Enter the account, book, reviewed evidence URI and document ID."); return; }
    setPending(true);
    try {
      const source = await importSource(bookId.trim(), accountId.trim(), { evidenceUri: evidenceUri.trim(), documentId: documentId.trim() }, { signal: controller.signal, allowDevelopmentFallback: false });
      if (!controller.signal.aborted) setSourceMessage(`Reviewed transaction population retained: ${source.populationId}. Recalculate the explanation to use the verified source.`);
    } catch (failure) { if (!controller.signal.aborted) setError(describeApiError(failure, "Reviewed transaction source unavailable.").summary); }
    finally { if (!controller.signal.aborted) setPending(false); }
  }
  return <details className="rounded border border-border p-3"><summary className="cursor-pointer text-sm font-medium">Income and market-return explanation</summary><div className="mt-3 space-y-3">
    <p className="text-xs text-muted-foreground">Uses the account, book and selected planning dates above. Historical marks, receipts, transaction coverage and book policies come from retained authorities. Accounting income and economic return remain separate; unexplained amounts stay visible.</p>
    <details><summary className="cursor-pointer text-sm">Use a reviewed transaction source</summary><p className="mt-2 text-xs text-muted-foreground">Enter an existing accepted account Evidence Vault document. The server reads its bytes, verifies review and scope, and derives transaction coverage; this form cannot declare completeness.</p><FormGrid columns={2}><FormRow label="Reviewed transaction evidence URI" labelFor={`${id}-evidence`}><Input id={`${id}-evidence`} value={evidenceUri} onChange={(event) => { active.current?.abort(); setPending(false); setResult(null); setSourceMessage(null); setEvidenceUri(event.target.value); }} /></FormRow><FormRow label="Reviewed transaction document ID" labelFor={`${id}-document`}><Input id={`${id}-document`} value={documentId} onChange={(event) => { active.current?.abort(); setPending(false); setResult(null); setSourceMessage(null); setDocumentId(event.target.value); }} /></FormRow></FormGrid><Button type="button" variant="outline" disabled={pending} onClick={() => void retainReviewedSource()}>Use reviewed transaction source</Button></details>
    {sourceMessage ? <StatusBanner role="status" tone="info" title={sourceMessage} /> : null}
    <label htmlFor={`${id}-lots`} className="block text-xs">Optional tax lot record IDs for the accounting-income bridge, separated by spaces or commas</label><textarea id={`${id}-lots`} value={lots} onChange={(e) => { active.current?.abort(); setPending(false); setResult(null); setError(null); setLots(e.target.value); }} className="min-h-20 w-full rounded border border-border bg-background p-2 font-mono text-xs" />
    <Button type="button" variant="outline" disabled={pending} onClick={() => void run()}>{pending ? "Resolving retained performance inputs…" : "Explain bond income and market return"}</Button>
    {error ? <StatusBanner role="alert" tone="danger" title="Performance explanation unavailable" detail={error} /> : null}
    {result ? <section aria-label="Bond performance explanation" className="space-y-3"><p className="text-xs text-muted-foreground">{result.modelVersion} · {result.periodStart} to {result.periodEnd} · {result.completeCoverage ? "Complete coverage" : "Partial coverage"}</p>
      {result.coverageGaps.length ? <StatusBanner tone="warning" title="Performance coverage gaps" detail={<ul>{result.coverageGaps.map((gap, index) => <li key={index}>{gap}</li>)}</ul>} /> : null}
      <p className="text-sm">Base-currency observed mark and cash change: {bondDecimalText(result.baseObservedMarkAndCashChange)} {result.scope.baseCurrency}. A mark-and-cash change is not a certified investment return when transaction coverage is incomplete.</p>
      <div className="overflow-x-auto"><table className="w-full text-left text-xs"><caption className="mb-2 text-left font-medium">Market values and actual cash · native currency</caption><thead><tr>{["Security", "Currency", "Opening dirty value", "Closing dirty value", "Actual net cash", "Observed change", "Economic return"].map((label) => <th className="p-2" key={label}>{label}</th>)}</tr></thead><tbody>{result.securities.map((row) => <tr key={row.securityId} className="border-t border-border"><th className="break-all p-2 font-normal">{row.securityId}</th><td className="p-2">{row.currency}</td><td className="p-2">{bondDecimalText(row.openingDirtyValue)}</td><td className="p-2">{bondDecimalText(row.closingDirtyValue)}</td><td className="p-2">{bondDecimalText(row.actualNetCash)}</td><td className="p-2">{bondDecimalText(row.observedMarkAndCashChange)}</td><td className="p-2">{bondDecimalText(row.economicReturn)}</td></tr>)}</tbody></table></div>
      <details><summary className="cursor-pointer text-sm font-medium">Model attribution, source inputs and residuals</summary><p className="mt-2 text-xs text-muted-foreground">Sequential repricing effects depend on calculation order. Noncash accounting amortization is excluded from market-return components.</p><ul className="space-y-3 pt-2 text-xs">{result.securities.map((row) => <li key={row.securityId}><p className="break-all font-medium">{row.securityId} · {row.currency}</p><p>Carry / roll {bondDecimalText(row.attribution.carryAndRoll)} · rates {bondDecimalText(row.attribution.rateEffect)} · spreads {bondDecimalText(row.attribution.spreadEffect)} · inflation {bondDecimalText(row.attribution.inflationEffect)} · collection / costs {bondDecimalText(row.attribution.collectionAndCosts)} · FX {bondDecimalText(row.attribution.fxEffect)} · residual {bondDecimalText(row.attribution.residual)}</p>{row.coverageGaps.map((gap, index) => <p key={index}>{gap}</p>)}{row.inputs.map((input, index) => <p className="break-all text-muted-foreground" key={index}>{input.kind} · {input.key} · {input.contentSha256 ?? "Hash unavailable"}</p>)}</li>)}</ul></details>
      <div className="overflow-x-auto"><table className="w-full text-left text-xs"><caption className="mb-2 text-left font-medium">Accounting-income bridge · selected authoritative lots</caption><thead><tr>{["Security", "Currency", "Coupon income", "Premium / discount", "Indexation", "Accounting income", "Gross coupon cash", "Accrual / cash difference"].map((label) => <th className="p-2" key={label}>{label}</th>)}</tr></thead><tbody>{result.accountingIncome.map((row, index) => <tr key={index} className="border-t border-border"><th className="break-all p-2 font-normal">{row.securityId}</th><td className="p-2">{row.currency}</td><td className="p-2">{bondDecimalText(row.couponIncome)}</td><td className="p-2">{bondDecimalText(row.premiumDiscountIncome)}</td><td className="p-2">{bondDecimalText(row.indexationIncome)}</td><td className="p-2">{bondDecimalText(row.accountingIncome)}</td><td className="p-2">{bondDecimalText(row.cashGrossCoupons)}</td><td className="p-2">{bondDecimalText(row.accrualVersusCashDifference)}</td></tr>)}</tbody></table>{!result.accountingIncome.length ? <p className="text-xs text-muted-foreground">No selected, supported lot-income bridge is available.</p> : null}</div>
    </section> : null}
  </div></details>;
}
