import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import { buildChartViz } from "@/lib/report-writer-grid-format";
import type { ReportWriterChartRender } from "@/types";

export interface ReportWriterChartPreviewProps {
  chart: ReportWriterChartRender;
  className?: string;
}

const SERIES_COLORS = [
  "bg-primary/70",
  "bg-success/70",
  "bg-warning/70",
  "bg-danger/70"
];

/**
 * Dependency-free inline chart preview for a rendered report-writer grid. Shows each
 * category as a row of magnitude bars (one per series), sized by {@link buildChartViz}.
 * Keeps the report's chart visible alongside its table without pulling in a chart library.
 */
export function ReportWriterChartPreview({ chart, className }: ReportWriterChartPreviewProps) {
  const viz = buildChartViz(chart);
  if (viz.categories.length === 0 || chart.series.length === 0) {
    return null;
  }

  return (
    <div
      className={cn("mt-2 rounded-sm border border-border/60 bg-secondary/25 px-2 py-2 text-xs", className)}
      aria-label={`${chart.title} chart preview`}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="eyebrow-label">Chart · {chart.title}</div>
        <Badge variant="outline">{chart.type}</Badge>
      </div>
      <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1">
        {chart.series.map((series, index) => (
          <span key={series.key} className="flex items-center gap-1 text-[10px] text-muted-foreground">
            <span className={cn("inline-block h-2 w-2 rounded-sm", SERIES_COLORS[index % SERIES_COLORS.length])} aria-hidden="true" />
            {series.label || series.key}
          </span>
        ))}
      </div>
      <ul className="mt-2 space-y-1.5">
        {viz.categories.map((category) => (
          <li key={category.category} className="grid grid-cols-[6rem_1fr] items-center gap-2">
            <span className="truncate text-[11px] text-muted-foreground" title={category.category}>
              {category.category}
            </span>
            <span className="space-y-1">
              {category.bars.map((bar, index) => (
                <span key={bar.seriesKey} className="flex items-center gap-2">
                  <span className="h-2 flex-1 overflow-hidden rounded-sm bg-background/60">
                    <span
                      className={cn(
                        "block h-full rounded-sm",
                        SERIES_COLORS[index % SERIES_COLORS.length],
                        bar.isNegative && "opacity-50"
                      )}
                      style={{ width: `${bar.widthPct}%` }}
                    />
                  </span>
                  <span className="w-16 text-right font-mono text-[10px] text-foreground" title={String(bar.value)}>
                    {bar.value}
                  </span>
                </span>
              ))}
            </span>
          </li>
        ))}
      </ul>
      {chart.warnings.length > 0 ? (
        <p className="mt-2 text-[10px] text-warning">{chart.warnings.join("; ")}</p>
      ) : null}
    </div>
  );
}
