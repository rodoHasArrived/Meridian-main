import { ChartEvidenceView, type ChartEvidence } from "@/components/charts/chart-evidence";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import { buildChartViz, type ChartBar } from "@/lib/report-writer-grid-format";
import type { ReportWriterChartRender, ReportWriterGridRender } from "@/types";

export interface ReportWriterChartPreviewProps {
  chart: ReportWriterChartRender;
  grid: ReportWriterGridRender;
  proofId: string;
  sourceDetail: string;
  className?: string;
}

/** Signed observations alongside the retained table, explicitly labeled as a summary. */
export function ReportWriterChartPreview({ chart, grid, proofId, sourceDetail, className }: ReportWriterChartPreviewProps) {
  const viz = buildChartViz(chart);
  if (viz.categories.length === 0 || chart.series.length === 0) {
    return null;
  }
  const evidence = viz.categories.flatMap(({ category, bars }, rowIndex) => bars.map((bar) => (
    reportChartEvidence(grid, bar, category, rowIndex, proofId, sourceDetail)
  )));

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
      <ChartEvidenceView label={chart.title} evidence={evidence}>
        {(inspection) => (
          <ul className="mt-1 max-h-80 space-y-2 overflow-y-auto p-1" aria-describedby={inspection.hintId}>
            {viz.categories.map((category, rowIndex) => (
              <li key={`${rowIndex}:${category.category}`} className="grid grid-cols-[6rem_minmax(0,1fr)] items-start gap-2">
                <span className="break-words text-xs text-muted-foreground">{category.category}</span>
                <span className="space-y-1">
                  {category.bars.map((bar, seriesIndex) => (
                    <button key={`${seriesIndex}:${bar.seriesKey}`} type="button" className="decision-chart-bar block w-full rounded-sm px-1 text-left aria-current:bg-secondary/60"
                      {...inspection.markProps(rowIndex * chart.series.length + seriesIndex)}>
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
                    </button>
                  ))}
                </span>
              </li>
            ))}
          </ul>
        )}
      </ChartEvidenceView>
      {chart.warnings.length > 0 ? <p className="mt-2 text-xs text-[var(--orange-dim)]">{chart.warnings.join("; ")}</p> : null}
    </section>
  );
}

function reportChartEvidence(grid: ReportWriterGridRender, bar: ChartBar, category: string, rowIndex: number, proofId: string, sourceDetail: string): ChartEvidence {
  const row = grid.rows[rowIndex];
  const key = bar.seriesKey.toLowerCase();
  const field = grid.dataDictionary?.find((item) => item.key.toLowerCase() === key);
  const metricKey = key.split(":").at(-1);
  const metric = grid.lineage?.metrics.find((item) => item.name.toLowerCase() === metricKey);
  const formula = grid.lineage?.formulas.find((item) => item.name.toLowerCase() === key);
  const contribution = grid.kind === "Contribution" && (key === "contributionpercent" || key === "contributionabspercent");
  const rowValues = new Map(Object.entries(row?.values ?? {}).map(([name, value]) => [name.toLowerCase(), value]));
  const asOf = ["asof", "asofdate", "asofutc", "timestamp", "valuationdate", "reportingdate", "periodend", "date"]
    .map((name) => rowValues.get(name))
    .find((value) => value && /^\d{4}-\d{2}-\d{2}(?:$|T)/.test(value) && Number.isFinite(Date.parse(value)));
  const unit = contribution ? "%" : metric?.function === "Count" && grid.kind !== "Detail" ? "Rows" : rowValues.get(`${key}unit`) || rowValues.get("unit") || null;
  const basis = contribution
    ? `${key === "contributionabspercent" ? "Absolute value of the first metric" : "Signed value of the first metric"} ÷ sum of absolute group values × 100.`
    : formula ? `Grid formula ${formula.expression}.`
      : metric && grid.kind !== "Detail" ? `${metric.function}(${metric.sourceField}) for the rendered group.`
        : `Rendered grid field ${field?.sourceField || metric?.sourceField || bar.seriesKey}.`;
  const conversion = bar.value === null ? " No value supplied for this point."
    : bar.value === 0 ? " The chart engine uses 0 for missing or nonnumeric cells." : "";
  return {
    id: `${grid.gridId}:${row?.rowKey ?? category}:${rowIndex}:${bar.seriesKey}`,
    label: `${category} · grid row ${rowIndex + 1}`,
    asOf,
    values: [{ series: bar.seriesLabel, value: bar.value, unit, basis: basis + conversion }],
    proof: row ? { href: `#${proofId}-row-${rowIndex}`, label: `Open source grid row ${rowIndex + 1}` } : null,
    sourceDetail
  };
}
