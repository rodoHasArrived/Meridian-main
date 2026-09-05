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
  return <section aria-label="Valuation mark impact preview" className="panel-surface space-y-3 p-4">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div><h3 className="font-semibold">Preview valuation mark controls</h3>
        <p className="text-sm text-muted-foreground">Review affected positions before configuring or running the current valuation schedule.</p></div>
      <Button disabled={preview.busy} onClick={() => void preview.run()}>{preview.busy ? "Assessing marks…" : "Preview mark impact"}</Button>
    </div>
    <div role="status" aria-live="polite">
      {preview.error ?? (preview.result
        ? `${preview.result.blockedPositionCount} of ${preview.result.assessedPositionCount} positions require review; ${preview.result.affectedValuationCount} valuation(s) affected. Policy ${preview.result.policyVersion}.`
        : "No current impact preview. A refresh is required when the schedule, scope, or policy changes.")}
    </div>
    {preview.result && <>
      <p className="text-xs text-muted-foreground">Assessed {preview.result.evaluatedAtUtc}. Approval rechecks current evidence.</p>
      <div className="overflow-x-auto"><table className="w-full text-sm" aria-label="Valuation mark preview positions">
        <thead><tr><th className="text-left">Position</th><th className="text-left">Mark readiness</th><th className="text-left">Reason</th></tr></thead>
        <tbody>{preview.result.positions.map((position, index) => <tr key={`${position.financialAccountId}-${position.securityId}-${position.symbol}-${index}`}>
          <td className="py-2">{position.symbol}<div className="text-xs text-muted-foreground">{position.financialAccountId ?? "Account unavailable"}</div></td>
          <td><MarkFreshnessCell mark={presentMarkFreshness(position)} /></td><td>{position.blockReason ?? "Current"}</td>
        </tr>)}</tbody>
      </table></div>
    </>}
  </section>;
}
