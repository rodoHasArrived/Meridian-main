export interface RangeCoverageCell { id: string; label: string; from: string; to: string; status: string }

/** Retained execution-range evidence; each cell keeps its literal source status visible. */
export function RangeCoverageHeatmap({ cells, label }: { cells: RangeCoverageCell[]; label: string }) {
  return <div role="img" aria-label={label} className="grid gap-1 overflow-x-auto" style={{ gridTemplateColumns: "repeat(3,minmax(9rem,1fr))" }}>
    {cells.map((cell) => {
      const status = cell.status.toLowerCase();
      const tone = status === "completed" || status === "success" || status === "succeeded"
        ? "bg-success/10 border-success/40" : status === "failed" || status === "error"
          ? "bg-danger/10 border-danger/40" : "bg-secondary border-border";
      return <div key={cell.id} className={`border p-2 text-xs ${tone}`}>
        <strong className="block truncate" title={cell.label}>{cell.label}</strong>
        <span className="block">{cell.from} → {cell.to}</span>
        <span className="block font-medium">{cell.status}</span>
      </div>;
    })}
  </div>;
}
