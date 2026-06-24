import { ArrowDown, ArrowUp } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import type { ReportWriterDiffRowState, ReportWriterGridDiff } from "@/types";

export interface ReportWriterGridDiffViewProps {
  diff: ReportWriterGridDiff;
  maxRows?: number;
  className?: string;
}

const STATE_BADGE: Record<ReportWriterDiffRowState, "success" | "warning" | "danger" | "outline"> = {
  Added: "success",
  Removed: "danger",
  Changed: "warning",
  Unchanged: "outline"
};

const STATE_LABEL: Record<ReportWriterDiffRowState, string> = {
  Added: "Added",
  Removed: "Removed",
  Changed: "Changed",
  Unchanged: "Unchanged"
};

/**
 * Renders a period-over-period grid diff: one row per matched/added/removed row, with the
 * current value and a signed delta per numeric column. Consumes the output of
 * {@link "@/lib/report-writer-grid-diff".buildReportWriterGridDiff}.
 */
export function ReportWriterGridDiffView({ diff, maxRows = 6, className }: ReportWriterGridDiffViewProps) {
  const rows = diff.rows.slice(0, maxRows);

  return (
    <div className={cn("rounded-sm border border-border/60 bg-background/35 px-2 py-2 text-xs", className)} aria-label={`${diff.title} comparison`}>
      <p className="text-[11px] text-muted-foreground">
        {diff.changedRowCount} changed · {diff.addedRowCount} added · {diff.removedRowCount} removed · {diff.unchangedRowCount} unchanged
      </p>
      <div className="mt-2 max-h-56 overflow-auto rounded-sm border border-border/60">
        <table className="min-w-full table-fixed text-left text-xs">
          <thead className="bg-secondary/40 text-[10px] uppercase tracking-[0.12em] text-muted-foreground">
            <tr>
              <th scope="col" className="min-w-20 px-2 py-1.5 font-semibold">Change</th>
              {diff.columns.map((column) => (
                <th key={column.key} scope="col" className="min-w-28 px-2 py-1.5 font-semibold">
                  <span className="block truncate" title={column.label}>{column.label}</span>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={`${row.state}:${row.rowKey}`} className="border-t border-border/50">
                <td className="px-2 py-1.5">
                  <Badge variant={STATE_BADGE[row.state]}>{STATE_LABEL[row.state]}</Badge>
                </td>
                {row.cells.map((cell) => {
                  const display = cell.currentValue ?? cell.priorValue ?? "";
                  const hasDelta = cell.delta !== null && cell.direction !== "Flat";
                  return (
                    <td key={`${row.rowKey}:${cell.column}`} className="px-2 py-1.5 font-mono text-foreground">
                      <span className="flex items-center gap-1">
                        <span className="block truncate" title={display}>{display}</span>
                        {hasDelta ? (
                          <span
                            className={cn(
                              "inline-flex items-center gap-0.5 text-[10px]",
                              cell.direction === "Up" ? "text-success" : "text-danger"
                            )}
                            title={`Delta ${cell.delta}`}
                          >
                            {cell.direction === "Up" ? (
                              <ArrowUp className="h-3 w-3" aria-hidden="true" />
                            ) : (
                              <ArrowDown className="h-3 w-3" aria-hidden="true" />
                            )}
                            {cell.delta}
                          </span>
                        ) : null}
                      </span>
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {diff.warnings.length > 0 ? (
        <p className="mt-2 text-[10px] text-warning">{diff.warnings.join("; ")}</p>
      ) : null}
    </div>
  );
}
