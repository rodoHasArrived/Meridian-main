import { TableWorkbench } from "@/components/meridian/table-workbench";
import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { MarkFreshnessCell } from "@/components/meridian/mark-freshness-cell";
import { presentMarkFreshness } from "@/lib/mark-freshness";
import { previewValuationMarks } from "@/lib/api/mark-freshness.api";
import type { DailyValuationScheduleWorkItem, ValuationFreshnessPreviewDto } from "@/types";

export function useValuationMarkPreview(schedule: DailyValuationScheduleWorkItem | null) {
  const key = JSON.stringify(schedule);
  const controller = useRef<AbortController | null>(null);
  const sequence = useRef(0);
  const [state, setState] = useState<{ key: string; busy: boolean; result: ValuationFreshnessPreviewDto | null; error: string | null }>({ key: "", busy: false, result: null, error: null });
  useEffect(() => () => { sequence.current++; controller.current?.abort(); }, [key]);
  const current = state.key === key;
  const result = current ? state.result : null;
  const run = async () => {
    if (!schedule || (current && state.busy)) return;
    controller.current?.abort();
    const abort = new AbortController();
    controller.current = abort;
    const requestId = ++sequence.current;
    setState({ key, busy: true, result: null, error: null });
    try {
      const next = await previewValuationMarks(schedule, { signal: abort.signal });
      if (!abort.signal.aborted && sequence.current === requestId) setState({ key, busy: false, result: next, error: null });
    } catch (error) {
      if (!abort.signal.aborted && sequence.current === requestId) setState({ key, busy: false, result: null,
        error: error instanceof Error ? error.message : "Mark preview could not be loaded. Retry before configuring or running valuation." });
    }
  };
  return { result, busy: current && state.busy, error: current ? state.error : null, isCurrent: result !== null, run };
}

export function ValuationMarkPreviewPanel({ preview }: { preview: ReturnType<typeof useValuationMarkPreview> }) {
  const result = preview.result;
  return <section aria-label="Valuation mark impact preview" className="panel-surface space-y-3 p-4">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div><h3 className="font-semibold">Preview valuation mark controls</h3>
        <p className="text-sm text-muted-foreground">Review affected positions before configuring or running the current valuation schedule.</p></div>
      <Button disabled={preview.busy} onClick={() => void preview.run()}>{preview.busy ? "Assessing marks…" : "Preview mark impact"}</Button>
    </div>
    <div role="status" aria-live="polite">
      {preview.error ?? (result
        ? `${result.blockedPositionCount} of ${result.assessedPositionCount} positions require review; ${result.affectedValuationCount} valuation(s) affected. Policy ${result.policyVersion}.`
        : "No current impact preview. A refresh is required when the schedule, scope, or policy changes.")}
    </div>
    {result && <>
      <p className="text-xs text-muted-foreground">Assessed {result.evaluatedAtUtc}. Approval rechecks current evidence.</p>
      <div className="overflow-x-auto"><TableWorkbench<(typeof result.positions)[number]> workbenchId="accounting-mark-preview" sourceKey={JSON.stringify(result.positions)} rows={result.positions} columns={[{id:"field-0",label:"Position",exportValue:position=>position.symbol,render:position=><>{position.symbol}<div className="text-xs text-muted-foreground">{position.financialAccountId ?? "Account unavailable"}</div></>},
{id:"field-1",label:"Mark readiness",exportValue:position=>JSON.stringify(presentMarkFreshness(position)),render:position=><><MarkFreshnessCell mark={presentMarkFreshness(position)} /></>},
{id:"field-2",label:"Reason",exportValue:position=>position.blockReason,render:position=><>{position.blockReason ?? "Current"}</>}]} getRowId={position=>`mark-preview-${result.positions.indexOf(position)}`} getSearchText={position=>[position.symbol,JSON.stringify(presentMarkFreshness(position)),position.blockReason].join(" ")} completePopulation sourceDescription="Current valuation freshness preview; no missing mark is substituted by a synthetic observation." ariaLabel="Valuation mark preview positions" emptyText="No source records returned." /></div>
    </>}
  </section>;
}
