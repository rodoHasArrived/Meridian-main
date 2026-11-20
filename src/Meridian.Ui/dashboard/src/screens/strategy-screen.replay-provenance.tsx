import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { ProviderProvenancePanel } from "@/components/meridian/provider-provenance-panel";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { formatCount } from "@/lib/format";
import type { ProviderProvenanceReceipt, StrategyRunReplayProvenance, StrategyRunReplayRevision, StrategyRunReplaySourceCount } from "@/types";

const sourceColumns: DenseDataTableColumn<StrategyRunReplaySourceCount>[] = [
  { id: "symbol", label: "Symbol", rowHeader: true, render: (row) => row.symbol },
  { id: "source", label: "Source", render: (row) => row.source === "UNKNOWN" ? "Unknown" : row.source },
  { id: "bars", label: "Replayed bars", align: "right", render: (row) => formatCount(row.bars) }
];

const revisionColumns: DenseDataTableColumn<StrategyRunReplayRevision>[] = [
  { id: "instrument", label: "Instrument", rowHeader: true, render: (row) => `${row.instrument} · ${row.granularity}` },
  { id: "provider", label: "Serving provider", render: (row) => row.servingProvider },
  { id: "revision", label: "Revision", render: (row) => <code className="break-all">{row.revisionId}</code> },
  { id: "content", label: "Content hash", render: (row) => <code className="break-all">{row.contentHash}</code> }
];

export function StrategyReplayProvenance({ evidence, receipt, loading }: {
  evidence: StrategyRunReplayProvenance | null;
  receipt: ProviderProvenanceReceipt | null;
  loading: boolean;
}) {
  return <section aria-label="Replay data sources" className="mt-4 rounded-md border border-border/70 bg-secondary/20 px-4 py-3">
    <h3 className="text-sm font-semibold text-foreground">Replay data sources</h3>
    {evidence?.sourceMix.length ? (
      <div className="mt-2">
        <DenseDataTable columns={sourceColumns} rows={evidence.sourceMix}
          getRowId={(row) => JSON.stringify([row.symbol, row.source])} ariaLabel="Replayed bars by source"
          emptyText="Replay provider mix is unavailable." />
      </div>
    ) : (
      <p className="mt-2 text-xs text-muted-foreground" role="status">
        {loading ? "Loading replay data sources…" : "Replay provider mix is unavailable for this run."}
      </p>
    )}
    {evidence && (evidence.completedInputHashSha256 || evidence.revisions.length > 0) ? (
      <TechnicalDetails label="Replay fingerprints and revisions" className="mt-3">
        {evidence.completedInputHashSha256 ? (
          <dl className="mb-2 text-xs">
            <dt>Completed input fingerprint</dt>
            <dd className="break-all font-mono">{evidence.completedInputHashSha256}</dd>
          </dl>
        ) : null}
        {evidence.revisions.length > 0 ? (
          <DenseDataTable columns={revisionColumns} rows={evidence.revisions}
            getRowId={(row) => row.revisionId} ariaLabel="Verified backfill revisions"
            emptyText="No verified backfill revisions." />
        ) : null}
      </TechnicalDetails>
    ) : null}
    {receipt ? <ProviderProvenancePanel receipt={receipt} /> : null}
  </section>;
}
