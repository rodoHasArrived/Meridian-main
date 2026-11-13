import { useId, useState } from "react";
import { DepthChart } from "@/components/charts/DepthChart";
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
  const vm = buildDepthQuantityIllustration(book, side, quantity);
  const chartAvailable = [...book.bids, ...book.asks].every((level) => Number.isFinite(level.price) && level.price > 0 && Number.isFinite(level.size) && level.size >= 0);
  return <section className="space-y-3" aria-label="Displayed liquidity quantity illustration">
    <p id={`${id}-caveat`} className="text-xs text-muted-foreground">Snapshot illustration of displayed liquidity; not predicted execution. Size units are those reported by the source. This quantity does not change the order ticket.</p>
    <div className="flex flex-wrap items-end gap-3">
      <label className="space-y-1 text-xs" htmlFor={`${id}-side`}>Illustrative side<Select aria-label="Illustrative side" id={`${id}-side`} value={side} onChange={(event) => setSide(event.target.value as "Buy" | "Sell")}><option value="Buy">Buy · ask levels</option><option value="Sell">Sell · bid levels</option></Select></label>
      <label className="space-y-1 text-xs" htmlFor={`${id}-quantity`}>Illustrative quantity · displayed size units<Input id={`${id}-quantity`} inputMode="decimal" value={quantity} onChange={(event) => setQuantity(event.target.value)} aria-invalid={Boolean(vm.error)} aria-describedby={`${id}-caveat ${vm.error ? `${id}-error` : ""}`} placeholder="Enter quantity" /></label>
      <Button size="sm" variant="outline" onClick={() => setSnapshot(latestBook)}>Capture latest depth snapshot</Button>
    </div>
    {vm.error ? <p id={`${id}-error`} role="alert" className="text-sm text-danger">{vm.error}</p> : null}
    {chartAvailable ? <div className="h-64 overflow-hidden rounded-sm border border-border/70">
      <DepthChart ariaLabel={`Order-book depth profile for ${book.symbol}`} ariaDescription="Cumulative bid and ask size. The quantity coverage table identifies fully and partly used displayed levels." bids={book.bids} asks={book.asks} mid={book.midPrice} illustration={vm.active ? { side, quantity: vm.quantity, terminalPrice: vm.terminalPrice } : null} />
    </div> : <p className="text-sm">Depth chart unavailable: the source contains unsupported price or size values.</p>}
    <p className="break-words font-mono text-xs text-muted-foreground">Snapshot {book.timestamp} · {book.venue ?? "Venue unreported"} · stream {book.streamId ?? "unreported"} · sequence {book.sequenceNumber} · {book.isStale ? "STALE" : book.marketState}</p>
    {book.timestamp !== latestBook.timestamp || book.sequenceNumber !== latestBook.sequenceNumber || book.streamId !== latestBook.streamId ? <p className="text-xs text-muted-foreground">Newer depth is available. Capture it explicitly to update this illustration.</p> : null}
    {vm.active ? <>
      <p role="status" className="text-sm">{size(vm.covered)} of {size(vm.quantity)} displayed size units covered across {vm.levels.filter((level) => level.used > 0).length} {side === "Buy" ? "ask" : "bid"} levels. {vm.shortfall > 0 ? `${size(vm.shortfall)} beyond displayed levels; liquidity outside this snapshot is unknown.` : "Quantity fits within the displayed levels."}</p>
      <details open><summary className="cursor-pointer text-xs font-medium">Quantity coverage by level</summary><DenseDataTable ariaLabel="Illustrative quantity coverage levels" rows={vm.levels} getRowId={(level) => level.id} getRowClassName={(level) => level.used > 0 ? "bg-primary/10" : "text-muted-foreground"} emptyText="No displayed levels on this side." maxVisibleRows={null} columns={[
        { id: "price", label: "Price", numeric: true, render: (level) => size(level.price) },
        { id: "displayed", label: "Displayed", numeric: true, render: (level) => size(level.displayed) },
        { id: "use", label: "Illustrated use", numeric: true, render: (level) => size(level.used) },
        { id: "coverage", label: "Coverage", render: (level) => level.used === 0 ? "Unused" : level.used < level.displayed ? "Partial level" : "Full level" }
      ]} /></details>
    </> : null}
  </section>;
}
