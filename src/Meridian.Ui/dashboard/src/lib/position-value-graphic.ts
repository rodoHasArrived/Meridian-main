/**
 * Presentation adapter for WorkstationEndpoints.FormatCurrency's invariant $0.##[KM]
 * and the grouped $ amounts retained by workstation fixtures. Values remain display
 * amounts, including server rounding; they must never become financial calculations.
 * The dollar symbol is a display denomination, not an inferred ISO currency.
 */
export interface DisplayedPositionAmount {
  units: bigint;
  denomination: "$";
}

export interface PositionValueMeasurement {
  magnitudePct: number;
  direction: "positive" | "negative" | "zero";
  isLargest: boolean;
  comparableCount: number;
  denomination: "$";
  /** Source currency, when supplied; null explicitly identifies the unknown-currency group. */
  currency?: string | null;
}

export function parseDisplayedPositionAmount(value: string): DisplayedPositionAmount | null {
  const match = /^([+-]?)\$((?:0|[1-9]\d*|[1-9]\d{0,2}(?:,\d{3})+))(?:\.(\d{1,2}))?([KM]?)$/.exec(value);
  if (!match) return null;
  const [, sign, integer, fraction = "", suffix] = match;
  const scale = suffix === "M" ? 1_000_000n : suffix === "K" ? 1_000n : 1n;
  const absoluteUnits = BigInt(integer.replaceAll(",", "") + fraction.padEnd(2, "0")) * scale;
  return { units: sign === "-" ? -absoluteUnits : absoluteUnits, denomination: "$" };
}

/** Stable across filters: callers pass the complete loaded position set.
 * Explicit source currencies stay separate, including the unavailable-currency group.
 */
export function measureDisplayedPositionAmounts(
  values: readonly string[],
  currencies?: readonly (string | null | undefined)[]
): (PositionValueMeasurement | null)[] {
  const amounts = values.map(parseDisplayedPositionAmount);
  const currencyGroups = amounts.map((_, index) => currencies?.[index]?.trim() || null);
  const groups = new Map<string | null, { maximum: bigint; comparableCount: number }>();
  for (const [index, amount] of amounts.entries()) {
    if (!amount) continue;
    const key = currencyGroups[index];
    const group = groups.get(key) ?? { maximum: 0n, comparableCount: 0 };
    group.comparableCount++;
    const absolute = amount.units < 0n ? -amount.units : amount.units;
    if (absolute > group.maximum) group.maximum = absolute;
    groups.set(key, group);
  }
  return amounts.map((amount, index) => {
    if (!amount) return null;
    const currency = currencyGroups[index];
    const { maximum, comparableCount } = groups.get(currency)!;
    const absolute = amount.units < 0n ? -amount.units : amount.units;
    return {
      // Convert only the bounded display ratio, never the retained monetary amount.
      magnitudePct: maximum === 0n ? 0 : Number(absolute * 10_000n / maximum) / 100,
      direction: amount.units < 0n ? "negative" : amount.units > 0n ? "positive" : "zero",
      isLargest: maximum > 0n && absolute === maximum,
      comparableCount,
      denomination: amount.denomination,
      ...(currencies ? { currency } : {})
    };
  });
}
