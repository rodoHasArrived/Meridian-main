import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import { buildChartViz } from "@/lib/report-writer-grid-format";
import type { ReportWriterChartRender } from "@/types";

export interface ReportWriterChartPreviewProps {
  chart: ReportWriterChartRender;
  className?: string;
}

/** Signed observations alongside the retained table, explicitly labeled as a summary. */
export function ReportWriterChartPreview({ chart, className }: ReportWriterChartPreviewProps) {
  const viz = buildChartViz(chart);
  if (viz.categories.length === 0 || chart.series.length === 0) return null;

  return (
    <section
      className={cn("mt-2 rounded-sm border border-border/60 bg-secondary/25 px-2 py-2 text-xs", className)}
      aria-label={`${chart.title} signed value summary`}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="eyebrow-label">Signed value summary · {chart.title}</div>
        <Badge variant="outline">Configured chart: {chart.type}</Badge>
      </div>
      <p className="mt-1 text-xs text-muted-foreground">Summary only. Negative values extend left of zero; positive values extend right. Missing observations are unavailable.</p>
      <div className="mt-2 grid grid-cols-[6rem_minmax(0,1fr)] gap-2" aria-hidden="true">
        <span />
        <span className="grid grid-cols-[minmax(0,1fr)_6rem] gap-2">
          <span className="grid grid-cols-3 font-mono text-xs text-muted-foreground">
            <span className="truncate">{viz.maxAbs ? `−${viz.maxAbs}` : "0"}</span><span className="text-center">0</span><span className="truncate text-right">{viz.maxAbs ? `+${viz.maxAbs}` : "0"}</span>
          </span><span className="text-right text-xs text-muted-foreground">Reported value</span>
        </span>
      </div>
      <ul className="mt-1 space-y-2">
        {viz.categories.map((category, categoryIndex) => (
          <li key={`${categoryIndex}:${category.category}`} className="grid grid-cols-[6rem_minmax(0,1fr)] items-start gap-2">
            <span className="break-words text-xs text-muted-foreground">{category.category}</span>
            <span className="space-y-1">
              {category.bars.map((bar, seriesIndex) => (
                <span key={`${seriesIndex}:${bar.seriesKey}`} className="block" role="img" aria-label={`${category.category}, ${bar.seriesLabel}: ${bar.value === null ? "Unavailable" : bar.value}`}>
                  <span className="mb-0.5 block text-xs text-muted-foreground" aria-hidden="true">{bar.seriesLabel}</span>
                  <span className="grid grid-cols-[minmax(0,1fr)_6rem] items-center gap-2" aria-hidden="true">
                    <span className="relative block h-3 bg-background/60">
                      <span className="absolute inset-y-0 left-1/2 border-l border-foreground/60" />
                      {bar.value !== null && bar.value !== 0 ? <span
                        className="absolute inset-y-0.5 bg-primary"
                        style={{ left: `${bar.leftPct}%`, width: `${bar.widthPct}%` }}
                        data-summary-value={bar.value}
                      /> : null}
                    </span>
                    <span className="break-all text-right font-mono text-xs text-foreground">
                      {bar.value === null ? "Unavailable" : bar.value}
                    </span>
                  </span>
                </span>
              ))}
            </span>
          </li>
        ))}
      </ul>
      {chart.warnings.length > 0 ? <p className="mt-2 text-xs text-warning">{chart.warnings.join("; ")}</p> : null}
    </section>
  );
}
