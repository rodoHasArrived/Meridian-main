import { useEffect, useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { FormGrid, FormRow } from "@/components/ui/form";
import { StatusBanner } from "@/components/ui/status-banner";
import { calculateAccountBondRisk, importAccountBondHoldings, loadAccountBondRisk } from "@/lib/api/bond-workflow.api";
import { describeApiError } from "@/lib/api-errors";
import { bondDecimalText, DECIMAL_LITERAL } from "@/lib/bond-decimal-format";
import type { BondPortfolioRiskResult } from "@/types/bond-workflow.types";

export function AccountBondTools({ bookId, accountId, asOf, calculateRisk = calculateAccountBondRisk, acceptHoldings = importAccountBondHoldings, loadRisk = loadAccountBondRisk }: {
  bookId: string; accountId: string; asOf: string; calculateRisk?: typeof calculateAccountBondRisk; acceptHoldings?: typeof importAccountBondHoldings; loadRisk?: typeof loadAccountBondRisk;
}) {
  const id = useId(); const [parallel, setParallel] = useState("100"); const [spread, setSpread] = useState("100"); const [pillars, setPillars] = useState("");
  const [connection, setConnection] = useState(""); const [syncRun, setSyncRun] = useState(""); const [batch, setBatch] = useState("");
  const [revision, setRevision] = useState("1"); const [supersedes, setSupersedes] = useState("");
  const [risk, setRisk] = useState<BondPortfolioRiskResult | null>(null); const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null); const [pending, setPending] = useState(false);
  const [riskRunId, setRiskRunId] = useState("");
  const [riskContext, setRiskContext] = useState<BondPortfolioRiskResult["context"]>(undefined);
  const active = useRef<AbortController | null>(null); useEffect(() => () => active.current?.abort(), []);
  function edit(change: () => void, retainRiskIdentity = false) { active.current?.abort(); setPending(false); setRisk(null); setError(null); setMessage(null); setRiskContext(undefined); if (!retainRiskIdentity) setRiskRunId(""); change(); }
  async function run(importing: boolean) {
    active.current?.abort(); const controller = new AbortController(); active.current = controller; setRisk(null); setError(null); setMessage(null);
    if (!bookId.trim() || !accountId.trim() || !asOf) { setError("Enter an account, ledger book and planning start date first."); return; }
    setPending(true);
    try {
      const options = { signal: controller.signal, allowDevelopmentFallback: false };
      if (importing) {
        if (!connection.trim() || !syncRun.trim() || !/^\d+$/.test(revision) || Number(revision) < 1) throw new Error("Enter a reviewed connection, sync run and positive statement revision.");
        const identity = batch.trim() || crypto.randomUUID(); setBatch(identity);
        const response = await acceptHoldings(bookId.trim(), accountId.trim(), { connectionId: connection.trim(), syncRunId: syncRun.trim(), accountId: accountId.trim(), batchId: identity, asOfDate: asOf, revision: Number(revision), supersedesBatchId: supersedes.trim() || null }, options);
        if (!controller.signal.aborted) setMessage(`Reviewed holdings accepted as snapshot ${response.batchId}. Rebuild the income plan to use the accepted population.`);
      } else {
        if (![parallel, spread].every((value) => DECIMAL_LITERAL.test(value.trim()))) throw new Error("Rate and spread shocks must be decimal basis points.");
        const points = pillars.split(/\r?\n/).filter((line) => line.trim()).map((line) => { const [date, shift, ...extra] = line.split(",").map((part) => part.trim()); if (extra.length || !/^\d{4}-\d{2}-\d{2}$/.test(date) || !DECIMAL_LITERAL.test(shift ?? "")) throw new Error("Enter steepening pillars as date, shift in basis points, one per line."); return { date, shiftBps: shift }; });
        const identity = riskRunId.trim() || crypto.randomUUID(); setRiskRunId(identity);
        const context = riskContext ?? { asOf: `${asOf}T00:00:00Z`, settlementDate: asOf, knownAt: new Date().toISOString() };
        setRiskContext(context);
        const response = await calculateRisk(bookId.trim(), accountId.trim(), { context, parallelShiftBps: parallel.trim(), spreadWideningBps: spread.trim(), steepeningPillars: points.length ? points : undefined, runId: identity }, options);
        if (controller.signal.aborted) return;
        if (response.scope.accountId.toLowerCase() !== accountId.trim().toLowerCase() || response.scope.bookId.toLowerCase() !== bookId.trim().toLowerCase()) throw new Error("Risk calculation returned a different account or book.");
        setRisk(response); setRiskContext(response.context ?? context);
      }
    } catch (failure) { if (!controller.signal.aborted) setError(describeApiError(failure, "Account bond operation unavailable.").summary); }
    finally { if (!controller.signal.aborted) setPending(false); }
  }
  async function reopenRisk() {
    active.current?.abort(); const controller = new AbortController(); active.current = controller; setRisk(null); setError(null); setMessage(null);
    if (!bookId.trim() || !accountId.trim() || !riskRunId.trim()) { setError("Enter the account, book and retained risk scenario ID."); return; }
    setPending(true);
    try {
      const saved = await loadRisk(bookId.trim(), accountId.trim(), riskRunId.trim(), { signal: controller.signal, allowDevelopmentFallback: false });
      if (controller.signal.aborted) return;
      if (saved.result.scope.accountId.toLowerCase() !== accountId.trim().toLowerCase() || saved.result.scope.bookId.toLowerCase() !== bookId.trim().toLowerCase()
        || saved.result.runId !== riskRunId.trim()) throw new Error("Retained risk returned a different account, book or scenario identity.");
      setParallel(String(saved.request.parallelShiftBps)); setSpread(String(saved.request.spreadWideningBps));
      setPillars((saved.request.steepeningPillars ?? []).map((pillar) => `${pillar.date}, ${pillar.shiftBps}`).join("\n")); setRiskContext(saved.request.context); setRisk(saved.result);
    } catch (failure) { if (!controller.signal.aborted) setError(describeApiError(failure, "Retained risk scenario unavailable.").summary); }
    finally { if (!controller.signal.aborted) setPending(false); }
  }
  return <div className="space-y-3"><details className="rounded border border-border p-3"><summary className="cursor-pointer text-sm font-medium">Portfolio rate and credit risk</summary><div className="mt-3 space-y-3"><FormGrid columns={2}>
    <FormRow label="Parallel rate shock (bp)" labelFor={`${id}-parallel`}><Input id={`${id}-parallel`} value={parallel} inputMode="decimal" onChange={(e) => edit(() => setParallel(e.target.value))} /></FormRow>
    <FormRow label="Spread widening (bp)" labelFor={`${id}-spread`}><Input id={`${id}-spread`} value={spread} inputMode="decimal" onChange={(e) => edit(() => setSpread(e.target.value))} /></FormRow>
  </FormGrid><label htmlFor={`${id}-pillars`} className="block text-xs">Optional curve-steepening pillars: date, shift in bp, one per line</label><textarea id={`${id}-pillars`} value={pillars} onChange={(e) => edit(() => setPillars(e.target.value))} className="min-h-20 w-full rounded border border-border bg-background p-2 font-mono text-xs" /><FormRow label="Retained risk scenario ID" labelFor={`${id}-risk-run`} hint="Blank generates an ID for safe retry. Changed shocks start a new scenario; reopening preserves the retained calculation date."><Input id={`${id}-risk-run`} value={riskRunId} onChange={(event) => edit(() => setRiskRunId(event.target.value), true)} /></FormRow><div className="flex flex-wrap gap-2"><Button type="button" variant="outline" disabled={pending} onClick={() => void run(false)}>Calculate account rate and credit risk</Button><Button type="button" variant="outline" disabled={pending} onClick={() => void reopenRisk()}>Reopen retained risk scenario</Button></div></div></details>
    <details className="rounded border border-border p-3"><summary className="cursor-pointer text-sm font-medium">Accept reviewed bond holdings</summary><div className="mt-3 space-y-3"><p className="text-xs text-muted-foreground">Use a complete holding population already mapped and reviewed in Data integrations. Acceptance publishes an observed account snapshot; it creates no acquisition lot or journal. The statement date is the planning start date above.</p><FormGrid columns={2}>
      <FormRow label="Reviewed connection ID" labelFor={`${id}-connection`}><Input id={`${id}-connection`} value={connection} onChange={(e) => edit(() => setConnection(e.target.value))} /></FormRow>
      <FormRow label="Reviewed sync run ID" labelFor={`${id}-sync`}><Input id={`${id}-sync`} value={syncRun} onChange={(e) => edit(() => setSyncRun(e.target.value))} /></FormRow>
      <FormRow label="Statement batch ID" labelFor={`${id}-batch`} hint="Blank generates an ID retained for safe retry."><Input id={`${id}-batch`} value={batch} onChange={(e) => edit(() => setBatch(e.target.value))} /></FormRow>
      <FormRow label="Statement revision" labelFor={`${id}-revision`}><Input id={`${id}-revision`} value={revision} inputMode="numeric" onChange={(e) => edit(() => setRevision(e.target.value))} /></FormRow>
      <FormRow label="Superseded batch ID (optional)" labelFor={`${id}-supersedes`}><Input id={`${id}-supersedes`} value={supersedes} onChange={(e) => edit(() => setSupersedes(e.target.value))} /></FormRow>
    </FormGrid><Button type="button" variant="outline" disabled={pending} onClick={() => void run(true)}>Accept reviewed holding snapshot</Button></div></details>
    {error ? <StatusBanner role="alert" tone="danger" title="Account bond operation unavailable" detail={error} /> : null}{message ? <StatusBanner role="status" tone="info" title={message} /> : null}
    {risk ? <section aria-label="Account bond risk result" className="space-y-3"><p className="text-sm font-medium">{risk.isComplete ? "Complete calculation coverage" : "Partial calculation coverage"} · {risk.positions.length} positions</p><p className="break-all text-xs text-muted-foreground">Retained scenario {risk.runId ?? "Identity unavailable"} · economic date {risk.context?.asOf ?? "Unavailable"}</p>{risk.issues.length ? <StatusBanner tone="warning" title="Risk coverage gaps" detail={<ul>{risk.issues.map((issue, index) => <li key={index}>{issue}</li>)}</ul>} /> : null}<div className="overflow-x-auto"><table className="w-full text-left text-xs"><caption className="mb-2 text-left">Exposure by security, issuer, currency and maturity · currencies are not summed</caption><thead><tr>{["Dimension", "Bucket", "Currency", "Covered holdings", "Known PV", "Known DV01", "Known CS01"].map((label) => <th key={label} className="p-2">{label}</th>)}</tr></thead><tbody>{risk.buckets.map((row, index) => <tr className="border-t border-border" key={index}><td className="p-2">{row.dimension}</td><td className="p-2">{row.key}</td><td className="p-2">{row.currency}</td><td className="p-2">{row.coveredHoldings} / {row.holdings}</td><td className="p-2">{bondDecimalText(row.knownPresentValue)}</td><td className="p-2">{bondDecimalText(row.knownDv01)}</td><td className="p-2">{bondDecimalText(row.knownCs01)}</td></tr>)}</tbody></table></div><details><summary className="cursor-pointer text-sm">Position scenarios and evidence gaps</summary><ul className="space-y-3 pt-2 text-xs">{risk.positions.map((position, index) => <li key={index}><p className="break-all">{position.securityId} · {position.issuer ?? "Issuer unavailable"} · {position.currency} · original face {bondDecimalText(position.signedOriginalFace)}</p>{position.scenarios.map((scenario) => <p key={scenario.scenario}>{scenario.scenario}: PV {bondDecimalText(scenario.presentValue)} · change {bondDecimalText(scenario.change)}</p>)}{position.issues.map((issue, issueIndex) => <p key={issueIndex}>{issue}</p>)}</li>)}</ul></details></section> : null}
  </div>;
}
