import { useState } from "react";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import type { BackfillBatchReceipt } from "@/types";

const receiptColumns: DenseDataTableColumn<BackfillBatchReceipt>[] = [
  {
    id: "instrument", label: "Instrument", rowHeader: true, render: receipt => <>
      {receipt.instrument} · {receipt.granularity}
      <div className="text-xs font-normal">{receipt.from ?? "Earliest available"} to {receipt.to ?? "Latest available"}</div>
    </>
  },
  {
    id: "provider", label: "Serving provider", render: receipt => <>
      {receipt.servingProvider ?? "Unknown"}
      {receipt.servingProvider !== receipt.requestedProvider ? <div className="text-xs text-muted-foreground">
        Requested {receipt.requestedProvider}
      </div> : null}
    </>
  },
  {
    id: "coverage", label: "Coverage", render: receipt => <>{receipt.status}<div className="text-xs">
      {receipt.expectedSessions ? `${receipt.receivedSessions.length} / ${receipt.expectedSessions.length}` : `${receipt.receivedSessions.length} observed`} sessions
    </div></>
  },
  {
    id: "evidence", label: "Evidence", render: receipt => <details>
      <summary className="cursor-pointer">{receipt.rowCount} rows</summary>
      <dl className="mt-1 space-y-1 text-xs">
        <div><dt>Calendar</dt><dd>{receipt.calendar ?? "Unknown"}</dd></div>
        <div><dt>Revision</dt><dd className="break-all font-mono">{receipt.revisionId}</dd></div>
        <div><dt>Content hash</dt><dd className="break-all font-mono">{receipt.contentHash}</dd></div>
      </dl>
    </details>
  }
];

export function BackfillBatchReceipts({ receipts }: { receipts: BackfillBatchReceipt[] }) {
  const [open, setOpen] = useState(false);
  return <details className="mt-4 rounded border border-border p-3" onToggle={event => setOpen(event.currentTarget.open)}>
    <summary className="cursor-pointer text-sm font-semibold">Retained batch receipts ({receipts.length})</summary>
    {open ? <div className="mt-2 max-h-72 overflow-auto">
      <DenseDataTable columns={receiptColumns} rows={receipts} getRowId={receipt => receipt.revisionId}
        ariaLabel="Backfill batch evidence" emptyText="No retained batch receipts." />
    </div> : null}
  </details>;
}
