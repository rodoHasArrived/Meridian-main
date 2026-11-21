import { RangeCoverageHeatmap } from "@/components/charts/RangeCoverageHeatmap";
import { TableWorkbench } from "@/components/meridian/table-workbench";
import type { BackfillExecutionHistoryResponse } from "@/types";

export function BackfillCoverageChart({ history }: { history: BackfillExecutionHistoryResponse }) {
  const validDate = (value: string) => /^\d{4}-\d{2}-\d{2}$/.test(value) && Number.isFinite(Date.parse(value))
    && new Date(value).toISOString().slice(0, 10) === value;
  const valid = history.executions.filter((row) => validDate(row.fromDate) && validDate(row.toDate) && row.fromDate <= row.toDate);
  const cells = valid.slice(0, 12).map((row) => ({
    id: row.executionId, label: row.symbols.length > 0 ? row.symbols.join(", ") : row.scheduleName,
    from: row.fromDate, to: row.toDate, status: row.status
  }));
  return <section aria-label="Backfill coverage evidence" className="space-y-3 border-t border-border pt-4">
    <h3 className="text-sm font-semibold">Backfill coverage evidence</h3>
    <p className="text-xs text-muted-foreground">Retained execution status by requested date range · as of {history.timestamp}. A completed execution does not by itself verify every market-data session in that range.</p>
    {cells.length === 0 ? <p role="status" className="text-sm text-muted-foreground">No retained dated backfill executions are available.</p> : <>
      <RangeCoverageHeatmap cells={cells} label="Backfill execution states by requested date range" />
      <p className="text-xs text-muted-foreground">Showing {cells.length} of {history.total} retained executions. {history.executions.length - valid.length} loaded execution(s) have no usable date range.</p>
      <details className="workspace-task-disclosure"><summary>View backfill coverage evidence table</summary>
        <TableWorkbench workbenchId="backfill-coverage-evidence" sourceKey={history.timestamp} sourceDescription="Retained backfill execution ranges and source statuses; execution success is not daily-session completeness." completePopulation={valid.length === history.total} tableId="backfill-coverage-evidence" rows={valid} getRowId={(row) => row.executionId} ariaLabel="Backfill coverage evidence data"
          columns={[{ id: "symbols", label: "Symbols", render: (row) => row.symbols.join(", ") || "Not retained", exportValue: (row) => row.symbols.join(", ") },
            { id: "from", label: "Requested from", render: (row) => row.fromDate, exportValue: (row) => row.fromDate }, { id: "to", label: "Requested to", render: (row) => row.toDate, exportValue: (row) => row.toDate },
            { id: "state", label: "Execution state", render: (row) => row.status, exportValue: (row) => row.status }, { id: "bars", label: "Bars downloaded", render: (row) => row.barsDownloaded, exportValue: (row) => row.barsDownloaded }]}
          emptyText="No retained execution ranges." />
      </details>
    </>}
  </section>;
}
