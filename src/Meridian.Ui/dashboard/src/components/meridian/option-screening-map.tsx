import { useId } from "react";
import { formatNumber } from "@/lib/format";
import type { CoveredCallChainPreview, CoveredCallChainPreviewRequest } from "@/types/covered-call.types";

interface Props {
  preview: CoveredCallChainPreview;
  filters: Pick<CoveredCallChainPreviewRequest, "minDte" | "maxDte" | "maxDelta">;
  selectedIndex: number;
  onSelect: (index: number) => void;
}

/** An alternate selection surface over the exact rows returned by the preview service. */
export function OptionScreeningMap({ preview, filters, selectedIndex, onSelect }: Props) {
  const id = useId();
  const captured = preview.filters ?? filters;
  const points = preview.candidates.flatMap((row, index) =>
    row.deltaAvailable !== false && Number.isFinite(row.delta) && Math.abs(row.delta) <= 1 && Number.isFinite(row.daysToExpiration) && row.daysToExpiration >= 0
      ? [{ row, index }] : []);
  const xMax = Math.max(1, captured.minDte, captured.maxDte ?? 0, ...points.map(({ row }) => row.daysToExpiration));
  const yMin = points.some(({ row }) => row.delta < 0) ? -1 : 0;
  const x = (dte: number) => 64 + dte / xMax * 620;
  const y = (delta: number) => 224 - (delta - yMin) / (1 - yMin) * 190;
  const snapshots = (preview.snapshotTimes ?? []).filter((value) => Number.isFinite(Date.parse(value)));
  const stale = snapshots.some((value) => Date.now() - Date.parse(value) > 15 * 60_000);
  const partial = preview.coverageKnown && (preview.loadedExpirations?.length ?? 0) < (preview.requestedExpirations?.length ?? 0);

  return <section aria-labelledby={`${id}-title`} className="mb-4 grid gap-2 rounded border border-border p-3">
    <h3 id={`${id}-title`} className="text-sm font-semibold">Options screening map</h3>
    <p className="text-xs text-muted-foreground">
      {preview.underlyingSymbol} · DTE measured from {preview.asOf}. Snapshot illustration of covered-call preview filters.
      The shaded region shows only DTE and absolute-delta limits; liquidity, strike and spread also determine pass status.
    </p>
    <p className="text-xs">
      {preview.coverageKnown
        ? `${preview.loadedExpirations?.length ?? 0} of ${preview.requestedExpirations?.length ?? 0} requested expiries loaded; next four expiries at most, ${preview.maxStrikesPerExpiration ?? "unspecified"} strikes per expiry at most.`
        : "Snapshot coverage is unspecified by this response."}
      {partial ? " Partial coverage: some requested expiries are unavailable." : ""}
      {snapshots.length ? ` Snapshot times: ${snapshots.join(", ")}.` : " Quote timestamp unavailable."}
      {stale ? " Stale snapshot: at least one is older than 15 minutes." : ""}
    </p>
    <p className="text-xs text-muted-foreground">Current snapshots do not establish historical quotes or executable premium. Preview filters omit IV-percentile and dividend-assignment checks. Select a mark with Enter or Space, or use the chain table.</p>
    {!points.length ? <p role="status" className="text-xs">No returned contracts have measured, valid delta and DTE coordinates. Inspect the chain table for unavailable data and rejection reasons.</p> : null}
    <div className="overflow-x-auto" role="region" aria-label="Options screening chart, scroll horizontally when needed" tabIndex={0}>
    <svg viewBox="0 0 730 278" className="w-full" style={{ minWidth: 730, fontFamily: "var(--font-data)" }} role="group" aria-label="Option contracts by days to expiry and delta">
      <rect x={x(Math.max(0, captured.minDte))} y={y(Math.min(1, captured.maxDelta))}
        width={Math.max(0, x(captured.maxDte ?? xMax) - x(Math.max(0, captured.minDte)))}
        height={Math.max(0, y(Math.max(yMin, -captured.maxDelta)) - y(Math.min(1, captured.maxDelta)))}
        fill="currentColor" opacity="0.08" />
      {[0, .25, .5, .75, 1, ...(yMin < 0 ? [-1, -.5] : [])].map((delta) => <g key={delta}>
        <line x1="64" x2="684" y1={y(delta)} y2={y(delta)} stroke="currentColor" opacity="0.16" />
        <text x="54" y={y(delta) + 4} textAnchor="end" fill="currentColor" fontSize="12">{formatNumber(delta, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</text>
      </g>)}
      {[0, xMax / 2, xMax].map((days) => <text key={days} x={x(days)} y="244" textAnchor="middle" fill="currentColor" fontSize="12">{formatNumber(days, { maximumFractionDigits: 0 })}</text>)}
      <text x="374" y="267" textAnchor="middle" fill="currentColor" fontSize="12">Days to expiry</text>
      <text x="17" y="135" transform="rotate(-90 17 135)" textAnchor="middle" fill="currentColor" fontSize="12">Delta</text>
      {points.map(({ row, index }) => <g key={`${row.expiration}:${row.strike}:${index}`} role="button" tabIndex={0}
        aria-label={`Select ${preview.underlyingSymbol} ${row.strike} call expiring ${row.expiration}; delta ${row.delta}; ${row.meetsAllFilters ? "passes preview filters" : row.rejectReason ?? "rejected"}`}
        aria-pressed={selectedIndex === index} className="cursor-pointer focus:outline focus:outline-2 focus:outline-offset-2" style={{ outlineColor: "var(--interaction-focus)" }}
        onClick={() => onSelect(index)} onKeyDown={(event) => { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); onSelect(index); } }}>
        <title>{`${row.strike} · ${row.expiration} · ${row.meetsAllFilters ? "Pass" : row.rejectReason ?? "Rejected"}`}</title>
        <circle cx={x(row.daysToExpiration)} cy={y(row.delta)} r={selectedIndex === index ? 9 : 6} fill={row.meetsAllFilters ? "currentColor" : "var(--chart-plot)"} stroke="currentColor" strokeWidth={selectedIndex === index ? 3 : 1.5} />
        {!row.meetsAllFilters ? <path d={`M${x(row.daysToExpiration) - 3},${y(row.delta) - 3}l6,6m0,-6l-6,6`} stroke="currentColor" /> : null}
      </g>)}
    </svg>
    </div>
    <p className="text-xs">● Passes preview filters · ⊗ Rejected · Larger ring: selected. {preview.candidates.length - points.length} candidates have unavailable chart coordinates and remain in the table.</p>
  </section>;
}
