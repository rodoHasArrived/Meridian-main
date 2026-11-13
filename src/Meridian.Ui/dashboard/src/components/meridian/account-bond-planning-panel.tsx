import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { AccountBondTools } from "@/components/meridian/account-bond-tools";
import { AccountBondPerformancePanel } from "@/components/meridian/account-bond-performance-panel";
import { captureBondMonitoring, getBondMonitoring, planAccountBondIncome } from "@/lib/api/bond-workflow.api";
import { describeApiError } from "@/lib/api-errors";
import { bondDecimalText } from "@/lib/bond-decimal-format";
import type { AccountBondIncomePlan, BondMonitoringCaptureRequest, BondMonitoringSnapshot } from "@/types/bond-workflow.types";

const today = () => new Date().toISOString().slice(0, 10);
function twelveMonthEnd() { const date = new Date(); date.setUTCFullYear(date.getUTCFullYear() + 1); date.setUTCDate(date.getUTCDate() - 1); return date.toISOString().slice(0, 10); }

export function AccountBondPlanningPanel({ accountId, plan = planAccountBondIncome, history = getBondMonitoring, capture = captureBondMonitoring }: {
  accountId?: string; plan?: typeof planAccountBondIncome; history?: typeof getBondMonitoring; capture?: typeof captureBondMonitoring;
}) { return <PlanningInputs key={accountId ?? "unscoped"} accountId={accountId} plan={plan} history={history} capture={capture} />; }

