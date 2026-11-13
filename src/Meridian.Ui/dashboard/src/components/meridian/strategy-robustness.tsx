import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useLocation } from "react-router-dom";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { getRunSweeps } from "@/lib/api";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type { StrategySweepResultGroup } from "@/types/workstation-6";
import { buildRobustnessGrid, classifyRobustnessCell, fixedSlice, numericAxes, numericParameter, type RobustnessMetric } from "./strategy-robustness.model";

const groupKey = (group: StrategySweepResultGroup) => JSON.stringify([group.sweepId, group.sweepDefinitionHash,
  group.runs[0]?.strategyId, group.runs[0]?.engine, group.runs[0]?.mode,
  group.runs[0]?.datasetReference, group.runs[0]?.feedReference, group.objective]);
const metricLabels: Record<RobustnessMetric, string> = { totalReturn: "Total return (fraction; 0.05 = 5%)", netPnl: "Net P&L (reported units)", finalEquity: "Final equity (reported units)" };
const displayValue = (value: number | null, metric: RobustnessMetric) => value == null ? "Unavailable" : metric === "totalReturn" ? `${(Number(value) * 100).toFixed(2)}%` : String(value);

/** A retained experiment's parameter sensitivity; no interpolation, inferred performance or promotion authority. */
export function StrategyRobustness() {
  const { search } = useLocation();
  const revision = useRef(0);
  const invalidateRequests = useCallback(() => { revision.current++; }, []);
  const [groups, setGroups] = useState<StrategySweepResultGroup[]>([]);
  const [loading, setLoading] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [selectedGroup, setSelectedGroup] = useState("");
  const [x, setX] = useState("");
  const [y, setY] = useState("");
  const [slice, setSlice] = useState("");
  const [metric, setMetric] = useState<RobustnessMetric>("totalReturn");
  const [threshold, setThreshold] = useState("0");
  const [direction, setDirection] = useState<"min" | "max">("min");
  const [selection, setSelection] = useState<{ x: number; y: number } | null>(null);
  useEffect(() => {
    invalidateRequests();
    setGroups([]); setLoaded(false); setLoading(false); setError(null); setSelection(null);
    return invalidateRequests;
  }, [search, invalidateRequests]);
  const chooseGroup = (group: StrategySweepResultGroup | undefined) => {
    setSelectedGroup(group ? groupKey(group) : "");
    const axes = numericAxes(group?.runs ?? []);
    setX(axes[0] ?? ""); setY(axes[1] ?? ""); setSlice(""); setSelection(null);
  };
  const load = async () => {
    const requestRevision = ++revision.current;
    setLoading(true); setError(null); setGroups([]); setSelection(null); setLoaded(false);
    try {
      const results = await getRunSweeps(100);
      if (!Array.isArray(results)) throw new Error("Invalid experiment response.");
      if (revision.current !== requestRevision) return;
      setGroups(results); chooseGroup(results[0]); setLoaded(true);
    } catch (cause) {
      if (revision.current === requestRevision) setError(cause instanceof Error ? cause.message : "Retained experiments could not be loaded.");
    } finally { if (revision.current === requestRevision) setLoading(false); }
  };
  const group = groups.find(candidate => groupKey(candidate) === selectedGroup);
  const axes = numericAxes(group?.runs ?? []);
  const slices = [...new Set((group?.runs ?? []).map(run => fixedSlice(run, x, y)))];
  const activeSlice = slices.includes(slice) ? slice : slices[0] ?? "";
  const grid = buildRobustnessGrid(group?.runs ?? [], x, y, activeSlice);
  const numericThreshold = numericParameter(threshold);
  const selected = grid.cells.find(cell => cell.x === selection?.x && cell.y === selection?.y);
  const validAxes = x !== "" && y !== "" && x !== y;
  const tooLarge = grid.cellCount > 2500;
  return <Card>
    <CardHeader>
      <CardTitle>Strategy robustness landscape</CardTitle>
      <CardDescription>Compare retained parameter results across one experiment. This is in-sample sensitivity; out-of-sample robustness requires separate walk-forward evidence.</CardDescription>
    </CardHeader>
    <CardContent className="space-y-4">
      <Button onClick={() => void load()} disabled={loading}>{loading ? "Loading experiments…" : loaded ? "Refresh retained experiments" : "Load retained experiments"}</Button>
      {error ? <p role="alert">{error} Retry loading retained experiments.</p> : null}
      {loading ? <p role="status">Loading retained sweep evidence…</p> : null}
      {loaded && groups.length === 0 ? <p role="status">No retained sweep experiments are available in this scope. Run a parameter sweep before comparing its landscape.</p> : null}
      {group ? <>
        <div className="grid gap-3 xl:grid-cols-3">
          <label className="text-sm">Experiment<Select value={selectedGroup} onChange={event => chooseGroup(groups.find(candidate => groupKey(candidate) === event.target.value))}>{groups.map(candidate => <option key={groupKey(candidate)} value={groupKey(candidate)}>{candidate.sweepId} · {candidate.runs[0]?.strategyName} · {candidate.runs[0]?.mode} · {candidate.runs[0]?.feedReference ?? "Unspecified feed"} · {candidate.objective ?? "Unspecified objective"} · {candidate.runCount} runs</option>)}</Select></label>
          <label className="text-sm">X parameter<Select value={x} onChange={event => { setX(event.target.value); setSlice(""); setSelection(null); }}>{axes.map(axis => <option key={axis}>{axis}</option>)}</Select></label>
          <label className="text-sm">Y parameter<Select value={y} onChange={event => { setY(event.target.value); setSlice(""); setSelection(null); }}>{axes.map(axis => <option key={axis}>{axis}</option>)}</Select></label>
          <label className="text-sm">Metric<Select value={metric} onChange={event => setMetric(event.target.value as RobustnessMetric)}>{Object.entries(metricLabels).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</Select></label>
          <label className="text-sm">Acceptance direction<Select value={direction} onChange={event => setDirection(event.target.value as "min" | "max")}><option value="min">At least threshold</option><option value="max">At most threshold</option></Select></label>
          <label className="text-sm">Illustrative acceptance threshold<Input value={threshold} onChange={event => setThreshold(event.target.value)} inputMode="decimal" aria-invalid={numericThreshold == null} /></label>
        </div>
        {numericThreshold == null ? <p role="alert">Enter a finite threshold to classify acceptable cells.</p> : null}
        {slices.length > 1 ? <label className="block text-sm">Fixed remaining parameters<Select value={activeSlice} onChange={event => { setSlice(event.target.value); setSelection(null); }}>{slices.map(value => <option key={value} value={value}>{value}</option>)}</Select></label> : null}
        <p className="text-xs text-muted-foreground">{group.runs[0]?.strategyName} · {group.runs[0]?.engine} · Dataset: {group.runs[0]?.datasetReference ?? "Unspecified"} · Recorded objective: {group.objective ?? "Unspecified"}. Currency for monetary metrics must be checked in the retained run evidence.</p>
        <p className="text-sm">{group.comparisonContextVerified ? "Runs share a retained window, engine, universe and cost-configuration fingerprint. Dataset contents and strategy binaries are only as reproducible as their retained references." : "Legacy experiment: window, strategy version and cost comparability are unverified. Treat comparisons as exploratory."}</p>
        <p className="text-xs text-muted-foreground">{group.coverageDescription ?? "Expected grid was not retained; missing cells are inferred from observed parameter values."} {group.historyMayBeTruncated ? "History reached its retrieval limit; additional runs may be omitted." : "History may still omit parameter values absent from every retained run."}</p>
        {!validAxes ? <p role="status">Choose two distinct numeric parameters. At least two numeric parameter coordinates must be retained.</p> : tooLarge ? <p role="status">This slice exceeds 2,500 cells. Choose parameters with fewer observed values or another fixed-parameter slice.</p> : <div className="overflow-x-auto">
          <table className="w-full border-collapse text-xs" aria-label="Retained parameter sensitivity grid">
            <caption className="mb-2 text-left">Columns: {x}; rows: {y}. ✓ acceptable · × outside threshold · — missing · ? unavailable · repeated runs require inspection. Thresholds are exploratory and do not authorize promotion.</caption>
            <thead><tr><th scope="col" className="border border-border p-2">{y} / {x}</th>{grid.xs.map(value => <th key={value} scope="col" className="border border-border p-2 font-mono">{value}</th>)}</tr></thead>
            <tbody>{grid.ys.map(yValue => <tr key={yValue}><th scope="row" className="border border-border p-2 font-mono">{yValue}</th>{grid.cells.filter(cell => cell.y === yValue).map(cell => {
              const status = classifyRobustnessCell(cell.runs, metric, numericThreshold, direction);
              const symbol = status === "Acceptable" ? "✓" : status === "Outside threshold" ? "×" : status === "Missing" ? "—" : "?";
              const tint = status === "Acceptable" ? "bg-success/10" : status === "Outside threshold" ? "bg-danger/10" : "bg-secondary/40";
              return <td key={cell.x} className={`border border-border p-0 ${tint}`}><button type="button" className={`min-h-16 w-full min-w-24 p-2 text-left focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary ${selected === cell ? "ring-2 ring-inset ring-primary" : ""}`} aria-pressed={selected === cell} aria-label={`${x} ${cell.x}, ${y} ${cell.y}: ${status}${cell.runs.length > 1 ? `, ${cell.runs.length} runs` : ""}`} onClick={() => setSelection({ x: cell.x, y: cell.y })}><span className="block font-semibold">{symbol} {status}</span><span className="font-mono">{cell.runs.length === 1 ? displayValue(cell.runs[0][metric], metric) : `${cell.runs.length} retained runs`}</span></button></td>;
            })}</tr>)}</tbody>
          </table>
        </div>}
        {grid.unplaced.length > 0 ? <p role="status">{grid.unplaced.length} runs have missing or nonnumeric coordinates and are excluded from this grid.</p> : null}
        {selected ? <section aria-label="Selected parameter result" aria-live="polite" className="space-y-2 rounded-sm border border-border p-3"><h3 className="font-semibold">{x} = {selected.x}; {y} = {selected.y}</h3>{selected.runs.length === 0 ? <p>No retained result at this coordinate. No performance is inferred.</p> : selected.runs.map(run => <div key={run.runId} className="space-y-1 border-b border-border py-2 text-sm"><p className="font-mono">{run.runId} · {run.status}</p><p>Total return: {displayValue(run.totalReturn, "totalReturn")} · Economic net P&amp;L: {displayValue(run.netPnl, "netPnl")} · Final equity: {displayValue(run.finalEquity, "finalEquity")}</p><p>Retained parameters: {JSON.stringify(run.parameters)}</p><p>Last updated: {run.lastUpdatedAt}</p><Link className="underline" to={{ pathname: WORKSTATION_ROUTE_CATALOG.strategyRunLedger, search: (() => { const query = new URLSearchParams(search); query.set("runId", run.runId); return query.toString(); })() }}>Inspect retained run ledger</Link></div>)}</section> : <p className="text-sm text-muted-foreground">Select any cell to inspect its retained runs or missing evidence.</p>}
      </> : null}
    </CardContent>
  </Card>;
}
