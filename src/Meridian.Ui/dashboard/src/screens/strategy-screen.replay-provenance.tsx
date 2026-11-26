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
    <section aria-label="Replay data-quality assessment" className="mt-3 text-sm">
      {loading ? <p role="status">Loading retained data-quality assessment…</p> : evidence?.dataQualityWarnings == null ? (
        <p role="status" className="text-muted-foreground">Data-quality coverage was not retained for this run. Repair, gap-fill and quality assessment status remain unknown.</p>
      ) : evidence.dataQualityWarnings.length === 0 ? (
        <p>No source-quality warnings were reported for the retained inputs.</p>
      ) : <ul className="space-y-3">
        {evidence.dataQualityWarnings.map((warning) => <li key={warning.kind} className="rounded border border-warning/30 bg-warning/10 p-3">
          <p className="font-semibold">{({ Repaired: "Repaired data", GapFilled: "Gap-filled data", LowQuality: "Low-quality data", Unknown: "Unknown source quality" } as Record<string, string>)[warning.kind] ?? warning.kind}</p>
          <p>{warning.message} Affected fills: {formatCount(warning.affectedFillCount)}.</p>
          {warning.evidence.length > 0 ? <TechnicalDetails label="Supporting source assessments" className="mt-2">
            <ul className="space-y-3">{warning.evidence.map((source, index) => <li key={index} className="break-all text-xs">
              <p>Provider: {source.providerId ?? "Unknown"} · Observed: {source.observedAt ?? "Unknown"}</p>
              <p>Event: {source.eventReference ?? "Unknown"} · Dataset: {source.datasetId ?? "Unknown"} · Revision: {source.backfillRevisionId ?? "Unknown"}</p>
              <p>Content hash: {source.contentHash ?? "Unknown"} · Repair receipt: {source.repairReceiptId ?? "Unknown"}</p>
              <p>Repaired: {source.repaired == null ? "Unknown" : source.repaired ? "Yes" : "No"} · Gap-filled: {source.gapFilled == null ? "Unknown" : source.gapFilled ? "Yes" : "No"}</p>
              <p>Quality score: {source.qualityScore ?? "Unassessed"} · Retained threshold: {source.qualityThreshold ?? "Unknown"} · Assessed: {source.qualityAssessedAt ?? "Unknown"} · Assessment range: {source.assessmentFrom ?? "Unknown"} to {source.assessmentTo ?? "Unknown"}</p>
            </li>)}</ul>
          </TechnicalDetails> : null}
        </li>)}
      </ul>}
    </section>
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
