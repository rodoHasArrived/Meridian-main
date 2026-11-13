import type { OrderBookResponse, OrderBookLevelDto } from "@/types/market-data";

export interface DepthCoverageLevel { id: string; price: number; displayed: number; used: number; cumulative: number }

export function buildDepthQuantityIllustration(book: OrderBookResponse, side: "Buy" | "Sell", input: string) {
  const quantity = Number(input);
  const validQuantity = /^\d+(?:\.\d+)?$/.test(input.trim()) && Number.isFinite(quantity) && quantity > 0 && quantity <= Number.MAX_SAFE_INTEGER;
  const source = side === "Buy" ? book.asks : book.bids;
  const invalidBook = [...book.bids, ...book.asks].some((level) => !Number.isFinite(level.price) || level.price <= 0 || !Number.isFinite(level.size) || level.size < 0 || level.size > Number.MAX_SAFE_INTEGER);
  const available = source.reduce((total, level) => total + level.size, 0);
  const error = !input.trim() ? null : !validQuantity ? "Enter a positive quantity in displayed size units."
    : invalidBook || !Number.isFinite(available) || available > Number.MAX_SAFE_INTEGER ? "Displayed depth contains unsupported values; coverage cannot be illustrated." : null;
  const coverage = validQuantity && !error ? illustrateDepth(source, quantity, side) : null;
  const levels: DepthCoverageLevel[] = coverage?.rows.map((level, index) => ({
    id: `${level.side}-${level.level}-${index}`, price: level.price, displayed: level.size,
    used: level.included, cumulative: level.coveredCumulative
  })) ?? [];
  const terminal = levels.filter((level) => level.used > 0).at(-1);
  return { quantity, error, levels, covered: coverage?.covered ?? 0, shortfall: coverage?.uncovered ?? (validQuantity ? quantity : 0), terminalPrice: terminal?.price ?? null, active: validQuantity && !error };
}

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
    return { ...level, included: toNumber(included), cumulative: toNumber(cumulative), coveredCumulative: toNumber(quantityTicks - remaining) };
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
