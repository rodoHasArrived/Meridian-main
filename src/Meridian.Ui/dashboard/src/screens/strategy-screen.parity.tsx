import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { apiGetJson } from "@/lib/api";
import { formatCurrency } from "@/lib/format";

interface ParityReport {
  runId: string; baselineRunId?: string | null; status: string; inputMode: string;
  baselineNetPnl?: number | null; observedNetPnl?: number | null; baselineFills?: number | null; observedFills?: number | null;
  checks: Array<{ code: string; status: string; message: string }>; journalRoute: string;
}
export function StrategyRunParityPanel({ accountId, runId }: { accountId?: string | null; runId: string }) {
  const [report, setReport] = useState<ParityReport | null>(null); const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    setReport(null); setError(null);
    if (!accountId) return;
    const controller = new AbortController();
    const query = new URLSearchParams({ accountId, runId });
    void apiGetJson<ParityReport>(`/api/execution/strategy-parity?${query}`, { signal: controller.signal, allowDevelopmentFallback: false })
      .then((next) => { if (!controller.signal.aborted) setReport(next); })
      .catch(() => { if (!controller.signal.aborted) setError("Account-bound comparison evidence is unavailable for this run."); });
    return () => controller.abort();
  }, [accountId, runId]);
  const money = (value?: number | null) => value == null ? "Unavailable" : formatCurrency(value);
  return <section className="my-4 space-y-2 rounded border border-border p-3 text-xs" aria-label="Backtest to paper comparison">
    <h4 className="font-semibold">Backtest → paper evidence</h4>
    {!accountId ? <p>Select the run's execution account to inspect comparison evidence.</p> : error ? <p role="status">{error}</p> : report ? <>
      <p>{report.status} · Input: {report.inputMode} · Parent: {report.baselineRunId ?? "Unavailable"}</p>
      <dl className="grid grid-cols-2 gap-1"><dt>Parent / observed net P&amp;L</dt><dd>{money(report.baselineNetPnl)} / {money(report.observedNetPnl)}</dd><dt>Parent / observed fills</dt><dd>{report.baselineFills ?? "Unknown"} / {report.observedFills ?? "Unknown"}</dd></dl>
      <ul>{report.checks.map((check) => <li key={check.code}><strong>{check.status}: </strong>{check.message}</li>)}</ul>
      <Link className="underline" to={report.journalRoute}>Inspect run orders, fills and alert evidence</Link>
    </> : <p role="status">Loading retained comparison evidence…</p>}
    <p className="text-muted-foreground">Agreement is evidence about retained inputs and results. Promotion and live routing retain their separate approval gates.</p>
  </section>;
}
