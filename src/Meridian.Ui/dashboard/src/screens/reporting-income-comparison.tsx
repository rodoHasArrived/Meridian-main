import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { useLocation, useNavigationType, useSearchParams } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { Sheet, SheetBody, SheetCloseButton, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ReportWriterGridDiffView } from "@/components/meridian/report-writer-grid-diff-view";
import { createIncomeComparison, getIncomeComparison, getIncomeComparisonCandidates, getIncomeContributionSupport } from "@/lib/reporting-income-comparison-api";
import type { IncomeComparison, IncomeComparisonRun, IncomeContributionSupport } from "@/types/reporting-income-comparison";

const cellClass = "px-3 py-2 text-left align-top";

/** Server-owned explanation; the URL retains selection and drill-through context. */
export function ReportingIncomeComparisonPanel({ currentRunId = "" }: { currentRunId?: string }) {
  const [query, setQuery] = useSearchParams();
  const location = useLocation();
  const navigationType = useNavigationType();
  const queryUpdates = useRef({ params: query, pending: new Set<string>() });
  useLayoutEffect(() => {
    const updates = queryUpdates.current;
    const committed = query.toString();
    // Adopt external/history navigation, but an intermediate commit must not replace
    // later selections already submitted while React Router was rendering.
    if (navigationType === "POP" || !updates.pending.has(committed) || committed === updates.params.toString()) {
      updates.params = query;
      updates.pending.clear();
    }
  }, [query, location.key, navigationType]);

  function updateQuery(change: (next: URLSearchParams) => void) {
    // setSearchParams functional callbacks are not queued like React state updates.
    const next = new URLSearchParams(queryUpdates.current.params);
    change(next);
    queryUpdates.current.params = next;
    queryUpdates.current.pending.add(next.toString());
    setQuery(next);
  }

  const baselineId = query.get("incomeBaseline") ?? "";
  const currentId = query.get("incomeCurrent") ?? currentRunId;
  const gridId = query.get("incomeGrid") ?? "";
  const metric = query.get("incomeMetric") ?? "";
  const comparisonId = query.get("incomeComparison") ?? "";
  const contributionId = query.get("incomeContribution") ?? "";
  const [candidates, setCandidates] = useState<IncomeComparisonRun[]>([]);
  const [candidatePhase, setCandidatePhase] = useState("loading");
  const [refresh, setRefresh] = useState(0);
  const [comparison, setComparison] = useState<IncomeComparison | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const mutation = useRef<AbortController | null>(null);

  useLayoutEffect(() => {
    // History can change only the saved comparison identity while keeping the pair.
    // A late retention response must not navigate over that operator choice.
    if (mutation.current) {
      mutation.current.abort();
      mutation.current = null;
      setBusy(false);
    }
  }, [location.key]);

  useEffect(() => {
    mutation.current?.abort();
    setBusy(false);
    return () => mutation.current?.abort();
  }, [baselineId, currentId, gridId, metric]);

  useEffect(() => {
    const controller = new AbortController();
    setCandidatePhase("loading");
    getIncomeComparisonCandidates({ signal: controller.signal }).then((runs) => {
      if (!controller.signal.aborted) { setCandidates(runs); setCandidatePhase("ready"); }
    }).catch(() => {
      if (!controller.signal.aborted) { setCandidates([]); setCandidatePhase("error"); }
    });
    return () => controller.abort();
  }, [refresh, currentRunId]);

  useEffect(() => {
    const controller = new AbortController();
    setComparison(null);
    setError(null);
    if (comparisonId) {
      setBusy(true);
      getIncomeComparison(comparisonId, { signal: controller.signal }).then((saved) => {
        if (controller.signal.aborted) return;
        if (saved.comparisonId !== comparisonId) throw new Error("Comparison identity mismatch");
        setComparison(saved);
      }).catch(() => {
        if (!controller.signal.aborted) setError("The retained comparison is unavailable or access is denied. Retry without recalculating it.");
      }).finally(() => { if (!controller.signal.aborted) setBusy(false); });
    } else { setBusy(false); }
    return () => controller.abort();
  }, [comparisonId, refresh, currentRunId]);

  useEffect(() => () => mutation.current?.abort(), [currentRunId]);

  const selectedCurrent = candidates.find((run) => run.runId === currentId);
  const grids = selectedCurrent?.grids ?? [];
  const metrics = grids.find((grid) => grid.gridId === gridId)?.metrics ?? [];
  const retainedComparison = comparison?.comparisonId === comparisonId ? comparison : null;
  const canCompare = !busy && candidatePhase === "ready" && baselineId !== currentId
    && candidates.some((run) => run.runId === baselineId) && !!selectedCurrent
    && metrics.some((item) => item.column === metric);

  function changeSelection(key: string, value: string) {
    mutation.current?.abort();
    setBusy(false);
    setComparison(null);
    setError(null);
    updateQuery((next) => {
      next.set(key, value);
      next.delete("incomeComparison");
      next.delete("incomeContribution");
      if (key === "incomeCurrent") { next.delete("incomeGrid"); next.delete("incomeMetric"); }
      if (key === "incomeGrid") next.delete("incomeMetric");
    });
  }

  async function compare() {
    if (!canCompare) return;
    mutation.current?.abort();
    const controller = new AbortController();
    mutation.current = controller;
    setBusy(true);
    setError(null);
    try {
      const result = await createIncomeComparison({ baselineRunId: baselineId, currentRunId: currentId, gridId, metricColumn: metric }, { signal: controller.signal });
      if (controller.signal.aborted) return;
      if (result.baseline.runId !== baselineId || result.current.runId !== currentId || result.gridId !== gridId || result.metricColumn !== metric) {
        throw new Error("Comparison inputs do not match the requested pair");
      }
      updateQuery((next) => {
        next.set("incomeComparison", result.comparisonId);
        next.delete("incomeContribution");
      });
    } catch {
      if (!controller.signal.aborted) setError("The comparison could not be confirmed. Check access and retained inputs, then retry. No retained explanation was confirmed.");
    } finally {
      if (mutation.current === controller) mutation.current = null;
      if (!controller.signal.aborted) setBusy(false);
    }
  }

  function selectContribution(id: string) {
    updateQuery((next) => {
      if (id) next.set("incomeContribution", id); else next.delete("incomeContribution");
    });
  }

  return (
    <Card className="panel-surface" id="income-comparison">
      <CardHeader>
        <div className="eyebrow-label">Retained report comparison</div>
        <CardTitle>Explain investment-income movement</CardTitle>
        <CardDescription>Choose the baseline explicitly, including originally published or restated results. Movement is current minus baseline; inputs and supporting records are retained with the explanation.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        {candidatePhase === "loading" ? <p role="status">Loading retained runs…</p> : null}
        {candidatePhase === "error" ? <StatusBanner tone="warning" title="Retained run selection unavailable" detail="Run discovery failed or access is denied. No live data or sample results are substituted." /> : null}
        {candidatePhase === "ready" && candidates.length < 2 ? <StatusBanner tone="info" title="Two retained runs required" detail="Generate and retain investment-income report runs before comparing them." /> : null}
        <div className="grid gap-3 lg:grid-cols-2">
          <label className="space-y-1 text-sm font-medium">Baseline run
            <Select aria-label="Baseline run" value={baselineId} onChange={(event) => changeSelection("incomeBaseline", event.target.value)} disabled={candidatePhase !== "ready"}>
              <option value="">Choose a baseline — no automatic selection</option>
              {candidates.map((run) => <option key={run.runId} value={run.runId} disabled={run.runId === currentId}>{runLabel(run)}</option>)}
            </Select>
          </label>
          <label className="space-y-1 text-sm font-medium">Current run
            <Select aria-label="Current run" value={currentId} onChange={(event) => changeSelection("incomeCurrent", event.target.value)} disabled={candidatePhase !== "ready"}>
              <option value="">Choose the current run</option>
              {candidates.map((run) => <option key={run.runId} value={run.runId}>{runLabel(run)}</option>)}
            </Select>
          </label>
          <label className="space-y-1 text-sm font-medium">Report grid
            <Select aria-label="Report grid" value={gridId} onChange={(event) => changeSelection("incomeGrid", event.target.value)} disabled={!selectedCurrent}>
              <option value="">Choose a retained grid</option>
              {grids.map((grid) => <option key={grid.gridId} value={grid.gridId}>{grid.title}</option>)}
            </Select>
          </label>
          <label className="space-y-1 text-sm font-medium">Income measure
            <Select aria-label="Income measure" value={metric} onChange={(event) => changeSelection("incomeMetric", event.target.value)} disabled={!metrics.length}>
              <option value="">Choose an income measure</option>
              {metrics.map((item) => <option key={item.column} value={item.column}>{item.label}</option>)}
            </Select>
          </label>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button type="button" onClick={compare} disabled={!canCompare} busy={busy} busyLabel="Loading comparison…">Compare and retain explanation</Button>
          <Button type="button" variant="outline" onClick={() => setRefresh((value) => value + 1)} disabled={busy}>Retry / refresh retained records</Button>
        </div>
        {baselineId && baselineId === currentId ? <p role="status" className="text-sm text-warning">Choose two different retained runs.</p> : null}
        {error ? <StatusBanner role="alert" tone="danger" title="Comparison unavailable" detail={error} /> : null}
        {retainedComparison ? <ComparisonExplanation comparison={retainedComparison} onSupport={selectContribution} /> : null}
        {retainedComparison && contributionId ? <ContributionSupport key={`${comparisonId}:${contributionId}`} comparison={retainedComparison} contributionId={contributionId} onClose={() => selectContribution("")} /> : null}
      </CardContent>
    </Card>
  );
}

