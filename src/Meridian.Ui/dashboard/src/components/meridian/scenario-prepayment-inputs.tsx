import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { DECIMAL_LITERAL } from "@/lib/bond-decimal-format";
import { PREPAYMENT_METHODS, type CashFlowPrepaymentAssumption, type PrepaymentMethod, type RecastPolicy } from "@/types/contractual-cash-flow.types";

export interface ScenarioPrepaymentDraft {
  id: string;
  streamId: string;
  startDate: string;
  endDate: string;
  method: PrepaymentMethod;
  value: string;
  seasoningOrigin: string;
  policy: RecastPolicy;
}

export function newPrepaymentDraft(): ScenarioPrepaymentDraft {
  return { id: crypto.randomUUID(), streamId: "", startDate: "", endDate: "", method: "Cpr", value: "", seasoningOrigin: "", policy: "PreservePayments" };
}

/** Validate input shape without rounding or calculating any economic amounts. */
export function buildScenarioPrepayments(rows: ScenarioPrepaymentDraft[], valuationDate: string): CashFlowPrepaymentAssumption[] | string {
  const assumptions: CashFlowPrepaymentAssumption[] = [];
  for (const [index, row] of rows.entries()) {
    const prefix = `Prepayment ${index + 1}`;
    const value = row.value.trim();
    if (!row.streamId.trim() || !row.startDate || !row.endDate || !DECIMAL_LITERAL.test(value))
      return `${prefix} needs a stream, a [start, end) interval and a decimal value.`;
    if (row.startDate < valuationDate || row.endDate <= row.startDate)
      return `${prefix} must start on or after the valuation date and end after its start.`;
    if (value.startsWith("-") && /[1-9]/.test(value)) return `${prefix} value cannot be negative.`;
    if ((row.method === "Cpr" || row.method === "Smm") && !/^(?:0+(?:\.\d*)?|\.\d+|0*1(?:\.0*)?)$/.test(value.replace(/^-/, "")))
      return `${prefix} CPR/SMM must be an annual/monthly fraction between 0 and 1.`;
    if (row.method === "Psa" && (!row.seasoningOrigin || row.seasoningOrigin > row.startDate))
      return `${prefix} PSA needs a seasoning origin on or before its start date.`;
    const next: CashFlowPrepaymentAssumption = {
      streamId: row.streamId.trim(), startDate: row.startDate, endDate: row.endDate,
      method: row.method, value, policy: row.policy,
      ...(row.method === "Psa" ? { seasoningOrigin: row.seasoningOrigin } : {})
    };
    if (assumptions.some((prior) => prior.streamId === next.streamId && next.startDate < prior.endDate && prior.startDate < next.endDate))
      return `${prefix} overlaps another prepayment interval for ${next.streamId}. Use nonoverlapping [start, end) intervals.`;
    assumptions.push(next);
  }
  return assumptions;
}

export function ScenarioPrepaymentInputs({ rows, onChange }: { rows: ScenarioPrepaymentDraft[]; onChange: (rows: ScenarioPrepaymentDraft[]) => void }) {
  return <fieldset className="grid gap-3 md:col-span-4">
    <legend className="font-medium text-muted-foreground">Prepayment assumptions</legend>
    <p className="text-muted-foreground">Intervals are [start, end) and cannot overlap within a stream. CPR/SMM use fractions (0.06 = 6%); PSA uses a multiplier (1 = 100% PSA). Amount pays on the start date in the contract currency.</p>
    {rows.length === 0 ? <p className="text-muted-foreground">No voluntary prepayment assumptions.</p> : null}
    {rows.map((row, index) => {
      const update = (patch: Partial<ScenarioPrepaymentDraft>) => onChange(rows.map((candidate) => candidate.id === row.id ? { ...candidate, ...patch } : candidate));
      const label = `Prepayment ${index + 1}`;
      return <fieldset key={row.id} className="grid gap-2 rounded border border-border/60 p-3 md:grid-cols-4">
        <legend className="font-medium">{label}</legend>
        <label className="grid gap-1">Stream id<Input aria-label={`${label} stream id`} value={row.streamId} onChange={(e) => update({ streamId: e.target.value })} /></label>
        <label className="grid gap-1">Start date (inclusive)<Input type="date" aria-label={`${label} start date`} value={row.startDate} onChange={(e) => update({ startDate: e.target.value })} /></label>
        <label className="grid gap-1">End date (exclusive)<Input type="date" aria-label={`${label} end date`} value={row.endDate} onChange={(e) => update({ endDate: e.target.value })} /></label>
        <label className="grid gap-1">Method<Select aria-label={`${label} method`} value={row.method} onChange={(e) => update({ method: e.target.value as PrepaymentMethod })}>
          {PREPAYMENT_METHODS.map((method) => <option key={method} value={method}>{method.toUpperCase()}</option>)}
        </Select></label>
        <label className="grid gap-1">Value<Input inputMode="decimal" aria-label={`${label} value`} value={row.value} onChange={(e) => update({ value: e.target.value })} /></label>
        <label className="grid gap-1">Recast policy<Select aria-label={`${label} recast policy`} value={row.policy} onChange={(e) => update({ policy: e.target.value as RecastPolicy })}>
          <option value="PreservePayments">Preserve payments</option><option value="Recast">Recast</option>
        </Select></label>
        {row.method === "Psa" ? <label className="grid gap-1">PSA seasoning origin<Input type="date" aria-label={`${label} seasoning origin`} value={row.seasoningOrigin} onChange={(e) => update({ seasoningOrigin: e.target.value })} /></label> : null}
        <Button type="button" size="sm" variant="outline" onClick={() => onChange(rows.filter((candidate) => candidate.id !== row.id))}>Remove prepayment {index + 1}</Button>
      </fieldset>;
    })}
    <Button type="button" size="sm" variant="outline" className="justify-self-start" onClick={() => onChange([...rows, newPrepaymentDraft()])}>Add prepayment assumption</Button>
  </fieldset>;
}