function PlanningInputs({ accountId, plan, history, capture }: {
  accountId?: string; plan: typeof planAccountBondIncome; history: typeof getBondMonitoring; capture: typeof captureBondMonitoring;
}) {
  const id = useId();
  const [account, setAccount] = useState(accountId ?? ""); const [book, setBook] = useState("");
  const [start, setStart] = useState(today); const [end, setEnd] = useState(twelveMonthEnd);
  const [evidence, setEvidence] = useState(""); const [currency, setCurrency] = useState("");
  const [receiptHandoff, setReceiptHandoff] = useState(""); const [receiptRationale, setReceiptRationale] = useState("");
  const [receiptRunId, setReceiptRunId] = useState("");
  const [result, setResult] = useState<AccountBondIncomePlan | null>(null);
  const [monitoring, setMonitoring] = useState<BondMonitoringSnapshot | null>(null);
  const [error, setError] = useState<string | null>(null); const [pending, setPending] = useState(false);
  const [message, setMessage] = useState<string | null>(null); const active = useRef<AbortController | null>(null);
  useEffect(() => () => active.current?.abort(), []);
  function edit(change: () => void) { active.current?.abort(); setResult(null); setMonitoring(null); setReceiptHandoff(""); setReceiptRationale(""); setReceiptRunId(""); setError(null); setMessage(null); setPending(false); change(); }
  function validateScope(scope: { accountId: string; bookId: string }) {
    if (scope.accountId.toLowerCase() !== account.trim().toLowerCase() || scope.bookId.toLowerCase() !== book.trim().toLowerCase())
      throw new Error("The response returned a different account or book.");
  }
  async function run(action: "plan" | "history" | "capture") {
    active.current?.abort(); const controller = new AbortController(); active.current = controller;
    setError(null); setMessage(null);
    if (!book.trim() || !account.trim()) { setError("Enter the account ID and its ledger book ID."); return; }
    setPending(true);
    try {
      const options = { signal: controller.signal, allowDevelopmentFallback: false };
      if (action === "plan") {
        setResult(null); setMonitoring(null);
        if (!start || !end || start > end) throw new Error("Planning end must be on or after its start date.");
        const retained = evidence.trim() ? JSON.parse(evidence) as { openingBalances?: unknown[]; fxConversions?: unknown[] } : {};
        if (retained === null || typeof retained !== "object" || Array.isArray(retained)
          || (retained.openingBalances !== undefined && !Array.isArray(retained.openingBalances))
          || (retained.fxConversions !== undefined && !Array.isArray(retained.fxConversions)))
          throw new Error("Retained cash evidence must contain openingBalances and fxConversions arrays.");
        const response = await plan(book.trim(), account.trim(), {
          scope: { tenantId: "", companyId: "", accountId: account.trim(), bookId: book.trim(), baseCurrency: "" },
          periodStart: start, periodEnd: end, context: { asOf: `${start}T00:00:00Z`, settlementDate: start },
          openingBalances: retained.openingBalances ?? [], fxConversions: retained.fxConversions ?? [], minimumCashThreshold: 0, bucketDays: 7
        }, options);
        if (controller.signal.aborted) return;
        validateScope(response.scope);
        if (response.periodStart !== start || response.periodEnd !== end) throw new Error("The forecast returned a different planning period.");
        setResult(response); setCurrency(response.currencyLadders[0]?.baseCurrency ?? "");
      } else if (action === "history") {
        const response = await history(book.trim(), account.trim(), options);
        if (controller.signal.aborted) return;
        response.forEach((item) => validateScope(item.inputs.scope));
        const latest = [...response].sort((a, b) => b.inputs.recordedAtUtc.localeCompare(a.inputs.recordedAtUtc))[0];
        setMonitoring(latest ?? null);
        if (!latest) setMessage("No retained collection or watch snapshots for this account and book.");
      } else if (result) {
        const response = await capture(book.trim(), account.trim(), result.monitoringInputs, options);
        if (controller.signal.aborted) return;
        validateScope(response.inputs.scope); setMonitoring(response);
        setMessage("Expected obligations retained. No cash receipt or journal was created.");
      }
    } catch (failure) { if (!controller.signal.aborted) setError(describeApiError(failure, "Account bond planning unavailable.").summary); }
    finally { if (!controller.signal.aborted) setPending(false); }
  }
  const ladder = currency === "__base__" ? result?.baseCurrencyLadder : result?.currencyLadders.find((item) => item.baseCurrency === currency);
  async function retainReceiptEvidence() {
    active.current?.abort(); const controller = new AbortController(); active.current = controller; setError(null); setMessage(null);
    const baseline = monitoring?.inputs ?? result?.monitoringInputs;
    if (!baseline) { setError("Build a plan or load monitoring history before retaining receipt evidence."); return; }
    setPending(true);
    try {
      const selected = JSON.parse(receiptHandoff) as Partial<BondMonitoringCaptureRequest>;
      if (!selected || typeof selected !== "object" || Array.isArray(selected)) throw new Error("Receipt evidence must be an object with selected input arrays.");
      for (const key of ["payments", "allocations", "receiptRevisions", "observations"] as const)
        if (selected[key] !== undefined && !Array.isArray(selected[key])) throw new Error(`${key} must be an array.`);
      if (monitoring && !receiptRationale.trim()) throw new Error("A revised snapshot requires a correction rationale.");
      const identity = receiptRunId || crypto.randomUUID(); setReceiptRunId(identity);
      const request: BondMonitoringCaptureRequest = { ...baseline, runId: identity,
        planningRunId: baseline.planningRunId ?? baseline.runId, recordedAtUtc: new Date().toISOString(),
        payments: selected.payments ?? baseline.payments, allocations: selected.allocations ?? baseline.allocations,
        receiptRevisions: selected.receiptRevisions ?? baseline.receiptRevisions,
        observations: selected.observations ?? baseline.observations,
        previousSnapshotId: monitoring?.snapshotId ?? null, correctionRationale: receiptRationale.trim() || null };
      const captured = await capture(book.trim(), account.trim(), request, { signal: controller.signal, allowDevelopmentFallback: false });
      if (controller.signal.aborted) return;
      validateScope(captured.inputs.scope); setMonitoring(captured); setMessage("Receipt evidence retained as a new monitoring snapshot. Prior evidence is preserved.");
      setReceiptRunId(""); setReceiptHandoff(""); setReceiptRationale("");
    } catch (failure) { if (!controller.signal.aborted) setError(describeApiError(failure, "Receipt evidence could not be retained.").summary); }
    finally { if (!controller.signal.aborted) setPending(false); }
  }
  return <Card className="panel-surface"><CardHeader><CardTitle>Account bond income planning</CardTitle>
    <CardDescription>Next twelve months from accepted holdings and retained terms. Native currencies stay separate; base totals require retained FX. Missing opening cash remains a coverage gap.</CardDescription></CardHeader>
    <CardContent className="space-y-4"><form onSubmit={(event: FormEvent) => { event.preventDefault(); void run("plan"); }} aria-label="Account bond planning" className="space-y-3">
      <FormGrid columns={2}>
        <FormRow label="Account ID" labelFor={`${id}-account`}><Input id={`${id}-account`} value={account} onChange={(e) => edit(() => setAccount(e.target.value))} required /></FormRow>
        <FormRow label="Ledger book ID" labelFor={`${id}-book`}><Input id={`${id}-book`} value={book} onChange={(e) => edit(() => setBook(e.target.value))} required /></FormRow>
        <FormRow label="Planning start" labelFor={`${id}-start`}><Input id={`${id}-start`} type="date" value={start} onChange={(e) => edit(() => setStart(e.target.value))} required /></FormRow>
        <FormRow label="Planning end (inclusive)" labelFor={`${id}-end`}><Input id={`${id}-end`} type="date" value={end} onChange={(e) => edit(() => setEnd(e.target.value))} required /></FormRow>
      </FormGrid>
      <details><summary className="cursor-pointer text-sm font-medium">Retained opening cash and FX inputs</summary><label htmlFor={`${id}-evidence`} className="mt-2 block text-xs text-muted-foreground">Optional evidence handoff JSON: openingBalances and fxConversions arrays, with retained source provenance for every input.</label><textarea id={`${id}-evidence`} value={evidence} onChange={(e) => edit(() => setEvidence(e.target.value))} className="mt-2 min-h-24 w-full rounded border border-border bg-background p-2 font-mono text-xs" /></details>
      <div className="flex flex-wrap gap-2"><Button type="submit" disabled={pending}>{pending ? "Loading account evidence…" : "Build bond income plan"}</Button><Button type="button" variant="outline" disabled={pending} onClick={() => void run("history")}>Load collection and watch history</Button></div>
    </form>
    <AccountBondTools key={`${book}-${account}-${start}`} bookId={book} accountId={account} asOf={start} />
    <AccountBondPerformancePanel key={`performance-${book}-${account}-${start}-${end}`} bookId={book} accountId={account} start={start} end={end} />
    {result || monitoring ? <details className="rounded border border-border p-3"><summary className="cursor-pointer text-sm font-medium">Retain payment, deduction and watch evidence</summary><div className="mt-3 space-y-3"><label htmlFor={`${id}-receipts`} className="block text-xs text-muted-foreground">Evidence handoff JSON with complete selected payments, allocations, receiptRevisions and observations arrays. Omitted arrays retain the previous snapshot’s inputs. Every observed amount or deduction requires its retained source evidence. Contractual obligations remain bound to the original plan.</label><textarea id={`${id}-receipts`} value={receiptHandoff} onChange={(e) => { active.current?.abort(); setPending(false); setReceiptRunId(""); setError(null); setReceiptHandoff(e.target.value); }} className="min-h-28 w-full rounded border border-border bg-background p-2 font-mono text-xs" /><FormRow label="Monitoring correction rationale" labelFor={`${id}-rationale`}><Input id={`${id}-rationale`} value={receiptRationale} onChange={(e) => { active.current?.abort(); setPending(false); setReceiptRunId(""); setReceiptRationale(e.target.value); }} /></FormRow><Button type="button" variant="outline" disabled={pending || !receiptHandoff.trim()} onClick={() => void retainReceiptEvidence()}>Retain receipt and watch evidence</Button></div></details> : null}
    {error ? <StatusBanner tone="danger" role="alert" title="Account planning unavailable" detail={error} /> : null}
    {message ? <StatusBanner tone="info" role="status" title={message} /> : null}
    {result ? <section aria-label="Account bond income plan" className="space-y-3">
      <p className="text-xs text-muted-foreground">Holdings snapshot <span className="break-all font-mono">{result.holdingsBatchId ?? "Unavailable"}</span> · {result.periodStart} to {result.periodEnd} · {result.calculations.length} bond calculations.</p>
      {result.blockers.length ? <StatusBanner tone="warning" title="Forecast coverage gaps" detail={<ul>{result.blockers.map((blocker, index) => <li key={index}>{blocker}</li>)}</ul>} /> : null}
      {result.currencyLadders.length ? <FormRow label="Cash view currency" labelFor={`${id}-currency`}><Select id={`${id}-currency`} value={currency} onChange={(e) => setCurrency(e.target.value)}>{result.currencyLadders.map((item) => <option key={item.baseCurrency} value={item.baseCurrency}>{item.baseCurrency} · native</option>)}<option value="__base__">{result.scope.baseCurrency} · declared FX base view</option></Select></FormRow> : <p className="text-sm text-muted-foreground">No native-currency ladder available.</p>}
      {ladder ? <div className="space-y-2 overflow-x-auto"><table className="w-full text-left text-sm"><caption className="mb-2 text-left font-medium">{ladder.baseCurrency} expected cash · {currency === "__base__" ? "declared FX base view" : "native currency"}</caption><thead><tr>{["Period", "Inflows", "Outflows", "Net cash", "Closing cash"].map((label) => <th key={label} className="p-2">{label}</th>)}</tr></thead><tbody>{ladder.buckets.map((bucket) => <tr key={bucket.bucketStart} className="border-t border-border"><th className="p-2 font-normal">{bucket.label}</th><td className="p-2 font-mono">{bondDecimalText(bucket.inflows)}</td><td className="p-2 font-mono">{bondDecimalText(bucket.outflows)}</td><td className="p-2 font-mono">{bondDecimalText(bucket.netFlow)}</td><td className="p-2 font-mono">{ladder.isDecisionReady === true ? bondDecimalText(bucket.cumulativeCash) : "Unavailable · incomplete inputs"}</td></tr>)}</tbody></table>{ladder.blockingReasons?.length ? <ul className="text-xs text-warning">{ladder.blockingReasons.map((reason, index) => <li key={index}>{reason}</li>)}</ul> : null}</div> : null}
      <details><summary className="cursor-pointer text-sm font-medium">Bond calculation coverage</summary><ul className="space-y-2 pt-2 text-xs">{result.calculations.map((item) => <li key={item.securityId}><span className="break-all font-mono">{item.securityId}</span> · {item.currency} · {item.isBlocked ? "Blocked" : item.issues.length ? "Partial" : "Calculated"}{item.issues.map((issue, index) => <p key={index}>{issue.code}: {issue.reason}</p>)}</li>)}</ul></details>
      <Button type="button" variant="outline" disabled={pending || !result.monitoringInputs.obligations.length} onClick={() => void run("capture")}>Retain expected obligations for monitoring</Button>
    </section> : null}
    {monitoring ? <section aria-label="Bond collection and watch" className="space-y-3"><p className="break-all text-xs text-muted-foreground">Monitoring snapshot {monitoring.snapshotId} · {monitoring.engineVersion}</p>
      {monitoring.issues.length ? <StatusBanner tone="warning" title="Monitoring issues" detail={<ul>{monitoring.issues.map((issue, index) => <li key={index}>{issue.code}: {issue.message}</li>)}</ul>} /> : null}
      <div className="overflow-x-auto"><table className="w-full text-left text-sm"><caption className="mb-2 text-left font-medium">Coupon and principal collections</caption><thead><tr>{["Due", "Type", "Currency", "Expected", "Gross collected", "Unpaid residual", "Status / age"].map((label) => <th key={label} className="p-2">{label}</th>)}</tr></thead><tbody>{monitoring.collections.map((row) => <tr key={row.obligationId} className="border-t border-border"><td className="p-2">{row.dueDate}</td><td className="p-2">{row.flowType}</td><td className="p-2">{row.currency}</td><td className="p-2">{bondDecimalText(row.expectedAmount)}</td><td className="p-2">{bondDecimalText(row.grossCollected)}</td><td className="p-2">{bondDecimalText(row.remainingAmount)}</td><td className="p-2">{row.status} · {row.ageDays} days</td></tr>)}</tbody></table>{!monitoring.collections.length ? <p className="text-sm text-muted-foreground">No retained collection obligations.</p> : null}</div>
      <h4 className="text-sm font-medium">Lifecycle and credit watch</h4>{monitoring.watch.length ? <ul className="space-y-2 text-sm">{monitoring.watch.map((item) => <li key={item.watchId}>{item.date} · {item.kind}{item.agency ? ` · ${item.agency}` : ""} · {item.summary} <span className="text-xs text-muted-foreground">Source: {item.source}</span></li>)}</ul> : <p className="text-sm text-muted-foreground">No retained watch observations.</p>}
    </section> : null}
  </CardContent></Card>;
}
