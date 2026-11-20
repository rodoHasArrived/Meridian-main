import { useId, useRef, useState } from "react";
import { buildDepthProfile, DepthChart } from "@/components/charts/DepthChart";
import { ChartEvidenceView, type ChartEvidence } from "@/components/charts/chart-evidence";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { Button } from "@/components/ui/button";
import { buildDepthQuantityIllustration } from "@/screens/depth-quantity.view-model";
import type { OrderBookResponse } from "@/types/market-data";
import { formatNumber, formatQuantity } from "@/lib/format";
import { DenseDataTable } from "@/components/meridian/ui-kit-primitives";

export { illustrateDepth } from "@/screens/depth-quantity.view-model";
const size = (value: number) => value !== 0 && Math.abs(value) < 1e-8
  ? formatNumber(value, { notation: "scientific", maximumFractionDigits: 11 }) : formatQuantity(value, { maxDecimals: 10 });

export function DepthQuantityIllustration({ book: latestBook }: { book: OrderBookResponse }) {
  const id = useId();
  const [book, setSnapshot] = useState(latestBook);
  const [quantity, setQuantity] = useState("");
  const [side, setSide] = useState<"Buy" | "Sell">("Buy");
  const snapshotDetails = useRef<HTMLDetailsElement>(null);
  const vm = buildDepthQuantityIllustration(book, side, quantity);
  const chartAvailable = [...book.bids, ...book.asks].every((level) => Number.isFinite(level.price) && level.price > 0 && Number.isFinite(level.size) && level.size >= 0);
  const profile = buildDepthProfile(book.bids, book.asks, book.midPrice);
  const levels = [...profile.bidPoints, ...profile.askPoints].map((level, index) => ({ ...level, proofId: `${id}-source-level-${index}` }));
  const priceUnit = "Price per unit (currency unreported)";
  const sourceDetail = `Snapshot ${book.sequenceNumber} · venue ${book.venue ?? "unreported"} · stream ${book.streamId ?? "unreported"}${book.isStale ? " · stale" : ""}`;
  const openSnapshot = () => { if (snapshotDetails.current) snapshotDetails.current.open = true; };
  const points: ChartEvidence[] = levels.map((level) => ({
    id: `${book.symbol}:${level.side}:${level.level}:${level.price}`,
    label: `${book.symbol} ${level.side.toLowerCase()} level ${level.level}`,
    asOf: book.timestamp,
    values: [
      { series: `${level.side} price`, value: level.price, unit: priceUnit, basis: "Price reported at this level in the captured snapshot." },
      { series: `Cumulative ${level.side.toLowerCase()} depth`, value: level.depth, unit: "Displayed size units", basis: "Visible size summed from the best price on this side through this level." }
    ],
    proof: { href: `#${level.proofId}`, label: `Open captured ${level.side.toLowerCase()} level ${level.level}`, onOpen: openSnapshot },
    sourceDetail
  }));
  points.push({
    id: `${book.symbol}:mid`, label: `${book.symbol} mid-price marker`, asOf: book.timestamp,
    values: [{ series: "Mid price", value: profile.midPrice, unit: priceUnit, basis: profile.midBasis }],
    proof: { href: `#${id}-snapshot-levels`, label: "Open captured depth levels", onOpen: openSnapshot }, sourceDetail
  });
  if (vm.active && vm.terminalPrice !== null) {
    points.push({
      id: `${book.symbol}:coverage`, label: `${side} displayed-liquidity illustration`, asOf: book.timestamp,
      values: [
        { series: "Terminal price", value: vm.terminalPrice, unit: priceUnit, basis: "Last captured level reached while applying the illustrative quantity." },
        { series: "Quantity guide", value: Math.min(vm.quantity, Math.max(1, ...levels.map((level) => level.depth))), unit: "Displayed size units", basis: "Illustrative quantity capped at the chart's maximum depth." }
      ],
      proof: { href: `#${id}-quantity-coverage`, label: "Open illustrated quantity coverage" }, sourceDetail
    });
  }
  return <section className="space-y-3" aria-label="Displayed liquidity quantity illustration">
    <p id={`${id}-caveat`} className="text-xs text-muted-foreground">Snapshot illustration of displayed liquidity; not predicted execution. Size units are those reported by the source. This quantity does not change the order ticket.</p>
    <div className="flex flex-wrap items-end gap-3">
      <label className="space-y-1 text-xs" htmlFor={`${id}-side`}>Illustrative side<Select aria-label="Illustrative side" id={`${id}-side`} value={side} onChange={(event) => setSide(event.target.value as "Buy" | "Sell")}><option value="Buy">Buy · ask levels</option><option value="Sell">Sell · bid levels</option></Select></label>
      <label className="space-y-1 text-xs" htmlFor={`${id}-quantity`}>Illustrative quantity · displayed size units<Input id={`${id}-quantity`} inputMode="decimal" value={quantity} onChange={(event) => setQuantity(event.target.value)} aria-invalid={Boolean(vm.error)} aria-describedby={`${id}-caveat ${vm.error ? `${id}-error` : ""}`} placeholder="Enter quantity" /></label>
      <Button size="sm" variant="outline" onClick={() => setSnapshot(latestBook)}>Capture latest depth snapshot</Button>
    </div>
    {vm.error ? <p id={`${id}-error`} role="alert" className="text-sm text-danger">{vm.error}</p> : null}
    {chartAvailable ? (
      <ChartEvidenceView label={`${book.symbol} order-book depth`} evidence={points} initialIndex={levels.length}>
        {(inspection) => (
          <div className="h-64 rounded-sm border border-border/70">
            <DepthChart
              ariaLabel={`Order-book depth profile for ${book.symbol}`}
              ariaDescription="Cumulative bid and ask size. The quantity coverage table identifies fully and partly used displayed levels."
              profile={profile}
              illustration={vm.active ? { side, quantity: vm.quantity, terminalPrice: vm.terminalPrice } : null}
              inspection={inspection}
            />
          </div>
        )}
      </ChartEvidenceView>
    ) : <p className="text-sm">Depth chart unavailable: the source contains unsupported price or size values.</p>}
    <p className="break-words font-mono text-xs text-muted-foreground">Snapshot {book.timestamp} · {book.venue ?? "Venue unreported"} · stream {book.streamId ?? "unreported"} · sequence {book.sequenceNumber} · {book.isStale ? "STALE" : book.marketState}</p>
    {book.timestamp !== latestBook.timestamp || book.sequenceNumber !== latestBook.sequenceNumber || book.streamId !== latestBook.streamId ? <p className="text-xs text-muted-foreground">Newer depth is available. Capture it explicitly to update this illustration.</p> : null}
    {chartAvailable ? (
      <details ref={snapshotDetails}>
        <summary id={`${id}-snapshot-levels`} className="cursor-pointer text-xs font-medium">Captured depth levels</summary>
        <DenseDataTable
          ariaLabel="Captured depth levels"
          rows={levels}
          getRowId={(level) => level.proofId}
          maxVisibleRows={null}
          emptyText="The captured book has no displayed levels."
          columns={[
            { id: "level", label: "Level", rowHeader: true, render: (level) => (
              <span id={level.proofId} tabIndex={-1} className="target:bg-secondary/60">{level.side} {level.level}</span>
            ) },
            { id: "price", label: "Price", numeric: true, render: (level) => level.price },
            { id: "size", label: "Displayed size", numeric: true, render: (level) => level.size }
          ]}
        />
      </details>
    ) : null}
    {vm.active ? <>
      <p role="status" className="text-sm">{size(vm.covered)} of {size(vm.quantity)} displayed size units covered across {vm.levels.filter((level) => level.used > 0).length} {side === "Buy" ? "ask" : "bid"} levels. {vm.shortfall > 0 ? `${size(vm.shortfall)} beyond displayed levels; liquidity outside this snapshot is unknown.` : "Quantity fits within the displayed levels."}</p>
      <details open><summary id={`${id}-quantity-coverage`} className="cursor-pointer text-xs font-medium">Quantity coverage by level</summary><DenseDataTable ariaLabel="Illustrative quantity coverage levels" rows={vm.levels} getRowId={(level) => level.id} getRowClassName={(level) => level.used > 0 ? "bg-primary/10" : "text-muted-foreground"} emptyText="No displayed levels on this side." maxVisibleRows={null} columns={[
        { id: "price", label: "Price", numeric: true, render: (level) => size(level.price) },
        { id: "displayed", label: "Displayed", numeric: true, render: (level) => size(level.displayed) },
        { id: "use", label: "Illustrated use", numeric: true, render: (level) => size(level.used) },
        { id: "coverage", label: "Coverage", render: (level) => level.used === 0 ? "Unused" : level.used < level.displayed ? "Partial level" : "Full level" }
      ]} /></details>
    </> : null}
  </section>;
}
