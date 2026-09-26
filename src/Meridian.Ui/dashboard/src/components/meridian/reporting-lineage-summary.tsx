import { DesignSystemStatus } from "@/design-system/status";
import { cn } from "@/lib/utils";
import {
  buildTraceFromRecordGraph,
  listTraceableRecordIds
} from "@/lib/reporting-provenance-adapter";
import { REPORTING_TRACE_STAGES, REPORTING_TRACE_STAGE_LABELS } from "@/lib/reporting-trace";
import type { FinancialRecordExplorerRecordGraphDto } from "@/types";

export interface ReportingLineageSummaryProps {
  graph: FinancialRecordExplorerRecordGraphDto | null | undefined;
  /** Stages this report class requires beyond source and report block. */
  requiredStages?: readonly string[];
  /** Records to render; defaults to every traceable record in the graph. */
  maxRecords?: number;
  className?: string;
}

const DEFAULT_MAX_RECORDS = 6;

/**
 * Stage-ordered lineage for the records in a provenance graph.
 *
 * The explorer already renders the record graph. What it cannot show is *where a
 * chain stops*: a record with no reconciliation looks structurally identical to one
 * that has it. This summary places each record's relationships on the governed
 * source-to-publication order and names the stages that produced nothing, so an
 * incomplete lineage reads as incomplete.
 */
export function ReportingLineageSummary({
  graph,
  requiredStages,
  maxRecords = DEFAULT_MAX_RECORDS,
  className
}: ReportingLineageSummaryProps) {
  const recordIds = listTraceableRecordIds(graph).slice(0, Math.max(0, maxRecords));

  if (recordIds.length === 0) {
    return null;
  }

  return (
    <div className={cn("reporting-lineage-summary space-y-3", className)}>
      <div>
        <div className="eyebrow-label">Lineage</div>
        <p className="text-xs text-muted-foreground">
          Each record placed on the governed source-to-publication order. Stages with no retained
          step are named rather than omitted.
        </p>
      </div>

      <ul className="space-y-3">
        {recordIds.map((recordId) => {
          const trace = buildTraceFromRecordGraph(graph, recordId, { requiredStages });
          const anchor = trace.steps[0];
          const presentStages = new Set(trace.steps.map((step) => step.stage));
          const gapStages = new Set(trace.gaps.map((gap) => gap.stage));

          return (
            <li key={recordId} className="rounded-md border border-border/60 p-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="text-sm font-semibold text-foreground">
                  {anchor?.label ?? recordId}
                </span>
                <DesignSystemStatus status={trace.severity}>
                  {trace.isComplete ? "Complete" : "Incomplete"}
                </DesignSystemStatus>
              </div>

              <ol className="mt-2 flex flex-wrap gap-x-1 gap-y-1">
                {REPORTING_TRACE_STAGES.map((stage, index) => {
                  const isPresent = presentStages.has(stage);
                  const isGap = gapStages.has(stage);

                  return (
                    <li key={stage} className="flex items-center gap-1">
                      {index > 0 ? (
                        <span aria-hidden="true" className="text-muted-foreground">
                          →
                        </span>
                      ) : null}
                      <span
                        className={cn(
                          "text-xs",
                          isPresent && "font-medium text-foreground",
                          isGap && "font-medium text-destructive",
                          !isPresent && !isGap && "text-muted-foreground line-through"
                        )}
                      >
                        {REPORTING_TRACE_STAGE_LABELS[stage]}
                        {isGap ? " (missing)" : ""}
                      </span>
                    </li>
                  );
                })}
              </ol>

              <p className="mt-2 text-xs text-muted-foreground">{trace.summary}</p>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