function runLabel(run: IncomeComparisonRun) {
  return `${run.publicationLabel} · ${run.periodId} · ${run.currency} · ${run.runId}`;
}

function isZeroAmount(value: string | null) {
  return value !== null && /^-?0(?:\.0+)?$/.test(value);
}

function amount(value: string | null, currency: string) {
  if (value === null) return "Not comparable";
  const negative = value.startsWith("-");
  const [integer, retainedFraction = ""] = (negative ? value.slice(1) : value).split(".");
  const groupedInteger = integer.replace(/\B(?=(\d{3})+(?!\d))/g, ",");
  // Group and pad decimal text directly: every significant retained digit remains visible.
  const fraction = retainedFraction.replace(/0+$/, "").padEnd(2, "0");
  const sign = negative && !isZeroAmount(value) ? "-" : "";
  return `${sign}${groupedInteger}.${fraction} ${currency}`;
}

function ComparisonExplanation({ comparison: c, onSupport }: { comparison: IncomeComparison; onSupport: (id: string) => void }) {
  const reconciled = c.compatible && c.status === "Reconciled" && isZeroAmount(c.residualAmount);
  const currency = c.baseline.currency === c.current.currency ? c.current.currency : "(different currencies)";
  return (
    <section aria-label="Retained income explanation" className="space-y-4">
      <StatusBanner role="status" tone={!c.compatible ? "danger" : reconciled ? "success" : "warning"}
        title={<span className="text-foreground">{!c.compatible ? "Incompatible comparison — not reconciled" : reconciled ? "Movement reconciled to retained evidence" : "Unexplained differences remain"}</span>}
        detail={`${runLabel(c.baseline)} → ${runLabel(c.current)}. ${c.metricColumn} · ${c.gridId}`} />
      <div className="overflow-x-auto">
        <table className="w-full text-sm" aria-label="Comparison dimensions">
          <thead><tr><th scope="col" className={cellClass}>Dimension</th><th scope="col" className={cellClass}>Baseline</th><th scope="col" className={cellClass}>Current</th><th scope="col" className={cellClass}>Assessment</th></tr></thead>
          <tbody>{c.differences.map((difference) => <tr className="border-t border-border" key={difference.dimension}>
            <th scope="row" className={cellClass}>{difference.dimension}</th><td className={cellClass}>{difference.baseline}</td><td className={cellClass}>{difference.current}</td>
            <td className={cellClass}>{difference.compatible ? "Comparable" : "Incompatible"}. {difference.detail}</td>
          </tr>)}</tbody>
        </table>
      </div>
      <dl className="grid gap-3 rounded-sm border border-border p-3 lg:grid-cols-3">
        <AmountFact label="Baseline income" value={amount(c.baselineAmount, c.baseline.currency)} />
        <AmountFact label="Current income" value={amount(c.currentAmount, c.current.currency)} />
        <AmountFact label="Total movement" value={amount(c.movement, currency)} />
        <AmountFact label="Supported contributions" value={amount(c.explainedAmount, currency)} />
        <AmountFact label="Unexplained residual" value={amount(c.residualAmount, currency)} />
        <div><dt className="text-xs text-muted-foreground">Retained on</dt><dd className="mt-1 break-all font-mono text-sm">{c.retainedAtUtc}</dd></div>
      </dl>
      {c.warnings.length ? <ul aria-label="Comparison warnings" className="space-y-1 text-sm text-warning">{c.warnings.map((warning, index) => <li key={index}>{warning}</li>)}</ul> : null}
      {["Journal", "Population", "Methodology", "Unexplained"].map((kind) => {
        const contributions = c.contributions.filter((item) => item.kind === kind);
        return <div key={kind} className="space-y-2">
          <h3 className="text-sm font-semibold">{kind === "Journal" ? "Retained journal contributions" : kind === "Population" ? "Population changes" : kind === "Methodology" ? "Documented methodology changes" : "Unexplained retained movements"}</h3>
          {contributions.length ? <ul className="divide-y divide-border rounded-sm border border-border">{contributions.map((item) => <li key={item.contributionId} className="flex flex-wrap items-start justify-between gap-3 px-3 py-2">
            <div className="min-w-0 flex-1"><p className="text-sm font-medium">{item.label}</p><p className="mt-1 text-xs text-muted-foreground">{item.detail}</p><p className="mt-1 break-all font-mono text-xs">{item.recordId}</p></div>
            <span className="font-mono text-sm">{amount(item.amount, currency)}</span>
            <Button type="button" size="sm" variant="outline" onClick={() => onSupport(item.contributionId)} aria-label={`Open retained support for ${item.label}`}>Open supporting record</Button>
          </li>)}</ul> : <p className="text-sm text-muted-foreground">{kind === "Unexplained" ? "No individual unexplained lines identified. Review the residual and warnings for gaps in retained evidence." : `No supported ${kind.toLowerCase()} contributions retained.`}</p>}
        </div>;
      })}
      <p className="text-sm text-muted-foreground">The residual remains visible even when no supporting record exists. A zero net residual does not resolve offsetting unsupported differences or incompatible inputs.</p>
      <TechnicalDetails label="Retained grid differences"><ReportWriterGridDiffView diff={c.gridDiff} maxRows={c.gridDiff.rows.length} /></TechnicalDetails>
      <TechnicalDetails label="Reproduction details">
        <p className="break-all font-mono text-xs">Comparison: {c.comparisonId}<br />Explanation version: {c.explanationVersion}<br />Baseline: {c.baseline.runId}<br />Current: {c.current.runId}</p>
        <p className="mt-2 text-xs text-muted-foreground">This saved URL reopens the retained inputs, explanation, and evidence. Later source changes do not recalculate this comparison.</p>
      </TechnicalDetails>
    </section>
  );
}

