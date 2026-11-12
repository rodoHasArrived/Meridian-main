import type { OrderBookResponse } from "@/types/market-data";

export interface DepthCoverageLevel { id: string; price: number; displayed: number; used: number; cumulative: number }

export function buildDepthQuantityIllustration(book: OrderBookResponse, side: "Buy" | "Sell", input: string) {
  const quantity = Number(input);
  const validQuantity = /^\d+(?:\.\d+)?$/.test(input.trim()) && Number.isFinite(quantity) && quantity > 0 && quantity <= Number.MAX_SAFE_INTEGER;
  const source = side === "Buy" ? book.asks : book.bids;
  const invalidBook = [...book.bids, ...book.asks].some((level) => !Number.isFinite(level.price) || level.price <= 0 || !Number.isFinite(level.size) || level.size < 0 || level.size > Number.MAX_SAFE_INTEGER);
  const available = source.reduce((total, level) => total + level.size, 0);
  const error = !input.trim() ? null : !validQuantity ? "Enter a positive quantity in displayed size units."
    : invalidBook || !Number.isFinite(available) || available > Number.MAX_SAFE_INTEGER ? "Displayed depth contains unsupported values; coverage cannot be illustrated." : null;
  let cumulative = 0;
  const levels: DepthCoverageLevel[] = !validQuantity || error ? [] : source
    .map((level, index) => ({ ...level, index }))
    .sort((a, b) => (side === "Buy" ? a.price - b.price : b.price - a.price) || a.index - b.index)
    .map((level) => {
      const used = Math.max(0, Math.min(level.size, quantity - cumulative));
      cumulative += used;
      return { id: `${level.side}-${level.level}-${level.index}`, price: level.price, displayed: level.size, used, cumulative };
    });
  const terminal = levels.filter((level) => level.used > 0).at(-1);
  return { quantity, error, levels, covered: cumulative, shortfall: Math.max(0, quantity - cumulative), terminalPrice: terminal?.price ?? null, active: validQuantity && !error };
}
