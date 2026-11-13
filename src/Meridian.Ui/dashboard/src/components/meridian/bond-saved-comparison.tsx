import { useEffect, useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { FormGrid, FormRow } from "@/components/ui/form";
import { StatusBanner } from "@/components/ui/status-banner";
import { loadBondComparison, saveBondComparison } from "@/lib/api/bond-workflow.api";
import { describeApiError } from "@/lib/api-errors";
import { bondDecimalText } from "@/lib/bond-decimal-format";
import type { BondComparisonResult, BondWorkflowRequest } from "@/types/bond-workflow.types";

export function BondSavedComparison({ requests }: { requests: BondWorkflowRequest[] }) {
  const id = useId(); const [book, setBook] = useState(""); const [account, setAccount] = useState(""); const [runId, setRunId] = useState("");
  const [result, setResult] = useState<BondComparisonResult | null>(null); const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null); const active = useRef<AbortController | null>(null);
  useEffect(() => () => active.current?.abort(), []);
  function edit(change: () => void) { active.current?.abort(); setPending(false); setResult(null); setError(null); change(); }
  async function run(saving: boolean) {
    active.current?.abort(); const controller = new AbortController(); active.current = controller; setResult(null); setError(null);
    if (!book.trim() || !account.trim() || (!saving && !runId.trim())) { setError("Enter the account, book and retained comparison ID when reopening."); return; }
    setPending(true);
    try {
      const options = { signal: controller.signal, allowDevelopmentFallback: false };
      if (saving && requests.length) {
        const identity = runId.trim() || crypto.randomUUID();
        setRunId(identity);
        const saved = await saveBondComparison(book.trim(), account.trim(), { runId: identity, context: requests[0].context, bonds: requests }, options);
        if (!controller.signal.aborted) { setRunId(identity); setResult(saved); }
      } else {
        const saved = await loadBondComparison(book.trim(), account.trim(), runId.trim(), options);
        if (!controller.signal.aborted) setResult(saved.result);
      }
    } catch (failure) { if (!controller.signal.aborted) setError(describeApiError(failure, "Retained comparison unavailable.").summary); }
    finally { if (!controller.signal.aborted) setPending(false); }
  }
  return <details className="rounded border border-border p-3"><summary className="cursor-pointer text-sm font-medium">Save or reopen an account comparison</summary><div className="mt-3 space-y-3"><FormGrid columns={3}>
    <FormRow label="Comparison account ID" labelFor={`${id}-account`}><Input id={`${id}-account`} value={account} onChange={(e) => edit(() => setAccount(e.target.value))} /></FormRow>
    <FormRow label="Comparison book ID" labelFor={`${id}-book`}><Input id={`${id}-book`} value={book} onChange={(e) => edit(() => setBook(e.target.value))} /></FormRow>
    <FormRow label="Retained comparison ID" labelFor={`${id}-run`} hint="A new ID is generated when saving with this blank."><Input id={`${id}-run`} value={runId} onChange={(e) => edit(() => setRunId(e.target.value))} /></FormRow>
  </FormGrid><div className="flex flex-wrap gap-2"><Button type="button" variant="outline" disabled={pending || !requests.length} onClick={() => void run(true)}>Retain calculated comparison</Button><Button type="button" variant="outline" disabled={pending} onClick={() => void run(false)}>Reopen retained comparison</Button></div>
    {error ? <StatusBanner role="alert" tone="danger" title="Comparison unavailable" detail={error} /> : null}
    {result ? <section aria-label="Retained bond comparison" className="space-y-2"><p className="break-all text-xs">Retained ID {result.runId} · {result.modelVersion} · settlement {result.context.settlementDate} · {result.isComplete ? "Complete coverage" : "Partial coverage"}</p>{result.issues.map((issue, index) => <p className="text-xs text-warning" key={index}>{issue}</p>)}<ul className="space-y-2 text-xs">{result.bonds.map((bond, index) => <li key={`${bond.securityId}-${index}`} className="break-all">{bond.securityId} · {bond.currency} · purchase cash {bondDecimalText(bond.totalCash)} · projected-flow yield {bondDecimalText(bond.yield)} · YTW {bondDecimalText(bond.yieldToWorst)}{bond.issues.map((issue, issueIndex) => <p key={issueIndex}>{issue.code}: {issue.reason}</p>)}</li>)}</ul></section> : null}
  </div></details>;
}