function AmountFact({ label, value }: { label: string; value: string }) {
  return <div><dt className="text-xs text-muted-foreground">{label}</dt><dd className="mt-1 font-mono text-sm font-semibold">{value}</dd></div>;
}

function ContributionSupport({ comparison, contributionId, onClose }: { comparison: IncomeComparison; contributionId: string; onClose: () => void }) {
  const [support, setSupport] = useState<IncomeContributionSupport | null>(null);
  const [failed, setFailed] = useState(false);
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    setSupport(null);
    setFailed(false);
    getIncomeContributionSupport(comparison.comparisonId, contributionId, { signal: controller.signal }).then((result) => {
      if (controller.signal.aborted) return;
      if (result.comparisonId !== comparison.comparisonId || result.baselineRunId !== comparison.baseline.runId || result.currentRunId !== comparison.current.runId || result.contribution.contributionId !== contributionId) throw new Error("Retained support context mismatch");
      setSupport(result);
    }).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [comparison, contributionId, retry]);
  return <Sheet open onOpenChange={(open) => { if (!open) onClose(); }}>
    <SheetContent aria-labelledby="income-support-title" aria-describedby="income-support-context" className="w-full max-w-2xl">
      <SheetHeader>
        <SheetTitle id="income-support-title">Retained supporting record</SheetTitle>
        <SheetDescription id="income-support-context">{comparison.baseline.runId} → {comparison.current.runId}. Closing returns to this comparison.</SheetDescription>
        <SheetCloseButton onClick={onClose} />
      </SheetHeader>
      <SheetBody className="space-y-4">
        {!support && !failed ? <p role="status">Loading retained support…</p> : null}
        {failed ? <><StatusBanner role="alert" tone="warning" title="Supporting record unavailable" detail="The retained record could not be loaded or does not match this comparison. No live record is substituted." /><Button variant="outline" onClick={() => setRetry((value) => value + 1)}>Retry supporting record</Button></> : null}
        {support ? <>
          <h3 className="font-semibold">{support.contribution.label}</h3>
          <p className="text-sm">{support.contribution.detail}</p>
          <p className="break-all font-mono text-sm">Record: {support.contribution.recordId}</p>
          <RetainedRecords title="Baseline records" records={support.baselineRecords} />
          <RetainedRecords title="Current records" records={support.currentRecords} />
          <h3 className="text-sm font-semibold">Evidence references</h3>
          {support.evidenceReferences.length ? <ul className="space-y-1 break-all font-mono text-xs">{support.evidenceReferences.map((reference, index) => <li key={index}>{reference}</li>)}</ul> : <p className="text-sm text-muted-foreground">No additional references retained.</p>}
        </> : null}
        <Button variant="outline" onClick={onClose}>Return to comparison</Button>
      </SheetBody>
    </SheetContent>
  </Sheet>;
}

function RetainedRecords({ title, records }: { title: string; records: Record<string, string>[] }) {
  return <section className="space-y-2"><h3 className="text-sm font-semibold">{title}</h3>
    {records.length ? records.map((record, index) => <dl key={index} className="grid grid-cols-[minmax(0,1fr)_minmax(0,2fr)] gap-x-3 gap-y-1 rounded-sm border border-border p-3 text-xs">
      {Object.entries(record).map(([key, value]) => <div key={key} className="contents"><dt className="break-all text-muted-foreground">{key}</dt><dd className="break-all font-mono">{value}</dd></div>)}
    </dl>) : <p className="text-sm text-muted-foreground">No records on this side.</p>}
  </section>;
}
