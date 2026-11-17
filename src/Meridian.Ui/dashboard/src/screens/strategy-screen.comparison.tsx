// Strategy run comparison UI (UX Phase E): the selection tray and the overlaid equity and drawdown
// charts for up to four runs. Split out of strategy-screen.tsx as it was touched.
import { X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { EquityCurve } from "@/components/charts/EquityCurve";
import { formatNumber } from "@/lib/format";
import { RUN_COMPARE_MAX, type RunComparisonOverlay } from "@/screens/strategy-screen.comparison.view-model";
import type { StrategyRunRecord } from "@/types";

const SERIES_COLORS = ["var(--chart-series-1)", "var(--chart-series-2)", "var(--chart-series-3)", "var(--chart-series-4)"] as const;

/**
 * The runs selected for comparison, each removable, with the limit stated. A run past the limit is
 * refused with the message from the view model; nothing is dropped silently.
 */
export function RunCompareTray({ selectedRuns, notice, onRemove, onClear }: {
  selectedRuns: readonly StrategyRunRecord[];
  notice: string | null;
  onRemove: (runId: string) => void;
  onClear: () => void;
}) {
  if (selectedRuns.length === 0 && !notice) return null;
  return (
    <section aria-label="Runs selected for comparison" className="space-y-1.5" data-compare-tray>
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-xs font-medium text-muted-foreground">{selectedRuns.length} of {RUN_COMPARE_MAX} selected</span>
        <ul className="flex flex-wrap gap-1.5" aria-label="Selected runs">
          {selectedRuns.map((run, index) => (
            <li key={run.id} className="inline-flex items-center gap-1 rounded-full border border-border/70 bg-card py-0.5 pl-2 pr-0.5 text-xs">
              <span aria-hidden="true" className="size-2 rounded-full" style={{ background: SERIES_COLORS[index % SERIES_COLORS.length] }} />
              <span>{run.strategyName}</span>
              <Button type="button" size="sm" variant="ghost" className="h-6 w-6 p-0" aria-label={`Remove ${run.strategyName} from the comparison`} onClick={() => onRemove(run.id)}>
                <X className="size-3.5" aria-hidden="true" />
              </Button>
            </li>
          ))}
        </ul>
        {selectedRuns.length > 0 ? <Button type="button" size="sm" variant="ghost" onClick={onClear}>Clear selection</Button> : null}
      </div>
      {notice ? <p role="status" className="text-xs text-warning">{notice}</p> : null}
    </section>
  );
}

const index100 = (value: number) => formatNumber(value, { maximumFractionDigits: 0 });
const percent = (value: number) => `${formatNumber(value, { minimumFractionDigits: 1, maximumFractionDigits: 1 })}%`;

/** Overlaid equity (rebased to 100) and drawdown for the compared runs, each with a data table. */
export function RunComparisonOverlayCharts({ overlay, loading }: {
  overlay: RunComparisonOverlay | { unavailable: string; omitted: string[] } | null;
  loading: boolean;
}) {
  if (loading) return <p role="status" className="text-sm text-muted-foreground">Loading equity curves for the compared runs…</p>;
  if (!overlay) return null;
  if ("unavailable" in overlay) {
    return (
      <div className="space-y-1 text-sm text-muted-foreground" role="status">
        <p>{overlay.unavailable}</p>
        {overlay.omitted.length > 0 ? <p className="text-xs">Not drawn: {overlay.omitted.join("; ")}.</p> : null}
      </div>
    );
  }
  const series = (entries: RunComparisonOverlay["equity"]) => entries.map((entry, index) => ({ label: entry.label, color: SERIES_COLORS[index % SERIES_COLORS.length]!, points: entry.points, area: false }));
  return (
    <section aria-label="Compared run curves" className="space-y-3" data-chart-table="strategy-run-overlay">
      <p className="text-xs text-muted-foreground">
        Shown over the dates every drawn run shares, {overlay.window.start} to {overlay.window.end}. Equity is rebased to 100 at the start;
        drawdown is measured from each run's peak within these dates.
        {overlay.omitted.length > 0 ? ` Not drawn: ${overlay.omitted.join("; ")}.` : ""}
      </p>
      <div className="grid gap-4 xl:grid-cols-2">
        <figure className="min-w-0 space-y-1">
          <figcaption className="text-sm font-semibold">Equity, rebased to 100</figcaption>
          <div className="h-64">
            <EquityCurve series={series(overlay.equity)} labels={overlay.labels} fill={false} valueFmt={index100}
              ariaLabel="Equity of the compared runs, rebased to 100" accessibleData={overlay.equityTable} />
          </div>
        </figure>
        <figure className="min-w-0 space-y-1">
          <figcaption className="text-sm font-semibold">Drawdown from peak</figcaption>
          <div className="h-64">
            <EquityCurve series={series(overlay.drawdown)} labels={overlay.labels} fill={false} valueFmt={percent}
              ariaLabel="Drawdown of the compared runs, percent from peak" accessibleData={overlay.drawdownTable} />
          </div>
        </figure>
      </div>
    </section>
  );
}
