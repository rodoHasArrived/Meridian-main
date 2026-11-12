import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import type { OrderBookLevelDto, OrderBookResponse } from "@/types/market-data";

export function illustrateDepth(levels: OrderBookLevelDto[], quantity: number, side: "Buy" | "Sell") {
  if (!Number.isFinite(quantity) || quantity <= 0) return null;
  if (levels.some((level) => !Number.isFinite(level.price) || level.price <= 0 || !Number.isFinite(level.size) || level.size < 0)) return null;
  // Size arithmetic uses decimal ticks so fractional levels do not produce a
  // spurious microscopic shortfall through repeated binary subtraction.
  const decimals = [quantity, ...levels.map((level) => level.size)].map(decimalSize);
  const scale = Math.max(...decimals.map((value) => value.scale));
  const ticks = decimals.map((value) => value.coefficient * 10n ** BigInt(scale - value.scale));
  const quantityTicks = ticks[0];
  let remaining = quantityTicks;
  let cumulative = 0n;
  const sizeTicks = new Map(levels.map((level, index) => [level, ticks[index + 1]]));
  const toNumber = (value: bigint) => Number(`${value}e-${scale}`);
  const rows = [...levels].sort((a, b) => side === "Buy" ? a.price - b.price : b.price - a.price).map((level) => {
    const size = sizeTicks.get(level)!;
    const included = remaining < size ? remaining : size;
    remaining -= included;
    cumulative += size;
    return { ...level, included: toNumber(included), cumulative: toNumber(cumulative) };
  });
  return { rows, covered: toNumber(quantityTicks - remaining), uncovered: toNumber(remaining), visible: toNumber(cumulative) };
}

function decimalSize(value: number) {
  const [mantissa, exponent = "0"] = String(value).split("e");
  const [whole, fraction = ""] = mantissa.split(".");
  const scale = fraction.length - Number(exponent);
  const coefficient = BigInt(whole + fraction);
  return scale < 0 ? { coefficient: coefficient * 10n ** BigInt(-scale), scale: 0 } : { coefficient, scale };
}

/** Local, frozen snapshot arithmetic. Never shares state with the execution ticket. */
export function DepthQuantityIllustration({ book }: { book: OrderBookResponse }) {
  const [snapshot, setSnapshot] = useState(book);
  const [side, setSide] = useState<"Buy" | "Sell">("Buy");
  const [quantity, setQuantity] = useState("100");
  const result = illustrateDepth(side === "Buy" ? snapshot.asks : snapshot.bids, Number(quantity), side);
  const changed = snapshot.timestamp !== book.timestamp || snapshot.sequenceNumber !== book.sequenceNumber || snapshot.streamId !== book.streamId;
  return (
    <section aria-label="Order size snapshot illustration" className="space-y-3 rounded-[2px] border border-border p-3 text-xs">
      <h3 className="font-semibold text-sm">Market depth and order size</h3>
      <p className="text-muted-foreground">Snapshot illustration of displayed liquidity, not predicted execution. Hidden liquidity, queue priority, fees and later book changes are excluded. Quantities use the feed’s size units; verify instrument units before comparison.</p>
      <div className="flex flex-wrap items-end gap-3">
        <label>Illustrative side<select className="ml-2 rounded border border-border bg-background p-2" value={side} onChange={(event) => setSide(event.target.value as "Buy" | "Sell")}><option>Buy</option><option>Sell</option></select></label>
        <label>Illustrative quantity<Input className="mt-1 w-40" type="number" min="0" step="any" value={quantity} onChange={(event) => setQuantity(event.target.value)} aria-invalid={!result} /></label>
        <Button size="sm" variant="outline" onClick={() => setSnapshot(book)}>Capture latest depth snapshot</Button>
      </div>
      <p>Frozen {snapshot.symbol} · {snapshot.timestamp} · sequence {snapshot.sequenceNumber} · venue {snapshot.venue ?? "unreported"} · stream {snapshot.streamId ?? "unreported"} · {snapshot.marketState}{snapshot.isStale ? " · STALE at capture" : ""}{changed ? " · newer depth is available" : ""}</p>
      {result ? <>
        <p role="status">{format(result.covered)} of {format(Number(quantity))} covered by displayed {side === "Buy" ? "asks" : "bids"}. {result.uncovered > 0 ? `${format(result.uncovered)} exceeds displayed depth; further liquidity is unknown.` : "Illustrative quantity fits within displayed depth."}</p>
        <div className="overflow-x-auto"><table className="w-full min-w-[620px] text-left [&_th]:px-2 [&_td]:px-2" aria-label="Illustrative depth coverage"><caption className="text-left text-muted-foreground">Only returned levels are included. Highlighted size is the portion used by the illustration.</caption><thead><tr><th>Level</th><th>Price</th><th>Displayed size</th><th>Cumulative size</th><th>Illustrated size</th><th>Coverage</th></tr></thead><tbody>
          {result.rows.map((row, index) => <tr key={`${row.level}:${row.price}:${index}`} className="border-t border-border"><td className="py-2">{row.level}</td><td className="font-mono">{format(row.price)}</td><td>{format(row.size)}</td><td>{format(row.cumulative)}</td><td>{format(row.included)}</td><td className="min-w-24"><div className="h-2 bg-muted" aria-hidden="true"><div className="h-full bg-primary" style={{ width: `${row.size > 0 ? row.included / row.size * 100 : 0}%` }} /></div><span>{row.included === 0 ? "Unused" : row.included === row.size ? "Whole level" : "Partial level"}</span></td></tr>)}
          {result.rows.length === 0 ? <tr><td colSpan={6} className="py-3">No displayed levels on this side. Available liquidity is unknown.</td></tr> : null}
        </tbody></table></div>
      </> : <p role="alert">Enter a finite quantity greater than zero. A snapshot with invalid prices or sizes cannot be illustrated.</p>}
    </section>
  );
}

function format(value: number) {
  return new Intl.NumberFormat(undefined, value !== 0 && Math.abs(value) < 1e-8
    ? { notation: "scientific", maximumSignificantDigits: 12 }
    : { maximumFractionDigits: 8 }).format(value);
}
