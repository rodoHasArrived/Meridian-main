import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { formatCount } from "@/lib/format";
import type { BackfillBatchReceipt, BackfillRoutingAttempt } from "@/types";

export interface BackfillSourceRow {
  symbol: string;
  providerId: string | null;
  bars: number;
}

const sourceKey = (row: BackfillSourceRow) => JSON.stringify([row.symbol, row.providerId]);
const sourceColumns: DenseDataTableColumn<BackfillSourceRow>[] = [
  { id: "symbol", label: "Symbol", rowHeader: true, render: row => row.symbol },
  { id: "provider", label: "Serving provider", render: row => row.providerId ?? "Unknown" },
  { id: "bars", label: "Retained bars", numeric: true, render: row => formatCount(row.bars) }
];

export function retainedSourceRows(receipts: readonly Pick<BackfillBatchReceipt, "revisionId" | "instrument" | "servingProvider" | "rowCount">[]): BackfillSourceRow[] {
  const seen = new Set<string>();
  const rows = new Map<string, BackfillSourceRow>();
  for (const receipt of receipts) {
    if (receipt.rowCount <= 0 || seen.has(receipt.revisionId)) continue;
    seen.add(receipt.revisionId);
    const row = { symbol: receipt.instrument, providerId: receipt.servingProvider, bars: receipt.rowCount };
    const key = sourceKey(row);
    const prior = rows.get(key);
    rows.set(key, prior ? { ...prior, bars: prior.bars + row.bars } : row);
  }
  return [...rows.values()].sort((a, b) => a.symbol.localeCompare(b.symbol) ||
    (a.providerId ?? "").localeCompare(b.providerId ?? ""));
}

function attemptLabel(reason: string): string {
  switch (reason) {
    case "served": case "completed": case "success": return "Returned bars";
    case "rate-limited": return "Rate limited";
    case "skipped-rate-limited": return "Skipped during rate-limit cooldown";
    case "skipped-backoff": return "Skipped during failure backoff";
    case "no-data": return "No bars available";
    case "stale-data": return "Stale data";
    case "unavailable": return "Provider unavailable";
    case "failed": return "Provider request failed";
    default: return "Provider routing event";
  }
}

export function BackfillAttribution({ sources, attempts = [] }: {
  sources: BackfillSourceRow[];
  attempts?: BackfillRoutingAttempt[];
}) {
  if (sources.length === 0 && attempts.length === 0) return null;

  return <div className="space-y-2 rounded border border-border p-3">
    {sources.length > 0 ? <>
      <h4 className="text-sm font-semibold">Served by</h4>
      <div className="max-h-56 overflow-auto">
        <DenseDataTable columns={sourceColumns} rows={sources} getRowId={sourceKey}
          ariaLabel="Backfill bars by source" emptyText="No retained bars." />
      </div>
    </> : null}
    {attempts.length > 0 ? <details>
      <summary className="cursor-pointer text-sm font-medium">Provider attempts ({attempts.length})</summary>
      <ul className="mt-2 max-h-52 list-disc space-y-1 overflow-auto pl-5 text-xs">
        {attempts.map((attempt, index) => <li key={`${attempt.symbol}:${attempt.providerId}:${index}`}>
          <span className="font-medium">{attempt.symbol} · {attempt.providerId}</span> · {attemptLabel(attempt.reason)}
          {attempt.bars > 0 ? ` · ${formatCount(attempt.bars)} bars` : ""}
        </li>)}
      </ul>
    </details> : null}
  </div>;
}
